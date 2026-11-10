using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using UserDimensions.Ledger;

internal static partial class Tests
{
    public static void RunCompressionInterop()
    {
        Check("raw LZMA2 encoder compresses repeated bytes and handles random/reset chunks",()=>
        {
            var repeated=Enumerable.Repeat((byte)64,150000).ToArray(); var packed=Lzma2.Encode(repeated);
            Assert(packed.Length<repeated.Length/10 && Lzma2.Decode(packed,new()).SequenceEqual(repeated));
            var random=RandomNumberGenerator.GetBytes(150000); Assert(Lzma2.Decode(Lzma2.Encode(random),new()).SequenceEqual(random));
        });
        var sevenZip=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"7-Zip","7z.exe");
        if(!File.Exists(sevenZip)) { Console.WriteLine("SKIP optional independent encoder check: 7-Zip is not installed"); return; }
        Check("7-Zip independently decodes authored compressed and stored LZMA2 chunks",()=>
        {
            foreach(var data in new[]{Binary.Utf8.GetBytes(string.Concat(Enumerable.Repeat("reference decoder interoperability ",5000))),RandomNumberGenerator.GetBytes(100000),Array.Empty<byte>()})
            {
                var packed=Lzma2.Encode(data); var path=Path.Combine(Scratch,"encoder-"+Guid.NewGuid().ToString("N")+".xz"); File.WriteAllBytes(path,XzWrapper(packed,data));
                var start=new ProcessStartInfo(sevenZip) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                start.ArgumentList.Add("t"); start.ArgumentList.Add(path); using var process=Process.Start(start)!;
                var output=process.StandardOutput.ReadToEnd(); var error=process.StandardError.ReadToEnd(); Assert(process.WaitForExit(10000) && process.ExitCode==0,"Independent decoder failed: "+output+error);
            }
        });
    }
    // Test-only XZ framing per https://tukaani.org/xz/xz-file-format.txt. Production emits only raw LZMA2.
    private static byte[] XzWrapper(byte[] raw,byte[] plain)
    {
        static uint Crc(byte[] b) { uint c=uint.MaxValue; foreach(var x in b) { c^=x; for(var i=0;i<8;i++) c=(c>>1)^((c&1)!=0?0xedb88320u:0u); } return ~c; }
        static void Le(Stream s,uint n) { Span<byte> b=stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b,n); s.Write(b); }
        using var output=new MemoryStream(); output.Write([0xfd,0x37,0x7a,0x58,0x5a,0,0,1]); Le(output,Crc([0,1]));
        byte[] blockHeader=[2,0,0x21,1,20,0,0,0]; output.Write(blockHeader); Le(output,Crc(blockHeader));
        output.Write(raw.AsSpan(1)); while(output.Length%4!=0) output.WriteByte(0); Le(output,Crc(plain));
        using var index=new MemoryStream(); index.Write([0,1]); Binary.WriteVar(index,(ulong)(12+raw.Length-1+4)); Binary.WriteVar(index,(ulong)plain.Length);
        while(index.Length%4!=0) index.WriteByte(0); Le(index,Crc(index.ToArray())); output.Write(index.ToArray());
        using var footer=new MemoryStream(); Le(footer,(uint)(index.Length/4-1)); footer.Write([0,1]); Le(output,Crc(footer.ToArray())); output.Write(footer.ToArray()); output.Write([0x59,0x5a]);
        return output.ToArray();
    }
}
