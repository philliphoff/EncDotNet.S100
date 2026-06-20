using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Guards the localized strings introduced for the consolidated map
/// Display Settings overlay (the segmented Display base / Text / Palette
/// control). A missing or misnamed <c>.resx</c> entry would silently fall
/// back to the key name; these assertions fail fast instead.
/// </summary>
public class DisplaySettingsStringsTests
{
    public static IEnumerable<object[]> DisplaySettingsKeys()
    {
        yield return new object[] { Strings.DisplaySettings_Title, nameof(Strings.DisplaySettings_Title) };
        yield return new object[] { Strings.Tooltip_DisplaySettings, nameof(Strings.Tooltip_DisplaySettings) };
        yield return new object[] { Strings.Tooltip_CloseDisplaySettings, nameof(Strings.Tooltip_CloseDisplaySettings) };
        yield return new object[] { Strings.DisplaySettings_DisplayBaseHeader, nameof(Strings.DisplaySettings_DisplayBaseHeader) };
        yield return new object[] { Strings.DisplaySettings_TextHeader, nameof(Strings.DisplaySettings_TextHeader) };
        yield return new object[] { Strings.DisplaySettings_PaletteHeader, nameof(Strings.DisplaySettings_PaletteHeader) };
        yield return new object[] { Strings.Segment_DisplayBase, nameof(Strings.Segment_DisplayBase) };
        yield return new object[] { Strings.Segment_DisplayOther, nameof(Strings.Segment_DisplayOther) };
        yield return new object[] { Strings.Segment_TextImportant, nameof(Strings.Segment_TextImportant) };
        yield return new object[] { Strings.Segment_TextOther, nameof(Strings.Segment_TextOther) };
        yield return new object[] { Strings.Segment_TextAll, nameof(Strings.Segment_TextAll) };
    }

    [Theory]
    [MemberData(nameof(DisplaySettingsKeys))]
    public void DisplaySettingsString_ResolvesToNonEmptyValue(string value, string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(value), $"String '{key}' did not resolve to a value.");
        // A resx miss returns the key name itself; ensure we got a real value.
        Assert.NotEqual(key, value);
    }
}
