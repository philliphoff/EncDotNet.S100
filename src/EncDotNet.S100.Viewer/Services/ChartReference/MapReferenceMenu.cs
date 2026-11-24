using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>A feature type the map's <b>Look up in reference</b> offers: its entry and its name.</summary>
/// <param name="Key">The feature type's entry.</param>
/// <param name="Name">The name shown, as the pick's hit list shows it.</param>
internal sealed record MapReferenceItem(ReferenceKey Key, string Name);

/// <summary>
/// The map's context menu (symbol dictionary design note §4.6, R9b, #1681):
/// <b>Look up in reference</b> for the features under the pointer, opening
/// each one's feature type entry in the Reference tab. One feature type gives
/// one item; several give a submenu in the pick's order, as its hit list
/// shows them.
/// </summary>
/// <remarks>
/// A feature type the chart reference doesn't hold gets no item, and with
/// none to offer there is no menu: it holds nothing else yet.
/// </remarks>
internal sealed class MapReferenceMenu
{
    /// <summary>The menu's automation id.</summary>
    public const string MenuAutomationId = "Map.ContextMenu";

    /// <summary>The automation id of <b>Look up in reference</b>.</summary>
    public const string AutomationId = "Map.ContextMenu.LookUpInReference";

    /// <summary>The automation id of a feature in its submenu.</summary>
    public const string ItemAutomationId = "Map.ContextMenu.LookUpInReference.Item";

    private readonly ReferenceTerms _terms;

    /// <summary>Creates the menu builder.</summary>
    /// <param name="terms">Resolves feature types and opens their entries.</param>
    public MapReferenceMenu(ReferenceTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        _terms = terms;
    }

    /// <summary>
    /// The feature types of <paramref name="hits"/> the chart reference
    /// holds, once each, in the hits' order.
    /// </summary>
    /// <param name="hits">The features under the pointer, topmost first.</param>
    /// <returns>The items; empty when the reference holds none of them.</returns>
    public async Task<IReadOnlyList<MapReferenceItem>> ItemsAsync(IReadOnlyList<PickHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        var items = new List<MapReferenceItem>();
        var seen = new HashSet<ReferenceKey>();
        foreach (var hit in hits)
        {
            var info = await _terms.FeatureTypeAsync(hit.ProductSpec, hit.FeatureType).ConfigureAwait(true);
            if (info is null || !seen.Add(info.Key))
                continue;
            var name = string.IsNullOrWhiteSpace(hit.FeatureTypeName) ? info.Name : hit.FeatureTypeName;
            items.Add(new MapReferenceItem(info.Key, name));
        }

        return items;
    }

    /// <summary>The menu for <paramref name="items"/>, or <see langword="null"/> when there are none.</summary>
    /// <param name="items">The feature types to offer.</param>
    public ContextMenu? Build(IReadOnlyList<MapReferenceItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return null;

        var lookUp = new MenuItem { Header = Strings.Map_LookUpInReference };
        AutomationProperties.SetAutomationId(lookUp, AutomationId);
        AutomationProperties.SetName(lookUp, Strings.Map_LookUpInReference);
        if (items.Count == 1)
        {
            var only = items[0];
            ToolTip.SetTip(lookUp, Tip(only));
            lookUp.Click += (_, _) => _terms.Open(only.Key);
        }
        else
        {
            foreach (var item in items)
            {
                var feature = new MenuItem { Header = item.Name };
                AutomationProperties.SetAutomationId(feature, ItemAutomationId);
                AutomationProperties.SetName(feature, item.Name);
                ToolTip.SetTip(feature, Tip(item));
                feature.Click += (_, _) => _terms.Open(item.Key);
                lookUp.Items.Add(feature);
            }
        }

        var menu = new ContextMenu { Items = { lookUp } };
        AutomationProperties.SetAutomationId(menu, MenuAutomationId);
        AutomationProperties.SetName(menu, Strings.Map_ContextMenuName);
        return menu;
    }

    /// <summary>The menu for the features in <paramref name="hits"/>, or <see langword="null"/> when there is nothing to look up.</summary>
    /// <param name="hits">The features under the pointer, topmost first.</param>
    public async Task<ContextMenu?> BuildAsync(IReadOnlyList<PickHit> hits) =>
        Build(await ItemsAsync(hits).ConfigureAwait(true));

    private static string Tip(MapReferenceItem item) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Tooltip_MapLookUpInReferenceFormat, item.Name);
}
