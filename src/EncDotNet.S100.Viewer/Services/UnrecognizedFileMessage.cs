using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The warning shown when no product recognises a file the user opened. When
/// the factory can say why (an HDF5 file with no <c>productSpecification</c>,
/// one naming an unsupported product, or one that can't be read as HDF5,
/// #1092) the toast names the file and gives that reason, the same text the
/// CLI and MCP give; otherwise it names the file type, as it always has.
/// </summary>
internal static class UnrecognizedFileMessage
{
    /// <summary>The toast text for <paramref name="path"/>.</summary>
    /// <param name="path">The file no product recognised.</param>
    public static string For(string path) =>
        DatasetPipelineFactory.DescribeUnrecognizedDataset(path) is { } reason
            ? string.Format(CultureInfo.CurrentCulture, Strings.Status_UnrecognizedDataset, Path.GetFileName(path), reason)
            : string.Format(CultureInfo.CurrentCulture, Strings.Status_UnrecognizedFileType, Path.GetExtension(path));
}
