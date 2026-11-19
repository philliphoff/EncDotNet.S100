using System.Diagnostics.CodeAnalysis;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// The <c>tiles serve</c> web page (issues #1573, #1575): Razor components
/// rendered in the CLI, with interactive-server parts. The components read
/// what's served through <see cref="IServePageData"/>, in-process. The
/// browser loads blazor.web.js and MapLibre from this server.
/// </summary>
internal static class ServeWeb
{
    /// <summary>Registers the components and the data they show; <paramref name="hasToken"/> when the URLs carry an access token.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The CLI isn't published trimmed; Razor Components aren't trim-safe.")]
    public static void AddServices(WebApplicationBuilder builder, ITileSource source, bool hasToken)
    {
        // Run from a build (not a publish), the page's files and the
        // framework's are found through the build's static web assets
        // manifest. The host reads it only in Development, and by the host
        // application's name (a test host's, in the CLI's tests), so it's
        // named here. Published, there's no manifest: the files are in wwwroot.
        var runtimeManifest = Manifest("runtime");
        if (File.Exists(runtimeManifest))
        {
            builder.WebHost.UseSetting(WebHostDefaults.StaticWebAssetsKey, runtimeManifest);
            builder.WebHost.UseStaticWebAssets();
        }

        builder.Services.AddSingleton<IServePageData>(new TileSourcePageData(source, hasToken));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
    }

    /// <summary>Maps the page, its static files (blazor.web.js, MapLibre) and the circuit's hub. Call after routing.</summary>
    public static void Map(WebApplication app)
    {
        app.UseAntiforgery();
        // The endpoints manifest, named for the CLI's assembly, not the host's
        // application (a test host's, in the CLI's tests).
        var endpointsManifest = Manifest("endpoints");
        if (File.Exists(endpointsManifest))
            app.MapStaticAssets(endpointsManifest);
        app.MapRazorComponents<ServeApp>().AddInteractiveServerRenderMode();
    }

    /// <summary>The path of the CLI's <paramref name="kind"/> static web assets manifest, beside it.</summary>
    private static string Manifest(string kind) =>
        Path.Combine(AppContext.BaseDirectory, typeof(ServeWeb).Assembly.GetName().Name + ".staticwebassets." + kind + ".json");
}
