using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.Secrets;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests.Sources;

/// <summary>
/// A live feed's settings page in the Library (data source design note D8,
/// slice 9b): generated from its options, with secrets in the D7 store.
/// </summary>
public sealed class LiveFeedSettingsTests
{
    private static readonly SourceRegistration Ais = AisSource.Registration(new ViewerSettings
    {
        SettingsFilePath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
    });

    private readonly List<(Guid Collection, CollectionSource Source)> _updates = [];

    private LiveFeedSettingsViewModel Page(ISecretStore? store = null, string? environmentKey = null) => new(
        id => id == Ais.Id ? Ais : SourceRegistry.Default.TryResolve(id, out var r) ? r : null,
        (collection, source) =>
        {
            _updates.Add((collection, source));
            return true;
        },
        store,
        name => name == AisFeedOptions.KeyEnvironmentVariable ? environmentKey : null);

    private static (LibraryCollection Collection, LibrarySource Source) Feed(string registrationId, IReadOnlyDictionary<string, string>? options = null)
    {
        var feed = new LiveFeedSource(Guid.NewGuid(), null, registrationId) { Options = options };
        var definition = new DatasetCollection(Guid.NewGuid(), "Live feeds", [feed], DateTimeOffset.UnixEpoch);
        var source = new LibrarySource(feed, null, LibrarySourceState.Ready);
        return (new LibraryCollection(definition, [source]), source);
    }

    [Fact]
    public void A_feed_that_needs_settings_says_so()
    {
        var page = Page();
        var (collection, source) = Feed(S100Sources.NmeaWeatherId);

        page.Show(collection, source);

        var input = Assert.Single(page.Options);
        Assert.Equal("Input", input.Field.Name);
        Assert.True(page.NeedsSettings);
        Assert.True(page.HasMessage);

        input.Text = "udp:10110";
        page.SaveCommand.Execute(null);

        var (collectionId, saved) = Assert.Single(_updates);
        Assert.Equal(collection.Id, collectionId);
        Assert.Equal("udp:10110", Assert.IsType<LiveFeedSource>(saved).Options!["Input"]);
    }

    [Fact]
    public void A_feed_with_nothing_to_set_says_so()
    {
        var page = Page();
        var (collection, source) = Feed(S100Sources.NdbcObservationsId);

        page.Show(collection, source);

        Assert.True(page.HasNoOptions);
        Assert.False(page.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void A_secret_goes_to_the_store_and_only_its_reference_is_saved()
    {
        var store = new InMemorySecretStore();
        var page = Page(store);
        var (collection, source) = Feed(Ais.Id);
        page.Show(collection, source);
        Assert.True(page.NeedsSettings);

        var key = page.Options.Single(o => o.IsSecret);
        key.Text = "my-key";
        page.SaveCommand.Execute(null);

        var saved = Assert.IsType<LiveFeedSource>(Assert.Single(_updates).Source);
        Assert.Equal("sc-secret:ais.aisstream.apiKey", saved.Options!["ApiKey"]);
        Assert.Equal("my-key", store.Read("ais.aisstream.apiKey"));
        Assert.Equal("50", saved.Options["ActivationSpanDegrees"]);
    }

    [Fact]
    public void A_saved_secret_is_kept_when_left_blank_and_never_shown()
    {
        var page = Page(new InMemorySecretStore());
        var (collection, source) = Feed(Ais.Id, new Dictionary<string, string> { ["ApiKey"] = "sc-secret:ais.aisstream.apiKey" });
        page.Show(collection, source);

        var key = page.Options.Single(o => o.IsSecret);
        Assert.Equal(string.Empty, key.Text);
        Assert.True(key.HasSavedSecret);
        Assert.False(page.NeedsSettings);

        page.Options.Single(o => o.Field.Name == "ActivationSpanDegrees").Text = "5";
        page.SaveCommand.Execute(null);

        var saved = Assert.IsType<LiveFeedSource>(Assert.Single(_updates).Source);
        Assert.Equal("sc-secret:ais.aisstream.apiKey", saved.Options!["ApiKey"]);
        Assert.Equal("5", saved.Options["ActivationSpanDegrees"]);
    }

    [Fact]
    public void Without_a_store_the_secret_is_kept_in_the_options()
    {
        var page = Page(new UnavailableSecretStore());
        var (collection, source) = Feed(Ais.Id);
        page.Show(collection, source);

        page.Options.Single(o => o.IsSecret).Text = "my-key";
        page.SaveCommand.Execute(null);

        Assert.Equal("my-key", Assert.IsType<LiveFeedSource>(Assert.Single(_updates).Source).Options!["ApiKey"]);
    }

    [Fact]
    public void A_key_from_the_environment_needs_nothing_saved()
    {
        var page = Page(environmentKey: "from-env");
        var (collection, source) = Feed(Ais.Id);

        page.Show(collection, source);

        Assert.False(page.NeedsSettings);
        Assert.Contains(AisFeedOptions.KeyEnvironmentVariable, page.Options.Single(o => o.IsSecret).Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_value_isnt_saved()
    {
        var page = Page(new InMemorySecretStore());
        var (collection, source) = Feed(Ais.Id, new Dictionary<string, string> { ["ApiKey"] = "sc-secret:ais.aisstream.apiKey" });
        page.Show(collection, source);

        page.Options.Single(o => o.Field.Name == "ActivationSpanDegrees").Text = "wide";
        page.SaveCommand.Execute(null);

        Assert.Empty(_updates);
        Assert.Contains("Runs in views", page.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_source_shows_nothing()
    {
        var page = Page();
        var folder = new LocalFolderSource(Guid.NewGuid(), null, "/charts");
        var definition = new DatasetCollection(Guid.NewGuid(), "Charts", [folder], DateTimeOffset.UnixEpoch);
        var source = new LibrarySource(folder, null, LibrarySourceState.Ready);

        page.Show(new LibraryCollection(definition, [source]), source);

        Assert.Null(page.SourceId);
    }

    [Fact]
    public void AIS_runs_where_its_options_say()
    {
        Assert.Equal(50, Ais.ActivationFor(null).MaxViewportSpanDegrees);
        Assert.Equal(3, Ais.ActivationFor(new AisFeedOptions { ActivationSpanDegrees = 3 }).MaxViewportSpanDegrees);
        Assert.Same(SourceActivation.Always, Ais.ActivationFor(new AisFeedOptions { ActivationSpanDegrees = 0 }));
    }
}
