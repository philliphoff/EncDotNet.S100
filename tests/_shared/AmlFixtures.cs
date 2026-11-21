using EncDotNet.S57;
using EncDotNet.S57.Aml;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Builds synthetic NATO Additional Military Layers (AML) cells for tests, in
/// memory, with chain-node topology: connected nodes, edges (straight or arcs),
/// and point, line and area features. The real AML samples must never be
/// committed, so every AML test draws from cells built here.
/// </summary>
/// <remarks>
/// Positions are (longitude, latitude) in decimal degrees. Attributes are
/// given by acronym and resolved through the product's
/// <see cref="S57ObjectCatalogue"/>, as are the object classes.
/// </remarks>
internal sealed class AmlCellBuilder
{
    private const int Comf = 10_000_000;
    private const int SoundingFactor = 10;
    private const int Agency = 0x021C;

    private readonly S57ObjectCatalogue _catalogue;
    private readonly int _productSpecification;
    private readonly string _edition;
    private readonly int _profile;
    private readonly string _name;
    private readonly List<S57VectorRecord> _vectors = [];
    private readonly List<S57FeatureRecord> _features = [];
    private readonly Dictionary<(int, int), int> _nodes = [];
    private int _nextNode = 1;
    private int _nextEdge = 1;
    private int _nextIsolated = 1;

    private AmlCellBuilder(S57ObjectCatalogue catalogue, int productSpecification, string edition, int profile, string name)
    {
        _catalogue = catalogue;
        _productSpecification = productSpecification;
        _edition = edition;
        _profile = profile;
        _name = name;
    }

    /// <summary>An AML 1.0 Routes, Areas and Limits (RAL) cell: DSID PRSP 52, PRED 1.0.</summary>
    public static AmlCellBuilder Ral(string name = "UKR0TEST.000") => new(S57ObjectCatalogue.Aml10, 52, "1.0", 16, name);

    /// <summary>An AML 1.0 Small Bottom Objects (SBO) cell: DSID PRSP 56, PRED 1.0.</summary>
    public static AmlCellBuilder Sbo(string name = "UKS0TEST.000") => new(S57ObjectCatalogue.Aml10, 56, "1.0", 18, name);

    /// <summary>An AML 1.0 Large Bottom Objects (LBO) cell: DSID PRSP 53, PRED 1.0.</summary>
    public static AmlCellBuilder Lbo(string name = "UKL0TEST.000") => new(S57ObjectCatalogue.Aml10, 53, "1.0", 10, name);

    /// <summary>An AML 1.0 Contour Line Bathymetry (CLB) cell: DSID PRSP 54, PRED 1.0.</summary>
    public static AmlCellBuilder Clb(string name = "UKC6TEST.000") => new(S57ObjectCatalogue.Aml10, 54, "1.0", 16, name);

    /// <summary>An AML 1.0 Maritime Foundation &amp; Facilities (MFF) cell: DSID PRSP 51, PRED 1.0.</summary>
    public static AmlCellBuilder Mff(string name = "UKM6TEST.000") => new(S57ObjectCatalogue.Aml10, 51, "1.0", 16, name);

    /// <summary>An AML 1.0 Environment, Seabed and Beach (ESB) cell: DSID PRSP 55, PRED 1.0.</summary>
    public static AmlCellBuilder Esb(string name = "UKE6TEST.000") => new(S57ObjectCatalogue.Aml10, 55, "1.0", 16, name);

    /// <summary>An AML 3.0 integrated cell (the FAI/MFI trials): DSID PRSP 57, PRED 3.0.</summary>
    public static AmlCellBuilder Aml30(string name = "AMLTEST0U_FAI.000") => new(S57ObjectCatalogue.Aml30, 57, "3.0", 20, name);

    /// <summary>Adds a point feature.</summary>
    public AmlCellBuilder Point(string acronym, (double Lon, double Lat) position, params (string Acronym, string Value)[] attributes)
    {
        var node = new S57RecordName { RecordNameCode = 110, RecordId = _nextIsolated++ };
        _vectors.Add(Vector(node, [Coordinate(position)], []));
        AddFeature(acronym, S57GeometricPrimitive.Point, attributes, [Pointer(node, S57Orientation.NotApplicable, S57UsageIndicator.NotApplicable)]);
        return this;
    }

    /// <summary>
    /// Adds a sounding feature (<c>SOUNDG</c>): one isolated node holding every
    /// sounding as a 3-D coordinate (<c>SG3D</c>), depths in metres, positive down.
    /// </summary>
    public AmlCellBuilder Soundings(params (double Lon, double Lat, double Depth)[] soundings)
    {
        var node = new S57RecordName { RecordNameCode = 110, RecordId = _nextIsolated++ };
        _vectors.Add(new S57VectorRecord
        {
            RecordName = node,
            RecordVersion = 1,
            UpdateInstruction = S57UpdateInstruction.Insert,
            Attributes = [],
            VectorPointers = [],
            Coordinates2D = [],
            Soundings = [.. soundings.Select(s => new S57Sounding
            {
                X = (int)Math.Round(s.Lon * Comf),
                Y = (int)Math.Round(s.Lat * Comf),
                Depth = (int)Math.Round(s.Depth * SoundingFactor),
            })],
            ArcCurves = [],
        });
        AddFeature("SOUNDG", S57GeometricPrimitive.Point, [], [Pointer(node, S57Orientation.NotApplicable, S57UsageIndicator.NotApplicable)]);
        return this;
    }

    /// <summary>Adds a line feature along straight edges between <paramref name="positions"/>.</summary>
    public AmlCellBuilder Line(string acronym, (double Lon, double Lat)[] positions, params (string Acronym, string Value)[] attributes)
    {
        var edge = Edge(positions);
        AddFeature(acronym, S57GeometricPrimitive.Line, attributes, [Pointer(edge, S57Orientation.Forward, S57UsageIndicator.NotApplicable)]);
        return this;
    }

    /// <summary>
    /// Adds an area feature whose exterior is the closed ring through
    /// <paramref name="ring"/> (first point repeated last), as one edge, in
    /// the given order (clockwise or not).
    /// </summary>
    public AmlCellBuilder Area(string acronym, (double Lon, double Lat)[] ring, params (string Acronym, string Value)[] attributes)
    {
        var edge = Edge(ring);
        AddFeature(acronym, S57GeometricPrimitive.Area, attributes, [Pointer(edge, S57Orientation.Forward, S57UsageIndicator.Exterior)]);
        return this;
    }

    /// <summary>
    /// Adds a circular area feature: one arc edge (ARCC/AR2D) clockwise about
    /// <paramref name="centre"/> from the point due north of it back to itself,
    /// as two half arcs.
    /// </summary>
    public AmlCellBuilder Circle(string acronym, (double Lon, double Lat) centre, double radiusDegrees, params (string Acronym, string Value)[] attributes)
    {
        var north = (centre.Lon, centre.Lat + radiusDegrees);
        var south = (centre.Lon, centre.Lat - radiusDegrees);
        var top = NodeAt(north);
        var bottom = NodeAt(south);
        var east = new S57RecordName { RecordNameCode = 130, RecordId = _nextEdge++ };
        var west = new S57RecordName { RecordNameCode = 130, RecordId = _nextEdge++ };
        _vectors.Add(ArcEdge(east, top, bottom, north, Coordinate(centre), south));
        _vectors.Add(ArcEdge(west, bottom, top, south, Coordinate(centre), north));
        AddFeature(
            acronym,
            S57GeometricPrimitive.Area,
            attributes,
            [Pointer(east, S57Orientation.Forward, S57UsageIndicator.Exterior), Pointer(west, S57Orientation.Forward, S57UsageIndicator.Exterior)]);
        return this;
    }

    /// <summary>The cell as a document.</summary>
    public S57Document BuildDocument() => new()
    {
        DataSetIdentification = new S57DataSetIdentification
        {
            RecordName = new S57RecordName { RecordNameCode = 10, RecordId = 1 },
            ExchangePurpose = 1,
            IntendedUsage = 5,
            DataSetName = _name,
            EditionNumber = "1",
            UpdateNumber = "0",
            UpdateApplicationDate = "20261101",
            IssueDate = "20261101",
            S57EditionNumber = "03.1",
            ProductSpecification = _productSpecification,
            ProductSpecificationEdition = _edition,
            ApplicationProfile = _profile,
            ProducingAgency = Agency,
            DataStructure = 2,
            AttfLexicalLevel = 1,
            NatfLexicalLevel = 1,
            Comment = "title=Synthetic test cell; Protective Marking=UNCLASSIFIED",
        },
        DataSetParameters = new S57DataSetParameters
        {
            RecordName = new S57RecordName { RecordNameCode = 20, RecordId = 1 },
            HorizontalDatum = 2,
            CompilationScale = 50_000,
            DepthUnits = 1,
            HeightUnits = 2,
            PositionalUnits = 1,
            CoordinateUnits = 1,
            CoordinateMultiplicationFactor = Comf,
            SoundingMultiplicationFactor = SoundingFactor,
        },
        VectorRecords = _vectors,
        FeatureRecords = _features,
    };

    /// <summary>The cell as an AML data set.</summary>
    public AmlDataSet Build() => new(S57Chart.FromDocument(BuildDocument()));

    private void AddFeature(string acronym, S57GeometricPrimitive primitive, (string Acronym, string Value)[] attributes, IReadOnlyList<S57SpatialPointer> pointers)
    {
        var objectClass = _catalogue.GetObjectClass(acronym)
            ?? throw new ArgumentException($"{_catalogue.Name} has no class {acronym}.", nameof(acronym));
        int id = _features.Count + 1;
        _features.Add(new S57FeatureRecord
        {
            RecordName = new S57RecordName { RecordNameCode = 100, RecordId = id, AgencyCode = Agency, FeatureId = id, FeatureSubdivision = 1 },
            Primitive = primitive,
            Group = 2,
            ObjectCode = (S57ObjectCode)objectClass.Code,
            RecordVersion = 1,
            UpdateInstruction = S57UpdateInstruction.Insert,
            Attributes = [.. attributes.Select(a => new S57AttributeValue { AttributeCode = (_catalogue.GetAttribute(a.Acronym) ?? throw new ArgumentException($"{_catalogue.Name} has no attribute {a.Acronym}.")).Code, Value = a.Value })],
            NationalAttributes = [],
            SpatialPointers = pointers,
            FeaturePointers = [],
        });
    }

    private S57RecordName Edge((double Lon, double Lat)[] positions)
    {
        if (positions.Length < 2)
            throw new ArgumentException("An edge needs two positions.", nameof(positions));
        var begin = NodeAt(positions[0]);
        var end = NodeAt(positions[^1]);
        var name = new S57RecordName { RecordNameCode = 130, RecordId = _nextEdge++ };
        _vectors.Add(Vector(
            name,
            [.. positions[1..^1].Select(Coordinate)],
            [NodePointer(begin, S57TopologyIndicator.Beginning), NodePointer(end, S57TopologyIndicator.End)]));
        return name;
    }

    private S57VectorRecord ArcEdge(S57RecordName name, S57RecordName begin, S57RecordName end, (double, double) start, S57Coordinate2D centre, (double, double) finish) => new()
    {
        RecordName = name,
        RecordVersion = 1,
        UpdateInstruction = S57UpdateInstruction.Insert,
        Attributes = [],
        VectorPointers = [NodePointer(begin, S57TopologyIndicator.Beginning), NodePointer(end, S57TopologyIndicator.End)],
        Coordinates2D = [],
        Soundings = [],
        ArcCurves =
        [
            new S57ArcCurve
            {
                Type = S57ArcCurveType.ArcThreePointCentre,
                Surface = S57ConstructionSurface.Ellipsoidal,
                Order = 2,
                Resolution = 0,
                FloatingPointMultiplicationFactor = Comf,
                Arcs = [new S57Arc { Start = Coordinate(start), Centre = centre, End = Coordinate(finish) }],
                EllipticalArcs = [],
                ControlPoints = [],
            },
        ],
    };

    private S57RecordName NodeAt((double Lon, double Lat) position)
    {
        var coordinate = Coordinate(position);
        if (_nodes.TryGetValue((coordinate.X, coordinate.Y), out var id))
            return new S57RecordName { RecordNameCode = 120, RecordId = id };
        id = _nextNode++;
        _nodes[(coordinate.X, coordinate.Y)] = id;
        var name = new S57RecordName { RecordNameCode = 120, RecordId = id };
        _vectors.Add(Vector(name, [coordinate], []));
        return name;
    }

    private static S57VectorRecord Vector(S57RecordName name, IReadOnlyList<S57Coordinate2D> coordinates, IReadOnlyList<S57VectorPointer> pointers) => new()
    {
        RecordName = name,
        RecordVersion = 1,
        UpdateInstruction = S57UpdateInstruction.Insert,
        Attributes = [],
        VectorPointers = pointers,
        Coordinates2D = coordinates,
        Soundings = [],
        ArcCurves = [],
    };

    private static S57VectorPointer NodePointer(S57RecordName node, S57TopologyIndicator topology) => new()
    {
        Name = node,
        Orientation = S57Orientation.NotApplicable,
        Usage = S57UsageIndicator.NotApplicable,
        Topology = topology,
        Mask = S57MaskingIndicator.NotApplicable,
    };

    private static S57SpatialPointer Pointer(S57RecordName name, S57Orientation orientation, S57UsageIndicator usage) => new()
    {
        Name = name,
        Orientation = orientation,
        Usage = usage,
        Mask = S57MaskingIndicator.NotApplicable,
    };

    private static S57Coordinate2D Coordinate((double Lon, double Lat) position) => new()
    {
        X = (int)Math.Round(position.Lon * Comf),
        Y = (int)Math.Round(position.Lat * Comf),
    };
}

/// <summary>The synthetic AML cells the tests draw.</summary>
internal static class AmlFixtures
{
    /// <summary>
    /// A RAL cell with every built-in RAL class, about 50.5°N 1.2°W, laid out
    /// like the design's B2a and B2b frames: two ATS routes through a NAVAID
    /// and waypoints, a danger area with limits, controlled airspace as an arc
    /// circle, exercise airspace; a practice area, a Q-route with turning
    /// points, a patrol area and internal waters. Also a radar station, a RAL
    /// class with no style.
    /// </summary>
    public static AmlDataSet Ral() => RalBuilder().Build();

    /// <summary>The builder of <see cref="Ral"/>, for tests that write the cell to a file.</summary>
    public static AmlCellBuilder RalBuilder() => AmlCellBuilder.Ral()
        // Air.
        .Line("atsctl", [(-1.40, 50.62), (-1.20, 50.52), (-1.02, 50.44)], ("OBJNAM", "N864"))
        .Line("atsctl", [(-1.46, 50.49), (-1.20, 50.52), (-1.10, 50.57)], ("OBJNAM", "UL975"))
        .Point("navaid", (-1.20, 50.52), ("OBJNAM", "SUM VOR"))
        .Area(
            "airres",
            [(-1.12, 50.62), (-1.02, 50.62), (-1.02, 50.56), (-1.12, 50.56), (-1.12, 50.62)],
            ("OBJNAM", "D 613"), ("catasr", "501"), ("maxftl", "245"), ("minalt", "0"), ("HUNITS", "2"))
        .Circle("ctlasp", (-1.30, 50.44), 0.03, ("OBJNAM", "CTA 8"), ("maxalt", "3000"), ("minalt", "500"), ("HUNITS", "2"))
        .Area(
            "mexasp",
            [(-1.44, 50.47), (-1.34, 50.48), (-1.33, 50.43), (-1.43, 50.42), (-1.44, 50.47)],
            ("OBJNAM", "Exercise airspace"), ("maxalt", "5000"), ("minalt", "0"), ("HUNITS", "2"))
        // Operational.
        .Area(
            "MIPARE",
            [(-1.16, 50.48), (-1.06, 50.48), (-1.06, 50.43), (-1.16, 50.43), (-1.16, 50.48)],
            ("OBJNAM", "Firing practice area X5"), ("CATMPA", "3"))
        .Line("qroute", [(-1.24, 50.66), (-1.22, 50.60), (-1.18, 50.56)], ("OBJNAM", "Q-route 12"))
        .Point("turnpt", (-1.24, 50.66), ("OBJNAM", "TP 3"))
        .Point("turnpt", (-1.22, 50.60), ("OBJNAM", "TP 4"))
        .Area(
            "patare",
            [(-1.40, 50.58), (-1.30, 50.60), (-1.28, 50.54), (-1.38, 50.53), (-1.40, 50.58)],
            ("OBJNAM", "Patrol area"))
        .Area("intwtr", [(-1.46, 50.66), (-1.36, 50.66), (-1.36, 50.62), (-1.46, 50.62), (-1.46, 50.66)])
        // No style.
        .Point("RADSTA", (-1.08, 50.66), ("OBJNAM", "Radar"));

    /// <summary>
    /// A CLB cell in the RAL cell's area (about 50.5°N 1.2°W), laid out like
    /// the design's B2e frame: nine depth contours from 22 m to 38 m, 2 m
    /// apart, curving down the frame; the depth areas between some of them;
    /// a row of soundings; and a named sea area.
    /// </summary>
    public static AmlDataSet Clb() => ClbBuilder().Build();

    /// <summary>The builder of <see cref="Clb"/>.</summary>
    public static AmlCellBuilder ClbBuilder()
    {
        var builder = AmlCellBuilder.Clb();
        for (int i = 0; i < 9; i++)
        {
            double x = -1.42 + (i * 0.05);
            builder.Line(
                "DEPCNT",
                [(x, 50.67), (x + 0.03, 50.60), (x + 0.01, 50.52), (x + 0.04, 50.41)],
                ("VALDCO", (22 + (2 * i)).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        builder
            .Area("DEPARE", [(-1.42, 50.67), (-1.37, 50.67), (-1.34, 50.41), (-1.38, 50.41), (-1.42, 50.67)], ("DRVAL1", "22"), ("DRVAL2", "24"))
            .Area("DEPARE", [(-1.37, 50.67), (-1.32, 50.67), (-1.29, 50.41), (-1.34, 50.41), (-1.37, 50.67)], ("DRVAL1", "24"), ("DRVAL2", "26"))
            .Soundings((-1.375, 50.47, 23.4), (-1.325, 50.47, 25.1), (-1.275, 50.47, 27.0), (-1.225, 50.47, 29.9), (-1.175, 50.47, 31.7), (-1.125, 50.47, 34.2))
            .Area("SEAARE", [(-1.46, 50.66), (-1.00, 50.66), (-1.00, 50.42), (-1.46, 50.42), (-1.46, 50.66)], ("OBJNAM", "Test Deep"));
        return builder;
    }

    /// <summary>
    /// An MFF cell with a class of each kind: a coastline, a light, a buoy and
    /// a beacon (chart background), a submarine cable and a pipeline area
    /// (seabed), an offshore platform and a traffic separation zone
    /// (operational), and a tidal stream station (environmental).
    /// </summary>
    public static AmlDataSet Mff() => AmlCellBuilder.Mff()
        .Line("COALNE", [(-1.46, 50.42), (-1.44, 50.55), (-1.46, 50.67)])
        .Point("LIGHTS", (-1.43, 50.60), ("OBJNAM", "Test Light"))
        .Point("BOYLAT", (-1.38, 50.58))
        .Point("BCNCAR", (-1.38, 50.50))
        .Line("CBLSUB", [(-1.44, 50.45), (-1.20, 50.47), (-1.02, 50.44)])
        .Area("PIPARE", [(-1.30, 50.64), (-1.20, 50.64), (-1.20, 50.60), (-1.30, 50.60), (-1.30, 50.64)], ("OBJNAM", "Pipeline area"))
        .Point("OFSPLF", (-1.10, 50.62), ("OBJNAM", "Alpha"))
        .Area("TSEZNE", [(-1.25, 50.56), (-1.05, 50.56), (-1.05, 50.53), (-1.25, 50.53), (-1.25, 50.56)])
        .Point("TS_PAD", (-1.15, 50.50))
        .Build();

    /// <summary>An AML 3.0 cell (FAI-like): an ATS route with coded limits, air waypoints, a NAVAID, an airspace restriction with coded limits, and a user-defined object, a class with no style.</summary>
    public static AmlDataSet Fai() => AmlCellBuilder.Aml30()
        .Line("atsctl", [(9.0, 53.6), (9.6, 53.9), (9.8, 54.0)], ("OBJNAM", "UL619"), ("limitu", "FL660"), ("limitl", "FL245"))
        .Point("awaypt", (9.0, 53.6), ("identy", "OSTOR"))
        .Point("awaypt", (9.8, 54.0), ("identy", "BOGMU"))
        .Point("navaid", (9.6, 53.9), ("OBJNAM", "ELBE"))
        .Area(
            "airres",
            [(9.1, 54.0), (9.3, 54.0), (9.3, 53.9), (9.1, 53.9), (9.1, 54.0)],
            ("OBJNAM", "TRA-WESER 2"), ("efaltu", "FL100"), ("efaltl", "01000AGL"), ("SCAMIN", "1500000"))
        .Point("u_defd", (9.6, 53.7))
        .Build();

    /// <summary>The centre of the dense SBO cluster in <see cref="Sbo"/>.</summary>
    public static (double Lon, double Lat) SboClusterCentre { get; } = (-1.10, 50.47);

    /// <summary>
    /// An SBO cell laid out like the design's B2c frame, about 50.5°N 1.2°W:
    /// a contact, a contact classified mine-like (MILCO), a mine and two more
    /// contacts, kilometres apart; and a dense field of 38 contacts within
    /// about 300 m of <see cref="SboClusterCentre"/>, two of them mine-like
    /// (one MILCO contact, one mine). Also a water location, a class with no style.
    /// </summary>
    public static AmlDataSet Sbo()
    {
        var builder = AmlCellBuilder.Sbo()
            .Point("contct", (-1.30, 50.55), ("stacon", "504"), ("contrn", "826"), ("conshp", "sphere"))
            .Point("contct", (-1.18, 50.60), ("stacon", "502"), ("contrn", "3214"))
            .Point("mindev", (-1.04, 50.58), ("stacon", "505"), ("minern", "nme45"))
            .Point("contct", (-1.36, 50.46), ("stacon", "501"))
            .Point("contct", (-1.24, 50.44), ("stacon", "503"));

        // A sunflower field: even, deterministic, about 300 m across.
        var (lon0, lat0) = SboClusterCentre;
        for (int i = 0; i < 38; i++)
        {
            double r = 0.0004 * Math.Sqrt(i + 0.5), a = i * 2.39996;
            var position = (lon0 + (r * Math.Cos(a) / Math.Cos(lat0 * Math.PI / 180)), lat0 + (r * Math.Sin(a)));
            if (i == 7)
                builder.Point("mindev", position, ("stacon", "505"));
            else
                builder.Point("contct", position, ("stacon", i == 21 ? "502" : "504"));
        }

        return builder.Point("watloc", (-1.40, 50.62)).Build();
    }

    /// <summary>
    /// An LBO cell, about 50.5°N 1.2°W: a sensor anomaly, impact scours
    /// east–west and northeast–southwest, and a wreck, an obstruction and a
    /// rock, the chart classes AML rings; all kilometres apart.
    /// </summary>
    public static AmlDataSet Lbo() => AmlCellBuilder.Lbo()
        .Point("senanm", (-1.34, 50.60), ("WATLEV", "3"), ("orcard", "503"))
        .Point("iscour", (-1.22, 50.60), ("orcard", "502"), ("WATLEV", "3"))
        .Point("iscour", (-1.10, 50.60), ("orcard", "503"), ("WATLEV", "3"))
        .Point("WRECKS", (-1.34, 50.50), ("CATWRK", "2"), ("OBJNAM", "SS Example"), ("WATLEV", "3"))
        .Point("OBSTRN", (-1.22, 50.50), ("CATOBS", "7"), ("WATLEV", "3"))
        .Point("UWTROC", (-1.10, 50.50), ("WATLEV", "4"))
        .Build();

    /// <summary>
    /// An AML 3.0 integrated SBO cell (the Baltic trials): small bottom objects,
    /// one classified non-MILCO, one identified as a mine, one MILCO and then neutralised.
    /// </summary>
    public static AmlDataSet SmallBottomObjects() => AmlCellBuilder.Aml30("AMLTEST0U_SBO.000")
        .Point("smalbo", (9.90, 54.47), ("staobj", "503"))
        .Point("smalbo", (9.94, 54.48), ("staobj", "505"), ("minern", "123"))
        .Point("smalbo", (9.98, 54.47), ("staobj", "502,508"))
        .Build();

    /// <summary>
    /// An ESB cell laid out as the design's frame B2d (#1376): sediment bands
    /// (sand, mud, gravel, sandy gravel, rock) with a chalk sample, a bottom
    /// feature as point, line and area, trawl scours, landing and drop zones,
    /// a beach exit, the framed location classes, ice, an environmentally
    /// sensitive area, and a chart light from the chart background family.
    /// </summary>
    public static AmlDataSet Esb() => AmlCellBuilder.Esb()
        // Sediment, north to south.
        .Area("sedlay", Band(50.62, 50.68), ("natsed", "523"))
        .Area("sedlay", Band(50.56, 50.62), ("natsed", "521"))
        .Area("sedlay", Band(50.50, 50.56), ("natsed", "525"))
        .Area("sedlay", Band(50.46, 50.50), ("natsed", "543"))
        .Area("sedlay", Band(50.42, 50.46), ("natsed", "528"))
        .Point("sedlay", (-1.44, 50.43), ("natsed", "519"))
        // Bottom features and scours.
        .Point("botmft", (-1.10, 50.655), ("OBJNAM", "Widget"), ("catbot", "507"))
        .Line("botmft", [(-1.20, 50.60), (-1.16, 50.61), (-1.12, 50.605)], ("OBJNAM", "Pooley"), ("catbot", "504"))
        .Area("botmft", [(-1.08, 50.53), (-1.03, 50.53), (-1.03, 50.51), (-1.08, 50.51), (-1.08, 50.53)], ("OBJNAM", "Reela"), ("catbot", "510"))
        .Line("twlscr", [(-1.10, 50.585), (-1.06, 50.595)], ("ORIENT", "75"))
        .Line("twlscr", [(-1.10, 50.578), (-1.06, 50.588)], ("ORIENT", "75"))
        .Line("twlscr", [(-1.10, 50.571), (-1.06, 50.581)], ("ORIENT", "75"))
        // Zones, the beach exit and locations, on and off the beach.
        .Area("lndzne", [(-1.46, 50.66), (-1.40, 50.66), (-1.40, 50.63), (-1.46, 50.63), (-1.46, 50.66)], ("OBJNAM", "LZ985"))
        .Point("lndpnt", (-1.36, 50.645), ("OBJNAM", "LP846"))
        .Point("drpzne", (-1.38, 50.47), ("OBJNAM", "AX56"))
        .Line("bchext", [(-1.33, 50.555), (-1.37, 50.565), (-1.41, 50.570)], ("exitus", "502"))
        .Point("divloc", (-1.22, 50.475), ("OBJNAM", "Stokes Bay"), ("divact", "503"))
        .Point("viewpt", (-1.44, 50.60))
        .Point("resloc", (-1.44, 50.55), ("typres", "501"))
        .Point("shlloc", (-1.44, 50.51), ("OBJNAM", "Village hall"))
        // Ice.
        .Point("icebrg", (-1.04, 50.45), ("OBJNAM", "Blanco"))
        .Point("icemov", (-1.12, 50.445), ("OBJNAM", "Little Ice Island"), ("icebdr", "504"))
        .Point("icemov", (-1.12, 50.415), ("icebdr", "501"))
        .Line("icelin", [(-1.30, 50.41), (-1.15, 50.405), (-1.00, 50.41)], ("OBJNAM", "Ice edge"))
        .Area("seaice", [(-1.08, 50.48), (-1.00, 50.48), (-1.00, 50.42), (-1.08, 50.42), (-1.08, 50.48)], ("OBJNAM", "SI001"))
        .Area("envare", [(-1.47, 50.58), (-1.38, 50.58), (-1.38, 50.53), (-1.47, 50.53), (-1.47, 50.58)], ("OBJNAM", "Nature reserve"))
        // A chart class, drawn by the chart background family.
        .Point("LIGHTS", (-1.36, 50.60))
        .Build();

    /// <summary>
    /// An AML 3.0 SED cell, as the Baltic trial (#1376): crystalline rock
    /// (544, which AML 1.0 reads as gravelly muddy sand), till, gravelly sand
    /// and shells, with a sediment sample.
    /// </summary>
    public static AmlDataSet Sed() => AmlCellBuilder.Aml30("AMLTEST6U_SED.000")
        .Area("sedlay", [(10.00, 54.60), (10.20, 54.60), (10.20, 54.50), (10.00, 54.50), (10.00, 54.60)], ("natsed", "544"), ("laynum", "0"))
        .Area("sedlay", [(10.20, 54.60), (10.40, 54.60), (10.40, 54.50), (10.20, 54.50), (10.20, 54.60)], ("natsed", "546"), ("laynum", "0"))
        .Area("sedlay", [(10.00, 54.50), (10.20, 54.50), (10.20, 54.40), (10.00, 54.40), (10.00, 54.50)], ("natsed", "540"), ("laynum", "0"))
        .Area("sedlay", [(10.20, 54.50), (10.40, 54.50), (10.40, 54.40), (10.20, 54.40), (10.20, 54.50)], ("natsed", "530"), ("laynum", "0"))
        .Point("sedlay", (10.30, 54.45), ("natsed", "523"))
        .Build();

    // A sediment band from 1.30°W to 1.00°W, clockwise.
    private static (double Lon, double Lat)[] Band(double south, double north) =>
        [(-1.30, north), (-1.00, north), (-1.00, south), (-1.30, south), (-1.30, north)];
}
