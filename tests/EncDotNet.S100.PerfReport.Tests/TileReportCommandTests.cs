namespace EncDotNet.S100.PerfReport.Tests;

public class TileReportCommandTests
{
    [Fact]
    public void WriteReport_attributes_queue_and_raster_costs()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                """{"kind":"header","version":1}""",
                """{"kind":"span","name":"s100.render.tile.job","traceId":"a","spanId":"job1","durationMs":20,"tags":{"s100.render.tile.keys":"8/10/20","s100.render.tile.priority":"visible","s100.render.tile.outcome":"raster","s100.render.tile.queue_wait_ms":"80"}}""",
                """{"kind":"span","name":"s100.render.tile.rasterize","traceId":"a","spanId":"r1","parentSpanId":"job1","durationMs":15,"tags":{"s100.render.tile.candidate_operations":"500"}}""",
                """{"kind":"span","name":"s100.render.tile.stage.publish","traceId":"a","spanId":"p1","parentSpanId":"job1","durationMs":2,"tags":{}}""",
                """{"kind":"span","name":"s100.render.tile.job","traceId":"b","spanId":"job2","durationMs":55,"tags":{"s100.render.tile.keys":"9/30/40","s100.render.tile.priority":"predicted","s100.render.tile.outcome":"raster","s100.render.tile.queue_wait_ms":"1"}}""",
                """{"kind":"span","name":"s100.render.tile.rasterize","traceId":"b","spanId":"r2","parentSpanId":"job2","durationMs":50,"tags":{"s100.render.tile.candidate_operations":"2000"}}""",
                """{"kind":"span","name":"s100.render.tile.cache.persist","traceId":"c","spanId":"w1","durationMs":12,"tags":{"s100.render.tile.key":"8/10/20"}}""",
            ]);
            var data = TelemetryFileReader.Read(path);
            using var writer = new StringWriter();

            TileReportCommand.WriteReport(writer, data, path, top: 10);

            var report = writer.ToString();
            Assert.Contains("# Tile Render Report", report);
            Assert.Contains("| queue | 1 | 50.0% |", report);
            Assert.Contains("| raster | 1 | 50.0% |", report);
            Assert.Contains("8/10/20", report);
            Assert.Contains("2000", report);
            Assert.Contains("## Background persistence", report);
            Assert.Contains("| Completed writes | 1 |", report);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
