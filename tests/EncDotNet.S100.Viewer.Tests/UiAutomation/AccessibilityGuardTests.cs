using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

/// <summary>
/// Keeps the viewer reachable by screen readers and by the <c>ui_*</c> MCP
/// tools (#784): a static check of every view's XAML. Every button and input
/// needs a short accessible name, and every interactive control an automation id
/// (<c>&lt;View&gt;.&lt;Element&gt;</c>, see this project's README).
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

    /// <summary>Controls a user enters or chooses a value with.</summary>
    private static readonly HashSet<string> Inputs =
        ["TextBox", "AutoCompleteBox", "ComboBox", "Slider", "NumericUpDown", "ToggleSwitch", "CheckBox", "RadioButton"];

    /// <summary>Inputs that a string <c>Content</c> names, as it names a button.</summary>
    private static readonly HashSet<string> NamedByContent = ["ToggleSwitch", "CheckBox", "RadioButton"];

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
                && !HasStringContent(e.Element))
            .Select(e => e.Where)
            .ToList();

        Assert.True(unnamed.Count == 0,
            "These buttons have no accessible name; set AutomationProperties.Name (usually to the tooltip's string):\n"
            + string.Join('\n', unnamed));
    }

    [Fact]
    public void Every_input_has_an_accessible_name()
    {
        // An input next to a label TextBlock is not named by it: on its own a
        // screen reader announces "toggle switch, on". A placeholder is not a
        // name either.
        var unnamed = Elements()
            .Where(e => Inputs.Contains(e.Element.Name.LocalName)
                && !IsHidden(e.Element)
                && !IsTemplatePart(e.Element)
                && Attribute(e.Element, "AutomationProperties.Name") is null
                && Attribute(e.Element, "AutomationProperties.LabeledBy") is null
                && !(NamedByContent.Contains(e.Element.Name.LocalName) && HasStringContent(e.Element)))
            .Select(e => e.Where)
            .ToList();

        Assert.True(unnamed.Count == 0,
            "These inputs have no accessible name; set AutomationProperties.Name to their label's string:\n"
            + string.Join('\n', unnamed));
    }

    [Fact]
    public void Every_interactive_control_has_an_automation_id()
    {
        var missing = MissingIds().Select(e => e.Where).ToList();

        Assert.True(missing.Count == 0,
            "These controls have no AutomationProperties.AutomationId (<View>.<Element>, see tests/EncDotNet.S100.Viewer.Tests/README.md):\n"
            + string.Join('\n', missing));
    }

    [Fact]
    public void Accessible_names_are_short_labels()
    {
        // A screen reader announces the name every time focus lands on the
        // control: a sentence or a shortcut there is noise. Longer text goes in
        // AutomationProperties.HelpText, a shortcut in AcceleratorKey.
        var strings = XDocument.Load(LibraryTestContext.RepoFile("src", "EncDotNet.S100.Viewer", "Resources", "Strings.resx"))
            .Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
        var tooLong = Elements()
            .Select(e => (e.Where, Key: StringKey(Attribute(e.Element, "AutomationProperties.Name"))))
            .Where(e => e.Key is not null && strings.TryGetValue(e.Key, out var value) && !IsShortLabel(value))
            .Select(e => $"{e.Where} {e.Key} = \"{strings[e.Key!]}\"")
            .ToList();

        Assert.True(tooLong.Count == 0,
            "These accessible names are not short labels (at most six words, no full stop, no brackets but a trailing unit); "
            + "name the control with a short Label_* string and move the text to AutomationProperties.HelpText:\n"
            + string.Join('\n', tooLong));
    }

    /// <summary>
    /// At most six words, no full stop, and no brackets except a trailing unit
    /// such as "Speed (kn)", which a field's label needs.
    /// </summary>
    private static bool IsShortLabel(string value)
    {
        if (value.EndsWith(')') && value.LastIndexOf(" (", StringComparison.Ordinal) is var unit and > 0)
        {
            value = value[..unit];
        }

        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6
            && value.IndexOfAny(['(', '[']) < 0
            && !value.EndsWith('.');
    }

    /// <summary>The key of a <c>{x:Static loc:Strings.Key}</c> value.</summary>
    private static string? StringKey(string? value)
        => value is not null && value.StartsWith("{x:Static loc:Strings.", StringComparison.Ordinal)
            ? value["{x:Static loc:Strings.".Length..].TrimEnd('}')
            : null;

    /// <summary>A string <c>Content</c> attribute, or text written as the element's content.</summary>
    private static bool HasStringContent(XElement element)
        => Attribute(element, "Content") is not null
            || !string.IsNullOrWhiteSpace(string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)));

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
