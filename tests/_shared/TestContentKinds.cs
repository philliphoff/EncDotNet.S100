using EncDotNet.S100.Core.Caching;

namespace EncDotNet.S100.Testing;

/// <summary>
/// Opens a content store kind whose folder is exactly <c>directory</c>: the
/// store is rooted at its parent and the kind is named after its leaf, so a
/// test that looks for files in <c>directory</c> still finds them.
/// </summary>
internal static class TestContentKinds
{
    public static ContentKind Open(string directory, long maxBytes, ContentKindOptions options)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var store = new ContentStore(Path.GetDirectoryName(full)!);
        return store.Open(Path.GetFileName(full), maxBytes, options);
    }
}
