namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Pins that a cell cut off part-way through a record (a partial download or copy) fails to
/// load with <see cref="EndOfStreamException"/>, rather than loading as the records before
/// the cut (issue #1105).
/// </summary>
public sealed class S101DocumentReaderTruncatedFileTests : IDisposable
{
    private const int LeaderLength = 24;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "S101Truncated-" + Guid.NewGuid().ToString("N"));

    public S101DocumentReaderTruncatedFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public enum Cut
    {
        FirstLeader,
        Leader,
        Directory,
        FieldArea,
    }

    [Theory]
    [InlineData(Cut.FirstLeader)]
    [InlineData(Cut.Leader)]
    [InlineData(Cut.Directory)]
    [InlineData(Cut.FieldArea)]
    public void Open_FileCutPartWayThroughRecord_ThrowsEndOfStream(Cut cut)
    {
        var bytes = File.ReadAllBytes(LocateFixture());
        var path = WriteTruncated(bytes, CutPoint(bytes, cut));

        Assert.Throws<EndOfStreamException>(() => S101Dataset.Open(path));
    }

    [Fact]
    public void Open_IssueReproductionFirst3000Bytes_ThrowsEndOfStream()
    {
        var bytes = File.ReadAllBytes(LocateFixture());
        var path = WriteTruncated(bytes, 3000);

        var exception = Assert.Throws<EndOfStreamException>(() => S101Dataset.Open(path));
        Assert.Contains("record 3", exception.Message);
    }

    [Fact]
    public void Open_StreamCutPartWayThroughRecord_ThrowsEndOfStream()
    {
        var bytes = File.ReadAllBytes(LocateFixture());
        using var stream = new MemoryStream(bytes[..CutPoint(bytes, Cut.FieldArea)]);

        Assert.Throws<EndOfStreamException>(() => S101Dataset.Open(stream));
    }

    [Fact]
    public void Open_FileWithUnreadableBytesAfterLastRecord_ThrowsInvalidData()
    {
        var bytes = File.ReadAllBytes(LocateFixture());
        var path = Path.Combine(_directory, "TRAILING.000");
        File.WriteAllBytes(path, [.. bytes, .. new byte[LeaderLength]]);

        Assert.Throws<InvalidDataException>(() => S101Dataset.Open(path));
    }

    [Fact]
    public void Open_CompleteFixture_Loads()
    {
        var dataset = S101Dataset.Open(LocateFixture());

        Assert.NotEmpty(dataset.Document.Features);
    }

    // Picks a byte offset inside the third record (the first data record after the DDR and
    // DSID), or inside the DDR's leader for FirstLeader.
    private static int CutPoint(byte[] bytes, Cut cut)
    {
        if (cut == Cut.FirstLeader)
            return LeaderLength / 2;

        var start = 0;
        for (var i = 0; i < 2; i++)
            start += RecordLength(bytes, start);

        var baseAddress = int.Parse(System.Text.Encoding.ASCII.GetString(bytes, start + 12, 5));
        return cut switch
        {
            Cut.Leader => start + (LeaderLength / 2),
            Cut.Directory => start + LeaderLength + 1,
            Cut.FieldArea => start + baseAddress + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(cut)),
        };
    }

    private static int RecordLength(byte[] bytes, int start) =>
        int.Parse(System.Text.Encoding.ASCII.GetString(bytes, start, 5));

    private string WriteTruncated(byte[] bytes, int length)
    {
        var path = Path.Combine(_directory, "US5BROKN.000");
        File.WriteAllBytes(path, bytes[..length]);
        return path;
    }

    private static string LocateFixture()
    {
        var relative = Path.Combine("S101", "S-101", "DATASET_FILES", "101AA00DS0022.000");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", relative);
            if (File.Exists(candidate))
                return candidate;
        }

        Assert.Skip("S-101 fixture 101AA00DS0022.000 not found.");
        return null!;
    }
}
