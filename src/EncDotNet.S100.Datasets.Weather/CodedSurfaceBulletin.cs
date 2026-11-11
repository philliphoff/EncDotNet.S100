using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// One WPC coded surface bulletin, read by
/// <see cref="CodedSurfaceBulletinReader"/>: the coded surface analysis
/// (<c>CODSUS</c>, standard or high resolution) or a coded forecast bulletin
/// (for example <c>CODSRP</c>), with the surface analyses it holds.
/// </summary>
/// <remarks>
/// WPC's bulletins cover WPC's domain only, North America and the waters
/// around it (design note <c>vector-weather.md</c> V36): the absence of a front
/// there means WPC analysed none, while elsewhere nothing is known.
/// <see cref="Domain"/> records it.
/// </remarks>
public sealed class CodedSurfaceBulletin
{
    /// <summary>
    /// WPC's domain: the Northern Hemisphere from 180° to 0°. The bulletins'
    /// points lie in it (from the Equator to the Arctic, from the Date Line
    /// to the Greenwich meridian), and WPC's charts don't cover anything
    /// beyond it.
    /// </summary>
    public static BoundingBox WpcDomain { get; } = new(0, -180, 90, 0);

    /// <summary>Creates a bulletin.</summary>
    /// <param name="kind">Analysis or forecast.</param>
    /// <param name="resolution">How precisely it gives positions.</param>
    /// <param name="analyses">Its analyses, one per valid time, in the bulletin's order. The list is copied.</param>
    /// <param name="header">What its header says.</param>
    /// <param name="skippedCodes">Feature codes the reader doesn't know, skipped with their points. The list is copied.</param>
    public CodedSurfaceBulletin(
        CodedSurfaceBulletinKind kind,
        CodedSurfaceBulletinResolution resolution,
        IEnumerable<SurfaceAnalysis> analyses,
        CodedSurfaceBulletinHeader header,
        IEnumerable<string> skippedCodes)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(skippedCodes);
        Kind = kind;
        Resolution = resolution;
        Analyses = analyses.ToArray();
        Header = header;
        SkippedCodes = skippedCodes.ToArray();
    }

    /// <summary>Whether it's an analysis or a forecast.</summary>
    public CodedSurfaceBulletinKind Kind { get; }

    /// <summary>How precisely it gives positions.</summary>
    public CodedSurfaceBulletinResolution Resolution { get; }

    /// <summary>
    /// Its analyses, one per valid time, in the bulletin's order: one for the
    /// coded surface analysis, one per forecast time for a forecast bulletin.
    /// </summary>
    public IReadOnlyList<SurfaceAnalysis> Analyses { get; }

    /// <summary>What its header says. Every part is optional: WPC's own examples have no header.</summary>
    public CodedSurfaceBulletinHeader Header { get; }

    /// <summary>
    /// Feature codes the reader doesn't know, each skipped with the points
    /// after it up to the next code it knows. Empty for every bulletin WPC
    /// documents.
    /// </summary>
    public IReadOnlyList<string> SkippedCodes { get; }

    /// <summary>The area the bulletin covers: <see cref="WpcDomain"/>.</summary>
    public BoundingBox Domain => WpcDomain;

    /// <summary>The issuing centre, from the header, or WPC's name when the header doesn't give one.</summary>
    public string IssuingCentre => Header.IssuingCentre ?? CodedSurfaceBulletinHeader.WpcName;
}
