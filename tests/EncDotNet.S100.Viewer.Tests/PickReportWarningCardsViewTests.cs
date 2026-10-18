using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Warning cards in the Object Information view (#932): links in the
/// message text, and a pick of several warnings as collapsible cards.
/// </summary>
public sealed class PickReportWarningCardsViewTests
{
    private static readonly DatasetEntry Dataset = new("/tmp/ccg.gml", "S-124");

    private const string BulletinText = "IN FORCE AT 060530 UTC APR 20:\n   P 477/20  P 478/20\n   P 999/20";

    private static PickHit Hit(string id, string number, string text = "Text.") => new()
    {
        FeatureType = "NavwarnPart",
        FeatureRef = id,
        DatasetFileName = Dataset.DisplayName,
        ProductSpec = "S-124",
        NavigationalWarning = new NavigationalWarningInfo
        {
            Series = "P",
            Number = number,
            WarningType = "Warnings in force",
            Texts = [new NavigationalWarningText("eng", text)],
        },
    };

    private static NavigationalWarningEntry Entry(PickHit hit, bool located) => new(
        Dataset, hit.FeatureRef, 0, hit.NavigationalWarning!,
        located ? S100GeometryType.Point : S100GeometryType.None,
        located ? new MRect(0, 0, 10, 10) : null);

    [AvaloniaFact]
    public void Bulletin_text_links_numbers_stays_selectable_and_a_click_opens_the_warning()
    {
        var bulletin = Hit("bulletin", "480/20", BulletinText);
        var p477 = Hit("p477", "477/20");
        var navigator = new PickReportWarningCardsTests.FakeNavigator();
        navigator.Entries.AddRange([Entry(bulletin, false), Entry(p477, true)]);
        var pick = new PickReportViewModel { WarningNavigator = navigator };
        pick.SetPicks([bulletin]);

        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);
        var text = host.Find<WarningMessageTextBlock>("PickReport.TextCard.Text");

        // Selecting everything copies the text with its line breaks, across the link.
        text.SelectAll();
        Assert.Equal(BulletinText, text.SelectedText);
        text.ClearSelection();
        host.Settle();

        // Click the trailing half of the last character of "477/20" (478/20
        // isn't listed, so it stays plain).
        var index = BulletinText.IndexOf("477/20", StringComparison.Ordinal) + 5;
        var rect = text.TextLayout.HitTestTextPosition(index);
        var at = text.TranslatePoint(new Point(rect.X + rect.Width * 0.8, rect.Y + rect.Height / 2), host.Window)!.Value;
        host.Window.MouseDown(at, MouseButton.Left);
        host.Window.MouseUp(at, MouseButton.Left);
        host.Settle();

        Assert.Equal("p477", Assert.Single(navigator.Selected).FeatureRef);
    }

    [AvaloniaFact]
    public void Several_warnings_show_as_cards_and_a_header_click_switches_the_open_one()
    {
        var first = Hit("a", "477/20");
        var second = Hit("b", "478/20");
        var navigator = new PickReportWarningCardsTests.FakeNavigator();
        navigator.Entries.AddRange([Entry(first, true), Entry(second, true)]);
        var pick = new PickReportViewModel { WarningNavigator = navigator };
        pick.SetPicks([second, first]);

        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);

        Assert.False(host.IsShown("PickReport.Hits"));
        var headers = host.FindAll<Button>("PickReport.WarningCard.Header").Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Equal(["P 477/20", "P 478/20"], headers.Select(h => AutomationProperties.GetName(h)));
        Assert.Single(host.FindAll<Button>("PickReport.TextCard.ZoomTo"), b => b.IsEffectivelyVisible);

        host.Click(headers[1]);

        Assert.Same(second, pick.SelectedHit);
        Assert.Equal([false, true], pick.WarningCards.Select(c => c.IsOpen));
    }

    [AvaloniaFact]
    public void A_not_for_navigation_warning_shows_the_footer_label_and_monospaced_text()
    {
        var hit = Hit("a", "477/20");
        var pick = new PickReportViewModel();
        pick.SetPicks([new PickHit
        {
            FeatureType = hit.FeatureType,
            FeatureRef = hit.FeatureRef,
            DatasetFileName = hit.DatasetFileName,
            ProductSpec = hit.ProductSpec,
            NavigationalWarning = hit.NavigationalWarning,
            NotForNavigation = true,
        }]);

        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);

        Assert.True(host.IsShown("PickReport.TextCard.NotForNavigation"));
        Assert.Contains("Menlo", host.Find<WarningMessageTextBlock>("PickReport.TextCard.Text").FontFamily.ToString());
    }

    [AvaloniaFact]
    public void Referenced_text_and_long_values_share_the_body_set_proportional_without_the_label()
    {
        var pick = new PickReportViewModel();
        pick.SetPicks([new PickHit
        {
            FeatureType = "CautionArea",
            FeatureRef = "7",
            DatasetFileName = "101GB.000",
            ProductSpec = "S-101",
            Attributes =
            [
                new PickAttribute { Code = "fileReference", RawValue = "CAUTION.TXT", ExternalText = "ANCHORING\nBeware of currents." },
                new PickAttribute { Code = "note", Name = "Note", RawValue = "A\nB" },
            ],
        }]);

        using var host = ViewHost.Show(new PickReportView { DataContext = pick }, width: 420, height: 900);

        var cards = host.FindAll<Border>("PickReport.TextCard").Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Equal(["ANCHORING", "Note"], cards.Select(c => AutomationProperties.GetName(c)));
        var texts = host.FindAll<WarningMessageTextBlock>("PickReport.TextCard.Text").Where(t => t.IsEffectivelyVisible).ToList();
        Assert.Equal(2, texts.Count);
        Assert.All(texts, t => Assert.DoesNotContain("Menlo", t.FontFamily.ToString()));
        Assert.False(host.IsShown("PickReport.TextCard.NotForNavigation"));
    }
}
