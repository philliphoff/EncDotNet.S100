using System.Text.Json;
using EncDotNet.S100.Viewer;

namespace EncDotNet.S100.Viewer.Tests;

public class OwnShipSettingsTests
{
    [Fact]
    public void Defaults_AreSensibleVisibleVessel()
    {
        var s = new OwnShipSettings();
        Assert.Equal(50, s.LengthMetres);
        Assert.Equal(10, s.BeamMetres);
        Assert.Equal(25, s.BowOffsetMetres);
        Assert.Equal(5, s.PortOffsetMetres);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var s = new OwnShipSettings
        {
            LengthMetres = 120, BeamMetres = 18,
            BowOffsetMetres = 90, PortOffsetMetres = 9,
        };
        var json = JsonSerializer.Serialize(s);
        var back = JsonSerializer.Deserialize<OwnShipSettings>(json)!;

        Assert.Equal(120, back.LengthMetres);
        Assert.Equal(18, back.BeamMetres);
        Assert.Equal(90, back.BowOffsetMetres);
        Assert.Equal(9, back.PortOffsetMetres);
    }

    [Fact]
    public void ViewerSettings_OwnShipDefaultsToNullForBackwardCompatibility()
    {
        var v = new ViewerSettings();
        Assert.Null(v.OwnShip);
    }

    [Fact]
    public void ViewerSettings_JsonWithoutOwnShipBlock_LeavesPropertyNull()
    {
        var json = "{}";
        var back = JsonSerializer.Deserialize<ViewerSettings>(json)!;
        Assert.Null(back.OwnShip);
    }
}
