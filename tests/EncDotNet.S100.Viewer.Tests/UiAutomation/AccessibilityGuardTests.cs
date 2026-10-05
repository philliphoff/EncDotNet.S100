using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

/// <summary>
/// Keeps the viewer reachable by screen readers and by the <c>ui_*</c> MCP
/// tools (#784): a static check of every view's XAML. Every button needs an
/// accessible name, and every interactive control in a finished view needs an
/// automation id (<c>&lt;View&gt;.&lt;Element&gt;</c>, see this project's README).
/// </summary>
public sealed class AccessibilityGuardTests
{
    /// <summary>Controls a user operates directly.</summary>
    private static readonly HashSet<string> Interactive =
    [
        "Button", "ToggleButton", "RepeatButton", "SplitButton", "DropDownButton", "HyperlinkButton",
        "RadioButton", "CheckBox", "ToggleSwitch", "TextBox", "AutoCompleteBox", "ComboBox", "Slider",
        "NumericUpDown", "ListBox", "TreeView", "TabItem", "MenuItem",
    ];

    /// <summary>Controls whose automation name comes from their content.</summary>
    private static readonly HashSet<string> ButtonLike =
        ["Button", "ToggleButton", "RepeatButton", "SplitButton", "DropDownButton", "HyperlinkButton"];

    /// <summary>
    /// Views whose controls do not all have automation ids yet (#784). When a
    /// view is finished, remove it here; the ratchet test fails until you do.
    /// </summary>
    private static readonly HashSet<string> ViewsAwaitingIds =
    [
        "AboutDialogView.axaml",
        "AddCollectionManifestDialogView.axaml",
        "AddOnlineCatalogueWizardView.axaml",
        "AddToLibraryDialogView.axaml",
        "CatalogueDirectoryDialogView.axaml",
        "CatalogueScopeStepView.axaml",
        "DisplayModeSelectorView.axaml",
        "EcdisDisplayPanelView.axaml",
        "FeatureCataloguesView.axaml",
        "FeatureSearchView.axaml",
        "FeedbackDialogView.axaml",
        "HelmView.axaml",
        "LayerStackView.axaml",
        "PickReportView.axaml",
        "PortrayalCataloguesView.axaml",
        "SharedFeedDialogView.axaml",
        "VesselListView.axaml",
    ];

    [Fact]
    public void Every_button_has_an_accessible_name()
    {
        // A button whose content is a control (an icon, or a panel of text) is
        // named by the content's type name, e.g. "FluentIcons.Avalonia.FluentIcon",
        // so it needs AutomationProperties.Name; string content names itself.
        var unnamed = Elements()
            .Where(e => ButtonLike.Contains(e.Element.Name.LocalName)
                && !IsHidden(e.Element)
                && Attribute(e.Element, "AutomationProperties.Name") is null
                && Attribute(e.Element, "Content") is null
                && string.IsNullOrWhiteSpace(string.Concat(e.Element.Nodes().OfType<XText>().Select(t => t.Value))))
            .Select(e => e.Where)
            .ToList();

        Assert.True(unnamed.Count == 0,
            "These buttons have no accessible name; set AutomationProperties.Name (usually to the tooltip's string):\n"
            + string.Join('\n', unnamed));
    }

    [Fact]
    public void Interactive_controls_in_finished_views_have_automation_ids()
    {
        var missing = MissingIds()
            .Where(e => !ViewsAwaitingIds.Contains(e.File))
            .Select(e => e.Where)
            .ToList();

        Assert.True(missing.Count == 0,
            "These controls have no AutomationProperties.AutomationId (<View>.<Element>, see tests/EncDotNet.S100.Viewer.Tests/README.md):\n"
            + string.Join('\n', missing));
    }

    [Fact]
    public void Views_awaiting_ids_are_still_unfinished()
    {
        var unfinished = MissingIds().Select(e => e.File).ToHashSet();
        var finished = ViewsAwaitingIds.Where(view => !unfinished.Contains(view)).Order().ToList();

        Assert.True(finished.Count == 0,
            "These views have automation ids on every control now; remove them from ViewsAwaitingIds:\n"
            + string.Join('\n', finished));
        Assert.All(ViewsAwaitingIds, view => Assert.Contains(view, Elements().Select(e => e.File)));
    }

    private static IEnumerable<ViewElement> MissingIds()
        => Elements().Where(e => Interactive.Contains(e.Element.Name.LocalName)
            && !IsHidden(e.Element)
            && !IsTemplatePart(e.Element)
            && Attribute(e.Element, "AutomationProperties.AutomationId") is null);

    private static IEnumerable<ViewElement> Elements()
    {
        var viewer = Path.GetDirectoryName(LibraryTestContext.RepoFile("src", "EncDotNet.S100.Viewer", "App.axaml"))!;
        foreach (var path in Directory.EnumerateFiles(viewer, "*.axaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var file = Path.GetFileName(path);
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                var line = ((IXmlLineInfo)element).LineNumber;
                yield return new ViewElement(file, element, $"{Path.GetRelativePath(viewer, path)}:{line} <{element.Name.LocalName}>");
            }
        }
    }

    /// <summary>Hidden from automation clients, e.g. a chevron that repeats its item's own expand.</summary>
    private static bool IsHidden(XElement element)
        => Attribute(element, "AutomationProperties.AccessibilityView") == "Raw";

    /// <summary>A part of a control template, shared by every control that uses the template.</summary>
    private static bool IsTemplatePart(XElement element)
        => element.Ancestors().Any(a => a.Name.LocalName == "ControlTemplate");

    /// <summary>An attribute by its local name, e.g. "AutomationProperties.Name".</summary>
    private static string? Attribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    private sealed record ViewElement(string File, XElement Element, string Where);
}
