using System;
using System.ComponentModel;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class ActivityTabVisibilityTests
{
    private sealed class FakeVisibility : ITabVisibilitySource
    {
        private bool _isVisible;

        public FakeVisibility(bool initial) => _isVisible = initial;

        public bool IsVisible => _isVisible;

        public event Action<bool>? VisibilityChanged;

        public void Set(bool value)
        {
            _isVisible = value;
            VisibilityChanged?.Invoke(value);
        }
    }

    private static ActivityTab<object, ContentControl> CreateTab(ITabVisibilitySource? visibility)
        => new(
            id: "Helm",
            order: 67,
            title: "Helm",
            tooltip: "Helm",
            iconFactory: () => new ContentControl(),
            viewModel: new object(),
            persistAsLastSelected: false,
            dock: TabDock.Left,
            autoOpenOnContentSignal: false,
            visibility: visibility);

    [Fact]
    public void IsVisible_DefaultsToTrue_WithoutVisibilitySource()
    {
        using var tab = CreateTab(visibility: null);
        Assert.True(tab.IsVisible);
    }

    [Fact]
    public void IsVisible_SeedsFromVisibilitySource()
    {
        using var tab = CreateTab(new FakeVisibility(initial: false));
        Assert.False(tab.IsVisible);
    }

    [Fact]
    public void VisibilityChanged_RaisesPropertyChanged()
    {
        var source = new FakeVisibility(initial: false);
        using var tab = CreateTab(source);

        var raised = 0;
        ((INotifyPropertyChanged)tab).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IActivityTab.IsVisible)) raised++;
        };

        source.Set(true);

        Assert.True(tab.IsVisible);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Dispose_UnsubscribesFromVisibilitySource()
    {
        var source = new FakeVisibility(initial: true);
        var tab = CreateTab(source);
        tab.Dispose();

        var raised = 0;
        ((INotifyPropertyChanged)tab).PropertyChanged += (_, _) => raised++;

        source.Set(false);

        Assert.True(tab.IsVisible); // unchanged after dispose
        Assert.Equal(0, raised);
    }
}

public sealed class OwnShipTrackingVisibilitySourceTests
{
    [Fact]
    public void IsVisible_ReflectsOverlayEnabled()
    {
        var settings = new ViewerSettings { OwnShipOverlayEnabled = true };
        var svm = new SettingsViewModel(settings);
        using var source = new OwnShipTrackingVisibilitySource(svm);

        Assert.True(source.IsVisible);
    }

    [Fact]
    public void VisibilityChanged_RaisedOnOverlayToggle()
    {
        var settings = new ViewerSettings { OwnShipOverlayEnabled = false };
        var svm = new SettingsViewModel(settings);
        using var source = new OwnShipTrackingVisibilitySource(svm);

        bool? last = null;
        source.VisibilityChanged += v => last = v;

        svm.OwnShipOverlayEnabled = true;

        Assert.True(source.IsVisible);
        Assert.Equal(true, last);
    }
}

public sealed class AisOverlayVisibilitySourceTests
{
    [Fact]
    public void IsVisible_ReflectsAisEnabled()
    {
        var settings = new ViewerSettings { AisOverlay = new AisOverlaySettings { Enabled = true } };
        var svm = new SettingsViewModel(settings);
        using var source = new AisOverlayVisibilitySource(svm);

        Assert.True(source.IsVisible);
    }

    [Fact]
    public void VisibilityChanged_RaisedOnAisToggle()
    {
        var settings = new ViewerSettings { AisOverlay = new AisOverlaySettings { Enabled = false } };
        var svm = new SettingsViewModel(settings);
        using var source = new AisOverlayVisibilitySource(svm);

        bool? last = null;
        source.VisibilityChanged += v => last = v;

        svm.AisEnabled = true;

        Assert.True(source.IsVisible);
        Assert.Equal(true, last);
    }

    [Fact]
    public void Dispose_UnsubscribesFromSettings()
    {
        var settings = new ViewerSettings { AisOverlay = new AisOverlaySettings { Enabled = false } };
        var svm = new SettingsViewModel(settings);
        var source = new AisOverlayVisibilitySource(svm);
        source.Dispose();

        var raised = 0;
        source.VisibilityChanged += _ => raised++;

        svm.AisEnabled = true;

        Assert.Equal(0, raised);
    }
}
