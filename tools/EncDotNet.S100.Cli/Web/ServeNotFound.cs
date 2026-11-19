using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// The page's static 404 (issue #1584, D34): for every path nothing answers,
/// including one under a wrong or missing token. It names nothing served and
/// has no script, so it gives nothing away and opens no live connection.
/// </summary>
internal static class ServeNotFound
{
    /// <summary>The page, in light and dark.</summary>
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="robots" content="noindex">
        <title>Nothing here</title>
        <style>
        :root { color-scheme: light dark; --background: #FFFFFF; --foreground: #09090B; --muted-foreground: #71717A; }
        @media (prefers-color-scheme: dark) { :root { --background: #09090B; --foreground: #FAFAFA; --muted-foreground: #A1A1AA; } }
        html, body { height: 100%; margin: 0; }
        body { display: grid; place-items: center; padding: 0 24px; background: var(--background); color: var(--foreground);
               font: 15px/1.6 Inter, system-ui, -apple-system, "Segoe UI", sans-serif; }
        main { max-width: 440px; }
        .code { margin: 0 0 14px; color: var(--muted-foreground); font: 13px ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
        h1 { margin: 0 0 12px; font-size: 22px; font-weight: 600; }
        p { margin: 0; color: var(--muted-foreground); }
        </style>
        </head>
        <body>
        <main>
        <p class="code">404</p>
        <h1>Nothing here</h1>
        <p>If you were sent a link to a chart server, check that you have all of it. Links to a server started with a token include it in the path.</p>
        </main>
        </body>
        </html>
        """;

    /// <summary>
    /// Gives a 404 that nothing answered (no endpoint matched, or the token
    /// didn't) the page as its body. A 404 an endpoint gave (a tile, a
    /// palette or layer the API doesn't have) is left as it was.
    /// </summary>
    public static void Use(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            await next(context).ConfigureAwait(false);
            var response = context.Response;
            if (response.StatusCode != StatusCodes.Status404NotFound || response.HasStarted || context.GetEndpoint() is not null)
                return;
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                return;

            response.ContentType = "text/html; charset=utf-8";
            response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsGet(context.Request.Method))
                await response.WriteAsync(Html, context.RequestAborted).ConfigureAwait(false);
        });
    }
}
