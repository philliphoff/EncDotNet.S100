using System.Net;

namespace EncDotNet.S100.Cli.Reference;

/// <summary>HTML text for the exported chart reference (R14, #1723).</summary>
internal static class Html
{
    /// <summary>Text, encoded for an element or a quoted attribute.</summary>
    /// <param name="text">The text.</param>
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
