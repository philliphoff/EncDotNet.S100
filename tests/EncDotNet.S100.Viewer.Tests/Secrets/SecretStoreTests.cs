using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;
using EncDotNet.S100.Viewer.Services.Secrets;

namespace EncDotNet.S100.Viewer.Tests.Secrets;

/// <summary>
/// The secret store (data source design note D7): the AIS key's move out of
/// <c>settings.json</c>, its lookup when AIS starts, and the platform stores.
/// </summary>
public sealed class SecretStoreTests
{
    private const string Reference = "sc-secret:" + ViewerSettingsService.AisApiKeySecretName;

    private static ViewerSettings Settings(string? aisKey = null) => new()
    {
        SettingsFilePath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        AisOverlay = new AisOverlaySettings { ApiKey = aisKey },
    };

    [Fact]
    public void A_key_in_settings_moves_to_the_store_on_first_read()
    {
        var settings = Settings("plain-key");
        var store = new InMemorySecretStore();

        var service = new ViewerSettingsService(settings, store);

        Assert.Equal("plain-key", service.AisApiKey);
        Assert.Equal(Reference, settings.AisOverlay!.ApiKey);
        Assert.Equal("plain-key", store.Read(ViewerSettingsService.AisApiKeySecretName));
        Assert.True(service.AisApiKeyIsInSecretStore);
        Assert.DoesNotContain("plain-key", File.ReadAllText(settings.SettingsFilePath!));
    }

    [Fact]
    public void A_new_key_goes_to_the_store_and_clearing_it_deletes_it()
    {
        var settings = Settings();
        var store = new InMemorySecretStore();
        var service = new ViewerSettingsService(settings, store);

        service.AisApiKey = "new-key";
        Assert.Equal(Reference, settings.AisOverlay!.ApiKey);
        Assert.Equal("new-key", store.Read(ViewerSettingsService.AisApiKeySecretName));

        service.AisApiKey = "  ";
        Assert.Null(settings.AisOverlay.ApiKey);
        Assert.Null(store.Read(ViewerSettingsService.AisApiKeySecretName));
    }

    [Fact]
    public void A_referenced_key_is_read_from_the_store()
    {
        var store = new InMemorySecretStore();
        store.Write(ViewerSettingsService.AisApiKeySecretName, "stored-key");

        var service = new ViewerSettingsService(Settings(Reference), store);

        Assert.Equal("stored-key", service.AisApiKey);
    }

    [Fact]
    public void Without_a_store_the_key_stays_in_settings()
    {
        var settings = Settings("plain-key");
        var service = new ViewerSettingsService(settings, new UnavailableSecretStore());

        Assert.Equal("plain-key", service.AisApiKey);
        Assert.Equal("plain-key", settings.AisOverlay!.ApiKey);
        Assert.False(service.AisApiKeyIsInSecretStore);
        Assert.Null(service.SecretStoreName);

        service.AisApiKey = "other-key";
        Assert.Equal("other-key", settings.AisOverlay.ApiKey);
    }

    [Fact]
    public void AIS_starts_with_the_key_its_reference_names_and_not_without_it()
    {
        var overlay = new AisOverlaySettings
        {
            Enabled = true,
            ApiKey = Reference,
            ApiKeyEnvironmentVariable = "AIS_SECRET_TEST_" + Guid.NewGuid().ToString("N"),
            ActivationViewportSpanDegrees = 1,
        };
        var notifier = new IdleNotifier();
        var empty = new InMemorySecretStore();
        var store = new InMemorySecretStore();
        store.Write(ViewerSettingsService.AisApiKeySecretName, "stored-key");

        Assert.IsType<DisabledAisFeatureSource>(AisOverlayServiceCollectionExtensions.BuildSource(overlay, null, notifier, empty));
        Assert.IsType<DisabledAisFeatureSource>(AisOverlayServiceCollectionExtensions.BuildSource(overlay, null, notifier, secrets: null));
        Assert.IsType<DeferredAisFeatureSource>(AisOverlayServiceCollectionExtensions.BuildSource(overlay, null, notifier, store));
    }

    [Fact]
    public void A_source_context_resolves_the_environment_then_the_store_then_a_plain_value()
    {
        var variable = "SECRET_CONTEXT_TEST_" + Guid.NewGuid().ToString("N");
        var context = new SourceCreationContext { ReadSecret = name => name == "a.key" ? "stored" : null };

        Assert.Equal("stored", context.ResolveSecret("sc-secret:a.key", variable));
        Assert.Null(context.ResolveSecret("sc-secret:missing", variable));
        Assert.Equal("plain", context.ResolveSecret("plain", variable));
        Assert.Null(context.ResolveSecret("  ", variable));
        Assert.Null(new SourceCreationContext().ResolveSecret("sc-secret:a.key"));

        Environment.SetEnvironmentVariable(variable, "from-env");
        try
        {
            Assert.Equal("from-env", context.ResolveSecret("sc-secret:a.key", variable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("sc-secret:ais.key", true, "ais.key")]
    [InlineData("sc-secret:", false, null)]
    [InlineData("plain", false, null)]
    [InlineData(null, false, null)]
    public void References_are_recognised(string? value, bool isReference, string? name)
    {
        Assert.Equal(isReference, SecretReference.IsReference(value));
        Assert.Equal(name, SecretReference.NameOf(value));
    }

    [Fact]
    public void Secret_tool_gets_the_secret_on_standard_input_never_the_command_line()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("secret-tool is the Linux store.");
            return;
        }
        var runner = new RecordingRunner { Output = "the-secret\n" };
        var store = new LibsecretToolSecretStore(runner);

        store.Write("ais.key", "the-secret");
        var read = store.Read("ais.key");
        store.Delete("ais.key");

        var (storeArgs, stdin) = runner.Calls[0];
        Assert.Equal(["store", "--label", "SoundCharts: ais.key", "service", "SoundCharts", "name", "ais.key"], storeArgs);
        Assert.Equal("the-secret", stdin);
        Assert.DoesNotContain("the-secret", storeArgs);
        Assert.Equal(["lookup", "service", "SoundCharts", "name", "ais.key"], runner.Calls[1].Arguments);
        Assert.Equal("the-secret", read);
        Assert.Equal("clear", runner.Calls[2].Arguments[0]);
    }

    [Fact]
    public void Secret_tool_failures_are_reported()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("secret-tool is the Linux store.");
            return;
        }
        var store = new LibsecretToolSecretStore(new RecordingRunner { ExitCode = 1 });

        SecretStoreException? refused = null;
        try
        {
            store.Write("ais.key", "x");
        }
        catch (SecretStoreException ex)
        {
            refused = ex;
        }
        Assert.NotNull(refused);
        Assert.Null(store.Read("ais.key"));
    }

    [Fact]
    public void Credential_Manager_round_trips_a_secret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Credential Manager is the Windows store.");
            return;
        }
        var store = new WindowsCredentialSecretStore();
        var name = "test." + Guid.NewGuid().ToString("N");
        try
        {
            store.Write(name, "first");
            store.Write(name, "second ✓");
            Assert.Equal("second ✓", store.Read(name));
        }
        finally
        {
            store.Delete(name);
        }
        Assert.Null(store.Read(name));
        store.Delete(name);
    }

    private sealed class IdleNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current => null;

        public event EventHandler<MapViewportSnapshot>? ViewportChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class RecordingRunner : LibsecretToolSecretStore.ISecretToolRunner
    {
        public List<(IReadOnlyList<string> Arguments, string? StandardInput)> Calls { get; } = [];

        public int ExitCode { get; init; }

        public string Output { get; init; } = string.Empty;

        public bool IsInstalled => true;

        public (int ExitCode, string Output) Run(IReadOnlyList<string> arguments, string? standardInput)
        {
            Calls.Add((arguments, standardInput));
            return (ExitCode, Output);
        }
    }
}
