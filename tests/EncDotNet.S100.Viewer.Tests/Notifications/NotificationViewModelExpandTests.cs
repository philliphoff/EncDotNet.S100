using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels.Notifications;

namespace EncDotNet.S100.Viewer.Tests.Notifications;

/// <summary>
/// Unit tests for the body expand/collapse ("Show more" / "Show less") state
/// on <see cref="NotificationViewModel"/>.
/// </summary>
public class NotificationViewModelExpandTests
{
    private static NotificationViewModel Create(string? message) =>
        new(Guid.NewGuid(), NotificationSeverity.Info, "Title", message, isPersistent: true);

    [Fact]
    public void Collapsed_NotTruncated_HidesToggle()
    {
        var vm = Create("short");

        Assert.False(vm.IsExpanded);
        Assert.Equal(2, vm.BodyMaxLines);
        Assert.False(vm.ShowExpandToggle);
        Assert.Equal(Strings.Notification_ShowMore, vm.ToggleExpandLabel);
    }

    [Fact]
    public void Truncated_ShowsShowMore()
    {
        var vm = Create("a long body that the host has reported as clipped");

        vm.IsMessageTruncated = true;

        Assert.True(vm.ShowExpandToggle);
        Assert.Equal(Strings.Notification_ShowMore, vm.ToggleExpandLabel);
        Assert.Equal(2, vm.BodyMaxLines);
    }

    [Fact]
    public void Toggle_ExpandsAndCollapses()
    {
        var vm = Create("a long body that the host has reported as clipped");
        vm.IsMessageTruncated = true;

        vm.ToggleExpandCommand.Execute(null);

        Assert.True(vm.IsExpanded);
        Assert.Equal(0, vm.BodyMaxLines);
        Assert.True(vm.ShowExpandToggle);
        Assert.Equal(Strings.Notification_ShowLess, vm.ToggleExpandLabel);

        vm.ToggleExpandCommand.Execute(null);

        Assert.False(vm.IsExpanded);
        Assert.Equal(2, vm.BodyMaxLines);
        Assert.Equal(Strings.Notification_ShowMore, vm.ToggleExpandLabel);
    }

    [Fact]
    public void Expanded_KeepsToggleEvenWhenNotTruncated()
    {
        var vm = Create("body");
        vm.IsExpanded = true;

        // While expanded the host stops reporting truncation, but the link must
        // remain visible so the user can collapse again.
        vm.IsMessageTruncated = false;

        Assert.True(vm.ShowExpandToggle);
        Assert.Equal(Strings.Notification_ShowLess, vm.ToggleExpandLabel);
    }

    [Fact]
    public void ChangingMessage_ResetsExpansion()
    {
        var vm = Create("original long clipped body");
        vm.IsMessageTruncated = true;
        vm.IsExpanded = true;

        vm.Message = "a replacement body";

        Assert.False(vm.IsExpanded);
        Assert.Equal(2, vm.BodyMaxLines);
    }

    [Fact]
    public void IsExpanded_RaisesDerivedPropertyChanges()
    {
        var vm = Create("clipped body");
        vm.IsMessageTruncated = true;

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsExpanded = true;

        Assert.Contains(nameof(NotificationViewModel.IsExpanded), changed);
        Assert.Contains(nameof(NotificationViewModel.BodyMaxLines), changed);
        Assert.Contains(nameof(NotificationViewModel.ToggleExpandLabel), changed);
        Assert.Contains(nameof(NotificationViewModel.ShowExpandToggle), changed);
    }
}
