// QA-only portable candidate probe. The independent Python oracle supplies expectations.
// No production changes or new fault hooks. Compiled against the frozen candidate DLL.
using System.Text.Json;
using UserDimensions.Ledger;

try
{
    var raw=File.ReadAllBytes(args[0]);
    var block=BlockCodec.Parse(raw,new ResourceLimits());
    Console.WriteLine(JsonSerializer.Serialize(new {accepted=true,sha512=block.Hash,block_guid=block.Id,bytes=raw.Length}));
    return 0;
}
catch(LedgerException e)
{
    Console.WriteLine(JsonSerializer.Serialize(new {accepted=false,code=e.Code,message=e.Message}));
    Console.Error.WriteLine(e.Code+": "+e.Message);return e.ExitCode;
}
catch(Exception e)
{
    Console.WriteLine(JsonSerializer.Serialize(new {accepted=false,probe_error=e.GetType().Name,message=e.Message}));
    Console.Error.WriteLine(e);return 90;
}
