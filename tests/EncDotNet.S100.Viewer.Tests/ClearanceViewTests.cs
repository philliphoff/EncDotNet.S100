using System.Globalization;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The real Clearance view (#902), laid out headlessly and found by its <c>Clearance.*</c> automation ids.</summary>
public sealed class ClearanceViewTests
{
    [AvaloniaFact]
    public void The_view_shows_the_profile_readout_and_sources()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var fixture = new PassageCheckFixture(post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
            var vm = new ClearanceViewModel(fixture.Check);
            using var host = ViewHost.Show(new ClearanceView { DataContext = vm }, width: 900, height: 260);

            Assert.True(host.IsShown("Clearance.Empty"));
            Assert.False(host.IsShown("Clearance.Chart"));

            fixture.Routes.IsCheckModeActive = true;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while ((fixture.Check.IsComputing || fixture.Check.Current is null) && DateTime.UtcNow < deadline)
            {
                host.Settle();
                Thread.Sleep(10);
            }
            host.Settle();

            Assert.True(host.IsShown("Clearance.Chart"));
            Assert.False(host.IsShown("Clearance.Empty"));
            Assert.True(host.IsShown("Clearance.Readout"));

            // Without a cursor the readout describes the minimum.
            Assert.Equal("Below your UKC setting", host.Find<TextBlock>("Clearance.Readout.State").Text);
            Assert.StartsWith("S-102 · 102FIXTURE", host.Find<TextBlock>("Clearance.Readout.DepthSource").Text);

            vm.IsSourcesOpen = true;
            host.Settle();
            Assert.True(host.IsShown("Clearance.Sources"));
            Assert.False(host.IsShown("Clearance.Readout"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
