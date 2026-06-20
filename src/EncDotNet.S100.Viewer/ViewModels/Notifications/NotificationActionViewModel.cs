using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.ViewModels.Notifications;

/// <summary>
/// A single action button rendered on a notification card.
/// </summary>
internal sealed class NotificationActionViewModel : ViewModelBase
{
    /// <summary>Creates an action button view-model from a descriptor.</summary>
    /// <param name="descriptor">The caller-supplied action.</param>
    /// <param name="requestClose">
    /// Callback invoked after the action runs when
    /// <see cref="NotificationActionDescriptor.DismissOnInvoke"/> is set.
    /// </param>
    public NotificationActionViewModel(NotificationActionDescriptor descriptor, Action requestClose)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(requestClose);

        Label = descriptor.Label;
        IsPrimary = descriptor.IsPrimary;
        Command = new RelayCommand(() =>
        {
            descriptor.Invoke();
            if (descriptor.DismissOnInvoke)
                requestClose();
        });
    }

    /// <summary>The button caption (caller-localized).</summary>
    public string Label { get; }

    /// <summary>When <see langword="true"/> the button is styled as primary.</summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// When <see langword="true"/> the button is styled as a secondary
    /// (outline) action. The complement of <see cref="IsPrimary"/>; exposed
    /// for direct XAML class binding.
    /// </summary>
    public bool IsSecondary => !IsPrimary;

    /// <summary>Runs the action and optionally dismisses the notification.</summary>
    public ICommand Command { get; }
}
