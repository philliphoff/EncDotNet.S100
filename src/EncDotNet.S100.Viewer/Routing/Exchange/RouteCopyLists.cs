using System.Globalization;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Routing.Exchange;

/// <summary>Lines the route formats share in the Copy to edit dialog's lists.</summary>
internal static class RouteCopyLists
{
    // "Speeds → Passage 12 kn, 2 leg overrides", or with no shared value
    // "Speeds → 9 leg overrides"; nothing when the file has none.
    public static void AddPassage(List<string> keeps, Route route, string label, RoutePassageField field, Func<double, string> format)
    {
        var overrides = Enumerable.Range(0, route.Legs.Count).Count(i => route.IsLegOverridden(i, field));
        var culture = CultureInfo.CurrentCulture;
        if (route.Passage.Get(field) is { } value)
        {
            keeps.Add(overrides switch
            {
                0 => string.Format(culture, Strings.Copy_KeepsPassageFormat, label, format(value)),
                1 => string.Format(culture, Strings.Copy_KeepsPassageOverrideFormat, label, format(value)),
                _ => string.Format(culture, Strings.Copy_KeepsPassageOverridesFormat, label, format(value), overrides),
            });
        }
        else if (overrides > 0)
        {
            keeps.Add(overrides == 1
                ? string.Format(culture, Strings.Copy_KeepsOverrideFormat, label)
                : string.Format(culture, Strings.Copy_KeepsOverridesFormat, label, overrides));
        }
    }
}
