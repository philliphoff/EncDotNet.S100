using System;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// PR-M1: <see cref="ActivityTabViewTemplate"/> contract — only matches
/// <see cref="IActivityTab"/> and instantiates the tab's
/// <see cref="IActivityTab.ViewType"/> with its <see cref="IActivityTab.ViewModel"/>
/// wired up as the <c>DataContext</c>.
/// </summary>
public sealed class ActivityTabViewTemplateTests
{
    private sealed class StubView : ContentControl
    {
    }

    private sealed class StubTab : IActivityTab
    {
        public string Id => "Stub";
        public int Order => 0;
        public string Title => "Stub";
        public string Tooltip => "Stub";
        public required object ViewModel { get; init; }
        public Type ViewType => typeof(StubView);
        public bool PersistAsLastSelected => true;

        public Control CreateIcon() => new ContentControl();
    }

    [Fact]
    public void Match_OnlyAcceptsIActivityTab()
    {
        var template = new ActivityTabViewTemplate();

        Assert.True(template.Match(new StubTab { ViewModel = new object() }));
        Assert.False(template.Match(null));
        Assert.False(template.Match(new object()));
        Assert.False(template.Match("not a tab"));
    }

    [Fact]
    public void Build_InstantiatesViewTypeAndSetsDataContext()
    {
        var template = new ActivityTabViewTemplate();
        var vm = new object();
        var tab = new StubTab { ViewModel = vm };

        var control = template.Build(tab);

        Assert.NotNull(control);
        Assert.IsType<StubView>(control);
        Assert.Same(vm, control!.DataContext);
    }

    [Fact]
    public void Build_NullInput_ReturnsNull()
    {
        var template = new ActivityTabViewTemplate();

        Assert.Null(template.Build(null));
        Assert.Null(template.Build(new object()));
    }
}
