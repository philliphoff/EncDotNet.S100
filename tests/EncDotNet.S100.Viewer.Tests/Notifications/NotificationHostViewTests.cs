using Avalonia.Controls;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Views.Notifications;

namespace EncDotNet.S100.Viewer.Tests.Notifications;

/// <summary>
/// The real notification host over a real <see cref="NotificationService"/>,
/// driven by clicks: cards show, their actions and close button work, they
/// auto-dismiss on the service's clock, and a long message can be expanded.
/// </summary>
public sealed class NotificationHostViewTests
{
    [AvaloniaFact]
    public void FourActions_LoadAndBindWithoutError()
    {
        var notifications = TestNotifications.Create();
        notifications.Create("Viewer update available")
            .WithContent("Version 2.5.0 is available to download from GitHub.")
            .WithAction("View release", static () => { }, isPrimary: true)
            .WithAction("Remind me later", static () => { })
            .WithAction("Skip this version", static () => { })
            .WithAction("Stop checking", static () => { })
            .Persistent()
            .Show();

        using var host = Show(notifications);

        Assert.Equal(4, host.FindAll<Button>("Notification.Action").Count);
    }

    [AvaloniaFact]
    public void Clicking_an_action_runs_it_and_dismisses_the_card()
    {
        var notifications = TestNotifications.Create();
        var viewed = 0;
        notifications.Create("Viewer update available")
            .WithAction("View release", () => viewed++, isPrimary: true)
            .WithAction("Remind me later", static () => { })
            .Persistent()
            .Show();
        using var host = Show(notifications);

        host.Click(host.FindAll<Button>("Notification.Action").Single(b => Equals(b.Content, "View release")));

        Assert.Equal(1, viewed);
        Assert.Empty(notifications.Active);
        Assert.False(host.IsShown("Notification.Card"));
    }

    [AvaloniaFact]
    public void An_action_that_keeps_the_card_leaves_it_open()
    {
        var notifications = TestNotifications.Create();
        var copied = 0;
        notifications.Create("Crash report")
            .WithAction("Copy details", () => copied++, dismissOnInvoke: false)
            .Persistent()
            .Show();
        using var host = Show(notifications);

        host.Click(host.Find<Button>("Notification.Action"));

        Assert.Equal(1, copied);
        Assert.True(host.IsShown("Notification.Card"));
    }

    [AvaloniaFact]
    public void The_close_button_dismisses_only_its_card()
    {
        var notifications = TestNotifications.Create();
        notifications.Create("First").Persistent().Show();
        notifications.Create("Second").Persistent().Show();
        using var host = Show(notifications);
        var first = notifications.Active.Single(n => n.Title == "First");

        var card = host.FindAll<Border>("Notification.Card").Single(b => ReferenceEquals(b.DataContext, first));
        host.Click(host.Find<Button>("Notification.Close", card));

        Assert.Equal(["Second"], notifications.Active.Select(n => n.Title));
        Assert.Single(host.FindAll<Border>("Notification.Card"));
    }

    [AvaloniaFact]
    public void A_card_auto_dismisses_on_the_services_clock()
    {
        var notifications = TestNotifications.Create(out var time);
        notifications.Create("Dataset loaded").AutoDismiss(TimeSpan.FromSeconds(5)).Show();
        using var host = Show(notifications);
        Assert.True(host.IsShown("Notification.Card"));

        time.Advance(TimeSpan.FromSeconds(4));
        host.Settle();
        Assert.True(host.IsShown("Notification.Card"));

        time.Advance(TimeSpan.FromSeconds(2));
        host.Settle();
        Assert.False(host.IsShown("Notification.Card"));
    }

    [AvaloniaFact]
    public void A_long_message_is_clamped_until_show_more_expands_it()
    {
        var notifications = TestNotifications.Create();
        notifications.Create("Validation finished")
            .WithContent(string.Join(" ", Enumerable.Repeat(
                "Feature 0123 has an attribute value outside the allowed range for its product specification.", 12)))
            .Persistent()
            .Show();
        using var host = Show(notifications);
        var body = host.Find<TextBlock>("Notification.Body");
        var clamped = body.Bounds.Height;

        // The toggle only appears because layout found the text truncated.
        host.Click(host.Find<Button>("Notification.ToggleExpand"));

        Assert.True(notifications.Active.Single().IsExpanded);
        Assert.True(body.Bounds.Height > clamped);
    }

    private static ViewHost Show(NotificationService notifications)
        => ViewHost.Show(new NotificationHost { ItemsSource = notifications.Active }, width: 420, height: 600);
}
