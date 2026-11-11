using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>"Connect to a shared feed…" (issue #680, UX refinement §1).</summary>
public sealed class SharedFeedDialogViewModelTests
{
    private static async Task ConnectAsync(SharedFeedDialogViewModel vm, string url)
    {
        vm.Url = url;
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.ConnectCommand).ExecuteAsync(null);
    }

    [Fact]
    public async Task A_reachable_feed_is_handed_on_as_a_catalogue()
    {
        KnownCatalogueSource? connected = null;
        var vm = new SharedFeedDialogViewModel((_, _) =>
            Task.FromResult(new CatalogueProbe(null, KnownCatalogueFormat.S100Feed, "bridge-pc", IsJson: true)));
        vm.Connected += (_, s) => connected = s;
        Assert.False(vm.ConnectCommand.CanExecute(null));

        await ConnectAsync(vm, " http://bridge-pc.local:8100/abc/feed.json ");

        Assert.False(vm.HasError);
        Assert.Equal(KnownCatalogueFormat.S100Feed, connected!.Format);
        Assert.Equal("bridge-pc", connected.Name);
        Assert.Equal(new Uri("http://bridge-pc.local:8100/abc/feed.json"), connected.CatalogUri);
    }

    [Fact]
    public async Task The_feed_is_named_after_the_serving_computer()
    {
        KnownCatalogueSource? connected = null;
        var vm = new SharedFeedDialogViewModel((_, _) =>
            Task.FromResult(new CatalogueProbe(null, KnownCatalogueFormat.S100Feed, "charts", IsJson: true, Machine: "bridge-pc")));
        vm.Connected += (_, s) => connected = s;

        await ConnectAsync(vm, "http://bridge-pc.local:8100/abc/feed.json");

        Assert.Equal("bridge-pc", connected!.Name);
    }

    [Theory]
    [InlineData("not a url", "http or https")]
    [InlineData("http://machine.test/catalog.xml", "not an S-100 feed")]
    public async Task Other_addresses_are_refused_with_a_reason(string url, string reason)
    {
        var connected = false;
        var vm = new SharedFeedDialogViewModel((_, _) =>
            Task.FromResult(new CatalogueProbe("EncProductCatalog", KnownCatalogueFormat.NoaaEnc, null)));
        vm.Connected += (_, _) => connected = true;

        await ConnectAsync(vm, url);

        Assert.False(connected);
        Assert.Contains(reason, vm.Error);
    }

    [Fact]
    public async Task An_unreachable_feed_says_why()
    {
        var vm = new SharedFeedDialogViewModel((_, _) => throw new HttpRequestException("Connection refused"));

        await ConnectAsync(vm, "http://machine.test:8100/feed.json");

        Assert.Contains("Connection refused", vm.Error);
        Assert.False(vm.IsChecking);
        Assert.True(vm.ConnectCommand.CanExecute(null));
    }
}
