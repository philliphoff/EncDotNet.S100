#:project ../../../src/EncDotNet.S100.Datasets.S57/EncDotNet.S100.Datasets.S57.csproj
// Derives an S-125 GML dataset from the aids to navigation of an S-57 ENC
// cell: S-57 -> S-101 (in-memory translation) -> S-125 GML. Attributes are
// filtered to the S-125 Feature Catalogue bindings; S-101 topmark complex
// attributes become S-125 Topmark equipment features; S-101 StructureEquipment
// associations become S-125 'parent' references.
// Usage: dotnet run tests/datasets/S125/derive-s125-from-enc.cs -c Release -- <cell.000> <S125 FeatureCatalogue.xml> <out.gml> <datasetId> "<title>" [minLon,minLat,maxLon,maxLat]
using System.Globalization;
using System.Security;
using System.Text;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;

var (cellPath, fcPath, outPath, dsId, title) = (args[0], args[1], args[2], args[3], args[4]);
double[]? bbox = args.Length > 5 ? args[5].Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray() : null;

XNamespace fcNs = "http://www.iho.int/S100FC/5.2";
var fc = XDocument.Load(fcPath).Root!;
var types = fc.Descendants(fcNs + "S100_FC_FeatureType").ToDictionary(
    t => t.Element(fcNs + "code")!.Value,
    t => (Super: t.Element(fcNs + "superType")?.Value,
          Attrs: t.Elements(fcNs + "attributeBinding").Select(a => a.Element(fcNs + "attribute")!.Attribute("ref")!.Value).ToList()));
var complexes = fc.Descendants(fcNs + "S100_FC_ComplexAttribute").ToDictionary(
    c => c.Element(fcNs + "code")!.Value,
    c => c.Elements(fcNs + "subAttributeBinding").Select(a => a.Element(fcNs + "attribute")!.Attribute("ref")!.Value).ToHashSet());
var booleans = fc.Descendants(fcNs + "S100_FC_SimpleAttribute")
    .Where(a => a.Element(fcNs + "valueType")?.Value == "boolean")
    .Select(a => a.Element(fcNs + "code")!.Value).ToHashSet();
HashSet<string> Bound(string type)
{
    var set = new HashSet<string>();
    for (string? t = type; t is not null && types.TryGetValue(t, out var info); t = info.Super)
        set.UnionWith(info.Attrs);
    return set;
}

string[] aton =
[
    "LateralBuoy", "CardinalBuoy", "IsolatedDangerBuoy", "SafeWaterBuoy", "SpecialPurposeGeneralBuoy",
    "InstallationBuoy", "MooringBuoy", "EmergencyWreckMarkingBuoy",
    "LateralBeacon", "CardinalBeacon", "IsolatedDangerBeacon", "SafeWaterBeacon", "SpecialPurposeGeneralBeacon",
    "Daymark", "LightAllAround", "LightSectored", "LightAirObstruction", "LightFogDetector", "LightFloat", "LightVessel",
    "FogSignal", "RadarReflector", "RadarTransponderBeacon", "Retroreflector",
    "PhysicalAISAidToNavigation", "VirtualAISAidToNavigation",
    "Landmark", "Pile", "NavigationLine", "RecommendedTrack",
];

var updates = Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(cellPath))!, Path.GetFileNameWithoutExtension(cellPath) + ".*")
    .Where(u => int.TryParse(Path.GetExtension(u).TrimStart('.'), out var k) && k > 0).OrderBy(u => u).ToList();
using var baseStream = File.OpenRead(cellPath);
var updateStreams = updates.Select(u => (Stream)File.OpenRead(u)).ToList();
var s57 = S57Dataset.Open(baseStream, updateStreams);
Console.WriteLine($"Applied {updates.Count} updates");
var doc = S57ToS101Translator.ForTarget(S57TranslationTarget.S101).Translate(s57);
var dataset = S101Dataset.FromDocument(doc);
var geometry = new S101VectorSource(dataset).GetFeatures().ToDictionary(f => f.Id);

string Code(S101FeatureRecord f) => doc.FeatureTypeCatalogue.TryGetValue(f.FeatureTypeCode, out var n) ? n : "";
bool InBox(EncDotNet.S100.Pipelines.Vector.Feature g) => bbox is null || g.Coordinates.Any(c =>
    c.Longitude >= bbox[0] && c.Latitude >= bbox[1] && c.Longitude <= bbox[2] && c.Latitude <= bbox[3]);

var selected = doc.Features
    .Where(f => aton.Contains(Code(f)) && geometry.TryGetValue(f.RecordId, out var g) && g.Coordinates.Count > 0 && g.GeometryType != EncDotNet.S100.Pipelines.Vector.GeometryType.Surface && InBox(g))
    .ToList();
// Landmarks / piles only when they host a selected aid (lighthouses, lit piles), else they are just topography.
var ids = selected.Select(f => f.RecordId).ToHashSet();
var lightPositions = selected.Where(f => Code(f).StartsWith("Light", StringComparison.Ordinal))
    .Select(f => geometry[f.RecordId].Coordinates[0]).ToList();
bool HostsAid(S101FeatureRecord s) =>
    doc.Features.Any(e => ids.Contains(e.RecordId) && e.RecordId != s.RecordId && e.FeatureAssociations.Any(a => a.RecordId == s.RecordId))
    || lightPositions.Any(p => Math.Abs(p.Latitude - geometry[s.RecordId].Coordinates[0].Latitude) < 1e-7
        && Math.Abs(p.Longitude - geometry[s.RecordId].Coordinates[0].Longitude) < 1e-7);
selected = selected.Where(f => Code(f) is not ("Landmark" or "Pile") || HostsAid(f)).ToList();
ids = selected.Select(f => f.RecordId).ToHashSet();

// Attribute tree from the flat ATTR list (PAIX = 1-based parent position).
List<Node> Tree(IReadOnlyList<S101Attribute> attrs)
{
    var nodes = attrs.Select(a => new Node(doc.AttributeTypeCatalogue.TryGetValue(a.NumericCode, out var n) ? n : "?", a.Value, [])).ToList();
    var roots = new List<Node>();
    for (int i = 0; i < attrs.Count; i++)
    {
        if (attrs[i].ParentIndex == 0 || attrs[i].ParentIndex > nodes.Count) roots.Add(nodes[i]);
        else nodes[attrs[i].ParentIndex - 1].Children.Add(nodes[i]);
    }
    return roots;
}

string P = "S125";
string Esc(string s) => SecurityElement.Escape(s)!;
void Write(StringBuilder sb, Node n, HashSet<string> allowed, string indent)
{
    if (!allowed.Contains(n.Code)) return;
    if (n.Children.Count > 0 || complexes.ContainsKey(n.Code))
    {
        if (!complexes.TryGetValue(n.Code, out var sub)) return;
        var inner = new StringBuilder();
        foreach (var c in n.Children) Write(inner, c, sub, indent + "  ");
        if (inner.Length == 0) return;
        sb.Append($"{indent}<{P}:{n.Code}>\n{inner}{indent}</{P}:{n.Code}>\n");
    }
    else if (n.Value.Length > 0)
    {
        var v = booleans.Contains(n.Code) ? (n.Value is "1" or "true" ? "true" : "false") : n.Value;
        sb.Append($"{indent}<{P}:{n.Code}>{Esc(v)}</{P}:{n.Code}>\n");
    }
}
string Pos(EncDotNet.S100.DataModel.GeoPosition p) => $"{p.Latitude.ToString("0.0000000", CultureInfo.InvariantCulture)} {p.Longitude.ToString("0.0000000", CultureInfo.InvariantCulture)}";
string Geom(string gid, EncDotNet.S100.Pipelines.Vector.Feature g) => g.GeometryType switch
{
    EncDotNet.S100.Pipelines.Vector.GeometryType.Point =>
        $"<S100:pointProperty><gml:Point gml:id=\"{gid}\" srsName=\"EPSG:4326\"><gml:pos>{Pos(g.Coordinates[0])}</gml:pos></gml:Point></S100:pointProperty>",
    EncDotNet.S100.Pipelines.Vector.GeometryType.Curve when g.Coordinates.Count > 1 =>
        $"<S100:curveProperty><gml:Curve gml:id=\"{gid}\" srsName=\"EPSG:4326\"><gml:segments><gml:LineStringSegment><gml:posList>{string.Join(" ", g.Coordinates.Select(Pos))}</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></S100:curveProperty>",
    _ => "",
};

var sb = new StringBuilder();
sb.Append($"""
<?xml version="1.0" encoding="UTF-8"?>
<!--
  {title}
  Derived from NOAA ENC cell {Path.GetFileNameWithoutExtension(cellPath)} (US public domain) by
  tests/datasets/S125/derive-s125-from-enc.cs: S-57 -> S-101 (EncDotNet translator)
  -> S-125 1.0.0 GML. Attributes are limited to the S-125 Feature Catalogue
  bindings; S-101 topmarks become Topmark equipment features and
  StructureEquipment associations become 'parent' references. Not for navigation.
-->
<{P}:Dataset xmlns:{P}="http://www.iho.int/S125/1.0" xmlns:S100="http://www.iho.int/s100gml/5.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="{dsId}">
  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-125</S100:productIdentifier>
    <S100:productEdition>1.0.0</S100:productEdition>
    <S100:datasetTitle>{Esc(title)}</S100:datasetTitle>
  </S100:DatasetIdentificationInformation>

""");

// Data coverage = bbox of the selected aids, padded.
var all = selected.SelectMany(f => geometry[f.RecordId].Coordinates).ToList();
double s = all.Min(c => c.Latitude) - 0.01, n = all.Max(c => c.Latitude) + 0.01, w = all.Min(c => c.Longitude) - 0.01, e = all.Max(c => c.Longitude) + 0.01;
if (bbox is not null) (w, s, e, n) = (bbox[0], bbox[1], bbox[2], bbox[3]);
string F(double d) => d.ToString("0.0000000", CultureInfo.InvariantCulture);
sb.Append($"""
  <{P}:member>
    <{P}:DataCoverage gml:id="coverage">
      <{P}:geometry><S100:surfaceProperty><gml:Surface gml:id="g_cov" srsName="EPSG:4326"><gml:patches><gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>{F(s)} {F(w)} {F(s)} {F(e)} {F(n)} {F(e)} {F(n)} {F(w)} {F(s)} {F(w)}</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></S100:surfaceProperty></{P}:geometry>
    </{P}:DataCoverage>
  </{P}:member>

""");

int count = 0, topmarks = 0;
string Gid(S101FeatureRecord f) => $"{Code(f)}.{f.RecordId}";
foreach (var f in selected)
{
    var code = Code(f);
    var g = geometry[f.RecordId];
    var bound = Bound(code);
    var tree = Tree(f.Attributes);
    var body = new StringBuilder();
    foreach (var node in tree) Write(body, node, bound, "      ");
    // StructureEquipment: equipment -> its structure.
    foreach (var a in f.FeatureAssociations)
    {
        var assoc = doc.FeatureAssociationCatalogue.TryGetValue(a.NumericCode, out var an) ? an : "";
        var role = doc.RoleCatalogue.TryGetValue(a.RoleCode, out var rn) ? rn : "";
        var target = doc.Features.FirstOrDefault(x => x.RecordId == a.RecordId);
        if (assoc == "StructureEquipment" && role == "theStructure" && target is not null && ids.Contains(target.RecordId) && Bound(code).Count > 0 && IsEquipment(code))
            body.Append($"      <{P}:parent xlink:href=\"#{Gid(target)}\"/>\n");
    }
    sb.Append($"  <{P}:member>\n    <{P}:{code} gml:id=\"{Gid(f)}\">\n      <{P}:geometry>{Geom("g" + f.RecordId, g)}</{P}:geometry>\n{body}    </{P}:{code}>\n  </{P}:member>\n");
    count++;

    var tm = tree.FirstOrDefault(t => t.Code == "topmark");
    if (tm is not null && g.GeometryType == EncDotNet.S100.Pipelines.Vector.GeometryType.Point)
    {
        var tb = new StringBuilder();
        foreach (var c in tm.Children) Write(tb, c, Bound("Topmark"), "      ");
        if (tb.Length > 0)
        {
            sb.Append($"  <{P}:member>\n    <{P}:Topmark gml:id=\"Topmark.{f.RecordId}\">\n      <{P}:geometry>{Geom("gt" + f.RecordId, g)}</{P}:geometry>\n{tb}      <{P}:parent xlink:href=\"#{Gid(f)}\"/>\n    </{P}:Topmark>\n  </{P}:member>\n");
            topmarks++;
        }
    }
}
sb.Append($"</{P}:Dataset>\n");
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine($"Wrote {count} aids + {topmarks} topmarks to {outPath}");
foreach (var grp in selected.GroupBy(Code).OrderBy(x => x.Key)) Console.WriteLine($"  {grp.Key}: {grp.Count()}");

bool IsEquipment(string t)
{
    for (string? c = t; c is not null && types.TryGetValue(c, out var info); c = info.Super)
        if (c == "Equipment") return true;
    return false;
}

sealed record Node(string Code, string Value, List<Node> Children);
