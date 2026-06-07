using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Walks the transitive managed-assembly reference closure of a root assembly
/// (resolving each reference from the root's own directory) to detect whether
/// any reachable assembly references <c>Mapsui*</c>.
/// </summary>
/// <remarks>
/// This is the proof for issue #189: the <c>EncDotNet.S100</c> facade, the
/// <c>s100</c> CLI, and the <c>EncDotNet.S100.Datasets.Pipelines</c> assembly
/// must not drag in Mapsui — even when sibling Mapsui binaries happen to sit in
/// the same output folder (the walk only follows references actually reachable
/// from the root, so a co-located but unreferenced Mapsui.dll is ignored).
/// </remarks>
internal static class MapsuiDependencyClosure
{
    /// <summary>
    /// Returns the set of <c>Mapsui*</c> assembly names reachable from
    /// <paramref name="rootAssemblyPath"/> via metadata references.
    /// </summary>
    /// <param name="rootAssemblyPath">Path to the assembly to inspect.</param>
    /// <returns>A sorted set of offending Mapsui assembly reference names (empty when clean).</returns>
    public static IReadOnlyCollection<string> FindMapsuiReferences(string rootAssemblyPath)
    {
        var fullRoot = Path.GetFullPath(rootAssemblyPath);
        var dir = Path.GetDirectoryName(fullRoot)!;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offenders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(fullRoot);

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var simpleName = Path.GetFileNameWithoutExtension(path);
            if (!visited.Add(simpleName))
            {
                continue;
            }

            // Framework / runtime assemblies are not copied next to the root and
            // are never Mapsui; skipping them keeps the walk to the app closure.
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var referenceName in ReadAssemblyReferenceNames(path))
            {
                if (referenceName.StartsWith("Mapsui", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(referenceName);
                }

                queue.Enqueue(Path.Combine(dir, referenceName + ".dll"));
            }
        }

        return offenders;
    }

    private static List<string> ReadAssemblyReferenceNames(string path)
    {
        var names = new List<string>();
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            return names;
        }

        var reader = peReader.GetMetadataReader();
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            names.Add(reader.GetString(reference.Name));
        }

        return names;
    }
}
