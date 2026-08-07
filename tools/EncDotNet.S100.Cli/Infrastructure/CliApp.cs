using EncDotNet.S100.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds the configured <see cref="CommandApp"/> for the <c>s100</c> CLI.
/// Shared by the executable entry point and the test suite.
/// </summary>
internal static class CliApp
{
    public static CommandApp Build(
        string? applicationVersion = null,
        IAnsiConsole? console = null,
        IHelpProvider? helpProvider = null)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("s100");
            config.SetApplicationVersion(
                applicationVersion ?? CliVersionInfo.FromAssembly(typeof(CliApp).Assembly).InformationalVersion);
            if (console is not null)
            {
                config.ConfigureConsole(console);
            }
            config.SetHelpProvider(helpProvider ?? new S100HelpProvider(config.Settings));

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

            config.AddCommand<IdentifyCommand>("identify")
                .WithDescription("Headless ECDIS-style pick: identify vector features and sample coverage values at a lat/lon across one or more dataset layers.")
                .WithExample("identify", "warnings.gml", "--lat", "50.1", "--lon", "-1.3")
                .WithExample("identify", "--layer", "enc.000", "--layer", "bathy.h5", "--lat", "50.1", "--lon", "-1.3")
                .WithExample("identify", "--from", "exchange-set.zip", "--lat", "50.1", "--lon", "-1.3", "--format", "json");

            config.AddCommand<ListSpecsCommand>("list-specs")
                .WithDescription("List supported product specifications and headless-render capability.")
                .WithExample("list-specs");

            config.AddBranch("s57", s57 =>
            {
                s57.SetDescription("S-57 (IHO S-57 / ENC) specific operations.");

                s57.AddCommand<S57ConvertCommand>("convert")
                    .WithDescription("Convert an S-57 base cell to an S-101 dataset (ISO/IEC 8211).")
                    .WithExample("s57", "convert", "-o", "my-s101-dataset.000", "my-s57-dataset.000");
            });

            config.AddBranch("mcp", mcp =>
            {
                mcp.SetDescription("Model Context Protocol (MCP) server operations.");

                mcp.AddCommand<McpServeCommand>("serve")
                    .WithDescription("Serve the read-only S-100 MCP tools over stdio for a fixed set of datasets, so an agent that spawns this process can query features and coverages without a GUI.")
                    .WithExample("mcp", "serve", "dataset.h5")
                    .WithExample("mcp", "serve", "--layer", "enc.000", "--layer", "bathy.h5")
                    .WithExample("mcp", "serve", "--from", "exchange-set.zip", "--only", "S101,S102");
            });
        });
        return app;
    }
}
