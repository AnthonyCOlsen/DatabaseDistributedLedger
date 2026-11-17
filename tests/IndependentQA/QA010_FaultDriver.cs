// QA-owned adapter: calls the public candidate API and injects EXISTING DurableFiles callbacks.
// Compiled in fresh excluded evidence space against a frozen candidate DLL; no production edits.
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using UserDimensions.Ledger;

var request=JsonDocument.Parse(File.ReadAllBytes(args[0])).RootElement;
string Text(string name,string fallback="") => request.TryGetProperty(name,out var v)?v.GetString()!:fallback;
var root=Text("root"); var op=Text("operation"); var barrier=Text("barrier"); var marker=Text("marker");
var occurrence=request.TryGetProperty("occurrence",out var n)?n.GetInt32():1; var seen=0;
var observed=new List<string>();
var batchEvents=new List<object>(); var batchOrdinal=0;
void Trace() { if(Text("trace") is {} path && path!="") File.WriteAllText(path,JsonSerializer.Serialize(new {observed_checkpoints=observed,batch_events=batchEvents})); }
void Ready(string checkpoint)
{
    File.WriteAllText(marker+".tmp",JsonSerializer.Serialize(new {pid=Environment.ProcessId,checkpoint,operation=op,utc=DateTimeOffset.UtcNow,
        observed_checkpoints=observed,batch_events=batchEvents,current_import_result_exposed=false}));
    File.Move(marker+".tmp",marker);
    // Positive marker/release polling with a bounded deadline, never a visibility sleep assertion.
    var release=marker+".release"; var until=DateTimeOffset.UtcNow.AddSeconds(120);
    while(!File.Exists(release) && DateTimeOffset.UtcNow<until) Thread.Sleep(10);
    if(!File.Exists(release)) Environment.Exit(91);
}
var files=new DurableFiles(checkpoint=>
{
    observed.Add(checkpoint);
    if(checkpoint.StartsWith("batch-",StringComparison.Ordinal))
    {
        var fields=checkpoint.Split(':');
        var index=fields.Length==3 && int.TryParse(fields[1],out var parsed)?parsed:-1;
        var hash=fields.Length==3?fields[2]:"";
        var retained=Directory.Exists(Path.Combine(root,"BLK"))?Directory.GetFiles(Path.Combine(root,"BLK"),"*.BLK",SearchOption.AllDirectories)
            .Where(p=>Convert.ToHexStringLower(SHA512.HashData(File.ReadAllBytes(p)))==hash).ToArray():[];
        batchEvents.Add(new {checkpoint,input_index=index,canonical_ordinal=batchOrdinal,sha512=hash,matching_final_blk_files=retained,
            current_import_result_exposed=false});
        Trace();
        if(checkpoint.StartsWith("batch-item-completed:",StringComparison.Ordinal))batchOrdinal++;
    }
    if(checkpoint==Text("throw_checkpoint"))throw new IOException("QA-injected callback interruption");
    if(checkpoint==barrier || (barrier=="after-block-flush" && checkpoint.StartsWith("after-flush:") && checkpoint.EndsWith(".BLK")) ||
        (barrier=="after-block-publish" && checkpoint.StartsWith("after-publish:") && checkpoint.EndsWith(".BLK")) ||
        (barrier=="before-block-rename" && checkpoint.StartsWith("before-rename:") && checkpoint.EndsWith(".BLK")) ||
        (barrier=="after-block-rename" && checkpoint.StartsWith("after-rename:") && checkpoint.EndsWith(".BLK")))
        if(++seen==occurrence) Ready(checkpoint);
});
try
{
    if(op is "batch" or "batch-normal" or "management")
    {
        try
        {
            var command=op=="management"?new[]{"--instance-root",root}.Concat(request.GetProperty("command").EnumerateArray().Select(x=>x.GetString()!)).ToArray():
                new[]{"--instance-root",root,"block","import","--input",Text("input")};
            return op=="batch-normal"?Management.Run(command,Console.Out,Console.Error):Management.Run(command,Console.Out,Console.Error,files);
        }
        finally { Trace(); }
    }
    if(op=="api-surface")
    {
        var names=new[]{"Publish","CreateRevocationConsent","PublishRevocationConsent"};
        var methods=typeof(LedgerInstance).GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(x=>names.Contains(x.Name))
            .Select(x=>new {name=x.Name,parameters=x.GetParameters().Select(p=>new {name=p.Name,type=p.ParameterType.FullName})});
        Console.WriteLine(JsonSerializer.Serialize(new {methods})); return 0;
    }
    if(op=="hold-file")
    {
        using var held=new FileStream(Text("input"),FileMode.Open,FileAccess.Read,FileShare.None);
        Ready("file-held"); return 0;
    }
    if(op=="init") LedgerInstance.Initialize(root,files);
    else if(op=="key-recover") LedgerInstance.Recover(root,Path.Combine(root,"signing-key.v1.json"),true,files);
    else
    {
        using var instance=LedgerInstance.Open(root,files);
        switch(op)
        {
            case "lease": Ready("lease-acquired"); break;
            case "genesis": instance.InitializeLedger(); break;
            case "join": instance.Join(Text("ledger"),Text("hash"),File.ReadAllBytes(Text("input"))); break;
            case "publish": instance.Publish(File.ReadAllBytes(Text("input")),request.TryGetProperty("block",out var id)?Guid.Parse(id.GetString()!):null,
                request.TryGetProperty("compress",out var compression) && compression.GetBoolean()); break;
            case "import": instance.Import(File.ReadAllBytes(Text("input")),out _); break;
            case "snapshot": instance.Snapshot(); break;
            case "config": instance.Configure("max_raw_block_bytes",request.GetProperty("value").GetInt64()); break;
            case "consent": instance.WriteOutput(Text("output"),instance.CreateRevocationConsent(Convert.FromHexString(Text("outer")),Convert.FromHexString(Text("target"))).Encode()); break;
            case "publish-consent": instance.PublishRevocationConsent(File.ReadAllBytes(Text("input"))); break;
            default: throw new Exception("Unknown QA adapter operation");
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new {completed=true,operation=op,observed_checkpoints=observed})); return 0;
}
catch(LedgerException e)
{
    Console.WriteLine(JsonSerializer.Serialize(new {completed=false,code=e.Code,message=e.Message,observed_checkpoints=observed})); return e.ExitCode;
}
