using System.Buffers.Binary;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Datasets.S104.Tests.Writing;

/// <summary>
/// The HDF5 writing layer (<see cref="Hdf5WriteGroup"/> and
/// <see cref="PureHdfWriter"/>) that the S-104 writer, and later grid
/// encoders, build on.
/// </summary>
public sealed class PureHdfWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("purehdf-writer-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Write_KeepsAttributeTypesAndValues()
    {
        var root = new Hdf5WriteGroup()
            .SetAttribute("text", "hello")
            .SetAttribute("u8", (byte)8)
            .SetAttribute("u16", (ushort)360)
            .SetAttribute("u32", 7u)
            .SetAttribute("i32", 4326)
            .SetAttribute("i64", -5L)
            .SetAttribute("f32", 0.25f)
            .SetAttribute("f64", 1.125)
            .SetEnumAttribute("kind", Kind.Second);
        root.Group("A").Group("B").SetAttribute("deep", "yes");

        var path = Write(root);

        using (var raw = H5File.OpenRead(path))
        {
            Assert.Equal(H5DataTypeClass.Enumerated, raw.Attribute("kind").Type.Class);
            Assert.Equal(H5DataTypeClass.VariableLength, raw.Attribute("text").Type.Class);
            Assert.Equal(2, raw.Attribute("u16").Type.Size);
        }

        using var file = PureHdfFile.Open(path);
        Assert.Equal("hello", file.Root.ReadStringAttribute("text"));
        Assert.Equal(8, file.Root.ReadInt64Attribute("u8"));
        Assert.Equal(360, file.Root.ReadInt64Attribute("u16"));
        Assert.Equal(7, file.Root.ReadInt64Attribute("u32"));
        Assert.Equal(4326, file.Root.ReadInt64Attribute("i32"));
        Assert.Equal(-5, file.Root.ReadInt64Attribute("i64"));
        Assert.Equal(0.25, file.Root.ReadDoubleAttribute("f32"));
        Assert.Equal(1.125, file.Root.ReadDoubleAttribute("f64"));
        Assert.Equal(2, file.Root.ReadInt64Attribute("kind"));
        Assert.Equal("yes", file.Root.OpenGroup("A").OpenGroup("B").ReadStringAttribute("deep"));
    }

    [Fact]
    public void Write_NamesCompoundMembersFromTheAttribute()
    {
        var root = new Hdf5WriteGroup()
            .SetDataset("values", [new Row { Height = 1.5f, Trend = 2 }, new Row { Height = -0.5f, Trend = 1 }])
            .SetDataset("table", [new TextRow { Unit = "metre" }]);

        var path = Write(root);

        using var file = PureHdfFile.Open(path);
        var compound = file.Root.ReadRawCompoundDataset("values");
        Assert.Equal(2, compound.RecordCount);
        var height = Assert.IsType<CompoundMemberInfo>(compound.FindMember("waterLevelHeight"));
        var trend = Assert.IsType<CompoundMemberInfo>(compound.FindMember("waterLevelTrend"));
        Assert.Equal(CompoundMemberKind.Float32, height.Kind);
        Assert.Equal(CompoundMemberKind.UInt8, trend.Kind);
        var second = compound.Data.AsSpan(compound.RecordSize, compound.RecordSize);
        Assert.Equal(-0.5f, BinaryPrimitives.ReadSingleLittleEndian(second[height.Offset..]));
        Assert.Equal(1, second[trend.Offset]);
        using var raw = H5File.OpenRead(path);
        var member = Assert.Single(raw.Dataset("table").Type.Compound.Members);
        Assert.Equal("uom.name", member.Name);
        Assert.Equal(H5DataTypeClass.VariableLength, member.Type.Class);
    }

    [Fact]
    public void Write_GivesAGridItsDimensions()
    {
        var root = new Hdf5WriteGroup().SetDataset("grid", [1f, 2f, 3f, 4f, 5f, 6f], 2, 3);

        var path = Write(root);

        using var raw = H5File.OpenRead(path);
        var dataset = raw.Dataset("grid");
        Assert.Equal([2ul, 3ul], dataset.Space.Dimensions);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], dataset.Read<float[]>());
    }

    [Fact]
    public void Group_RejectsBadAndClashingNames()
    {
        var root = new Hdf5WriteGroup().SetDataset("values", [1f]);

        Assert.Throws<ArgumentException>(() => root.Group("values"));
        Assert.Throws<ArgumentException>(() => root.Group("a/b"));
        Assert.Throws<ArgumentException>(() => root.Group(""));
        root.Group("child");
        Assert.Throws<ArgumentException>(() => root.SetDataset("child", [1f]));
        Assert.Throws<ArgumentException>(() => root.SetDataset("grid", [1f, 2f, 3f], 2, 2));
    }

    private string Write(Hdf5WriteGroup root)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.h5");
        PureHdfWriter.Write(root, path);
        return path;
    }

    private enum Kind : byte
    {
        First = 1,
        Second = 2,
    }

    private struct Row
    {
        [Hdf5Member("waterLevelHeight")] public float Height;
        [Hdf5Member("waterLevelTrend")] public byte Trend;
    }

    private struct TextRow
    {
        [Hdf5Member("uom.name")] public string Unit;
    }
}
