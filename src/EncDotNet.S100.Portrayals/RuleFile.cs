namespace EncDotNet.S100.Portrayals;

/// <summary>
/// A portrayal rule file declared in the catalogue's <c>rules</c> section —
/// an XSLT template or Lua script that maps features to drawing instructions.
/// Files are loaded from the catalogue's <c>Rules/</c> folder.
/// </summary>
public sealed class RuleFile
{
    /// <summary>Rule file identifier, from <c>ruleFile/@id</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The rule file's <c>description</c> block.</summary>
    public required Description Description { get; init; }

    /// <summary>File name relative to the <c>Rules/</c> folder, from <c>fileName</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>Kind of file, from <c>fileType</c> as written in the catalogue (e.g. <c>Rule</c>).</summary>
    public required string FileType { get; init; }

    /// <summary>Rule language, from <c>fileFormat</c> (e.g. <c>XSLT</c> or <c>LUA</c>).</summary>
    public required string FileFormat { get; init; }

    /// <summary>
    /// Role of the file in rule execution, from <c>ruleType</c>:
    /// <c>TopLevelTemplate</c> marks an entry point; other values (e.g.
    /// <c>SubTemplate</c>) are included by an entry point. XSLT catalogues
    /// only execute <c>TopLevelTemplate</c> files directly.
    /// </summary>
    public required string RuleType { get; init; }
}
