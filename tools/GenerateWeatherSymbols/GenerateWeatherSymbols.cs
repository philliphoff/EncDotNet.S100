#:project ../../src/EncDotNet.S100.Datasets.Weather/EncDotNet.S100.Datasets.Weather.csproj

// Generates the weather symbols embedded in EncDotNet.S100.Datasets.Weather
// (issues #1068 and #1072): the northern-hemisphere wind barbs WXBARBN005 to
// WXBARBN200, their southern-hemisphere mirror images WXBARBS005 to
// WXBARBS200, the calm circle WXCALM, the wave direction arrow WXWAVE01 and
// the current arrows WXCURR1 to WXCURR9 (issue #1228), and the tropical
// cyclone symbols (issue #1296): the class glyphs WXTCD, WXTCS, WXTCH, WXTCM,
// WXTCSD, WXTCSS and WXTCPT with N (northern) and S (southern) suffixes, the
// storm rings WXTCRING and WXTCRINGEST, the past-track dot WXTCDOT and the
// cone stipple WXTCSTIPPLE.
//
//   dotnet run tools/GenerateWeatherSymbols/GenerateWeatherSymbols.cs \
//       src/EncDotNet.S100.Datasets.Weather/Symbols
//
// The output is checked in; run this again only to change the symbols, and
// bump WeatherCoverageStyle.StyleVersion when you do. The feathers come from
// WindBarb, the same decomposition the tests check.
//
// Conventions (as the S-104 station symbols): S-100 SVG, sizes in
// millimetres with one user unit per millimetre, colours by palette class
// (sTOKEN stroke, fTOKEN fill, "sl" round caps and joins, "f0" no fill), and
// the layout-only symbolBox and pivotPoint. The pivot is the origin and the
// centre of the view box, because the arrow renderers centre a symbol's box on
// its grid cell.
//
// A barb's staff runs up (north) from the station at the origin, so a
// renderer rotating it by the direction the wind blows from points the staff
// into the wind. Feathers sit at the outer end on the clockwise (+x) side in
// the northern hemisphere and on the anticlockwise (-x) side in the southern
// hemisphere (the mirror image), slanting outward: pennants (50 kn) first, then
// full barbs (10 kn), then a half barb (5 kn), which on its own sits one
// spacing in from the end. Each feather has an id (pennant1, barb1, half), so
// tests can count them.

using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Weather;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: GenerateWeatherSymbols <output directory>");
    return 1;
}

string output = args[0];
Directory.CreateDirectory(output);

const double StrokeWidth = 0.3;
const double MinimumStaff = 7.0;       // staff length when the feathers fit
const double StationClearance = 2.5;   // bare staff kept next to the station
const double FeatherLength = 3.2;
const double FeatherAngle = 70.0;      // degrees between staff and feather
const double FeatherSpacing = 1.0;
const double PennantWidth = 1.3;       // along the staff
const double PennantGap = 0.4;         // between the pennants and the barbs
const double StationDotRadius = 0.45;

double featherOutX = FeatherLength * Math.Sin(FeatherAngle * Math.PI / 180);
double featherY = FeatherLength * Math.Cos(FeatherAngle * Math.PI / 180);

var barbs = WindBarb.All.Where(b => !b.IsCalm).ToArray();
double longestStaff = barbs.Max(StaffLength);
// Half the view box: the longest staff plus a pennant's outward slant and the stroke.
double half = Math.Ceiling(longestStaff + featherY + StrokeWidth);

foreach (var barb in barbs)
{
    Write(barb.SymbolName, BarbSvg(barb, southern: false));
    Write(barb.SouthernSymbolName, BarbSvg(barb, southern: true));
}

Write(WindBarb.CalmSymbol, CalmSvg());
Write(WaveDirectionStyle.ArrowSymbol, WaveArrowSvg());
for (int band = 1; band <= WeatherPalette.CurrentBandCount; band++)
    Write(CurrentArrowStyle.SymbolName(band), CurrentArrowSvg(band));

// Tropical cyclone symbols (vector weather V5-V7). The WMO weather-map
// glyphs: a circle, open for a depression or storm and filled for a hurricane
// or typhoon, with two arms for a storm and stronger, and an outer ring for a
// major hurricane (category 3+) or super typhoon; a square centre for the
// subtropical classes; a circled x for post-tropical, remnant lows and
// disturbances. In the northern hemisphere the arms leave the circle
// clockwise, the top arm towards the east and the bottom one towards the west,
// trailing like a spiral band of a storm turning anticlockwise; the southern
// set is the mirror image (x -> -x), as the barbs are. Every glyph has a halo
// under its ink. Sizes: the arms reach 3.74 mm from the centre (point
// symbols draw at 0.6 of their size at 96 dpi, so about 3.5 mm across on screen); the
// storm at view time is drawn 6/3.5 times larger (TropicalCycloneStyle).
const string TcInk = WeatherPalette.TropicalCycloneInkToken;
const string TcHalo = WeatherPalette.TropicalCycloneHaloToken;
int tcCount = 0;
foreach (var (name, centre, arms, filled, ring) in new (string, string, bool, bool, bool)[]
{
    ("WXTCD", "circle", false, false, false),
    ("WXTCS", "circle", true, false, false),
    ("WXTCH", "circle", true, true, false),
    ("WXTCM", "circle", true, true, true),
    ("WXTCSD", "square", false, false, false),
    ("WXTCSS", "square", true, false, false),
    ("WXTCPT", "cross", false, false, false),
})
{
    Write(name + "N", CycloneSvg(name + "N", centre, arms, filled, ring, southern: false));
    Write(name + "S", CycloneSvg(name + "S", centre, arms, filled, ring, southern: true));
    tcCount += 2;
}

Write(TropicalCycloneStyle.RingSymbol, CycloneRingSvg(TropicalCycloneStyle.RingSymbol, "The ring round the storm on a forecast point.", dashed: false));
Write(TropicalCycloneStyle.EstimatedRingSymbol, CycloneRingSvg(TropicalCycloneStyle.EstimatedRingSymbol, "The dashed ring round a storm position estimated between forecast points.", dashed: true));
Write(TropicalCycloneStyle.PastPointSymbol,
    "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"2mm\" height=\"2mm\" viewBox=\"-1 -1 2 2\">"
    + $"<title>{TropicalCycloneStyle.PastPointSymbol}</title><desc>A best-track point on a tropical cyclone's past track.</desc>"
    + "<rect class=\"symbolBox layout\" fill=\"none\" x=\"-1\" y=\"-1\" width=\"2\" height=\"2\"/>"
    + $"<circle id=\"dot\" class=\"f{TcInk} s{TcHalo}\" stroke-width=\"0.3\" cx=\"0\" cy=\"0\" r=\"0.8\"/>"
    + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"0.5\"/></svg>\n");
// The stipple: two dots in each 2 mm square tile, on a diagonal.
Write(TropicalCycloneStyle.StippleFill,
    "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"2mm\" height=\"2mm\" viewBox=\"-1 -1 2 2\">"
    + $"<title>{TropicalCycloneStyle.StippleFill}</title><desc>Stipple of a tropical cyclone's cone beyond 72 hours. One tile of a 2 mm square lattice.</desc>"
    + "<rect class=\"symbolBox layout\" fill=\"none\" x=\"-1\" y=\"-1\" width=\"2\" height=\"2\"/>"
    + $"<circle class=\"f{TcInk}\" cx=\"-0.5\" cy=\"-0.5\" r=\"0.16\"/><circle class=\"f{TcInk}\" cx=\"0.5\" cy=\"0.5\" r=\"0.16\"/>"
    + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"0.5\"/></svg>\n");
tcCount += 4;

Console.WriteLine($"Wrote {2 * barbs.Length + 2 + WeatherPalette.CurrentBandCount + tcCount} symbols to {output}.");
return 0;

// One class glyph. The pivot is the storm's centre.
static string CycloneSvg(string name, string centre, bool arms, bool filled, bool ring, bool southern)
{
    const double size = 4.4;
    double r = 0.32 * size;              // the centre's radius (or half-side)
    double a = 0.85 * size;              // how far the arms reach
    double armWidth = 0.16 * size;
    double centreWidth = 0.14 * size;
    double halo = 0.6;                   // the halo's extra width
    double s = southern ? -1 : 1;
    const double half = 4.8;

    string topArm = $"M 0 {N(-r)} C {N(s * a * 0.55)} {N(-r * 1.1)} {N(s * a * 0.9)} {N(-a * 0.75)} {N(s * a * 0.55)} {N(-a)}";
    string bottomArm = $"M 0 {N(r)} C {N(-s * a * 0.55)} {N(r * 1.1)} {N(-s * a * 0.9)} {N(a * 0.75)} {N(-s * a * 0.55)} {N(a)}";
    string fill = filled ? $"f{TcInk}" : $"f{TcHalo}";
    string centreShape = centre switch
    {
        "square" => $"<rect id=\"centre\" class=\"{fill} s{TcInk}\" stroke-width=\"{N(centreWidth)}\" x=\"{N(-r)}\" y=\"{N(-r)}\" width=\"{N(2 * r)}\" height=\"{N(2 * r)}\"/>",
        "cross" => $"<circle id=\"centre\" class=\"f{TcHalo} s{TcInk}\" stroke-width=\"{N(0.12 * size)}\" cx=\"0\" cy=\"0\" r=\"{N(r * 1.1)}\"/>"
            + $"<path id=\"cross\" class=\"sl f0 s{TcInk}\" stroke-width=\"{N(0.12 * size)}\" d=\"M {N(-0.6 * r)} {N(-0.6 * r)} L {N(0.6 * r)} {N(0.6 * r)} M {N(0.6 * r)} {N(-0.6 * r)} L {N(-0.6 * r)} {N(0.6 * r)}\"/>",
        _ => $"<circle id=\"centre\" class=\"{fill} s{TcInk}\" stroke-width=\"{N(centreWidth)}\" cx=\"0\" cy=\"0\" r=\"{N(r)}\"/>",
    };

    var body = new StringBuilder();
    // Halo first, under everything.
    if (arms)
    {
        body.Append($"<path class=\"sl f0 s{TcHalo}\" stroke-width=\"{N(armWidth + halo)}\" d=\"{topArm}\"/>");
        body.Append($"<path class=\"sl f0 s{TcHalo}\" stroke-width=\"{N(armWidth + halo)}\" d=\"{bottomArm}\"/>");
    }

    body.Append($"<circle class=\"f{TcHalo}\" cx=\"0\" cy=\"0\" r=\"{N((ring ? r * 1.65 : r * 1.1) + centreWidth / 2 + halo / 2)}\"/>");
    if (arms)
    {
        body.Append($"<path id=\"arm1\" class=\"sl f0 s{TcInk}\" stroke-width=\"{N(armWidth)}\" d=\"{topArm}\"/>");
        body.Append($"<path id=\"arm2\" class=\"sl f0 s{TcInk}\" stroke-width=\"{N(armWidth)}\" d=\"{bottomArm}\"/>");
    }

    body.Append(centreShape);
    if (ring)
        body.Append($"<circle id=\"ring\" class=\"f0 s{TcInk}\" stroke-width=\"{N(0.09 * size)}\" cx=\"0\" cy=\"0\" r=\"{N(r * 1.65)}\"/>");

    string what = name[..^1] switch
    {
        "WXTCD" => "Tropical depression: an open circle",
        "WXTCS" => "Tropical storm: an open circle with two arms",
        "WXTCH" => "Hurricane or typhoon: a filled circle with two arms",
        "WXTCM" => "Major hurricane (category 3+) or super typhoon: a filled circle with two arms and an outer ring",
        "WXTCSD" => "Subtropical depression: an open square",
        "WXTCSS" => "Subtropical storm: an open square with two arms",
        _ => "Post-tropical cyclone, remnant low or disturbance: a circled x",
    };
    string hemisphere = southern ? "southern hemisphere (clockwise)" : "northern hemisphere (anticlockwise)";
    return $"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"{N(2 * half)}mm\" height=\"{N(2 * half)}mm\" viewBox=\"{N(-half)} {N(-half)} {N(2 * half)} {N(2 * half)}\">"
        + $"<title>{name}</title><desc>{what}, {hemisphere}.</desc>"
        + $"<rect class=\"symbolBox layout\" fill=\"none\" x=\"{N(-half)}\" y=\"{N(-half)}\" width=\"{N(2 * half)}\" height=\"{N(2 * half)}\"/>"
        + body
        + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/></svg>\n";
}

// The ring round the storm, at the class glyphs' scale, with its halo.
static string CycloneRingSvg(string name, string description, bool dashed)
{
    const double radius = 4.2;
    const double half = 4.8;
    string dash = dashed ? " stroke-dasharray=\"1.2 1.0\"" : string.Empty;
    return $"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"{N(2 * half)}mm\" height=\"{N(2 * half)}mm\" viewBox=\"{N(-half)} {N(-half)} {N(2 * half)} {N(2 * half)}\">"
        + $"<title>{name}</title><desc>{description}</desc>"
        + $"<rect class=\"symbolBox layout\" fill=\"none\" x=\"{N(-half)}\" y=\"{N(-half)}\" width=\"{N(2 * half)}\" height=\"{N(2 * half)}\"/>"
        + $"<circle class=\"f0 s{TcHalo}\" stroke-width=\"1.0\" cx=\"0\" cy=\"0\" r=\"{N(radius)}\"/>"
        + $"<circle id=\"ring\" class=\"f0 s{TcInk}\" stroke-width=\"0.4\"{dash} cx=\"0\" cy=\"0\" r=\"{N(radius)}\"/>"
        + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/></svg>\n";
}

// The distance from the tip to the innermost feather's base.
double FeatherRun(WindBarb barb)
{
    double run = barb.Pennants * PennantWidth;
    if (barb.Pennants > 0 && barb.FullBarbs + barb.HalfBarbs > 0)
        run += PennantGap;
    int barbCount = barb.FullBarbs + barb.HalfBarbs;
    if (barbCount > 0)
        run += (barbCount - 1) * FeatherSpacing;
    if (barb.Pennants == 0 && barb.FullBarbs == 0)
        run += FeatherSpacing; // a lone half barb sits one spacing in
    return run;
}

double StaffLength(WindBarb barb) => Math.Max(MinimumStaff, FeatherRun(barb) + StationClearance);

string BarbSvg(WindBarb barb, bool southern)
{
    double staff = StaffLength(barb);
    // The feathers' side of the staff: +x (clockwise) in the north, -x in the south.
    double featherX = southern ? -featherOutX : featherOutX;
    var body = new StringBuilder();
    body.Append(Path("staff", "sl f0 sWXBARB", $"M 0 0 L 0 {N(-staff)}"));

    // Distance from the tip, toward the station.
    double t = 0;
    for (int i = 1; i <= barb.Pennants; i++)
    {
        double y = -staff + t;
        body.Append(Path(
            $"pennant{i}",
            "sl fWXBARB sWXBARB",
            $"M 0 {N(y)} L {N(featherX)} {N(y - featherY)} L 0 {N(y + PennantWidth)} Z"));
        t += PennantWidth;
    }

    if (barb.Pennants > 0 && barb.FullBarbs + barb.HalfBarbs > 0)
        t += PennantGap;
    else if (barb.Pennants == 0 && barb.FullBarbs == 0)
        t = FeatherSpacing;

    for (int i = 1; i <= barb.FullBarbs; i++)
    {
        double y = -staff + t;
        body.Append(Path($"barb{i}", "sl f0 sWXBARB", $"M 0 {N(y)} L {N(featherX)} {N(y - featherY)}"));
        t += FeatherSpacing;
    }

    if (barb.HalfBarbs > 0)
    {
        double y = -staff + t;
        body.Append(Path("half", "sl f0 sWXBARB", $"M 0 {N(y)} L {N(featherX / 2)} {N(y - featherY / 2)}"));
    }

    body.Append($"<circle id=\"station\" class=\"fWXBARB\" cx=\"0\" cy=\"0\" r=\"{N(StationDotRadius)}\"/>");
    return Svg(
        southern ? barb.SouthernSymbolName : barb.SymbolName,
        $"Wind barb, {barb.Knots} kn, {(southern ? "southern" : "northern")} hemisphere: {Count(barb.Pennants, "pennant")}, {Count(barb.FullBarbs, "full barb")}, {Count(barb.HalfBarbs, "half barb")}. The staff points up, into the wind.",
        half,
        body.ToString());
}

string CalmSvg()
{
    // WMO calm: a circle around the station.
    string body =
        $"<circle id=\"inner\" class=\"f0 sWXBARB\" stroke-width=\"{N(StrokeWidth)}\" cx=\"0\" cy=\"0\" r=\"0.8\"/>"
        + $"<circle id=\"outer\" class=\"f0 sWXBARB\" stroke-width=\"{N(StrokeWidth)}\" cx=\"0\" cy=\"0\" r=\"1.6\"/>";
    return Svg(WindBarb.CalmSymbol, "Calm wind, under 2.5 kn.", half, body);
}

string WaveArrowSvg()
{
    // Points up (north) at rotation 0; the style adds 180 to the "from" direction.
    string body =
        $"<path id=\"shaft\" class=\"sl f0 sWXWAVE\" stroke-width=\"0.45\" d=\"M 0 4.5 L 0 -2.6\"/>"
        + $"<path id=\"head\" class=\"sl fWXWAVE sWXWAVE\" stroke-width=\"{N(StrokeWidth)}\" d=\"M 0 -4.5 L 1.6 -1.7 L 0 -2.6 L -1.6 -1.7 Z\"/>";
    return Svg(WaveDirectionStyle.ArrowSymbol, "Wave direction: the arrow points the way the waves travel.", 5, body);
}

string CurrentArrowSvg(int band)
{
    // S-111's SCAROW outline (11 mm tall), filled with the band colour and
    // outlined in WXCUROUT. Points up (north) at rotation 0: the direction is
    // the way the water flows.
    const string Outline = "M 0 5 L -0.5 5 L -1 -1.5 L -2 -1.5 L 0 -5 L 2 -1.5 L 1 -1.5 L 0.5 5 Z";
    string token = WeatherPalette.CurrentToken(band);
    string body =
        $"<path id=\"fill\" class=\"f{token}\" d=\"{Outline}\"/>"
        + $"<path id=\"outline\" class=\"sl f0 s{WeatherPalette.CurrentOutlineToken}\" stroke-width=\"0.32\" d=\"{Outline}\"/>";
    return Svg(CurrentArrowStyle.SymbolName(band), $"Current, speed band {band}: the arrow points the way the water flows.", 5.5, body);
}

string Path(string id, string classes, string d) =>
    $"<path id=\"{id}\" class=\"{classes}\" stroke-width=\"{N(StrokeWidth)}\" d=\"{d}\"/>";

string Svg(string name, string description, double halfSize, string body)
{
    string size = N(2 * halfSize);
    return $"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"{size}mm\" height=\"{size}mm\" viewBox=\"{N(-halfSize)} {N(-halfSize)} {size} {size}\">"
        + $"<title>{name}</title><desc>{description}</desc>"
        + $"<rect class=\"symbolBox layout\" fill=\"none\" x=\"{N(-halfSize)}\" y=\"{N(-halfSize)}\" width=\"{size}\" height=\"{size}\"/>"
        + body
        + "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/></svg>\n";
}

void Write(string name, string svg) =>
    File.WriteAllText(System.IO.Path.Combine(output, name + ".svg"), svg, new UTF8Encoding(false));

static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

