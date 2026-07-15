using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Round-trip fidelity of <see cref="DrawingInstructionSerializer"/> across all
/// four <see cref="DrawingInstruction"/> subtypes — every field populated, every
/// nullable left null, tuple lists (dashes / coordinate overrides) preserved,
/// list order preserved — plus the corruption / version-mismatch contract that
/// the disk cache relies on (a bad frame deserializes to <c>null</c>, i.e. a
/// miss, never an exception).
/// </summary>
public class DrawingInstructionSerializerTests
{
    private static PointInstruction FullPoint() => new()
    {
        FeatureReference = "feat-point",
        Plane = DisplayPlane.OverRadar,
        ViewingGroup = 26010,
        DrawingPriority = 7,
        ScaleMinimum = 1000.0,
        ScaleMaximum = 90000.0,
        SymbolReference = "BOYLAT01",
        SymbolScale = 1.25,
        Rotation = 45.5,
        LocalOffsetX = 1.5,
        LocalOffsetY = -2.5,
        LinePlacementPosition = 0.5,
        CoordinateOverride = new GeoPosition(50.5, -1.25),
    };

    private static LineInstruction FullLine() => new()
    {
        FeatureReference = "feat-line",
        Plane = DisplayPlane.UnderRadar,
        ViewingGroup = 27010,
        DrawingPriority = 4,
        ScaleMinimum = null,
        ScaleMaximum = 50000.0,
        LineStyleReference = "DASH",
        LineWidth = 0.32,
        LineColor = "CHBLK",
        Dashes = [(0.0, 1.0), (1.0, 2.0)],
        DashOnLength = 1.0,
        CoordinatesOverride = [new GeoPosition(50.1, -1.1), new GeoPosition(50.2, -1.2), new GeoPosition(50.3, -1.3)],
    };

    private static AreaInstruction FullArea() => new()
    {
        FeatureReference = "feat-area",
        Plane = DisplayPlane.UnderRadar,
        ViewingGroup = 25010,
        DrawingPriority = 2,
        ScaleMinimum = 500.0,
        ScaleMaximum = null,
        AreaFillReference = "DIAMOND1",
        FillColor = "DEPVS",
        Transparency = 0.25,
        OutlineStyleReference = "OUTLINE",
    };

    private static TextInstruction FullText() => new()
    {
        FeatureReference = "feat-text",
        Plane = DisplayPlane.OverRadar,
        ViewingGroup = 28010,
        DrawingPriority = 9,
        ScaleMinimum = 100.0,
        ScaleMaximum = 12000.0,
        Text = "No 5",
        FontReference = "FONT1",
        FontSize = 11.5,
        FontColor = "CHWHT",
        FontTransparency = 0.1,
        BackgroundColor = "CHBLK",
        BackgroundTransparency = 0.4,
        Rotation = 12.0,
        LinePlacementPosition = 0.75,
        HorizontalAlignment = TextHorizontalAlignment.End,
        VerticalAlignment = TextVerticalAlignment.Top,
        OffsetX = 0.6,
        OffsetY = -0.6,
        LineStartOffset = 0.1,
        LineEndOffset = 0.9,
        LineOffsetMode = LinePlacementMode.Absolute,
        CoordinateOverride = new GeoPosition(51.0, 1.0),
    };

    // Minimal variants: every optional / nullable left at its default so the
    // null-branch of every Write/Read helper is exercised.
    private static PointInstruction MinimalPoint() => new() { FeatureReference = "p" };
    private static LineInstruction MinimalLine() => new() { FeatureReference = "l" };
    private static AreaInstruction MinimalArea() => new() { FeatureReference = "a" };
    private static TextInstruction MinimalText() => new() { FeatureReference = "t", Text = "x" };

    [Fact]
    public void RoundTrip_AllTypes_PreservesEveryFieldAndOrder()
    {
        var original = new DrawingInstruction[]
        {
            FullArea(), FullLine(), FullPoint(), FullText(),
            MinimalArea(), MinimalLine(), MinimalPoint(), MinimalText(),
        };

        var bytes = DrawingInstructionSerializer.Serialize(original);
        var restored = DrawingInstructionSerializer.TryDeserialize(bytes);

        Assert.NotNull(restored);
        Assert.Equal(original.Length, restored!.Count);
        for (var i = 0; i < original.Length; i++)
            AssertInstructionEqual(original[i], restored[i]);
    }

    [Fact]
    public void RoundTrip_EmptyList_IsEmpty()
    {
        var bytes = DrawingInstructionSerializer.Serialize([]);
        var restored = DrawingInstructionSerializer.TryDeserialize(bytes);

        Assert.NotNull(restored);
        Assert.Empty(restored!);
    }

    [Fact]
    public void TryDeserialize_FormatVersionMismatch_ReturnsNull()
    {
        var bytes = DrawingInstructionSerializer.Serialize([FullPoint()]);
        // The leading four bytes are the FormatVersion int; corrupt them.
        BitConverter.GetBytes(DrawingInstructionSerializer.FormatVersion + 999).CopyTo(bytes, 0);

        Assert.Null(DrawingInstructionSerializer.TryDeserialize(bytes));
    }

    [Fact]
    public void TryDeserialize_Truncated_ReturnsNull()
    {
        var bytes = DrawingInstructionSerializer.Serialize([FullArea(), FullText()]);
        var truncated = bytes[..(bytes.Length / 2)];

        Assert.Null(DrawingInstructionSerializer.TryDeserialize(truncated));
    }

    [Fact]
    public void TryDeserialize_GarbageCount_ReturnsNull_NoThrow()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(DrawingInstructionSerializer.FormatVersion);
            w.Write(int.MaxValue); // hostile entry count
        }

        Assert.Null(DrawingInstructionSerializer.TryDeserialize(ms.ToArray()));
    }

    [Fact]
    public void Serialize_NullArgument_Throws() =>
        Assert.Throws<ArgumentNullException>(() => DrawingInstructionSerializer.Serialize(null!));

    private static void AssertInstructionEqual(DrawingInstruction expected, DrawingInstruction actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.FeatureReference, actual.FeatureReference);
        Assert.Equal(expected.Plane, actual.Plane);
        Assert.Equal(expected.ViewingGroup, actual.ViewingGroup);
        Assert.Equal(expected.DrawingPriority, actual.DrawingPriority);
        Assert.Equal(expected.ScaleMinimum, actual.ScaleMinimum);
        Assert.Equal(expected.ScaleMaximum, actual.ScaleMaximum);

        switch (expected)
        {
            case PointInstruction e:
                var ap = Assert.IsType<PointInstruction>(actual);
                Assert.Equal(e.SymbolReference, ap.SymbolReference);
                Assert.Equal(e.SymbolScale, ap.SymbolScale);
                Assert.Equal(e.Rotation, ap.Rotation);
                Assert.Equal(e.LocalOffsetX, ap.LocalOffsetX);
                Assert.Equal(e.LocalOffsetY, ap.LocalOffsetY);
                Assert.Equal(e.LinePlacementPosition, ap.LinePlacementPosition);
                Assert.Equal(e.CoordinateOverride, ap.CoordinateOverride);
                break;

            case LineInstruction e:
                var al = Assert.IsType<LineInstruction>(actual);
                Assert.Equal(e.LineStyleReference, al.LineStyleReference);
                Assert.Equal(e.LineWidth, al.LineWidth);
                Assert.Equal(e.LineColor, al.LineColor);
                Assert.Equal(e.Dashes, al.Dashes);
                Assert.Equal(e.DashOnLength, al.DashOnLength);
                Assert.Equal(e.CoordinatesOverride, al.CoordinatesOverride);
                break;

            case AreaInstruction e:
                var aa = Assert.IsType<AreaInstruction>(actual);
                Assert.Equal(e.AreaFillReference, aa.AreaFillReference);
                Assert.Equal(e.FillColor, aa.FillColor);
                Assert.Equal(e.Transparency, aa.Transparency);
                Assert.Equal(e.OutlineStyleReference, aa.OutlineStyleReference);
                break;

            case TextInstruction e:
                var at = Assert.IsType<TextInstruction>(actual);
                Assert.Equal(e.Text, at.Text);
                Assert.Equal(e.FontReference, at.FontReference);
                Assert.Equal(e.FontSize, at.FontSize);
                Assert.Equal(e.FontColor, at.FontColor);
                Assert.Equal(e.FontTransparency, at.FontTransparency);
                Assert.Equal(e.BackgroundColor, at.BackgroundColor);
                Assert.Equal(e.BackgroundTransparency, at.BackgroundTransparency);
                Assert.Equal(e.Rotation, at.Rotation);
                Assert.Equal(e.LinePlacementPosition, at.LinePlacementPosition);
                Assert.Equal(e.HorizontalAlignment, at.HorizontalAlignment);
                Assert.Equal(e.VerticalAlignment, at.VerticalAlignment);
                Assert.Equal(e.OffsetX, at.OffsetX);
                Assert.Equal(e.OffsetY, at.OffsetY);
                Assert.Equal(e.LineStartOffset, at.LineStartOffset);
                Assert.Equal(e.LineEndOffset, at.LineEndOffset);
                Assert.Equal(e.LineOffsetMode, at.LineOffsetMode);
                Assert.Equal(e.CoordinateOverride, at.CoordinateOverride);
                break;

            default:
                Assert.Fail($"Unhandled instruction type {expected.GetType().Name}");
                break;
        }
    }

    // The disk-backed portrayal cache persists these enums by their numeric
    // ordinal (DrawingInstructionSerializer writes/reads them as int). The
    // values are therefore an on-disk contract: reordering or renumbering a
    // member would silently reinterpret already-cached files. These asserts
    // pin the values so any accidental change fails the build; a deliberate
    // change must also bump DrawingInstructionSerializer.FormatVersion.
    [Fact]
    public void PersistedEnumValues_AreStable()
    {
        Assert.Equal(0, (int)DisplayPlane.UnderRadar);
        Assert.Equal(1, (int)DisplayPlane.OverRadar);

        Assert.Equal(0, (int)TextHorizontalAlignment.Start);
        Assert.Equal(1, (int)TextHorizontalAlignment.Center);
        Assert.Equal(2, (int)TextHorizontalAlignment.End);

        Assert.Equal(0, (int)TextVerticalAlignment.Top);
        Assert.Equal(1, (int)TextVerticalAlignment.Center);
        Assert.Equal(2, (int)TextVerticalAlignment.Bottom);

        Assert.Equal(0, (int)LinePlacementMode.Relative);
        Assert.Equal(1, (int)LinePlacementMode.Absolute);
    }
}
