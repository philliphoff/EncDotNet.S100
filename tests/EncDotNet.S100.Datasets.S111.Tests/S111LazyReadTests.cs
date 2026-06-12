using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Tests for the deferred (lazy) dcf2 read path added via
/// <see cref="S111ReadOptions.DeferValueReads"/>. The lazy path derives
/// per-time-step time points arithmetically from
/// <c>dateTimeOfFirstRecord</c> + i × <c>timeRecordInterval</c>
/// (S-111 Edition 2.0.0 §10.2.6) and reads each step's <c>values</c>
/// compound only on first access.
/// </summary>
public class S111LazyReadTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"s111-lazy-{Guid.NewGuid():N}.h5");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static S111FixtureBuilder.SpecRow[] Step(int numLat, int numLon, float speedBase)
    {
        var rows = new S111FixtureBuilder.SpecRow[numLat * numLon];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new S111FixtureBuilder.SpecRow
            {
                SurfaceCurrentSpeed = speedBase + i * 0.01f,
                SurfaceCurrentDirection = (speedBase * 10 + i) % 360,
            };
        }
        return rows;
    }

    [Fact]
    public void LazyRead_DerivesTimePointsArithmetically()
    {
        const int numLat = 2, numLon = 3;
        var steps = new[]
        {
            Step(numLat, numLon, 1f),
            Step(numLat, numLon, 2f),
            Step(numLat, numLon, 3f),
        };
        S111FixtureBuilder.WriteMultiStepFile(
            _path, steps, numLat, numLon,
            dateTimeOfFirstRecord: "20260101T00:00:00Z",
            timeRecordInterval: 1200);

        using var file = PureHdfFile.Open(_path);
        var data = S111DatasetReader.ReadAny(file, new S111ReadOptions { DeferValueReads = true });

        var g = Assert.IsType<S111DatasetData.GriddedCoverage>(data);
        Assert.Equal(3, g.Dataset.Coverages.Count);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(baseTime, g.Dataset.Coverages[0].TimePoint);
        Assert.Equal(baseTime.AddMinutes(20), g.Dataset.Coverages[1].TimePoint);
        Assert.Equal(baseTime.AddMinutes(40), g.Dataset.Coverages[2].TimePoint);
    }

    [Fact]
    public void LazyRead_ProducesSameValuesAsEager()
    {
        const int numLat = 3, numLon = 4;
        var steps = new[]
        {
            Step(numLat, numLon, 1f),
            Step(numLat, numLon, 5f),
        };
        S111FixtureBuilder.WriteMultiStepFile(_path, steps, numLat, numLon);

        S111DatasetData eager, lazy;
        using (var f1 = PureHdfFile.Open(_path))
            eager = S111DatasetReader.ReadAny(f1);

        using var f2 = PureHdfFile.Open(_path);
        lazy = S111DatasetReader.ReadAny(f2, new S111ReadOptions { DeferValueReads = true });

        var ge = Assert.IsType<S111DatasetData.GriddedCoverage>(eager);
        var gl = Assert.IsType<S111DatasetData.GriddedCoverage>(lazy);

        Assert.Equal(ge.Dataset.Coverages.Count, gl.Dataset.Coverages.Count);
        for (int c = 0; c < ge.Dataset.Coverages.Count; c++)
        {
            var ec = ge.Dataset.Coverages[c];
            var lc = gl.Dataset.Coverages[c];
            Assert.Equal(ec.TimePoint, lc.TimePoint);
            Assert.Equal(ec.Values.Length, lc.Values.Length);
            for (int i = 0; i < ec.Values.Length; i++)
            {
                Assert.Equal(ec.Values[i].Speed, lc.Values[i].Speed);
                Assert.Equal(ec.Values[i].Direction, lc.Values[i].Direction);
            }
        }
    }

    [Fact]
    public void LazyRead_ValuesCachedAcrossAccesses()
    {
        const int numLat = 2, numLon = 2;
        var steps = new[] { Step(numLat, numLon, 1f) };
        S111FixtureBuilder.WriteMultiStepFile(_path, steps, numLat, numLon);

        using var file = PureHdfFile.Open(_path);
        var data = S111DatasetReader.ReadAny(file, new S111ReadOptions { DeferValueReads = true });
        var g = Assert.IsType<S111DatasetData.GriddedCoverage>(data);

        var first = g.Dataset.Coverages[0].Values;
        var second = g.Dataset.Coverages[0].Values;
        Assert.Same(first, second);
    }

    [Fact]
    public void EagerRead_DefaultOverload_StillWorks()
    {
        const int numLat = 2, numLon = 2;
        var steps = new[] { Step(numLat, numLon, 1f), Step(numLat, numLon, 2f) };
        S111FixtureBuilder.WriteMultiStepFile(_path, steps, numLat, numLon);

        using var file = PureHdfFile.Open(_path);
        var data = S111DatasetReader.ReadAny(file, options: null);
        var g = Assert.IsType<S111DatasetData.GriddedCoverage>(data);

        Assert.Equal(2, g.Dataset.Coverages.Count);
        Assert.Equal(numLat * numLon, g.Dataset.Coverages[0].Values.Length);
    }
}
