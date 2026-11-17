using System.Reflection;
using System.Text.Json;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunPreSigningRecovery()
    {
        Check("Fresh publication verifies retained history before obtaining a signing timestamp",()=>
        {
            var test=Qa014Data("pre-signing");var body=File.ReadAllBytes(test.EventFile);
            // Exercise the existing clock dependency without adding production instrumentation.
            // A final recovery error alone would not detect signing before the history gate.
            var publish=typeof(LedgerInstance).GetMethod("PublishCore",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var clockCalls=0;var timestamp=test.Genesis.Timestamp+3;
            using(var healthy=LedgerInstance.Open(test.Root))
            {
                var accepted=(BlockDisposition)publish.Invoke(healthy,[body,null,new Func<long>(()=>{clockCalls++;return timestamp;}),false])!;
                Assert(clockCalls==1 && accepted.Block.Timestamp==timestamp && accepted.Authorization=="authorized");
            }
            string path;
            using(var instance=LedgerInstance.Open(test.Root)) path=instance.BlockPath(test.Block,false);
            var corrupt=test.Block.Raw.ToArray();corrupt[163]^=1;File.WriteAllBytes(path,corrupt);
            var before=Qa014Inventory(test.Root);
            using(var instance=LedgerInstance.Open(test.Root,new DurableFiles(_=>throw new Exception("Recovery gate reached a mutation callback"))))
            {
                foreach(var id in new Guid?[]{null,Guid.NewGuid()})
                {
                    clockCalls=0;
                    try
                    {
                        publish.Invoke(instance,[body,id,new Func<long>(()=>{clockCalls++;throw new Exception("Fresh signing timestamp requested before recovery gate");}),false]);
                        throw new Exception("Expected retained-history recovery failure");
                    }
                    catch(TargetInvocationException e) when(e.InnerException is LedgerException failure)
                    {
                        var details=JsonSerializer.SerializeToElement(failure.Details);
                        Assert(failure.Code=="RECOVERY_REQUIRED" && failure.ExitCode==4 &&
                            details.GetProperty("reason").GetString()=="LEDGER_HISTORY_CORRUPT" && details.GetProperty("path").GetString()==path);
                    }
                    Assert(clockCalls==0,"Recovery must precede fresh timestamp acquisition and signing");
                    Assert(Qa014Inventory(test.Root).SequenceEqual(before));
                }
                Throws("RECOVERY_REQUIRED",()=>instance.Publish(body));
            }
            var response=Cli(test.Root,"block","publish","--event-file",test.EventFile);
            Assert(response.Exit==4 && response.Json.GetProperty("error").GetProperty("code").GetString()=="RECOVERY_REQUIRED" &&
                response.Json.GetProperty("error").GetProperty("details").GetProperty("reason").GetString()=="LEDGER_HISTORY_CORRUPT");
            var status=Cli(test.Root,"status").Json.GetProperty("data");
            Assert(status.GetProperty("signing").GetProperty("available").GetBoolean() && !status.GetProperty("signing").GetProperty("authorized").GetBoolean());
            Assert(status.GetProperty("binding").GetProperty("genesis_sha512").GetString()==test.Genesis.Hash && status.GetProperty("blocks").ValueKind==JsonValueKind.Null);
            Assert(Qa014Inventory(test.Root).SequenceEqual(before) && File.ReadAllBytes(path).SequenceEqual(corrupt),
                "Failed recovery gating must preserve original identity and all signed/diagnostic files");
        });
    }
}
