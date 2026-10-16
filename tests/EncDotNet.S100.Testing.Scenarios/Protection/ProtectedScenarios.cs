using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Protection;

/// <summary>
/// Protected (S-100 Part 15) scenarios (#943, absorbing #768): small
/// exchange sets over Synthetic Sound with permits that exercise each
/// outcome a reader can reach. Write one with
/// <see cref="ProtectedScenarioGenerator.Generate"/>.
/// </summary>
/// <remarks>
/// The five <c>Protected-*</c> sets #768 asks for are checked in under
/// <c>tests/datasets/ExchangeSets/</c>, generated with the default seed.
/// </remarks>
public static class ProtectedScenarios
{
    /// <summary>
    /// The plain data the basic, hardware-ID and issuer sets protect: the
    /// coastal S-101 cell and an S-102 surface over the channel.
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition Small(ulong seed = SyntheticScenarios.DefaultSeed) =>
        SyntheticScenarios.SpitAcrossScales(seed) with
        {
            Cells = [SyntheticScenarios.SyntheticSoundCells[1]],
            Stations = null,
            WaterLevelGrids = [],
        };

    /// <summary>
    /// "Protected-Basic": the coastal S-101 cell encrypted with a valid
    /// permit, next to an unprotected, unsigned S-102 surface.
    /// </summary>
    /// <returns>The protection.</returns>
    public static ProtectionSpec Basic() => new("Protected-Basic", [new DatasetProtection(CoastalCell)]);

    /// <summary>
    /// "Protected-WrongHardwareId": as <see cref="Basic"/>, but the cell key
    /// is wrapped for another system, so the permit passes and decryption
    /// fails.
    /// </summary>
    /// <returns>The protection.</returns>
    public static ProtectionSpec WrongHardwareId() =>
        new("Protected-WrongHardwareId", [new DatasetProtection(CoastalCell) { Permit = PermitKind.WrongHardwareId }]);

    /// <summary>
    /// "Protected-UntrustedIssuer": as <see cref="Basic"/>, but
    /// <c>PERMIT.SIGN</c>'s certificate comes from a Scheme Administrator the
    /// test system doesn't trust.
    /// </summary>
    /// <returns>The protection.</returns>
    public static ProtectionSpec UntrustedIssuer() =>
        Basic() with { Name = "Protected-UntrustedIssuer", PermitSigning = PermitSigning.UntrustedSchemeAdministrator };

    /// <summary>
    /// "Protected-PermitOnly": as <see cref="Basic"/>, but the permit is
    /// delivered apart from the data, in <c>TEST-ONLY/separate-permit/</c>.
    /// </summary>
    /// <returns>The protection.</returns>
    public static ProtectionSpec PermitOnly() => Basic() with { Name = "Protected-PermitOnly", PermitOnly = true };

    /// <summary>
    /// <see cref="Basic"/> with <c>PERMIT.XML</c> signed as
    /// <paramref name="signing"/> says.
    /// </summary>
    /// <param name="signing">How to sign the permit.</param>
    /// <returns>The protection.</returns>
    public static ProtectionSpec Signed(PermitSigning signing) =>
        Basic() with { Name = "protected-permit-" + signing.ToString().ToLowerInvariant(), PermitSigning = signing };

    /// <summary>
    /// A protected update (<c>.001</c>) of the coastal cell whose base isn't
    /// in the set: <c>BaseDatasetMissing</c>.
    /// </summary>
    /// <returns>The protection.</returns>
    public static ProtectionSpec UpdateWithoutBase() =>
        new("protected-update-without-base", [new DatasetProtection(CoastalCell) { AsUpdateWithoutBase = true }]);

    /// <summary>
    /// <see cref="Basic"/> with its catalogue signature written without
    /// <c>dataStatus</c> (#754), and the S-102 surface signed the same way
    /// but not encrypted.
    /// </summary>
    /// <returns>The protection.</returns>
    public static ProtectionSpec SignaturesWithoutDataStatus() => new(
        "protected-signatures-without-datastatus",
        [
            new DatasetProtection(CoastalCell) { OmitSignatureDataStatus = true },
            new DatasetProtection("102ZZ00SPIT") { Encrypt = false, OmitSignatureDataStatus = true },
        ]);

    /// <summary>
    /// The plain data of <see cref="Mixed"/>: the harbour split into four
    /// quadrant cells, the coastal cell, an S-102 surface and S-104 stations.
    /// </summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition MixedData(ulong seed = SyntheticScenarios.DefaultSeed) =>
        SyntheticScenarios.SpitAcrossScales(seed) with
        {
            Cells = [.. Quadrants, SyntheticScenarios.SyntheticSoundCells[1]],
            WaterLevelGrids = [],
        };

    /// <summary>
    /// "Protected-Mixed": one outcome per dataset, over two products, with
    /// protected and unprotected data side by side.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Dataset</term><description>Outcome</description></listheader>
    /// <item><term><c>101ZZ00MIXSW</c></term><description><c>Allowed</c></description></item>
    /// <item><term><c>101ZZ00MIXSE</c></term><description><c>PermitNotFound</c></description></item>
    /// <item><term><c>101ZZ00MIXNW</c></term><description><c>EditionMismatch</c></description></item>
    /// <item><term><c>101ZZ00MIXNE</c></term><description><c>IssuedAfterExpiry</c></description></item>
    /// <item><term><c>101ZZ00SPIT3</c></term><description><c>Allowed</c>, expiring 30 days after issue</description></item>
    /// <item><term><c>102ZZ00SPIT</c></term><description><c>IssueDateMismatch</c></description></item>
    /// <item><term><c>104ZZ00SPIT_STN</c></term><description><c>NotProtected</c>, unsigned</description></item>
    /// </list>
    /// </remarks>
    /// <returns>The protection.</returns>
    public static ProtectionSpec Mixed() => new(
        "Protected-Mixed",
        [
            new DatasetProtection("101ZZ00MIXSW"),
            new DatasetProtection("101ZZ00MIXSE") { Permit = PermitKind.Omitted },
            new DatasetProtection("101ZZ00MIXNW") { Permit = PermitKind.WrongEdition },
            new DatasetProtection("101ZZ00MIXNE") { Permit = PermitKind.ExpiredBeforeIssue },
            new DatasetProtection(CoastalCell) { Permit = PermitKind.ExpiringSoon },
            new DatasetProtection("102ZZ00SPIT") { Permit = PermitKind.WrongIssueDate },
        ]);

    /// <summary>The checked-in sets: each name, its plain data and its protection.</summary>
    public static IReadOnlyList<(ScenarioDefinition Data, ProtectionSpec Protection)> CheckedIn(ulong seed = SyntheticScenarios.DefaultSeed) =>
    [
        (Small(seed), Basic()),
        (MixedData(seed), Mixed()),
        (Small(seed), WrongHardwareId()),
        (Small(seed), UntrustedIssuer()),
        (Small(seed), PermitOnly()),
    ];

    private const string CoastalCell = "101ZZ00SPIT3";

    // Harbour-band quadrants of the harbour cell, sampled at 100 m so they stay small.
    private static readonly S101CellSpec[] Quadrants =
    [
        Quadrant("101ZZ00MIXSW", "south-west", new LocalRect(0, 0, 3000, 3000), 11),
        Quadrant("101ZZ00MIXSE", "south-east", new LocalRect(3000, 0, 6000, 3000), 12),
        Quadrant("101ZZ00MIXNW", "north-west", new LocalRect(0, 3000, 3000, 6000), 13),
        Quadrant("101ZZ00MIXNE", "north-east", new LocalRect(3000, 3000, 6000, 6000), 14),
    ];

    private static S101CellSpec Quadrant(string name, string where, LocalRect extent, ushort subdivision) => new(
        name,
        $"Synthetic Sound harbour {where} (SYNTHETIC, fictitious; not for navigation)",
        extent,
        GridSpacing: 100,
        GeneralisationRadius: 0,
        DepthContours: [0, 2, 5, 10, 20],
        SoundingSpacing: 500,
        MinimumDisplayScale: 22000,
        OptimumDisplayScale: 12000,
        MaximumDisplayScale: 3000,
        FoidSubdivision: subdivision);
}
