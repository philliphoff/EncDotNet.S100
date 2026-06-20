using EncDotNet.S100.Viewer.Services.Notifications;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests.Notifications;

/// <summary>
/// Factory helpers that build a real <see cref="NotificationService"/> wired
/// to the synchronous <see cref="ImmediateUiDispatcher"/> for headless tests.
/// </summary>
internal static class TestNotifications
{
    /// <summary>Creates a service backed by a <see cref="FakeTimeProvider"/>.</summary>
    public static NotificationService Create(out FakeTimeProvider time)
    {
        time = new FakeTimeProvider();
        return new NotificationService(new ImmediateUiDispatcher(), time);
    }

    /// <summary>Creates a service with a throwaway time provider.</summary>
    public static NotificationService Create() => Create(out _);
}
