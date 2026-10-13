using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Passage values and per-leg overrides on <see cref="Route"/> (#897).</summary>
public class RoutePassageTests
{
    private static Route ThreeLegRoute()
    {
        var route = new Route("r");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.1));
        route.AppendWaypoint(new GeoPosition(50.2, -1.2));
        route.AppendWaypoint(new GeoPosition(50.3, -1.3));
        return route;
    }

    [Fact]
    public void NewRoute_HasEmptyPassage_AndLegsInheritNothing()
    {
        var route = ThreeLegRoute();

        Assert.True(route.Passage.IsEmpty);
        foreach (var field in RoutePassageFields.LegOverridable)
        {
            Assert.Null(route.GetEffectiveLegValue(0, field));
            Assert.False(route.IsLegOverridden(0, field));
        }
    }

    [Fact]
    public void ChangingPassageSpeed_ChangesInheritedLegsOnly()
    {
        var route = ThreeLegRoute();
        route.SetPassageValue(RoutePassageField.Speed, 10.0);
        route.SetLegOverride(1, RoutePassageField.Speed, 6.0);

        route.SetPassageValue(RoutePassageField.Speed, 12.0);

        Assert.Equal(12.0, route.GetEffectiveLegValue(0, RoutePassageField.Speed));
        Assert.Equal(6.0, route.GetEffectiveLegValue(1, RoutePassageField.Speed));
        Assert.Equal(12.0, route.GetEffectiveLegValue(2, RoutePassageField.Speed));
        Assert.Null(route.Legs[0].PlannedSpeedKnots);
        Assert.Equal(6.0, route.Legs[1].PlannedSpeedKnots);
    }

    [Fact]
    public void SetLegOverride_EqualToPassage_StoresNoOverride()
    {
        var route = ThreeLegRoute();
        route.SetPassageValue(RoutePassageField.Draft, 4.5);

        route.SetLegOverride(0, RoutePassageField.Draft, 4.5);

        Assert.False(route.IsLegOverridden(0, RoutePassageField.Draft));
        Assert.Null(route.Legs[0].DraftMeters);
        Assert.Equal(4.5, route.GetEffectiveLegValue(0, RoutePassageField.Draft));
    }

    [Fact]
    public void SetLegOverride_EqualToPassage_RemovesAnExistingOverride()
    {
        var route = ThreeLegRoute();
        route.SetPassageValue(RoutePassageField.Draft, 4.5);
        route.SetLegOverride(0, RoutePassageField.Draft, 5.0);

        var previous = route.SetLegOverride(0, RoutePassageField.Draft, 4.5);

        Assert.Equal(5.0, previous);
        Assert.False(route.IsLegOverridden(0, RoutePassageField.Draft));
    }

    [Fact]
    public void ResetLegOverride_ReturnsToInheriting_AndReturnsRemovedValue()
    {
        var route = ThreeLegRoute();
        route.SetPassageValue(RoutePassageField.RequiredUkc, 0.5);
        route.SetLegOverride(2, RoutePassageField.RequiredUkc, 1.2);
        Assert.True(route.IsLegOverridden(2, RoutePassageField.RequiredUkc));

        var removed = route.ResetLegOverride(2, RoutePassageField.RequiredUkc);

        Assert.Equal(1.2, removed);
        Assert.False(route.IsLegOverridden(2, RoutePassageField.RequiredUkc));
        Assert.Equal(0.5, route.GetEffectiveLegValue(2, RoutePassageField.RequiredUkc));
    }

    [Fact]
    public void LegOverrides_MapOntoTheS421LegFields()
    {
        var route = ThreeLegRoute();
        route.SetLegOverride(0, RoutePassageField.Draft, 1);
        route.SetLegOverride(0, RoutePassageField.Speed, 2);
        route.SetLegOverride(0, RoutePassageField.RequiredUkc, 3);
        route.SetLegOverride(0, RoutePassageField.PortCrossTrack, 4);
        route.SetLegOverride(0, RoutePassageField.StarboardCrossTrack, 5);

        var leg = route.Legs[0];
        Assert.Equal(1, leg.DraftMeters);
        Assert.Equal(2, leg.PlannedSpeedKnots);
        Assert.Equal(3, leg.StaticUnderKeelClearanceMeters);
        Assert.Equal(4, leg.PortCrossTrackDistanceLimitMeters);
        Assert.Equal(5, leg.StarboardCrossTrackDistanceLimitMeters);
    }

    [Fact]
    public void Squat_IsRouteLevelOnly()
    {
        var route = ThreeLegRoute();

        Assert.False(RoutePassageFields.IsLegOverridable(RoutePassageField.Squat));
        Assert.DoesNotContain(RoutePassageField.Squat, RoutePassageFields.LegOverridable);
        Assert.Throws<ArgumentException>(() => route.SetLegOverride(0, RoutePassageField.Squat, 0.3));
    }

    [Fact]
    public void Edits_RaiseChangedOnlyWhenSomethingChanges()
    {
        var route = ThreeLegRoute();
        var raised = 0;
        route.Changed += (_, _) => raised++;

        route.SetPassageValue(RoutePassageField.Speed, 8.0);
        route.SetPassageValue(RoutePassageField.Speed, 8.0);
        route.SetLegOverride(0, RoutePassageField.Speed, 8.0); // equal to passage: no override
        route.ResetLegOverride(0, RoutePassageField.Speed);    // already inheriting
        var departure = new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero);
        route.SetDeparture(departure);
        route.SetDeparture(departure);

        Assert.Equal(2, raised);
        Assert.Equal(departure, route.Passage.Departure);
    }

    [Fact]
    public void Mutators_ReturnThePreviousValue_SoEditsCanBeReverted()
    {
        var route = ThreeLegRoute();
        Assert.Null(route.SetPassageValue(RoutePassageField.Speed, 8.0));
        Assert.Equal(8.0, route.SetPassageValue(RoutePassageField.Speed, 9.0));
        Assert.Null(route.SetLegOverride(1, RoutePassageField.Speed, 5.0));
        Assert.Equal(5.0, route.SetLegOverride(1, RoutePassageField.Speed, 6.0));
        Assert.Null(route.SetDeparture(DateTimeOffset.UnixEpoch));
        Assert.Equal(DateTimeOffset.UnixEpoch, route.SetDeparture(null));
    }

    [Fact]
    public void NonFiniteValues_AreRejected()
    {
        var route = ThreeLegRoute();

        Assert.Throws<ArgumentOutOfRangeException>(() => route.SetPassageValue(RoutePassageField.Draft, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.SetLegOverride(0, RoutePassageField.Draft, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.SetLegOverride(5, RoutePassageField.Draft, 1.0));
    }

    [Fact]
    public void Overrides_TravelWithTheirSegment_OnReverse()
    {
        var route = ThreeLegRoute();
        route.SetLegOverride(0, RoutePassageField.Speed, 4.0);

        route.Reverse();

        Assert.Equal(4.0, route.Legs[2].PlannedSpeedKnots);
        Assert.False(route.IsLegOverridden(0, RoutePassageField.Speed));
    }
}
