using EncDotNet.S100.Viewer.Services.Notifications;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests.Notifications;

/// <summary>
/// Headless unit tests for the custom notification service, exercised through
/// a synchronous <see cref="ImmediateUiDispatcher"/> and a
/// <see cref="FakeTimeProvider"/> so auto-dismiss timing is deterministic.
/// </summary>
public class NotificationServiceTests
{
    [Fact]
    public void Builder_ComposesOptionalRegions()
    {
        var service = TestNotifications.Create();

        var handle = service.Create("Title")
            .WithSeverity(NotificationSeverity.Warning)
            .WithContent("body")
            .AsProgress(0.5)
            .WithAction("Do", () => { })
            .Persistent()
            .Show();

        var vm = service.Active.Single();
        Assert.Equal(handle.Id, vm.Id);
        Assert.Equal(NotificationSeverity.Warning, vm.Severity);
        Assert.True(vm.HasMessage);
        Assert.True(vm.HasProgress);
        Assert.True(vm.HasActions);
        Assert.True(vm.IsPersistent);
    }

    [Fact]
    public void WithContent_NullOrEmpty_LeavesMessageEmpty()
    {
        var service = TestNotifications.Create();

        service.Create("Title").WithContent(null).Show();

        Assert.False(service.Active.Single().HasMessage);
    }

    [Fact]
    public void Ephemeral_ExpiresToHistoryAfterDefaultDelay()
    {
        var service = TestNotifications.Create(out var time);

        service.Create("Title").WithSeverity(NotificationSeverity.Success).Show();
        Assert.Single(service.Active);

        time.Advance(NotificationService.DefaultDelayFor(NotificationSeverity.Success));

        Assert.Empty(service.Active);
        Assert.Single(service.History);
    }

    [Fact]
    public void Persistent_NeverAutoExpires()
    {
        var service = TestNotifications.Create(out var time);

        service.Create("Title").Persistent().Show();

        time.Advance(TimeSpan.FromHours(1));

        Assert.Single(service.Active);
        Assert.Empty(service.History);
    }

    [Fact]
    public void Handle_Update_MutatesViewModel()
    {
        var service = TestNotifications.Create();
        var handle = service.Create("Title").Persistent().Show();

        handle.Update(title: "New", message: "msg", severity: NotificationSeverity.Error);

        var vm = service.Active.Single();
        Assert.Equal("New", vm.Title);
        Assert.Equal("msg", vm.Message);
        Assert.Equal(NotificationSeverity.Error, vm.Severity);
    }

    [Fact]
    public void Handle_Report_SetsDeterminateProgress()
    {
        var service = TestNotifications.Create();
        var handle = service.Create("Title").AsProgress(indeterminate: true).Persistent().Show();

        handle.Report(0.42);

        var vm = service.Active.Single();
        Assert.False(vm.IsIndeterminate);
        Assert.Equal(0.42, vm.Progress);
    }

    [Fact]
    public void Handle_SetActions_ReplacesActions()
    {
        var service = TestNotifications.Create();
        var handle = service.Create("Title").WithAction("A", () => { }).Persistent().Show();

        handle.SetActions(new NotificationActionDescriptor("B", () => { }));
        Assert.Equal("B", service.Active.Single().Actions.Single().Label);

        handle.SetActions();
        Assert.False(service.Active.Single().HasActions);
    }

    [Fact]
    public void Dismiss_FiresDismissedOnceAndMovesToHistory()
    {
        var service = TestNotifications.Create();
        var handle = service.Create("Title").Persistent().Show();
        var fired = 0;
        handle.Dismissed += (_, _) => fired++;

        handle.Dismiss();
        handle.Dismiss();

        Assert.True(handle.IsDismissed);
        Assert.Equal(1, fired);
        Assert.Empty(service.Active);
        Assert.Single(service.History);
    }

    [Fact]
    public void Mutation_AfterDismiss_IsNoOp()
    {
        var service = TestNotifications.Create();
        var handle = service.Create("Title").Persistent().Show();
        handle.Dismiss();

        handle.Update(title: "Ignored");

        Assert.Equal("Title", service.History.Single().Title);
    }

    [Fact]
    public void History_RingBuffer_CapsAndEvictsOldest()
    {
        var service = new NotificationService(
            new ImmediateUiDispatcher(), new FakeTimeProvider(), historyCapacity: 3);

        for (var i = 0; i < 5; i++)
            service.Create($"N{i}").Persistent().Show().Dismiss();

        Assert.Equal(3, service.History.Count);
        // Newest first; the two oldest (N0, N1) are evicted.
        Assert.Equal("N4", service.History[0].Title);
        Assert.Equal("N2", service.History[2].Title);
    }

    [Fact]
    public void ClearHistory_EmptiesHistory()
    {
        var service = TestNotifications.Create();
        service.Create("Title").Persistent().Show().Dismiss();
        Assert.Single(service.History);

        service.ClearHistory();

        Assert.Empty(service.History);
    }

    [Fact]
    public void DismissAll_MovesActiveToHistory()
    {
        var service = TestNotifications.Create();
        service.Create("A").Persistent().Show();
        service.Create("B").Persistent().Show();

        service.DismissAll();

        Assert.Empty(service.Active);
        Assert.Equal(2, service.History.Count);
    }

    [Fact]
    public void ProgressHandle_DrivenToTerminalState_AutoDismisses()
    {
        // Mirrors the loader flow: one persistent progress notification that is
        // mutated to a terminal Success state and scheduled for auto-dismiss.
        var service = TestNotifications.Create(out var time);
        var handle = service.Create("Loading")
            .AsProgress(indeterminate: true)
            .Persistent()
            .WithAction("Cancel", () => { }, dismissOnInvoke: false)
            .Show();

        handle.ClearProgress();
        handle.SetActions();
        handle.Update(title: "Loaded", severity: NotificationSeverity.Success);
        handle.ScheduleAutoDismiss(NotificationService.DefaultDelayFor(NotificationSeverity.Success));

        var vm = service.Active.Single();
        Assert.False(vm.HasProgress);
        Assert.False(vm.HasActions);
        Assert.Equal(NotificationSeverity.Success, vm.Severity);

        time.Advance(NotificationService.DefaultDelayFor(NotificationSeverity.Success));

        Assert.Empty(service.Active);
        Assert.Single(service.History);
    }
}
