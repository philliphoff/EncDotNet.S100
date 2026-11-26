namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// The choices <see cref="NisToS57Translator"/> makes that the NIS doesn't
/// record: which cell to write, and its dataset records.
/// </summary>
public sealed class NisToS57Options
{
    /// <summary>The default options.</summary>
    public static NisToS57Options Default { get; } = new();

    /// <summary>
    /// The S-62 two-character agency tokens the translator knows the numeric
    /// codes of. Only NOAA's is built in; add others through
    /// <see cref="AgencyCodes"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, int> KnownAgencyCodes { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["US"] = 550 };

    /// <summary>
    /// The cell to convert: features whose <see cref="NisFeature.DatasetName"/>
    /// is this, such as <c>US5CA98M.005</c>. <see langword="null"/> converts
    /// every feature, which suits an NIS that holds a single cell.
    /// </summary>
    public string? DatasetName { get; init; }

    /// <summary>
    /// The name of the cell to write, without extension, such as <c>US5CA98M</c>.
    /// <see langword="null"/> takes it from <see cref="DatasetName"/>.
    /// </summary>
    public string? CellName { get; init; }

    /// <summary>Only features whose extent overlaps this box; <see langword="null"/> for no limit.</summary>
    public NisBounds? Bounds { get; init; }

    /// <summary>The edition number written in DSID (EDTN).</summary>
    public int EditionNumber { get; init; } = 1;

    /// <summary>The issue date written in DSID (ISDT and UADT); <see langword="null"/> for today (UTC).</summary>
    public DateOnly? IssueDate { get; init; }

    /// <summary>
    /// The producing agency written in DSID (AGEN), and given to features whose
    /// long name's agency the translator doesn't know. <see langword="null"/>
    /// takes the commonest agency of the features.
    /// </summary>
    public int? ProducingAgency { get; init; }

    /// <summary>Numeric S-62 codes for agency tokens, added to <see cref="KnownAgencyCodes"/>.</summary>
    public IReadOnlyDictionary<string, int>? AgencyCodes { get; init; }

    /// <summary>
    /// The compilation scale written in DSPM (CSCL). <see langword="null"/>
    /// takes the commonest <see cref="NisFeature.CompilationScale"/>.
    /// </summary>
    public int? CompilationScale { get; init; }

    /// <summary>
    /// The sounding datum written in DSPM (SDAT, an S-57 VERDAT value).
    /// <see langword="null"/> takes the commonest <c>VERDAT</c> of the depth
    /// features (SOUNDG, DEPARE, DEPCNT, DRGARE).
    /// </summary>
    public int? SoundingDatum { get; init; }

    /// <summary>
    /// The vertical datum written in DSPM (VDAT, an S-57 VERDAT value).
    /// <see langword="null"/> takes the commonest <c>VERDAT</c> of the other features.
    /// </summary>
    public int? VerticalDatum { get; init; }

    /// <summary>The most soundings one SOUNDG object holds.</summary>
    public int MaxSoundingsPerObject { get; init; } = 500;

    /// <summary>
    /// Whether to add an M_COVR (CATCOV 1) over the extent of the data when
    /// the cell has none, as an ENC needs one.
    /// </summary>
    public bool SynthesiseCoverage { get; init; } = true;
}
