using System.IO.Compression;
using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// S-164 Part 4 feature-information tests (FeatureInformation1,
/// FeatureInformation2, TidalStreamPanelData, SupplementaryFile2,
/// PictorialRepresentation, ChartOne2). The manual picks features on the
/// screen and reads the report; these pick the same features with
/// <c>identify_features</c> and check the pick report the viewer shows
/// (<see cref="Datasets.Pipelines.S101DatasetProcessor.GetFeatureInfo"/>) and,
/// where it differs, <c>describe_feature</c>. Unlike the Part 5–7 data, the
/// PowerUp cell <c>10100AA_X0000</c> lies where the manual's positions put it.
/// Feature ids are the cell's feature record ids.
/// </summary>
public sealed class Part4FeatureInformationTests
{
    private const string PowerUp = "PowerUp";

    /// <summary>The same cell as PowerUp's, with the support files its notes and pictures name.</summary>
    private const string InitialPowerUp = "InitialPowerUp";

    private const string Cell = "10100AA_X0000.000";

    /// <summary>The manual's example of a buoy and light: 32°29.50′S 061°00.46′E.</summary>
    private static readonly GeoPosition BuoyAndLight = new(-(32 + 29.50 / 60), 61 + 0.46 / 60);

    /// <summary>A yellow-and-black south cardinal buoy, S Skerries, whose light is feature 66.</summary>
    private const string SouthSkerries = "1791";

    private const string RepeatedValues = "#1156: the S-101 pick report keeps one value per attribute code and flattens complex attributes.";
    private const string Associations = "#1157: the S-101 pick report doesn't show information or feature associations.";
    private const string ValueFormat = "#1158: pick report values have no units, keep their padding and show dates as encoded.";
    private const string PriorityOrder = "#1159: picks are ranked by geometry, not by drawing priority.";

    private static Task<S164Pick> OpenAsync(string exchangeSet = PowerUp) =>
        S164Pick.OpenAsync(exchangeSet, Cell, TestContext.Current.CancellationToken);

    // ── FeatureInformation1: general rules for the pick report (S-98 15.1) ──

    [Fact]
    public async Task FeatureInformation1_PickListsEveryFeatureUnderTheCursor()
    {
        var pick = await OpenAsync();

        var picked = await pick.PickAsync(BuoyAndLight, radiusMeters: 100);
        var types = picked.Select(m => m.FeatureType).ToHashSet();

        Assert.Contains("SafeWaterBuoy", types);
        Assert.Contains("LightAllAround", types);
        Assert.Contains("DepthArea", types);
    }

    [Fact]
    public async Task FeatureInformation1_ShowsFullNamesAndListedValueNames()
    {
        var pick = await OpenAsync();

        foreach (var match in await pick.PickAsync(BuoyAndLight, radiusMeters: 100))
        {
            var report = pick.Report(match.FeatureId);
            Assert.False(string.IsNullOrEmpty(report.FeatureTypeName), $"{match.FeatureType} has no name.");
            Assert.All(S164Pick.Rows(report), row => Assert.False(string.IsNullOrEmpty(row.Name), $"{match.FeatureType}.{row.Code} has no name."));
        }

        var buoy = pick.Report(SouthSkerries);
        Assert.Equal("Cardinal Buoy", buoy.FeatureTypeName);
        Assert.Equal("South Cardinal Mark", Row(buoy, "categoryOfCardinalMark").DisplayValue);
        Assert.Equal("Horizontal Stripes", Row(buoy, "colourPattern").DisplayValue);
    }

    [Fact(Skip = RepeatedValues)]
    public async Task FeatureInformation1_ShowsEveryValueOfARepeatedAttribute()
    {
        var pick = await OpenAsync();

        // The buoy is yellow and black, with a black topmark.
        var colours = S164Pick.Rows(pick.Report(SouthSkerries))
            .Where(r => r.Code == "colour")
            .Select(r => r.DisplayValue)
            .ToList();

        Assert.Equal(["Yellow", "Black", "Black"], colours);
    }

    [Fact]
    public async Task FeatureInformation1_DescribeFeatureInlinesInformationAssociations()
    {
        // describe_feature (s100 identify --attributes, MCP) already follows
        // information associations to the linked records' attributes.
        var pick = await OpenAsync();

        var (_, text) = await NoteTextAsync(pick);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact(Skip = Associations)]
    public async Task FeatureInformation1_IncludesInformationAssociations()
    {
        var pick = await OpenAsync();
        var (area, text) = await NoteTextAsync(pick);

        var picked = await pick.PickAsync(S164Pick.PositionOf(area));

        Assert.Contains(picked, m => m.FeatureId == area.Id);
        Assert.Contains(S164Pick.Rows(pick.Report(area.Id)), r => (r.DisplayValue ?? r.RawValue) == text);
    }

    [Fact(Skip = Associations)]
    public async Task FeatureInformation1_IncludesFeatureAssociations()
    {
        var pick = await OpenAsync();
        var described = await pick.DescribeAsync(SouthSkerries);
        var light = Assert.Single(described.GetProperty("featureAssociations").EnumerateArray());
        Assert.Equal("StructureEquipment", light.GetProperty("acronym").GetString());

        var report = pick.Report(SouthSkerries);

        Assert.Contains(report.References, r => r.TargetRef == light.GetProperty("targetRecordId").GetUInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact(Skip = ValueFormat)]
    public async Task FeatureInformation1_ShowsUnitsOfMeasure()
    {
        // describe_feature gives each value's unit from the feature catalogue;
        // the pick report should show it after the value.
        var pick = await OpenAsync();
        var light = pick.Features("LightAllAround").First(f => f.Attributes.Any(a => a.Key == "signalPeriod"));
        var described = await pick.DescribeAsync(light.Id);
        var unit = described.GetProperty("attributes").EnumerateArray()
            .First(a => a.GetProperty("acronym").GetString() == "signalPeriod")
            .GetProperty("unit").GetString();

        var period = Row(pick.Report(light.Id), "signalPeriod");

        Assert.EndsWith(" " + unit, period.DisplayValue);
    }

    [Fact(Skip = ValueFormat)]
    public async Task FeatureInformation1_DoesNotPadValues()
    {
        // Tidal stream directions are reals encoded with leading zeros ("010").
        var pick = await OpenAsync();
        var panel = pick.Features("TidalStreamPanelData").First();

        var padded = S164Pick.Rows(pick.Report(panel.Id))
            .Where(r => r.Code == "orientationValue")
            .Select(r => r.DisplayValue ?? r.RawValue)
            .Where(v => v.Length > 1 && v[0] == '0' && char.IsAsciiDigit(v[1]))
            .ToList();

        Assert.Empty(padded);
    }

    [Fact(Skip = ValueFormat)]
    public async Task FeatureInformation1_DatesReadDayMonthYear()
    {
        // The Settings cell's buoy laid on 16.08.2012 (Part 3 FixedDateRange).
        var pick = await S164Pick.OpenAsync("Settings", "10100AA_X0001.000", TestContext.Current.CancellationToken);

        var values = S164Pick.Rows(pick.Report("69")).Select(r => r.DisplayValue ?? r.RawValue);

        Assert.Contains("16-AUG-2012", values);
    }

    // ── FeatureInformation2: descriptions and sorting (S-98 15.1) ──

    [Fact(Skip = PriorityOrder)]
    public async Task FeatureInformation2_SortedByDrawingPriority()
    {
        // Highest drawing priority first; at equal priority points, then
        // curves, then surfaces. Features with no drawing instruction aren't
        // drawn, so they aren't ranked here.
        var pick = await OpenAsync();
        var priorities = pick.Instructions
            .GroupBy(i => i.FeatureReference)
            .ToDictionary(g => g.Key, g => g.Max(i => i.DrawingPriority));

        var ranked = (await pick.PickAsync(BuoyAndLight, radiusMeters: 100))
            .Where(m => priorities.ContainsKey(m.FeatureId))
            .Select(m => (m.FeatureType, Priority: priorities[m.FeatureId], Geometry: GeometryRank(m.Geometry)))
            .ToList();
        var expected = ranked.OrderByDescending(m => m.Priority).ThenBy(m => m.Geometry).ToList();

        Assert.Equal(expected.Select(m => (m.Priority, m.Geometry)), ranked.Select(m => (m.Priority, m.Geometry)));
    }

    // ── TidalStreamPanelData (S-98 15.4) ──

    [Fact(Skip = RepeatedValues)]
    public async Task TidalStreamPanelData_ShowsEveryPanelValue()
    {
        // Each panel carries a speed and direction for every hour from six
        // hours before high water to six after.
        var pick = await OpenAsync();
        var panel = pick.Features("TidalStreamPanelData").First();
        var described = await pick.DescribeAsync(panel.Id);
        var speeds = described.GetProperty("attributes").EnumerateArray()
            .Count(a => a.GetProperty("acronym").GetString() == "speedMaximum");
        Assert.Equal(13, speeds);

        var picked = await pick.PickAsync(S164Pick.PositionOf(panel));
        Assert.Contains(picked, m => m.FeatureId == panel.Id);

        Assert.Equal(speeds, S164Pick.Rows(pick.Report(panel.Id)).Count(r => r.Code == "speedMaximum"));
    }

    // ── SupplementaryFile2 and PictorialRepresentation (S-98 15.2.2) ──

    [Fact(Skip = Associations)]
    public async Task SupplementaryFile2_ShowsTheNoteText()
    {
        // A caution area whose note is a support file, through an
        // AdditionalInformation association.
        const string note = "10100AAIECTMP.TXT";
        var pick = await OpenAsync(InitialPowerUp);
        var area = await LinkedToAsync(pick, "CautionArea", "fileReference", note);
        var text = await SupportFileTextAsync(note);

        var picked = await pick.PickAsync(S164Pick.PositionOf(area));

        Assert.Contains(picked, m => m.FeatureId == area.Id);
        Assert.Contains(S164Pick.Rows(pick.Report(area.Id)), r => r.ExternalText?.Trim() == text.Trim());
    }

    [Fact(Skip = Associations)]
    public async Task PictorialRepresentation_NamesThePicture()
    {
        // The information area whose picture is 10100AAX4000T.TIF. The
        // manual's landmark with 10100AATESTPC.TIF isn't in the v2.0.0 data:
        // no feature or information type names that file.
        const string picture = "10100AAX4000T.TIF";
        var pick = await OpenAsync(InitialPowerUp);
        var area = await LinkedToAsync(pick, "InformationArea", "pictorialRepresentation", picture);

        var picked = await pick.PickAsync(S164Pick.PositionOf(area));

        Assert.Contains(picked, m => m.FeatureId == area.Id);
        Assert.Contains(S164Pick.Rows(pick.Report(area.Id)), r => r.RawValue == picture);
    }

    // ── ChartOne2: interrogating Chart 1 (S-52 19.6.1) ──

    [Fact]
    public async Task ChartOne2_PickDescribesChart1Symbols()
    {
        // Pick three Chart 1 symbols of different feature types; each report
        // names the feature and its attributes.
        var ct = TestContext.Current.CancellationToken;
        var zip = await S164TestData.GetExchangeSetAsync("Chart1", ct);
        string[] cells;
        using (var archive = ZipFile.OpenRead(zip))
        {
            cells = archive.Entries
                .Where(e => e.FullName.StartsWith("S100_ROOT/S-101/DATASET_FILES/", StringComparison.Ordinal)
                    && e.Name.EndsWith(".000", StringComparison.Ordinal))
                .Select(e => e.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        var types = new HashSet<string>();
        foreach (var cell in cells)
        {
            var pick = await S164Pick.OpenAsync("Chart1", cell, ct);
            var symbols = pick.Instructions.OfType<PointInstruction>()
                .Select(p => p.FeatureReference)
                .ToHashSet();
            var symbol = FeatureAccessor(pick)
                .FirstOrDefault(f => f.Points.Count > 0 && symbols.Contains(f.Id)
                    && f.FeatureType != "Chart1Feature" && !types.Contains(f.FeatureType));
            if (symbol is null)
            {
                continue;
            }

            var picked = await pick.PickAsync(S164Pick.PositionOf(symbol));
            Assert.Contains(picked, m => m.FeatureId == symbol.Id);
            var report = pick.Report(symbol.Id);
            Assert.False(string.IsNullOrEmpty(report.FeatureTypeName), $"{cell}: {symbol.FeatureType} has no name.");
            Assert.All(S164Pick.Rows(report), row => Assert.False(string.IsNullOrEmpty(row.Name), $"{cell}: {symbol.FeatureType}.{row.Code} has no name."));

            types.Add(symbol.FeatureType);
            if (types.Count == 3)
            {
                break;
            }
        }

        Assert.Equal(3, types.Count);
    }

    // ── Helpers ──

    private static IEnumerable<IS100Feature> FeatureAccessor(S164Pick pick) =>
        Datasets.Pipelines.Spec.FeatureAccessor.GetFeatures(pick.Dataset)!;

    private static PickAttribute Row(FeatureInfo report, string code) =>
        S164Pick.Rows(report).First(r => r.Code == code);

    private static int GeometryRank(string geometry) => geometry switch
    {
        "point" => 0,
        "curve" => 1,
        _ => 2,
    };

    /// <summary>The attributes of the information records <paramref name="described"/> links to.</summary>
    private static IEnumerable<JsonElement> LinkedAttributes(JsonElement described) =>
        described.GetProperty("informationAssociations").EnumerateArray()
            .Where(a => a.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Object)
            .SelectMany(a => a.GetProperty("target").GetProperty("attributes").EnumerateArray());

    /// <summary>A caution area with a note written as text, and the text.</summary>
    private static async Task<(IS100Feature Area, string Text)> NoteTextAsync(S164Pick pick)
    {
        foreach (var area in pick.Features("CautionArea"))
        {
            var text = LinkedAttributes(await pick.DescribeAsync(area.Id))
                .Where(a => a.GetProperty("acronym").GetString() == "text")
                .Select(a => a.GetProperty("value").GetString())
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (text is not null)
            {
                return (area, text);
            }
        }

        throw new InvalidOperationException("No caution area has a text note.");
    }

    /// <summary>The feature of type <paramref name="featureType"/> linked to an information record whose <paramref name="attribute"/> is <paramref name="value"/>.</summary>
    private static async Task<IS100Feature> LinkedToAsync(S164Pick pick, string featureType, string attribute, string value)
    {
        foreach (var feature in pick.Features(featureType))
        {
            if (LinkedAttributes(await pick.DescribeAsync(feature.Id))
                .Any(a => a.GetProperty("acronym").GetString() == attribute && a.GetProperty("value").GetString() == value))
            {
                return feature;
            }
        }

        throw new InvalidOperationException($"No {featureType} links to {attribute} {value}.");
    }

    private static async Task<string> SupportFileTextAsync(string file)
    {
        var zip = await S164TestData.GetExchangeSetAsync(InitialPowerUp, TestContext.Current.CancellationToken);
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry($"S100_ROOT/S-101/SUPPORT_FILES/{file}")!.Open());
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }
}
