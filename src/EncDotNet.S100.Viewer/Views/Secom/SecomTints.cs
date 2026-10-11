using Avalonia.Controls;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Views.Secom;

/// <summary>
/// The tone tints SECOM status blocks and tags use (#883 C3): each tone's
/// colour at 7 % for the fill and 36 % for the border, from the live theme.
/// </summary>
internal static class SecomTints
{
    /// <summary>Sets <c>Secom{Success,Warning,Destructive}{Fill,Border}Brush</c> in <paramref name="control"/>'s resources.</summary>
    public static void Apply(Control control)
    {
        var theme = control.ActualThemeVariant;
        foreach (var (tone, fallback) in new[] { ("Success", Colors.SeaGreen), ("Warning", Colors.DarkOrange), ("Destructive", Colors.IndianRed) })
        {
            var color = control.TryFindResource(tone + "Color", theme, out var value) && value is Color c ? c : fallback;
            control.Resources[$"Secom{tone}FillBrush"] = new SolidColorBrush(color, 0.07);
            control.Resources[$"Secom{tone}BorderBrush"] = new SolidColorBrush(color, 0.36);
        }
    }
}
