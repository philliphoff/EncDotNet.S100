using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Guards against a central package pin that moves an Avalonia dependency to
/// a version whose API no longer matches what Avalonia was compiled against.
/// The X11 and FreeDesktop backends only load on Linux, so a mismatch there
/// builds and passes everywhere else, then fails at startup with a
/// <see cref="TypeLoadException"/> (as <c>Tmds.DBus.Protocol</c> 0.94.2 did
/// under Avalonia 12, which needs 0.92.0).
/// </summary>
public sealed class AvaloniaDependencyTypeTests
{
    [Fact]
    public void Avalonia_TypeReferences_ResolveInDeployedAssemblies()
    {
        var baseDir = AppContext.BaseDirectory;
        var avaloniaAssemblies = Directory.GetFiles(baseDir, "Avalonia*.dll");
        Assert.Contains(avaloniaAssemblies, p => Path.GetFileName(p) == "Avalonia.FreeDesktop.dll");

        var exportedTypes = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var path in avaloniaAssemblies)
        {
            using var pe = new PEReader(File.OpenRead(path));
            var reader = pe.GetMetadataReader();

            foreach (var handle in reader.TypeReferences)
            {
                var typeRef = reader.GetTypeReference(handle);
                if (typeRef.ResolutionScope.Kind != HandleKind.AssemblyReference)
                    continue; // nested types resolve through their declaring type

                var target = reader.GetString(
                    reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope).Name);
                if (!exportedTypes.TryGetValue(target, out var types))
                    exportedTypes[target] = types = ReadTypeNames(Path.Combine(baseDir, target + ".dll"));
                if (types is null)
                    continue; // framework or not deployed alongside; not ours to check

                var name = reader.GetString(typeRef.Name);
                var ns = reader.GetString(typeRef.Namespace);
                var fullName = ns.Length == 0 ? name : $"{ns}.{name}";
                if (!types.Contains(fullName))
                    missing.Add($"{Path.GetFileName(path)} -> {target}: {fullName}");
            }
        }

        Assert.True(missing.Count == 0,
            "Avalonia references types missing from the deployed dependency versions " +
            "(check Directory.Packages.props pins against Avalonia's nuspec dependencies):\n" +
            string.Join("\n", missing.Distinct()));
    }

    /// <summary>
    /// Top-level types defined in, or forwarded from, the assembly at
    /// <paramref name="path"/>; <see langword="null"/> when it isn't deployed.
    /// </summary>
    private static HashSet<string>? ReadTypeNames(string path)
    {
        if (!File.Exists(path))
            return null;

        using var pe = new PEReader(File.OpenRead(path));
        var reader = pe.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handle in reader.TypeDefinitions)
        {
            var def = reader.GetTypeDefinition(handle);
            if (def.GetDeclaringType().IsNil)
                names.Add(FullName(reader, def.Namespace, def.Name));
        }

        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            names.Add(FullName(reader, exported.Namespace, exported.Name));
        }

        return names;
    }

    private static string FullName(MetadataReader reader, StringHandle ns, StringHandle name)
    {
        var n = reader.GetString(ns);
        return n.Length == 0 ? reader.GetString(name) : $"{n}.{reader.GetString(name)}";
    }
}
