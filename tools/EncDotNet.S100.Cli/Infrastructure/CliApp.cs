using EncDotNet.S100.Cli.Commands;
using Spectre.Console.Cli;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds the configured <see cref="CommandApp"/> for the <c>s100</c> CLI.
/// Shared by the executable entry point and the test suite.
/// </summary>
internal static class CliApp
{
    public static CommandApp Build()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("s100");

            config.AddCommand<RenderCommand>("render")
                .WithDescription("Render one S-100 dataset — or composite several (with --layer, or an entire exchange set) — to an image (PNG, JPEG, or WebP).")
                .WithExample("render", "dataset.h5", "out.png")
                .WithExample("render", "warnings.gml", "out.png", "--width", "2048", "--height", "1536")
                .WithExample("render", "currents.h5", "out.png", "--time-step", "2", "--palette", "night")
                .WithExample("render", "--layer", "enc.000", "--layer", "bathy.h5", "--layer", "warnings.gml", "chart.png")
                .WithExample("render", "--layer", "enc.000", "--layer", "bathy.h5", "-o", "chart.png", "--bbox", "-1.5,50.0,-1.0,50.5")
                .WithExample("render", "exchange-set/", "chart.png")
                .WithExample("render", "--exchange-set", "exchange-set.zip", "-o", "chart.png", "--only", "S101,S102");

            config.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate an S-100 dataset against its product specification's normative rule pack, or verify an exchange set's integrity (S-100 Part 15 signatures, or S-57 / S-63 CATALOG.031 CRCs).")
                .WithExample("validate", "warnings.gml")
                .WithExample("validate", "route.gml", "--strict")
                .WithExample("validate", "chart.000", "--suppress", "S101-R-1.2,S101-R-3.2")
                .WithExample("validate", "currents.h5", "--format", "json")
                .WithExample("validate", "exchangeset/CATALOG.XML")
                .WithExample("validate", "exchangeset.zip", "--format", "json")
                .WithExample("validate", "s57set/CATALOG.031");

            config.AddCommand<InfoCommand>("info")
                .WithDescription("Show the detected spec, edition, and available time steps for a dataset.")
                .WithExample("info", "dataset.h5");

            config.AddCommand<ListSpecsCommand>("list-specs")
                .WithDescription("List supported product specifications and headless-render capability.")
                .WithExample("list-specs");
        });
        return app;
    }
}
