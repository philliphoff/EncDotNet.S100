using System.Collections.ObjectModel;
using EncDotNet.S100.Viewer.ViewModels.Notifications;

namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Central notification service. Owns the live (active) and historical
/// notification collections and is the single entry point callers use to
/// surface notifications. All collection and view-model mutation is marshaled
/// onto the UI thread, so callers may invoke it from any thread.
/// </summary>
internal interface INotificationService
{
    /// <summary>
    /// Begins building a notification with the given title. Call the returned
    /// builder's <see cref="INotificationBuilder.Show"/> to display it.
    /// </summary>
    /// <param name="title">The notification title (caller-localized).</param>
    INotificationBuilder Create(string title);

    /// <summary>The notifications currently displayed, newest last.</summary>
    ReadOnlyObservableCollection<NotificationViewModel> Active { get; }

    /// <summary>
    /// Previously dismissed notifications, newest first, capped to a bounded
    /// ring buffer.
    /// </summary>
    ReadOnlyObservableCollection<NotificationViewModel> History { get; }

    /// <summary>
    /// Dismisses every active notification, moving each into history. Does not
    /// affect already-historical entries.
    /// </summary>
    void DismissAll();

    /// <summary>Clears the notification history.</summary>
    void ClearHistory();
}
