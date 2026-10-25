using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests.UiAutomation;

public sealed partial class LiveAccessibleNameTests
{
    [AvaloniaFact]
    public async Task Log_rows_have_real_names()
    {
        using var buffer = new LogBuffer();
        buffer.Add(LogLevel.Information, "EncDotNet.S100.Viewer.Services.DatasetLoaderService", "Loaded US5MA1BO");
        buffer.Add(LogLevel.Warning, "EncDotNet.S100.Viewer.Services.Secom.SecomClient", "SECOM fetch failed");
        using var panel = new LogsViewModel(buffer, showFeedback: () => { }, post: action => action());
        using var host = ViewHost.Show(new LogsView { DataContext = panel }, width: 1100, height: 300);
        host.Click(host.Find<Avalonia.Controls.ListBoxItem>(r => ReferenceEquals(r.DataContext, panel.Entries[1])));

        await AssertRealNamesAsync(host, minimumRows: 2);
    }
}
