using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace EncDotNet.S100.Routes.Rtz.Tests;

/// <summary>
/// Reads the hand-written RTZ 1.0, 1.1 and 1.2 fixtures and writes them back
/// in each version (#961).
/// </summary>
public class RtzRoundTripTests
{
    public static TheoryData<string, string> Fixtures => new()
    {
        { "rtz-1.0-solent.rtz", RtzVersions.V1_0 },
        { "rtz-1.1-stm.rtz", RtzVersions.V1_1 },
        { "rtz-1.2-pas.rtz", RtzVersions.V1_2 },
    };

    internal static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Sniff_names_the_fixture_version(string fixture, string version)
    {
        var head = File.ReadAllText(FixturePath(fixture));
        Assert.Equal(version, RtzVersions.Sniff(head));
    }

    [Fact]
    public void Sniff_rejects_other_xml()
    {
        Assert.Null(RtzVersions.Sniff("<?xml version=\"1.0\"?><Dataset xmlns=\"http://www.iho.int/S421/gml/cs0/1.0\"/>"));
        Assert.Null(RtzVersions.Sniff("<gpx xmlns=\"http://www.topografix.com/GPX/1/1\"><rte/></gpx>"));
        Assert.Equal("1.3", RtzVersions.Sniff("<route xmlns=\"http://www.cirm.org/RTZ/1/3\" version=\"1.3\">"));
        Assert.False(RtzVersions.IsSupported("1.3"));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Reading_and_writing_the_same_version_keeps_everything(string fixture, string version)
    {
        var read = RtzReader.Read(FixturePath(fixture));
        Assert.Equal(version, read.Version);

        var written = RtzWriter.ToXml(read);
        var reread = RtzReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(written)));

        Assert.Equal(Dump(read), Dump(reread));
        Assert.Equal(written, RtzWriter.ToXml(reread));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Writing_another_version_keeps_the_route(string fixture, string _)
    {
        var read = RtzReader.Read(FixturePath(fixture));
        foreach (var target in RtzVersions.Supported)
        {
            var reread = RtzReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(RtzWriter.ToXml(read, target))));
            Assert.Equal(target, reread.Version);
            Assert.Equal(read.Info.RouteName, reread.Info.RouteName);
            Assert.Equal(read.Waypoints.Select(w => (w.Id, w.Name, w.Position, w.Radius)), reread.Waypoints.Select(w => (w.Id, w.Name, w.Position, w.Radius)));
            for (var i = 0; i < read.Waypoints.Count; i++)
            {
                var (a, b) = (read.GetEffectiveLeg(i), reread.GetEffectiveLeg(i));
                Assert.Equal(a?.PortsideXtd, b?.PortsideXtd);
                Assert.Equal(a?.StarboardXtd, b?.StarboardXtd);
                Assert.Equal(a?.MaxDraught, b?.MaxDraught);
                Assert.Equal(a?.StaticUkc, b?.StaticUkc);
                Assert.Equal(a?.GeometryType, b?.GeometryType);
            }
            Assert.Equal(
                read.Schedules.SelectMany(s => Tables(s)).SelectMany(t => t.Elements).Select(e => (e.WaypointId, e.Etd, e.Eta, e.Speed, e.Stay, e.EtdWindowBefore, e.EtaWindowBefore)),
                reread.Schedules.SelectMany(s => Tables(s)).SelectMany(t => t.Elements).Select(e => (e.WaypointId, e.Etd, e.Eta, e.Speed, e.Stay, e.EtdWindowBefore, e.EtaWindowBefore)));
        }
    }

    [Fact]
    public void Rtz_1_0_reads_defaults_misspelt_elements_and_time_windows()
    {
        var route = RtzReader.Read(FixturePath("rtz-1.0-solent.rtz"));

        Assert.Equal("Nab to Southampton", route.Info.RouteName);
        Assert.Equal("235000001", route.Info.VesselMmsi);
        Assert.Equal(12000, route.Info.VesselDisplacement);
        Assert.Equal("P-7", route.Info.ExtraAttributes[XName.Get("planId", "urn:example:acme")]);
        Assert.Equal("planner", Assert.Single(route.Info.Extensions).Name.LocalName);

        Assert.Equal(4, route.Waypoints.Count);
        Assert.Equal(0.3, route.GetEffectiveRadius(0));
        Assert.Equal(0.5, route.GetEffectiveRadius(1));

        // Waypoint 3's leg sets only draught and UKC; XTD comes from the default.
        var leg = route.GetEffectiveLeg(2)!;
        Assert.Equal(0.1, leg.PortsideXtd);
        Assert.Equal(8.5, leg.MaxDraught);
        Assert.Equal(RtzGeometryType.Loxodrome, leg.GeometryType);
        Assert.Equal("Keep clear of the spit", route.Waypoints[1].Leg!.LegNote2);

        var schedule = Assert.Single(route.Schedules);
        Assert.Null(schedule.Calculated);
        Assert.Same(schedule, route.GetPlanningSchedule());
        var first = schedule.Manual!.Elements[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), first.Etd);
        Assert.Equal(TimeSpan.FromMinutes(30), first.EtdWindowBefore);
        Assert.Equal(8, schedule.ElementFor(4)!.Speed);
        Assert.Equal("Pilot on board", schedule.ElementFor(4)!.Note);
    }

    [Fact]
    public void Rtz_1_1_reads_extensions_and_prefers_the_calculated_table()
    {
        var route = RtzReader.Read(FixturePath("rtz-1.1-stm.rtz"));

        var extension = Assert.Single(route.Info.Extensions);
        Assert.Equal("STM", (string?)extension.Attribute("manufacturer"));
        Assert.Equal("stm:RouteInfoExtension", (string?)extension.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")));
        Assert.Equal("http://stmvalidation.eu/STM/1/0/0", route.NamespaceDeclarations["stm"]);

        Assert.Equal(2, route.Schedules.Count);
        var planning = route.GetPlanningSchedule()!;
        Assert.Equal("Plan A", planning.Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 10, 38, 0, TimeSpan.Zero), planning.ElementFor(11)!.Eta);
        Assert.Equal(7.5, planning.ElementFor(11)!.WindSpeed);
        Assert.Equal(TimeSpan.FromHours(1), planning.Manual!.ElementFor(10)!.EtdWindowAfter);

        var trimmed = route.GetEffectiveLeg(1)!;
        Assert.Equal(7.8, trimmed.DraughtForward);
        Assert.Equal(8.4, trimmed.MaxDraught);
        Assert.Equal(38.5, trimmed.Masthead);
    }

    [Fact]
    public void Rtz_1_2_reads_leg_extensions_and_unknown_attributes()
    {
        var route = RtzReader.Read(FixturePath("rtz-1.2-pas.rtz"));

        Assert.Equal(0, route.Waypoints[0].Id);
        Assert.Equal(3, route.Waypoints[2].Revision);
        Assert.Equal("LegWeather", (string?)Assert.Single(route.Waypoints[1].Leg!.Extensions).Attribute("name"));
        Assert.Equal("high", route.Waypoints[2].Leg!.ExtraAttributes[XName.Get("priority", "urn:example:vendor")]);
        Assert.Equal(RtzGeometryType.Orthodrome, route.GetEffectiveLeg(2)!.GeometryType);
        Assert.Equal(2.5, route.GetEffectiveLeg(2)!.StaticUkc);
        Assert.Equal(TimeSpan.Zero, route.GetPlanningSchedule()!.ElementFor(1)!.Stay);
        Assert.Equal("Signature", (string?)Assert.Single(route.Extensions).Attribute("name"));
    }

    [Fact]
    public void Rtz_1_0_output_uses_the_1_0_spellings()
    {
        var route = RtzReader.Read(FixturePath("rtz-1.1-stm.rtz"));
        var xml = XDocument.Parse(RtzWriter.ToXml(route, RtzVersions.V1_0));
        XNamespace ns = "http://www.cirm.org/RTZ/1/0";

        Assert.Equal(ns + "route", xml.Root!.Name);
        Assert.Equal("1.0", (string?)xml.Root.Attribute("version"));
        Assert.NotEmpty(xml.Descendants(ns + "sheduleElement"));
        Assert.Empty(xml.Descendants(ns + "scheduleElement"));
        Assert.Equal("01:00:00", (string?)xml.Descendants(ns + "sheduleElement").First().Attribute("etdWindowAfter"));
        Assert.Equal("1.4", (string?)xml.Descendants(ns + "sheduleElement").Single(e => (string?)e.Attribute("fuel") is not null).Attribute("fuel"));
        // The STM extension element moves into the 1.0 namespace with its content.
        var extension = xml.Descendants(ns + "extension").Single();
        Assert.Equal("RouteInfoEx", (string?)extension.Attribute("name"));
        Assert.DoesNotContain(xml.Descendants(), e => e.Name.NamespaceName == "http://www.cirm.org/RTZ/1/1");
    }

    [Fact]
    public void Rtz_1_2_output_wraps_bare_extensions_and_numbers_revisions()
    {
        var route = RtzReader.Read(FixturePath("rtz-1.0-solent.rtz"));
        var xml = XDocument.Parse(RtzWriter.ToXml(route, RtzVersions.V1_2));
        XNamespace ns = "http://www.cirm.org/RTZ/1/2";

        Assert.All(xml.Descendants(ns + "waypoint"), w => Assert.NotNull(w.Attribute("revision")));
        Assert.Equal("PT30M", (string?)xml.Descendants(ns + "scheduleElement").First().Attribute("etdWindowBefore"));
        var wrapped = xml.Descendants(ns + "routeInfo").Single().Element(ns + "extensions")!.Element(ns + "extension")!;
        Assert.Equal("unknown", (string?)wrapped.Attribute("manufacturer"));
        Assert.Equal("planner", (string?)wrapped.Attribute("name"));
        Assert.Equal("Bridge copy", wrapped.Element(XName.Get("planner", "urn:example:acme"))!.Value);
    }

    [Theory]
    [InlineData(RtzVersions.V1_0)]
    [InlineData(RtzVersions.V1_1)]
    public void Output_before_1_2_leaves_out_leg_extensions(string version)
    {
        var route = RtzReader.Read(FixturePath("rtz-1.2-pas.rtz"));
        var xml = XDocument.Parse(RtzWriter.ToXml(route, version));
        var ns = RtzVersions.NamespaceOf(version);

        Assert.All(xml.Descendants(ns + "leg"), l => Assert.Null(l.Element(ns + "extensions")));
        // Unknown attributes are kept in every version.
        Assert.Contains(xml.Descendants(ns + "leg"), l => (string?)l.Attribute(XName.Get("priority", "urn:example:vendor")) == "high");
    }

    [Fact]
    public void Unparsable_values_are_kept_verbatim()
    {
        const string text = """
            <route xmlns="http://www.cirm.org/RTZ/1/1" version="1.1">
              <routeInfo routeName="Odd"/>
              <waypoints>
                <waypoint id="1"><position lat="50" lon="-1"/></waypoint>
                <waypoint id="2"><position lat="50.1" lon="-1"/><leg portsideXTD="wide" geometryType="Spiral"/></waypoint>
              </waypoints>
            </route>
            """;
        var route = RtzReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        var leg = route.Waypoints[1].Leg!;

        Assert.Null(leg.PortsideXtd);
        Assert.Null(leg.GeometryType);
        Assert.Equal("wide", leg.ExtraAttributes["portsideXTD"]);
        Assert.Equal("Spiral", leg.ExtraAttributes["geometryType"]);
        Assert.Contains("portsideXTD=\"wide\"", RtzWriter.ToXml(route), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<route xmlns=\"http://example.com/other\" version=\"1.0\"><waypoints/></route>")]
    [InlineData("<route xmlns=\"http://www.cirm.org/RTZ/1/0\" version=\"1.0\"><routeInfo routeName=\"x\"/></route>")]
    [InlineData("<route xmlns=\"http://www.cirm.org/RTZ/1/0\" version=\"1.0\"><waypoints><waypoint id=\"1\"/></waypoints></route>")]
    [InlineData("not xml")]
    public void Content_that_isnt_an_rtz_route_is_rejected(string text)
    {
        Assert.Throws<RtzFormatException>(() => RtzReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void Writing_an_unsupported_version_is_refused()
    {
        var route = RtzReader.Read(FixturePath("rtz-1.2-pas.rtz"));
        Assert.Throws<ArgumentException>(() => RtzWriter.ToXml(route, "2.0"));
    }

    private static IEnumerable<RtzScheduleTable> Tables(RtzSchedule schedule)
        => new[] { schedule.Manual, schedule.Calculated }.OfType<RtzScheduleTable>();

    // Every public property of the model, recursively, as text.
    internal static string Dump(object? value)
    {
        var builder = new StringBuilder();
        Write(builder, value, 0);
        return builder.ToString();

        static void Write(StringBuilder b, object? v, int depth)
        {
            switch (v)
            {
                case null:
                    b.Append("null");
                    return;
                case string s:
                    b.Append('"').Append(s).Append('"');
                    return;
                case XElement e:
                    b.Append(e.ToString(SaveOptions.DisableFormatting));
                    return;
                case IFormattable f when v.GetType().IsPrimitive || v is DateTimeOffset or TimeSpan or decimal:
                    b.Append(f.ToString(null, CultureInfo.InvariantCulture));
                    return;
                case Enum or bool:
                    b.Append(v);
                    return;
                case IDictionary dictionary:
                    foreach (var key in dictionary.Keys.Cast<object>().OrderBy(k => k.ToString(), StringComparer.Ordinal))
                    {
                        b.Append('[').Append(key).Append("]=");
                        Write(b, dictionary[key], depth + 1);
                        b.Append(';');
                    }
                    return;
                case IEnumerable list:
                    b.Append('[');
                    foreach (var item in list)
                    {
                        Write(b, item, depth + 1);
                        b.Append(',');
                    }
                    b.Append(']');
                    return;
            }
            var type = v.GetType();
            if (type.Namespace == "EncDotNet.S100.DataModel")
            {
                b.Append(v);
                return;
            }
            b.Append(type.Name).Append('{');
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;
                b.Append(property.Name).Append('=');
                Write(b, property.GetValue(v), depth + 1);
                b.Append(' ');
            }
            b.Append('}');
        }
    }
}
