using System.Globalization;
using System.Net;
using EncDotNet.S100.Collections.Feeds;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Cli.Infrastructure.Feeds;

/// <summary>
/// Serves a <see cref="FeedPublisher"/>'s feed over HTTP (issue #680):
/// <c>feed.json</c> (with its ETag, so readers revalidate cheaply) and
/// <c>items/&lt;id&gt;.zip</c> for each published dataset. With a token, every
/// route lives under <c>/&lt;token&gt;/</c>, so the feed's relative item URLs
/// carry it too.
/// </summary>
internal sealed class FeedServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FeedServer(WebApplication app, Uri feedUri)
    {
        _app = app;
        FeedUri = feedUri;
    }

    /// <summary>The feed's URL, as bound (the host as given; the actual port).</summary>
    public Uri FeedUri { get; }

    /// <summary>The bound port.</summary>
    public int Port => FeedUri.Port;

    /// <summary>Starts serving <paramref name="publisher"/> on <paramref name="address"/>:<paramref name="port"/> (0 for any free port).</summary>
    public static async Task<FeedServer> StartAsync(
        FeedPublisher publisher,
        IPAddress address,
        int port,
        string? token,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(address);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(address, port));
        builder.Services.AddResponseCompression(o =>
        {
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
            o.MimeTypes = ["application/json"];
        });

        var app = builder.Build();
        app.UseResponseCompression();

        var prefix = token is null ? string.Empty : "/" + Uri.EscapeDataString(token);
        app.MapGet(prefix + "/", () => Results.Redirect(prefix + "/" + S100Feed.FileName));
        app.MapGet(prefix + "/" + S100Feed.FileName, (HttpContext context) => ServeFeedAsync(context, publisher));
        app.MapGet(prefix + "/items/{file}", (HttpContext context, string file) => ServeItemAsync(context, publisher, file, log));

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        return new FeedServer(app, FeedUriFor(address, bound.Port, token));
    }

    /// <summary>The feed URL for a host and port, e.g. <c>http://192.168.1.20:8100/&lt;token&gt;/feed.json</c>.</summary>
    public static Uri FeedUriFor(IPAddress address, int port, string? token)
    {
        var host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        var prefix = token is null ? string.Empty : Uri.EscapeDataString(token) + "/";
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"http://{host}:{port}/{prefix}{S100Feed.FileName}"));
    }

    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled.</summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task ServeFeedAsync(HttpContext context, FeedPublisher publisher)
    {
        var feed = await publisher.GetAsync(cancellationToken: context.RequestAborted).ConfigureAwait(false);
        context.Response.Headers.ETag = feed.ETag;
        context.Response.Headers.CacheControl = "no-cache";

        if (context.Request.Headers.IfNoneMatch.Contains(feed.ETag))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.Body.WriteAsync(feed.Json, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task ServeItemAsync(HttpContext context, FeedPublisher publisher, string file, Action<string>? log)
    {
        const string extension = ".zip";
        var id = file.EndsWith(extension, StringComparison.Ordinal) ? file[..^extension.Length] : null;
        var feed = await publisher.GetAsync(cancellationToken: context.RequestAborted).ConfigureAwait(false);
        if (id is null || !feed.Items.TryGetValue(id, out var entry) || !S100FeedPackager.Exists(entry.Location))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{id}.zip\"";

        // ZipArchive writes its headers synchronously; streaming straight to
        // the response avoids buffering large (e.g. S-102) datasets.
        var body = context.Features.Get<IHttpBodyControlFeature>();
        if (body is not null)
            body.AllowSynchronousIO = true;

        await S100FeedPackager.WriteZipAsync(context.Response.Body, entry.Location, context.RequestAborted).ConfigureAwait(false);
        log?.Invoke($"{entry.Item.Name} ({entry.Item.ProductSpec}) → {context.Connection.RemoteIpAddress}");
    }
}
