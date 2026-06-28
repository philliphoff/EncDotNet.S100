using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Updates;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Smoke test that the About dialog's AXAML loads and binds against its
/// view-model without error (issue #379). Catches XAML/compiled-binding
/// regressions that the pure view-model tests cannot.
/// </summary>
public sealed class AboutDialogViewTests
{
    private sealed class StubUpdateService : IUpdateService
    {
        public bool UpdateChecksEnabled => true;
        public Task<UpdateStatus> CheckForUpdatesAsync(bool force, CancellationToken cancellationToken = default)
            => Task.FromResult(new UpdateStatus
            {
                Availability = UpdateAvailability.UpdateAvailable,
                CheckedAtUtc = DateTimeOffset.UtcNow,
                LatestVersion = "2.5.0",
                LatestRelease = new GitHubRelease(
                    "v2.5.0", "v2.5.0", "https://example/2.5.0", "* Item",
                    DateTimeOffset.UtcNow.AddDays(-3), false, 50_331_648),
            });
        public void SkipVersion(string version) { }
        public void SetUpdateChecksEnabled(bool enabled) { }
    }

    private sealed class StubVersionProvider : IAppVersionProvider
    {
        public AppVersionInfo Current { get; } =
            new("2.4.1", "2.4.1+a1f9c20", "a1f9c20", new DateOnly(2026, 6, 18));
    }

    private sealed class StubUrlOpener : IUrlOpener
    {
        public string? LastUrl { get; private set; }
        public void Open(string url) => LastUrl = url;
    }

    private static AboutDialogViewModel CreateViewModel() => new(
        new ShadUI.DialogManager(),
        new StubUpdateService(),
        new StubVersionProvider(),
        new StubUrlOpener(),
        TimeProvider.System);

    [Fact]
    public void View_LoadsAndBinds_WithoutError()
    {
        HeadlessTest.Run(() =>
        {
            var vm = CreateViewModel();
            var view = new AboutDialogView { DataContext = vm };

            var window = new Window { Content = view, Width = 480, Height = 640 };
            window.Show();
            window.Measure(new Size(480, 640));
            window.Arrange(new Rect(0, 0, 480, 640));

            Assert.NotNull(view.DataContext);
            window.Close();
        });
    }

    [Fact]
    public void ViewModel_FormatsVersionAndBuildLines()
    {
        var vm = CreateViewModel();

        Assert.Equal("Version 2.4.1", vm.VersionLine);
        Assert.Equal("build 2.4.1+a1f9c20 · 2026-06-18", vm.BuildLine);
    }

    [Fact]
    public async Task ViewModel_Initialize_PopulatesUpdateAvailableState()
    {
        var vm = CreateViewModel();

        await vm.InitializeAsync();

        Assert.True(vm.ShowUpdateAvailable);
        Assert.False(vm.IsChecking);
        Assert.Equal("Update available — 2.5.0", vm.UpdateAvailableHeader);
    }

    [Fact]
    public async Task ViewModel_Initialize_RaisesShowUpdateAvailableNotification()
    {
        var vm = CreateViewModel();
        var raised = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name)
                raised.Add(name);
        };

        await vm.InitializeAsync();

        Assert.Contains(nameof(vm.ShowUpdateAvailable), raised);
        Assert.Contains(nameof(vm.IsChecking), raised);
    }

    [Fact]
    public async Task ViewModel_Skip_KeepsUpdateVisibleAndMutes()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        Assert.True(vm.CanSkip);
        Assert.False(vm.IsUpdateSkipped);

        vm.SkipCommand.Execute(null);

        Assert.True(vm.ShowUpdateAvailable);
        Assert.True(vm.IsUpdateSkipped);
        Assert.False(vm.CanSkip);
    }

    [Fact]
    public void ThirdPartyNoticesCommand_OpensNoticesDocument()
    {
        var opener = new StubUrlOpener();
        var vm = new AboutDialogViewModel(
            new ShadUI.DialogManager(),
            new StubUpdateService(),
            new StubVersionProvider(),
            opener,
            TimeProvider.System);

        vm.ThirdPartyNoticesCommand.Execute(null);

        Assert.Equal(GitHubReleaseClient.ThirdPartyNoticesUrl, opener.LastUrl);
        Assert.EndsWith("/THIRD-PARTY-NOTICES.md", GitHubReleaseClient.ThirdPartyNoticesUrl);
    }
}
