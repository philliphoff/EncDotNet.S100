#:project ../../src/EncDotNet.S100.Datasets.Weather/EncDotNet.S100.Datasets.Weather.csproj

// Generates the weather symbols embedded in EncDotNet.S100.Datasets.Weather
// (issues #1068 and #1072): the northern-hemisphere wind barbs WXBARBN005 to
// WXBARBN200, their southern-hemisphere mirror images WXBARBS005 to
// WXBARBS200, the calm circle WXCALM and the wave direction arrow WXWAVE01.
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

Console.WriteLine($"Wrote {2 * barbs.Length + 2} symbols to {output}.");
return 0;

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
