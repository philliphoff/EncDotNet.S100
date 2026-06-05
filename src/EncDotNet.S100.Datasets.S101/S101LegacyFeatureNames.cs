using System.Collections.Frozen;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Maps legacy (pre-2.0.0) S-101 feature class names to their S-101 Edition
/// 2.0.0 equivalents so that datasets authored against an earlier edition of
/// the S-101 Feature Catalogue can be portrayed with the bundled Edition 2.0.0
/// Portrayal Catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Between S-101 1.x and 2.0.0 several compound feature class names were
/// re-ordered (for example <c>BuoyLateral</c> became <c>LateralBuoy</c> and
/// <c>BeaconCardinal</c> became <c>CardinalBeacon</c>). Because the S-100
/// Part 9A Lua portrayal dispatcher (<c>main.lua</c>) loads and invokes a rule
/// module whose file name and global function name both equal the feature's
/// class code (<c>require(feature.Code)</c> then <c>_G[feature.Code](...)</c>),
/// a dataset that reports a legacy class code finds no matching 2.0.0 rule
/// module and falls back to DEFAULT symbology.
/// </para>
/// <para>
/// Simple attribute names are stable across these editions, so re-mapping only
/// the feature <em>class</em> name restores correct symbology. The mapping is
/// applied at the portrayal boundary only (see
/// <see cref="S101LuaDataProvider"/>); feature names are left as-authored
/// everywhere else.
/// </para>
/// <para>
/// <c>MooringWarpingFacility</c> was structurally removed in S-101 2.0.0 and
/// split into several distinct feature classes, so it is mapped conditionally
/// on the legacy <c>categoryOfMooringWarpingFacility</c> enumerant. Categories
/// without a clean 2.0.0 equivalent are left unchanged (and therefore portrayed
/// with DEFAULT symbology). The conditional targets are approximations: only
/// the class name is aliased, not the attributes the 2.0.0 rule reads (for
/// example <c>Dolphin.lua</c> inspects <c>categoryOfDolphin</c>), so the
/// resulting symbology may be generic. A target rule that rejects the legacy
/// feature's geometric primitive simply errors inside the dispatcher's
/// <c>pcall</c> and falls back to DEFAULT — i.e. no worse than today.
/// </para>
/// <para>References: S-101 Feature Catalogue (Edition 1.x and 2.0.0); S-100
/// Part 9A (Portrayal — Lua rules engine).</para>
/// </remarks>
public static class S101LegacyFeatureNames
{
    /// <summary>The legacy attribute used to discriminate the removed
    /// <c>MooringWarpingFacility</c> feature class.</summary>
    private const string MooringWarpingFacility = "MooringWarpingFacility";
    private const string MooringWarpingCategoryAttribute = "categoryOfMooringWarpingFacility";

    /// <summary>
    /// Legacy S-101 1.x feature class name → S-101 2.0.0 feature class name.
    /// Keys are matched case-insensitively. Only names that do not exist in
    /// 2.0.0 are present, so genuine 2.0.0 datasets are never altered.
    /// </summary>
    private static readonly FrozenDictionary<string, string> SimpleRenames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Buoys (S-101 2.0.0 reverses the compound order).
            ["BuoyCardinal"] = "CardinalBuoy",
            ["BuoyInstallation"] = "InstallationBuoy",
            ["BuoyIsolatedDanger"] = "IsolatedDangerBuoy",
            ["BuoyLateral"] = "LateralBuoy",
            ["BuoySafeWater"] = "SafeWaterBuoy",
            ["BuoySpecialPurposeGeneral"] = "SpecialPurposeGeneralBuoy",
            // The "new danger marking" buoy was renamed "emergency wreck marking".
            ["BuoyNewDangerMarking"] = "EmergencyWreckMarkingBuoy",

            // Beacons (S-101 2.0.0 reverses the compound order).
            ["BeaconCardinal"] = "CardinalBeacon",
            ["BeaconIsolatedDanger"] = "IsolatedDangerBeacon",
            ["BeaconLateral"] = "LateralBeacon",
            ["BeaconSafeWater"] = "SafeWaterBeacon",
            ["BeaconSpecialPurposeGeneral"] = "SpecialPurposeGeneralBeacon",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Legacy <c>categoryOfMooringWarpingFacility</c> enumerant (stringified
    /// integer code) → S-101 2.0.0 feature class name. Categories without a
    /// clean 2.0.0 equivalent (tie-up wall = 4, chain/wire/cable = 6) are
    /// intentionally absent so the feature is left on DEFAULT symbology.
    /// </summary>
    private static readonly FrozenDictionary<string, string> MooringWarpingByCategory =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = "Dolphin",     // dolphin
            ["2"] = "Dolphin",     // deviation dolphin (approximated as a dolphin)
            ["3"] = "Bollard",     // bollard
            ["5"] = "Pile",        // post or pile
            ["7"] = "MooringBuoy", // mooring buoy
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the S-101 2.0.0 feature class name equivalent to
    /// <paramref name="featureCode"/>, or <paramref name="featureCode"/>
    /// unchanged when it is already a 2.0.0 name or has no clean mapping.
    /// </summary>
    /// <param name="featureCode">
    /// The feature class code reported by the dataset (from its embedded
    /// feature-type-name catalogue).
    /// </param>
    /// <param name="categoryLookup">
    /// Optional callback that resolves the first value of a simple attribute on
    /// the feature being normalized, used to disambiguate the removed
    /// <c>MooringWarpingFacility</c> class via
    /// <c>categoryOfMooringWarpingFacility</c>. When <see langword="null"/>,
    /// <c>MooringWarpingFacility</c> is left unchanged.
    /// </param>
    /// <returns>The normalized feature class name.</returns>
    public static string Normalize(string featureCode, Func<string, string?>? categoryLookup = null)
    {
        if (string.IsNullOrEmpty(featureCode))
        {
            return featureCode;
        }

        if (SimpleRenames.TryGetValue(featureCode, out var renamed))
        {
            return renamed;
        }

        if (categoryLookup is not null &&
            string.Equals(featureCode, MooringWarpingFacility, StringComparison.OrdinalIgnoreCase))
        {
            var category = categoryLookup(MooringWarpingCategoryAttribute);
            if (category is not null &&
                MooringWarpingByCategory.TryGetValue(category.Trim(), out var mapped))
            {
                return mapped;
            }
        }

        return featureCode;
    }
}
