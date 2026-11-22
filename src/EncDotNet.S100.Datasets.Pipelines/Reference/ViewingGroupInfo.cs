namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A viewing group of the portrayal catalogue, with the display category it
/// falls in and the Chart 1 section its description names.
/// </summary>
/// <param name="Id">The viewing group id, such as <c>27020</c>.</param>
/// <param name="Name">The viewing group's name, such as <c>Beacon (BCNxxx)</c>, or <see langword="null"/> when the catalogue doesn't declare it.</param>
/// <param name="Description">
/// The viewing group's description. S-101's names the INT 1 sections, such
/// as <c>Standard: P, Q, R, S - Buoys and Beacons, Lights, Fog signals, RADAR</c>.
/// </param>
/// <param name="DisplayModeId">
/// The id of the most restrictive display mode that shows the group, such
/// as <c>DisplayBase</c>, <c>StandardDisplay</c> or <c>OtherInformation</c>,
/// or <see langword="null"/> when no display mode shows it.
/// </param>
/// <param name="DisplayModeName">The display mode's name, such as <c>Standard Display</c>.</param>
public sealed record ViewingGroupInfo(
    string Id,
    string? Name,
    string? Description,
    string? DisplayModeId,
    string? DisplayModeName);
