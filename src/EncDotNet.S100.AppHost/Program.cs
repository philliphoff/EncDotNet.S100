// .NET Aspire AppHost orchestrating the EncDotNet.S100 viewer.
//
// Running this project starts the Aspire dashboard (logs / traces /
// metrics) and launches the viewer as a managed resource. OTLP
// endpoint and service-name environment variables are injected
// automatically by Aspire; the viewer's
// EncDotNet.S100.Viewer.Diagnostics.ViewerObservability composition
// root reads them via the standard OTEL_* contract.

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EncDotNet_S100_Viewer>("viewer")
    .WithEnvironment("OTEL_SERVICE_NAME", "EncDotNet.S100.Viewer");

builder.Build().Run();
