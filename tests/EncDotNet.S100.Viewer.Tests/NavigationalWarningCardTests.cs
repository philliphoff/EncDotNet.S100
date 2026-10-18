using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Warning card in Object Information (#932). One class, so the
/// session-held language choice is never shared between parallel tests.
/// </summary>
public sealed class NavigationalWarningCardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    private const string Amsa106 =
        "SECURITE\n   FM JRCC AUSTRALIA 262222Z SEP 26\n   NAVAREA X 106/26\n   CORAL SEA.\n" +
        "   SPECIAL PURPOSE VESSEL LAYLA CONDUCTING CABLE RECOVERY OPERATIONS.\n   2NM CLEARANCE REQUESTED.\nNNNN";

    public NavigationalWarningCardTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        NavigationalWarningCardViewModel.ResetSessionLanguage();
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        NavigationalWarningCardViewModel.ResetSessionLanguage();
    }

    private static NavigationalWarningInfo Warning(params NavigationalWarningText[] texts) => new()
    {
        Series = "NAVAREA X",
        Number = "106/26",
        WarningType = "Cable Operations",
        GeneralType = "Works",
        GeneralArea = "Coral Sea",
        Issuer = "JRCC Australia",
        HasLocation = true,
        Validity = [new NavigationalWarningValidity(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 15, 23, 59, 0, TimeSpan.Zero))],
        Texts = texts.Length == 0 ? [new NavigationalWarningText("eng", Amsa106)] : texts,
    };

    private static NavigationalWarningCardViewModel Card(NavigationalWarningInfo? info = null, DateTimeOffset? at = null) =>
        new(info ?? Warning(), "124AU01NA10626.gml", TimeFormat.Utc, at ?? Now);

    [Fact]
    public void Header_ShowsNumberTypeStatusAndDetails()
    {
        var card = Card();

        Assert.Equal("NAVAREA X 106/26", card.Label);
        Assert.Equal("Cable Operations", card.TypeText);
        Assert.Equal(" · Works", card.GeneralTypeSuffix);
        Assert.True(card.IsInForce);
        Assert.Equal("In force", card.StatusText);
        Assert.Equal("2026-09-27 00:00:00 UTC – 2026-10-15 23:59:00 UTC", card.ValidityText);
        Assert.Equal("NAVAREA X · Coral Sea", card.AreaText);
        // The DTG is shown exactly as the message writes it.
        Assert.Equal("262222Z SEP 26", card.IssuedText);
        Assert.Equal(" · JRCC Australia", card.IssuerSuffix);
    }

    [Fact]
    public void Header_WithoutNumber_FallsBackToTheFileName()
    {
        var card = Card(Warning() with { Number = null });

        Assert.Equal("124AU01NA10626.gml", card.Label);
    }

    [Fact]
    public void Area_SaysWhenThereIsNoLocation()
    {
        var card = Card(Warning() with { HasLocation = false, GeneralArea = null });

        Assert.Equal("NAVAREA X · no location", card.AreaText);
    }

    [Fact]
    public void Status_FollowsTheViewTime()
    {
        var card = Card(at: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(card.IsNotYetInForce);

        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        card.SetViewTime(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.True(card.IsEnded);
        Assert.Equal("Ended", card.StatusText);
        Assert.Contains(nameof(card.StatusText), changed);
    }

    [Fact]
    public void LongMessage_ClipsAtTwentyLinesUntilShowAll()
    {
        var text = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"LINE {i}"));
        var card = Card(Warning(new NavigationalWarningText("eng", text)));

        Assert.True(card.IsClipped);
        Assert.Equal(30, card.LineCount);
        Assert.Equal(20, card.DisplayedText.Split('\n').Length);
        Assert.Equal("Show all 30 lines", card.ShowAllText);

        card.ShowAllCommand.Execute(null);

        Assert.False(card.IsClipped);
        Assert.Equal(30, card.DisplayedText.Split('\n').Length);
    }

    [Fact]
    public void MessageOfTwentyTwoLines_IsNotClipped()
    {
        var text = string.Join('\n', Enumerable.Range(1, 22).Select(i => $"LINE {i}"));

        Assert.False(Card(Warning(new NavigationalWarningText("eng", text))).IsClipped);
    }

    [Fact]
    public void Message_KeepsIndentation()
    {
        Assert.Contains("\n   FM JRCC AUSTRALIA", Card().DisplayedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Languages_UiLanguageFirst_ThenDataOrder()
    {
        var card = Card(Warning(
            new NavigationalWarningText("fra", "FRANÇAIS"),
            new NavigationalWarningText("eng", "ENGLISH")));

        Assert.True(card.HasLanguageTabs);
        Assert.Equal(["English", "Français"], card.Languages.Select(l => l.Name));
        Assert.Equal("ENGLISH", card.DisplayedText);
        Assert.True(card.Languages[0].IsSelected);
    }

    [Fact]
    public void Languages_ChoiceHoldsForTheSessionAndCopyTakesTheLanguageShown()
    {
        var info = Warning(new NavigationalWarningText("eng", "ENGLISH"), new NavigationalWarningText("fre", "FRANÇAIS"));
        var card = Card(info);
        string? copied = null;
        card.CopyRequested += (_, t) => copied = t;

        card.Languages[1].SelectCommand.Execute(null);
        card.CopyCommand.Execute(null);

        Assert.Equal("FRANÇAIS", copied);
        Assert.Equal("FRANÇAIS", Card(info).DisplayedText);
    }

    [Fact]
    public void Languages_UnknownCodeShowsAsWritten_SingleLanguageHasNoTabs()
    {
        Assert.Equal("xx-Unknown", LanguageNames.Display("xx-Unknown"));
        Assert.Equal("Deutsch", LanguageNames.Display("ger"));
        Assert.False(Card().HasLanguageTabs);
    }

    [Fact]
    public void NoText_SaysSo()
    {
        var card = Card(Warning() with { Texts = [] });

        Assert.False(card.HasText);
        Assert.False(card.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void PickReport_WarningHit_ShowsCardAndFoldsAttributesWithoutTheText()
    {
        var vm = new PickReportViewModel(timeFormat: null, marinerSettings: null, urlOpener: null, examinerLinks: null, viewTime: new FixedViewTime(Now));
        var warningInformation = new PickAttribute
        {
            Code = "warningInformation",
            RawValue = string.Empty,
            DisplayValue = null,
            Children =
            [
                new PickAttribute { Code = "information.language", RawValue = "eng", DisplayValue = null, Children = [] },
                new PickAttribute { Code = "information.text", RawValue = Amsa106, DisplayValue = null, Children = [] },
                new PickAttribute { Code = "navwarnTypeDetails", RawValue = "Cable Operations", DisplayValue = null, Children = [] },
            ],
        };
        var hit = new PickHit
        {
            FeatureType = "NavwarnPart",
            FeatureRef = "part.1",
            ProductSpec = "S-124",
            Attributes = [warningInformation, new PickAttribute { Code = "restriction", RawValue = "1", DisplayValue = null, Children = [] }],
            NavigationalWarning = Warning(),
        };

        vm.SetPicks([hit]);

        Assert.True(vm.HasWarningCard);
        Assert.False(vm.ShowInlineAttributes);
        Assert.False(vm.WarningAttributesExpanded);
        Assert.Equal("NAVAREA X 106/26", hit.HitListLabel);
        Assert.Equal("NavwarnPart", hit.HitListSecondaryLabel);
        Assert.Equal("NavwarnPart", hit.PrimaryLabel);
        var info = Assert.Single(vm.Attributes, a => a.Code == "warningInformation");
        Assert.Equal(["navwarnTypeDetails"], info.Children.Select(c => c.Code));
        Assert.Equal(2, vm.AttributeCount);

        string? copied = null;
        vm.CopyIdentityRequested += (_, t) => copied = t;
        vm.WarningCard!.CopyCommand.Execute(null);
        Assert.Equal(Amsa106, copied);

        vm.Clear();
        Assert.False(vm.HasWarningCard);
        Assert.True(vm.ShowInlineAttributes);
    }

    private sealed class FixedViewTime(DateTimeOffset now) : IViewTime
    {
        public DateTimeOffset Current => now;

        public event EventHandler? Changed { add { } remove { } }
    }
}
