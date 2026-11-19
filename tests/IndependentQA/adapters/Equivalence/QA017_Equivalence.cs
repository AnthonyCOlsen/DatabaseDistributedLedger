// QA-017 independent verifier. Does not load or execute production assemblies.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length != 3) throw new ArgumentException("baseline-worktree candidate-worktree output-directory");
var output = args[2]; Directory.CreateDirectory(output);
var failures = new List<string>();
var sources = new List<object>();
foreach (var file in Directory.EnumerateFiles(Path.Combine(args[0],"src"),"*.cs",SearchOption.AllDirectories)
    .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(x=>x is "bin" or "obj")).Order())
{
    var relative = Path.GetRelativePath(args[0],file);
    var left = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
    var right = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(args[1],relative))).GetRoot();
    string[] Imports(SyntaxNode node) => node.DescendantNodes().OfType<UsingDirectiveSyntax>().Select(x=>x.WithoutTrivia().ToFullString()).ToArray();
    string[] Tokens(SyntaxNode node) => node.RemoveNodes(node.DescendantNodes().OfType<UsingDirectiveSyntax>(),SyntaxRemoveOptions.KeepNoTrivia)!
        .DescendantTokens().Select(x=>x.RawKind+":"+x.Text).ToArray();
    var removed = Imports(left).Except(Imports(right)).ToArray();
    var added = Imports(right).Except(Imports(left)).ToArray();
    var equal = Tokens(left).SequenceEqual(Tokens(right));
    if (!equal || added.Length != 0) failures.Add("Source token/import change: "+relative);
    sources.Add(new {path=relative,executable_tokens_equal=equal,removed_imports=removed,added_imports=added});
}
var assemblies = new List<object>();
foreach (var name in new[]{"UserDimensions.Ledger.dll","ledgerctl.dll"})
{
    var relative = name == "ledgerctl.dll" ? "src/ledgerctl/bin/Release/net10.0/" : "src/UserDimensions.Ledger/bin/Release/net10.0/";
    var left = Snapshot.Read(Path.Combine(args[0],relative,name));
    var right = Snapshot.Read(Path.Combine(args[1],relative,name));
    var changedMethods = left.Methods.Keys.Union(right.Methods.Keys).Where(k=>!left.Methods.TryGetValue(k,out var a) || !right.Methods.TryGetValue(k,out var b) || a!=b).ToArray();
    var changedMetadata = left.Metadata.Keys.Union(right.Metadata.Keys).Where(k=>!left.Metadata.TryGetValue(k,out var a) || !right.Metadata.TryGetValue(k,out var b) || a!=b).ToArray();
    if (changedMethods.Length+changedMetadata.Length != 0) failures.Add(name+": compiled differences");
    File.WriteAllText(Path.Combine(output,name+".baseline.json"),JsonSerializer.Serialize(left,Snapshot.Options));
    File.WriteAllText(Path.Combine(output,name+".candidate.json"),JsonSerializer.Serialize(right,Snapshot.Options));
    assemblies.Add(new {name,baseline_sha256=left.Sha256,candidate_sha256=right.Sha256,
        method_definitions=left.Methods.Count,methods_with_il=left.Bodies,metadata_records=left.Metadata.Count,
        changed_methods=changedMethods,changed_metadata=changedMetadata,
        baseline_build_identity=left.BuildIdentity,candidate_build_identity=right.BuildIdentity});
}
var result = new {assignment="QA-017",independently_written_verifier=true,
    sources,assemblies,failures,pass=failures.Count==0,
    coverage="All method IL, locals/max-stack/exception regions; type/field/method/property/event/parameter signatures and flags; references, generics, constants, marshalling, custom attributes, managed resources, RVA initializers, #US/#Strings heaps. Source tokens exclude only using directives and trivia.",
    exclusions="Module MVID, PDB/debug-directory records and AssemblyInformationalVersion are build identity, recorded separately rather than executable equivalence. No claim of a formal proof or physical storage qualification."};
File.WriteAllText(Path.Combine(output,"equivalence-results.json"),JsonSerializer.Serialize(result,Snapshot.Options));
Console.WriteLine(JsonSerializer.Serialize(new {result.pass,assemblies,failures}));
return failures.Count==0?0:1;

sealed class Snapshot
{
    public static readonly JsonSerializerOptions Options = new(){WriteIndented=true};
    public string Sha256 {get;set;} = "";
    public int Bodies {get;set;}
    public Dictionary<string,string> Methods {get;} = new();
    public Dictionary<string,string> Metadata {get;} = new();
    public Dictionary<string,string> BuildIdentity {get;} = new();
    static string Hex(byte[] bytes)=>Convert.ToHexString(bytes);
    static string Hash(byte[] bytes)=>Hex(SHA256.HashData(bytes)).ToLowerInvariant();
    public static Snapshot Read(string path)
    {
        var bytes=File.ReadAllBytes(path); using var pe=new PEReader(new MemoryStream(bytes)); var md=pe.GetMetadataReader();
        var s=new Snapshot{Sha256=Hash(bytes)};
        string S(StringHandle h)=>md.GetString(h);
        string B(BlobHandle h)=>Hex(md.GetBlobBytes(h));
        int T(EntityHandle h)=>h.IsNil?0:MetadataTokens.GetToken(h);
        void Add(string key,object value)=>s.Metadata.Add(key,JsonSerializer.Serialize(value));
        var module=md.GetModuleDefinition();
        s.BuildIdentity["mvid"]=md.GetGuid(module.Mvid).ToString();
        Add("module",new {name=S(module.Name),module.Generation});
        var a=md.GetAssemblyDefinition();
        Add("assembly",new{name=S(a.Name),a.Version,culture=S(a.Culture),key=B(a.PublicKey),a.Flags,a.HashAlgorithm});
        foreach(var h in md.TypeDefinitions)
        {
            var x=md.GetTypeDefinition(h);var layout=x.GetLayout();
            Add("type:"+T(h),new {name=S(x.Name),ns=S(x.Namespace),x.Attributes,base_type=T(x.BaseType),
                declaring=T(x.GetDeclaringType()),layout.Size,layout.PackingSize,
                interfaces=x.GetInterfaceImplementations().Select(i=>T(md.GetInterfaceImplementation(i).Interface)).ToArray()});
        }
        foreach(var h in md.MethodDefinitions)
        {
            var x=md.GetMethodDefinition(h);var import=x.GetImport();
            var key=T(h)+":"+S(md.GetTypeDefinition(x.GetDeclaringType()).Name)+"."+S(x.Name);
            Add("method:"+T(h),new{name=S(x.Name),sig=B(x.Signature),x.Attributes,x.ImplAttributes,
                owner=T(x.GetDeclaringType()),import_name=S(import.Name),import_module=T(import.Module),import_attributes=import.Attributes});
            if(x.RelativeVirtualAddress==0){s.Methods[key]="no IL";continue;}
            var body=pe.GetMethodBody(x.RelativeVirtualAddress);s.Bodies++;
            s.Methods[key]=JsonSerializer.Serialize(new {il=Hex(body.GetILBytes()!),body.MaxStack,body.LocalVariablesInitialized,
                locals=body.LocalSignature.IsNil?"":B(md.GetStandaloneSignature(body.LocalSignature).Signature),
                exceptions=body.ExceptionRegions.Select(e=>new{e.Kind,e.TryOffset,e.TryLength,e.HandlerOffset,e.HandlerLength,e.FilterOffset,catch_type=T(e.CatchType)}).ToArray()});
        }
        foreach(var h in md.FieldDefinitions)
        {
            var x=md.GetFieldDefinition(h);
            Add("field:"+T(h),new{name=S(x.Name),sig=B(x.Signature),x.Attributes,owner=T(x.GetDeclaringType()),offset=x.GetOffset(),marshal=B(x.GetMarshallingDescriptor())});
            if(x.GetRelativeVirtualAddress()!=0)
            {
                var sig=md.GetBlobReader(x.Signature); if(sig.ReadByte()!=6)throw new InvalidDataException("Field signature");
                var code=sig.ReadByte(); var size=code switch {2 or 4 or 5=>1,3 or 6 or 7=>2,8 or 9 or 12=>4,10 or 11 or 13=>8,17=>-1,_=>throw new InvalidDataException("Unknown RVA field type")};
                if(size==-1){var coded=sig.ReadCompressedInteger();if((coded&3)!=0)throw new InvalidDataException("External RVA field type");size=md.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(coded>>2)).GetLayout().Size;}
                Add("field-data:"+T(h),Hex(pe.GetSectionData(x.GetRelativeVirtualAddress()).GetContent(0,size).ToArray()));
            }
        }
        foreach(var h in md.PropertyDefinitions)
        {
            var x=md.GetPropertyDefinition(h);var access=x.GetAccessors();
            Add("property:"+T(h),new{name=S(x.Name),sig=B(x.Signature),x.Attributes,get=T(access.Getter),set=T(access.Setter),other=access.Others.Select(v=>T(v)).ToArray()});
        }
        foreach(var h in md.EventDefinitions)
        {
            var x=md.GetEventDefinition(h);var access=x.GetAccessors();
            Add("event:"+T(h),new{name=S(x.Name),type=T(x.Type),x.Attributes,add=T(access.Adder),remove=T(access.Remover),raise=T(access.Raiser),other=access.Others.Select(v=>T(v)).ToArray()});
        }
        foreach(var h in md.TypeReferences){var x=md.GetTypeReference(h);Add("typeref:"+T(h),new{name=S(x.Name),ns=S(x.Namespace),scope=T(x.ResolutionScope)});}
        foreach(var h in md.MemberReferences){var x=md.GetMemberReference(h);Add("memberref:"+T(h),new{name=S(x.Name),parent=T(x.Parent),sig=B(x.Signature)});}
        foreach(var h in md.AssemblyReferences){var x=md.GetAssemblyReference(h);Add("assemblyref:"+T(h),new{name=S(x.Name),x.Version,culture=S(x.Culture),key=B(x.PublicKeyOrToken),hash=B(x.HashValue),x.Flags});}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.Param);i++){var h=MetadataTokens.ParameterHandle(i);var x=md.GetParameter(h);Add("parameter:"+T(h),new{name=S(x.Name),x.Attributes,x.SequenceNumber,marshal=B(x.GetMarshallingDescriptor())});}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.GenericParam);i++){var h=MetadataTokens.GenericParameterHandle(i);var x=md.GetGenericParameter(h);Add("generic:"+T(h),new{name=S(x.Name),x.Index,x.Attributes,parent=T(x.Parent),constraints=x.GetConstraints().Select(v=>T(md.GetGenericParameterConstraint(v).Type)).ToArray()});}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.TypeSpec);i++){var h=MetadataTokens.TypeSpecificationHandle(i);Add("typespec:"+T(h),B(md.GetTypeSpecification(h).Signature));}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.MethodSpec);i++){var h=MetadataTokens.MethodSpecificationHandle(i);var x=md.GetMethodSpecification(h);Add("methodspec:"+T(h),new{method=T(x.Method),sig=B(x.Signature)});}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.StandAloneSig);i++){var h=MetadataTokens.StandaloneSignatureHandle(i);Add("localsig:"+T(h),B(md.GetStandaloneSignature(h).Signature));}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.ModuleRef);i++){var h=MetadataTokens.ModuleReferenceHandle(i);Add("moduleref:"+T(h),S(md.GetModuleReference(h).Name));}
        for(int i=1;i<=md.GetTableRowCount(TableIndex.Constant);i++){var h=MetadataTokens.ConstantHandle(i);var x=md.GetConstant(h);Add("constant:"+T(h),new{parent=T(x.Parent),x.TypeCode,value=B(x.Value)});}
        foreach(var h in md.CustomAttributes)
        {
            var x=md.GetCustomAttribute(h);string name="";
            if(x.Constructor.Kind==HandleKind.MemberReference){var parent=md.GetMemberReference((MemberReferenceHandle)x.Constructor).Parent;if(parent.Kind==HandleKind.TypeReference)name=S(md.GetTypeReference((TypeReferenceHandle)parent).Name);}
            if(x.Parent.Kind==HandleKind.AssemblyDefinition && name=="AssemblyInformationalVersionAttribute")
                s.BuildIdentity["informational_version_blob"]=B(x.Value);
            else Add("attribute:"+T(h),new{parent=T(x.Parent),ctor=T(x.Constructor),value=B(x.Value)});
        }
        foreach(var h in md.ManifestResources){var x=md.GetManifestResource(h);Add("resource:"+T(h),new{name=S(x.Name),x.Offset,x.Attributes,implementation=T(x.Implementation)});}
        var resources=pe.PEHeaders.CorHeader!.ResourcesDirectory;
        Add("managed-resource-bytes",resources.Size==0?"":Hex(pe.GetSectionData(resources.RelativeVirtualAddress).GetContent(0,resources.Size).ToArray()));
        // Entire user-string heap proves ldstr operands still resolve to the same values.
        var metadata=pe.GetMetadata().GetContent().ToArray();
        var position=16+BitConverter.ToInt32(metadata,12);position=(position+3)&~3;
        var streams=BitConverter.ToUInt16(metadata,position+2);position+=4;
        for(int i=0;i<streams;i++)
        {
            var offset=BitConverter.ToInt32(metadata,position);var size=BitConverter.ToInt32(metadata,position+4);position+=8;
            var end=Array.IndexOf(metadata,(byte)0,position);var name=Encoding.ASCII.GetString(metadata,position,end-position);position=(end+4)&~3;
            if(name is "#US" or "#Strings")Add("heap:"+name,Hex(metadata.AsSpan(offset,size).ToArray()));
        }
        return s;
    }
}
