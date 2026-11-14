using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Aether.Installer;

public enum ModKind
{
    /// <summary>Managed assembly carrying <c>[AetherMod]</c>: goes to <c>Aether/Mods</c>.</summary>
    AetherMod,

    /// <summary>Managed assembly carrying <c>[BepInPlugin]</c>: goes to <c>BepInEx/plugins</c>.</summary>
    BepInExPlugin,

    /// <summary>Managed assembly without a mod attribute: a shared library that follows its package.</summary>
    Library,

    /// <summary>Not a managed .NET assembly (native library, garbage, truncated file).</summary>
    NotManaged
}

public sealed record ModInfo(
    string FilePath,
    ModKind Kind,
    string? Id,
    string? Version,
    string? Depends,
    string? Breaks)
{
    public string FileName => Path.GetFileName(FilePath);
}

/// <summary>
/// Classifies a DLL by reading its metadata. The assembly is never loaded or executed, so inspecting
/// an untrusted download is safe.
/// </summary>
public static class ModInspector
{
    public static ModInfo Inspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return new ModInfo(path, ModKind.NotManaged, null, null, null, null);

            MetadataReader md = pe.GetMetadataReader();
            foreach (CustomAttributeHandle handle in md.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attr = md.GetCustomAttribute(handle);
                string? typeName = AttributeTypeName(md, attr);
                if (typeName == null) continue;

                if (typeName == "Aether.Loader.Discovery.AetherModAttribute" ||
                    typeName.EndsWith(".AetherModAttribute", StringComparison.Ordinal))
                {
                    var (fixedArgs, named) = DecodeStrings(md, attr);
                    named.TryGetValue("Depends", out string? depends);
                    named.TryGetValue("Breaks", out string? breaks);
                    return new ModInfo(path, ModKind.AetherMod,
                        fixedArgs.ElementAtOrDefault(0), fixedArgs.ElementAtOrDefault(1), depends, breaks);
                }
            }

            // [BepInPlugin] is applied to a class, not to the assembly.
            foreach (TypeDefinitionHandle typeHandle in md.TypeDefinitions)
            {
                TypeDefinition type = md.GetTypeDefinition(typeHandle);
                foreach (CustomAttributeHandle handle in type.GetCustomAttributes())
                {
                    CustomAttribute attr = md.GetCustomAttribute(handle);
                    string? typeName = AttributeTypeName(md, attr);
                    if (typeName == "BepInEx.BepInPlugin")
                    {
                        var (fixedArgs, _) = DecodeStrings(md, attr);
                        // BepInPlugin(guid, name, version)
                        return new ModInfo(path, ModKind.BepInExPlugin,
                            fixedArgs.ElementAtOrDefault(0), fixedArgs.ElementAtOrDefault(2), null, null);
                    }
                }
            }

            return new ModInfo(path, ModKind.Library, null, null, null, null);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException)
        {
            return new ModInfo(path, ModKind.NotManaged, null, null, null, null);
        }
    }

    private static string? AttributeTypeName(MetadataReader md, CustomAttribute attr)
    {
        switch (attr.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                MemberReference member = md.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                if (member.Parent.Kind == HandleKind.TypeReference)
                {
                    TypeReference tr = md.GetTypeReference((TypeReferenceHandle)member.Parent);
                    return Join(md.GetString(tr.Namespace), md.GetString(tr.Name));
                }
                return null;
            case HandleKind.MethodDefinition:
                MethodDefinition method = md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                TypeDefinition td = md.GetTypeDefinition(method.GetDeclaringType());
                return Join(md.GetString(td.Namespace), md.GetString(td.Name));
            default:
                return null;
        }
    }

    private static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    /// <summary>
    /// Reads the leading string constructor arguments and string-valued named properties of an
    /// attribute blob (ECMA-335 II.23.3). Anything that is not a string stops the decoding, which is
    /// enough for <c>AetherMod(id, version, Depends=...)</c> and <c>BepInPlugin(guid, name, version)</c>.
    /// </summary>
    private static (List<string?> Fixed, Dictionary<string, string?> Named) DecodeStrings(
        MetadataReader md, CustomAttribute attr)
    {
        var fixedArgs = new List<string?>();
        var named = new Dictionary<string, string?>(StringComparer.Ordinal);

        BlobReader blob = md.GetBlobReader(attr.Value);
        if (blob.Length < 4 || blob.ReadUInt16() != 1) return (fixedArgs, named);

        int paramCount = CountCtorParams(md, attr);
        for (int i = 0; i < paramCount; i++)
        {
            if (blob.RemainingBytes == 0) return (fixedArgs, named);
            // A string argument is a SerString; a null string is encoded as 0xFF.
            fixedArgs.Add(blob.ReadSerializedString());
        }

        if (blob.RemainingBytes < 2) return (fixedArgs, named);
        int namedCount = blob.ReadUInt16();
        for (int i = 0; i < namedCount && blob.RemainingBytes > 0; i++)
        {
            byte kind = blob.ReadByte();           // 0x53 field, 0x54 property
            byte type = blob.ReadByte();           // 0x0E = string
            string? name = blob.ReadSerializedString();
            if (kind != 0x54 || type != 0x0E || name == null) break;
            named[name] = blob.ReadSerializedString();
        }

        return (fixedArgs, named);
    }

    private static int CountCtorParams(MetadataReader md, CustomAttribute attr)
    {
        BlobHandle signature = attr.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Signature,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor).Signature,
            _ => default
        };
        if (signature.IsNil) return 0;

        BlobReader sig = md.GetBlobReader(signature);
        sig.ReadByte();                       // calling convention
        return sig.ReadCompressedInteger();   // parameter count
    }
}
