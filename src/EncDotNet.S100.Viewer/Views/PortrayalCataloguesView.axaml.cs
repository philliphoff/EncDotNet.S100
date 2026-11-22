using Avalonia.Controls;
using Avalonia.Interactivity;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer.Views;

public partial class PortrayalCataloguesView : UserControl
{
    public PortrayalCataloguesView()
    {
        InitializeComponent();
        AddButton.Click += OnAddClick;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PortrayalCataloguesViewModel vm) return;

        var fileDialog = App.Services.GetRequiredService<IFileDialogService>();
        var folderPath = await fileDialog.OpenPortrayalCatalogueFolderAsync(TopLevel.GetTopLevel(this));
        if (folderPath is null) return;

        // The product comes from the catalogue's content; a folder that
        // can't be used says why (A2).
        CatalogueAddFeedback.Show(vm.Add(folderPath));
    }
}

/// <summary>Tells the user why a catalogue they chose isn't in use.</summary>
internal static class CatalogueAddFeedback
{
    /// <summary>Shows <paramref name="reason"/> as a warning toast; does nothing when it's null.</summary>
    public static void Show(string? reason)
    {
        if (reason is null) return;
        App.Services.GetRequiredService<Services.Notifications.INotificationService>()
            .Create(Resources.Strings.Toast_Warning)
            .WithSeverity(Services.Notifications.NotificationSeverity.Warning)
            .WithContent(string.Format(Resources.Strings.Catalogues_AddFailed, reason))
            .Show();
    }
}
