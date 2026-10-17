namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Host-side corrections for defects in the IEHG S-401 portrayal catalogue,
/// applied as post-load Lua scripts so the bundled catalogue stays byte-for-byte
/// upstream (see <c>content/S401/README.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>main.lua</c> loads a feature's rule file with <c>require(feature.Code)</c>
/// the first time it portrays that feature type, which runs after these scripts.
/// A patch that only redefined the rule's function would be overwritten then,
/// so each patch <c>require</c>s the rule file first (Lua caches the module, so
/// <c>main.lua</c>'s later <c>require</c> doesn't reload it) and then adjusts
/// the function.
/// </para>
/// <para>
/// Each patch acts only while the defect is present, so a fixed upstream
/// catalogue makes it a no-op. <c>S401PortrayalPatchTests</c> fails once the
/// bundled catalogue no longer has a defect, as the cue to delete its patch.
/// </para>
/// </remarks>
internal static class S401PortrayalPatches
{
    /// <summary>The product whose portrayal catalogue these patches correct.</summary>
    internal const string ProductId = "S-401";

    /// <summary>
    /// IEHG Portrayal-Catalogue issue #40: <c>WaterwayProfile.lua</c> defines
    /// <c>WaterwayAxis</c> instead of <c>WaterwayProfile</c>, so every
    /// WaterwayProfile feature fails with "attempt to call a nil value". The
    /// upstream fix renames the function; this binds the function the file
    /// defines to the <c>WaterwayProfile</c> name and restores any
    /// <c>WaterwayAxis</c> it replaced.
    /// </summary>
    internal const string WaterwayProfileEntryPoint = """
        if WaterwayProfile == nil then
            local previousAxis = WaterwayAxis
            WaterwayAxis = nil
            require 'WaterwayProfile'
            if WaterwayProfile == nil and WaterwayAxis ~= nil then
                WaterwayProfile = WaterwayAxis
            end
            WaterwayAxis = previousAxis
        end
        """;

    /// <summary>
    /// IEHG Portrayal-Catalogue issue #41: <c>CommunicationArea.lua</c> returns
    /// a viewing group it never assigns, so <c>main.lua</c> rejects every
    /// CommunicationArea with "Viewing group not returned". The rule emits only
    /// a <c>NullInstruction</c>; this supplies viewing group 38010, the value
    /// the issue proposes, when the rule returns none.
    /// </summary>
    internal const string CommunicationAreaViewingGroup = """
        require 'CommunicationArea'
        do
            local rule = CommunicationArea
            if rule ~= nil then
                CommunicationArea = function(feature, featurePortrayal, contextParameters)
                    local viewingGroup = rule(feature, featurePortrayal, contextParameters)
                    if viewingGroup == nil then
                        viewingGroup = 38010
                    end
                    return viewingGroup
                end
            end
        end
        """;

    /// <summary>The patches, in the order they run.</summary>
    internal static IReadOnlyList<string> Scripts { get; } =
    [
        WaterwayProfileEntryPoint,
        CommunicationAreaViewingGroup,
    ];
}
