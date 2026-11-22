using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The viewer's UI font is the bundled Inter on every platform. Without a
/// default family name Avalonia uses the platform's default typeface, which on
/// Linux is Skia's fontconfig fallback (often DejaVu Serif).
/// </summary>
public sealed class DefaultFontTests
{
    [Fact]
    public void Font_options_name_the_bundled_Inter()
    {
        Assert.Equal("fonts:Inter#Inter", ViewerCommand.CreateFontOptions().DefaultFamilyName);
    }

    [AvaloniaFact]
    public void The_default_font_family_is_Inter()
    {
        var fonts = FontManager.Current;

        Assert.Equal("Inter", fonts.DefaultFontFamily.Name);
        Assert.True(fonts.TryGetGlyphTypeface(new Typeface(FontFamily.Default), out var glyphs));
        Assert.Equal("Inter", glyphs.FamilyName);
    }

    /// <summary>
    /// A character Inter lacks is drawn from whatever fallback font the platform
    /// picks, which differs between macOS, Windows and Linux (on Linux a "▾"
    /// came out as a stack of bars). UI text uses only characters Inter has.
    /// </summary>
    [AvaloniaFact]
    public void Every_UI_string_uses_only_characters_Inter_has()
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(FontFamily.Default), out var inter));
        var resources = new ResourceManager("EncDotNet.S100.Viewer.Resources.Strings", typeof(ViewerCommand).Assembly)
            .GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        Assert.NotNull(resources);

        var missing = new List<string>();
        foreach (DictionaryEntry entry in resources)
        {
            if (entry.Value is not string text)
                continue;
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Value < 0x80 || Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
                    continue;
                if (!inter.CharacterToGlyphMap.ContainsGlyph(rune.Value))
                    missing.Add($"{entry.Key}: U+{rune.Value:X4} '{rune}'");
            }
        }

        Assert.Empty(missing);
    }
}
