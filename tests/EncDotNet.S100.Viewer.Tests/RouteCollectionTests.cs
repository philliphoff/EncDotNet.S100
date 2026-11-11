using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Tests;

public class RouteCollectionTests
{
    [Fact]
    public void NewCollection_IsEmptyWithNoActiveRoute()
    {
        var collection = new RouteCollection();
        Assert.Empty(collection.Routes);
        Assert.Null(collection.ActiveRoute);
    }

    [Fact]
    public void CreateRoute_AddsAndActivates()
    {
        var collection = new RouteCollection();
        var route = collection.CreateRoute("Channel");
        Assert.Single(collection.Routes);
        Assert.Same(route, collection.ActiveRoute);
        Assert.Equal("Channel", route.Name);
    }

    [Fact]
    public void CreateRoute_SecondRoute_BecomesActive()
    {
        var collection = new RouteCollection();
        collection.CreateRoute("first");
        var second = collection.CreateRoute("second");
        Assert.Same(second, collection.ActiveRoute);
    }

    [Fact]
    public void Add_DuplicateId_Throws()
    {
        var collection = new RouteCollection();
        collection.CreateRoute(id: "dup");
        Assert.Throws<ArgumentException>(() => collection.Add(new Route("dup")));
    }

    [Fact]
    public void Remove_ActiveRoute_PromotesNeighbour()
    {
        var collection = new RouteCollection();
        var a = collection.CreateRoute("a");
        var b = collection.CreateRoute("b");
        var c = collection.CreateRoute("c");

        collection.SetActiveRoute(b);
        Assert.True(collection.Remove(b));

        // Slot of removed route now holds c.
        Assert.Same(c, collection.ActiveRoute);
        Assert.Equal(2, collection.Routes.Count);
        Assert.DoesNotContain(b, collection.Routes);
        Assert.Contains(a, collection.Routes);
    }

    [Fact]
    public void Remove_LastRemaining_ClearsActive()
    {
        var collection = new RouteCollection();
        var a = collection.CreateRoute("a");
        collection.Remove(a);
        Assert.Null(collection.ActiveRoute);
        Assert.Empty(collection.Routes);
    }

    [Fact]
    public void Remove_NonMember_ReturnsFalse()
    {
        var collection = new RouteCollection();
        Assert.False(collection.Remove(new Route()));
    }

    [Fact]
    public void Remove_NonActiveRoute_LeavesActiveUnchanged()
    {
        var collection = new RouteCollection();
        var a = collection.CreateRoute("a");
        var b = collection.CreateRoute("b");
        collection.SetActiveRoute(a);

        collection.Remove(b);

        Assert.Same(a, collection.ActiveRoute);
    }

    [Fact]
    public void SetActiveRoute_NonMember_Throws()
    {
        var collection = new RouteCollection();
        collection.CreateRoute("a");
        Assert.Throws<ArgumentException>(() => collection.SetActiveRoute(new Route()));
    }

    [Fact]
    public void SetActiveRoute_Null_ClearsSelection()
    {
        var collection = new RouteCollection();
        collection.CreateRoute("a");
        Assert.True(collection.SetActiveRoute(null));
        Assert.Null(collection.ActiveRoute);
    }

    [Fact]
    public void FindById_ReturnsMatchOrNull()
    {
        var collection = new RouteCollection();
        var a = collection.CreateRoute(id: "route-x");
        Assert.Same(a, collection.FindById("route-x"));
        Assert.Null(collection.FindById("missing"));
    }

    [Fact]
    public void Changed_RaisedOnAddRemoveAndActiveChange()
    {
        var collection = new RouteCollection();
        var count = 0;
        collection.Changed += (_, _) => count++;

        var a = collection.CreateRoute("a"); // add
        var b = collection.CreateRoute("b"); // add
        collection.SetActiveRoute(a);        // active change
        collection.Remove(b);                // remove

        Assert.Equal(4, count);
    }

    [Fact]
    public void Changed_BubblesFromMemberRouteEdits()
    {
        var collection = new RouteCollection();
        var route = collection.CreateRoute("a");
        var count = 0;
        collection.Changed += (_, _) => count++;

        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(40.0, -74.0));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Changed_NotRaisedAfterRouteRemoved()
    {
        var collection = new RouteCollection();
        var route = collection.CreateRoute("a");
        collection.Remove(route);

        var count = 0;
        collection.Changed += (_, _) => count++;
        route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(40.0, -74.0));

        Assert.Equal(0, count);
    }
}
