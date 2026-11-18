using System.Text.Json;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunMissingGenesis()
    {
        foreach(var wholeDirectory in new[]{false,true}) Check("QA015-D01 healthy identity and safe recovery diagnostics with missing "+(wholeDirectory?"BLK directory":"pinned genesis"),()=>
        {
            var name=wholeDirectory?"missing-directory":"missing-genesis";var test=Qa014Data(name);
            var originalIdentity=Cli(test.Root,"instance","status").Json.GetProperty("data");
            var directory=Path.Combine(test.Root,"BLK");string genesisPath;
            using(var instance=LedgerInstance.Open(test.Root)) genesisPath=instance.BlockPath(test.Genesis,false);
            var source=wholeDirectory?directory:genesisPath;var preserved=Path.Combine(Scratch,name+"-original");
            var scope=Path.GetFullPath(Scratch)+Path.DirectorySeparatorChar;
            var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            Assert(Path.GetFullPath(source).StartsWith(scope,comparison) && Path.GetFullPath(preserved).StartsWith(scope,comparison),"Only fresh Developer scratch files may be moved");
            if(wholeDirectory) Directory.Move(directory,preserved);else File.Move(genesisPath,preserved);
            var before=Qa014Inventory(test.Root);

            using(var instance=LedgerInstance.Open(test.Root,new DurableFiles(_=>throw new Exception("Incomplete-history gate reached mutation callback"))))
            {
                try { instance.Snapshot();throw new Exception("Expected incomplete-history failure"); }
                catch(LedgerException e)
                {
                    var details=JsonSerializer.SerializeToElement(e.Details);
                    Assert(e.Code=="RECOVERY_REQUIRED" && e.ExitCode==4 && details.GetProperty("reason").GetString()=="LEDGER_HISTORY_INCOMPLETE");
                    Assert(details.GetProperty("path").GetString()==directory,"Diagnostic must identify the affected block-storage directory");
                }
            }

            var statusResponse=Cli(test.Root,"status");var status=statusResponse.Json.GetProperty("data");
            Assert(statusResponse.Exit==0 && status.GetProperty("signing").GetProperty("available").GetBoolean());
            Assert(!status.GetProperty("signing").GetProperty("authorized").GetBoolean() && status.GetProperty("blocks").ValueKind==JsonValueKind.Null);
            Assert(status.GetProperty("instance").GetProperty("recovery_codes").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(["LEDGER_HISTORY_INCOMPLETE"]));
            Assert(status.GetProperty("binding").GetProperty("ledger_id_hex").GetString()==test.Genesis.LedgerId &&
                status.GetProperty("binding").GetProperty("genesis_sha512").GetString()==test.Genesis.Hash);
            var identityResponse=Cli(test.Root,"instance","status");var identity=identityResponse.Json.GetProperty("data");
            Assert(identityResponse.Exit==0 && identity.GetProperty("signing_available").GetBoolean());
            foreach(var field in new[]{"instance_guid","public_key_hex","ledger_id_hex","genesis_sha512"})
                Assert(identity.GetProperty(field).GetRawText()==originalIdentity.GetProperty(field).GetRawText());
            Assert(identity.GetProperty("recovery_codes").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(["LEDGER_HISTORY_INCOMPLETE"]));

            var output=Path.Combine(Scratch,name+"-output.bin");
            string[][] commands=[
                ["block","publish","--event-file",test.EventFile],
                ["block","get","--id",test.Block.Id.ToString("D"),"--output",output],
                ["block","export","--output",output],
                ["master","countersign-revocation","--outer-signer-public-key",new string('1',64),"--revoked-public-key",identity.GetProperty("public_key_hex").GetString()!,"--output",output]];
            foreach(var command in commands)
            {
                var response=Cli(test.Root,command);var error=response.Json.GetProperty("error");
                Assert(response.Exit==4 && error.GetProperty("code").GetString()=="RECOVERY_REQUIRED" && !error.GetProperty("retryable").GetBoolean());
                Assert(error.GetProperty("details").GetProperty("reason").GetString()=="LEDGER_HISTORY_INCOMPLETE" &&
                    error.GetProperty("details").GetProperty("path").GetString()==directory);
                Assert(!File.Exists(output) && Qa014Inventory(test.Root).SequenceEqual(before));
            }

            var batch=Path.Combine(Scratch,name+".DDLB");var incoming=BlockCodec.Parse(File.ReadAllBytes(test.Input));
            File.WriteAllBytes(batch,Ddlb.Encode([incoming,incoming]));
            foreach(var input in new[]{test.Input,batch})
            {
                var response=Cli(test.Root,"block","import","--input",input);var error=response.Json.GetProperty("error");var details=error.GetProperty("details");
                Assert(response.Exit==4 && error.GetProperty("code").GetString()=="RECOVERY_REQUIRED" && details.GetProperty("reason").GetString()=="LEDGER_HISTORY_INCOMPLETE");
                Assert(details.GetProperty("durable_count").GetInt32()==0 && details.GetProperty("duplicate_count").GetInt32()==0 &&
                    details.GetProperty("rejected_count").GetInt32()==(input==batch?2:1));
                foreach(var item in details.GetProperty("results").EnumerateArray())
                    Assert(!item.GetProperty("durable").GetBoolean() && item.GetProperty("authorization").GetString()=="not_evaluated");
                Assert(Qa014Inventory(test.Root).SequenceEqual(before));
            }

            foreach(var kind in new[]{"frame","hash","signature"})
            {
                var bytes=incoming.Raw.ToArray();var code="BAD_FRAME";
                if(kind=="frame") bytes=bytes[..^1];
                if(kind=="hash") { bytes[90]^=1;code="HASH_MISMATCH"; }
                if(kind=="signature") { bytes[^1]^=1;code="BAD_SIGNATURE"; }
                var input=Path.Combine(Scratch,name+"-bad-"+kind+".bin");File.WriteAllBytes(input,bytes);
                var response=Cli(test.Root,"block","import","--input",input);
                Assert(response.Exit==2 && response.Json.GetProperty("error").GetProperty("code").GetString()==code);
            }
            Assert(Qa014Inventory(test.Root).SequenceEqual(before),"Identity, genesis binding and every unaffected signed file must remain exact");
            if(wholeDirectory) Assert(Directory.GetFiles(preserved,"*.BLK",SearchOption.AllDirectories).Any(p=>File.ReadAllBytes(p).SequenceEqual(test.Genesis.Raw)));
            else Assert(File.ReadAllBytes(preserved).SequenceEqual(test.Genesis.Raw));
        });
    }
}
