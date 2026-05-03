using System;
using System.Collections.Generic;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class RecentFilesServiceTests
{
    private static (RecentFilesService Service, ViewerSettings Settings) Create()
    {
        var settings = new ViewerSettings();
        return (new RecentFilesService(settings), settings);
    }

    [Fact]
    public void Add_StoresPath_AndRaisesChanged()
    {
        var (svc, settings) = Create();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.Add("/tmp/a.000");

        Assert.Equal(new[] { "/tmp/a.000" }, svc.Items);
        Assert.Equal(new[] { "/tmp/a.000" }, settings.RecentDatasetPaths);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Add_IgnoresNullOrWhitespace()
    {
        var (svc, _) = Create();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.Add("");
        svc.Add("   ");

        Assert.Empty(svc.Items);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Add_DeduplicatesAndPromotes()
    {
        var (svc, _) = Create();
        svc.Add("/tmp/a");
        svc.Add("/tmp/b");
        svc.Add("/tmp/a");

        Assert.Equal(new[] { "/tmp/a", "/tmp/b" }, svc.Items);
    }

    [Fact]
    public void Remove_DropsPath_AndRaisesChanged_OnlyWhenSomethingRemoved()
    {
        var (svc, _) = Create();
        svc.Add("/tmp/a");
        svc.Add("/tmp/b");

        var fired = 0;
        svc.Changed += () => fired++;

        svc.Remove("/tmp/missing");
        Assert.Equal(0, fired);

        svc.Remove("/tmp/a");
        Assert.Equal(new[] { "/tmp/b" }, svc.Items);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clear_EmptiesList_AndRaisesChanged_OnlyWhenNonEmpty()
    {
        var (svc, _) = Create();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.Clear();
        Assert.Equal(0, fired);

        svc.Add("/tmp/a");
        fired = 0;
        svc.Clear();

        Assert.Empty(svc.Items);
        Assert.Equal(1, fired);
    }
}
