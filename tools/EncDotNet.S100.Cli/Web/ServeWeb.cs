using System.Diagnostics.CodeAnalysis;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// Spike: the serve UI as Razor components rendered in the CLI, with
/// interactive-server islands. The components read the <see cref="ITileSource"/>
/// in-process; the browser downloads only blazor.web.js and MapLibre.
/// </summary>
internal static class ServeWeb
{
    /// <summary>Registers the components and the source they show.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The CLI isn't published trimmed; Razor Components aren't trim-safe.")]
    public static void AddServices(WebApplicationBuilder builder, ITileSource source)
    {
        // Run from a build (not a publish), the framework's files are found
        // through the build's static web assets manifest, which the host only
        // reads in Development unless asked; published, they're in wwwroot.
        builder.WebHost.UseStaticWebAssets();
        builder.Services.AddSingleton(source);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    }

    /// <summary>Maps the UI's pages, blazor.web.js and the circuit's hub.</summary>
    public static void Map(WebApplication app)
    {
        app.UseAntiforgery();
        // The manifest is named for the CLI's assembly, not the host's
        // application (a test host's, in the CLI's tests).
        var manifest = Path.Combine(AppContext.BaseDirectory, typeof(ServeWeb).Assembly.GetName().Name + ".staticwebassets.endpoints.json");
        if (File.Exists(manifest))
            app.MapStaticAssets(manifest);
        app.MapRazorComponents<ServeApp>().AddInteractiveServerRenderMode();
    }

    /// <summary>The page's styles.</summary>
    public const string Css = """
        html, body { margin: 0; height: 100%; font: 14px system-ui, sans-serif; color: #1b1f24; background: #fff; }
        .served { display: flex; height: 100vh; }
        .served aside { width: 340px; overflow: auto; padding: 12px; border-right: 1px solid #d0d7de; display: flex; flex-direction: column; gap: 8px; box-sizing: border-box; }
        .served aside h2 { margin: 0; font-size: 16px; word-break: break-all; }
        .served aside h3 { margin: 8px 0 0; font-size: 14px; }
        .served aside label { display: flex; flex-direction: column; gap: 4px; }
        .map { flex: 1; }
        .muted { color: #656d76; }
        .datasets, .features { list-style: none; padding: 0; margin: 0; }
        .datasets li, .features li { padding: 6px 4px; border-bottom: 1px solid #eaeef2; display: flex; flex-wrap: wrap; gap: 6px; }
        .datasets li.clickable { cursor: pointer; }
        .datasets li.clickable:hover { background: #f6f8fa; }
        .product { font-weight: 600; }
        .file { font-family: ui-monospace, monospace; }
        .features dl { display: grid; grid-template-columns: max-content 1fr; gap: 2px 8px; margin: 4px 0 0; width: 100%; font-size: 12px; }
        .features dt { color: #656d76; }
        .features dd { margin: 0; word-break: break-word; }
        @media (max-width: 700px) { .served { flex-direction: column; } .served aside { width: auto; max-height: 45%; border-right: 0; } }
        """;

    /// <summary>A thin MapLibre wrapper: the component owns the state, this owns the map.</summary>
    public const string MapScript = """
        window.s100Map = (() => {
          let map;
          return {
            show(container, tileJsonUrl, dotnet) {
              map = new maplibregl.Map({
                container,
                style: {
                  version: 8,
                  sources: {
                    osm: { type: "raster", tiles: ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"], tileSize: 256, attribution: "© OpenStreetMap contributors" },
                    chart: { type: "raster", url: new URL(tileJsonUrl, document.baseURI).href, tileSize: 256 },
                  },
                  layers: [{ id: "osm", type: "raster", source: "osm" }, { id: "chart", type: "raster", source: "chart" }],
                },
                center: [0, 0],
                zoom: 1,
              });
              map.addControl(new maplibregl.NavigationControl());
              map.on("click", e => {
                const metresPerPixel = 156543.03392 * Math.cos(e.lngLat.lat * Math.PI / 180) / Math.pow(2, map.getZoom()) / 2;
                dotnet.invokeMethodAsync("PickAsync", e.lngLat.lat, e.lngLat.lng, metresPerPixel);
              });
            },
            setChart(tileJsonUrl) {
              const apply = () => map.getSource("chart").setUrl(new URL(tileJsonUrl, document.baseURI).href);
              map.isStyleLoaded() ? apply() : map.once("load", apply);
            },
            fit([w, s, e, n]) {
              const fit = () => map.fitBounds([[w, s], [e, n]], { padding: 20, animate: false });
              map.loaded() ? fit() : map.once("load", fit);
            },
          };
        })();
        """;
}
