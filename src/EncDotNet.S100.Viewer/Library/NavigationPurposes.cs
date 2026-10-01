using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>Names a remote S-100 dataset's navigation purpose and grid resolution ("Port 4 m").</summary>
internal static class NavigationPurposes
{
    /// <summary>"Port 4 m": the purpose and a grid resolution in metres, or the purpose alone.</summary>
    public static string Label(string purpose, double? metres) => metres is { } m
        ? string.Format(CultureInfo.CurrentCulture, Strings.Wizard_PurposeResolutionFormat, Name(purpose), m)
        : Name(purpose);

    /// <summary>The label for <paramref name="item"/>, or <see langword="null"/> when it declares no purpose.</summary>
    public static string? Of(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.Properties.TryGetValue(RemoteS100Catalogue.NavigationPurposeProperty, out var purpose))
            return null;
        var metres = item.Properties.TryGetValue(RemoteS100Catalogue.GridResolutionProperty, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : (double?)null;
        return Label(purpose, metres);
    }

    /// <summary>The catalogue's purpose code as a name ("port" → "Port").</summary>
    public static string Name(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..];
}
