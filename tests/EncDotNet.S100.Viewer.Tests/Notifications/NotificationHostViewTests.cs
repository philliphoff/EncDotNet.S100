using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.Views.Notifications;

namespace EncDotNet.S100.Viewer.Tests.Notifications;

public sealed class NotificationHostViewTests
{
    [Fact]
    public void FourActions_LoadAndBindWithoutError()
    {
        HeadlessTest.Run(() =>
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

            var host = new NotificationHost { ItemsSource = notifications.Active };
            var window = new Window { Content = host, Width = 420, Height = 320 };
            window.Show();
            window.Measure(new Size(420, 320));
            window.Arrange(new Rect(0, 0, 420, 320));

            Assert.Same(notifications.Active, host.ItemsSource);
            Assert.Equal(4, notifications.Active.Single().Actions.Count);
            window.Close();
        });
    }
}
