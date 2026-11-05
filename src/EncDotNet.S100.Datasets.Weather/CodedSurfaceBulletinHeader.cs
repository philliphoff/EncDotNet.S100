namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// What a coded surface bulletin's header says, for example:
/// <code>
/// ASUS02 KWBC 040000
/// CODSUS
///
/// CODED SURFACE FRONTAL POSITIONS
/// NWS WEATHER PREDICTION CENTER COLLEGE PARK MD
/// 834 PM EST TUE NOV 03 2026
/// </code>
/// Every part is optional.
/// </summary>
public sealed record CodedSurfaceBulletinHeader
{
    /// <summary>WPC's name, the issuing centre when the header doesn't give one.</summary>
    public const string WpcName = "NWS WEATHER PREDICTION CENTER";

    /// <summary>The WMO abbreviated heading, for example <c>"ASUS02 KWBC 040000"</c>.</summary>
    public string? WmoHeading { get; init; }

    /// <summary>The AWIPS product identifier, for example <c>"CODSUS"</c> or <c>"CODSRP"</c>.</summary>
    public string? ProductId { get; init; }

    /// <summary>The title, for example <c>"CODED SURFACE FRONTAL POSITIONS"</c>.</summary>
    public string? Title { get; init; }

    /// <summary>The issuing office line, for example <c>"NWS WEATHER PREDICTION CENTER COLLEGE PARK MD"</c>.</summary>
    public string? IssuingCentre { get; init; }

    /// <summary>When it was issued, from the issuance line (in the office's local time, with its offset).</summary>
    public DateTimeOffset? IssuedAt { get; init; }
}
