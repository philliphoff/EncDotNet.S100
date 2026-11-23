using System.Globalization;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Gives a console a usable width when the detected one is not (issue #1692).
/// </summary>
/// <remarks>
/// Spectre.Console takes its width from <see cref="Console.BufferWidth"/>
/// and only replaces a width of 0 with its default. When standard output is
/// not a terminal, .NET on Unix reads the width from the terminfo entry
/// for <c>TERM</c>; an entry without a <c>cols</c> capability (such as
/// <c>TERM=linux</c>) yields -1, and Spectre.Console then truncates every
/// line to "…". A non-positive width is replaced by <c>COLUMNS</c> when
/// it holds a positive integer, otherwise by <see cref="DefaultWidth"/>.
/// A positive detected width, including a real terminal's, is kept.
/// </remarks>
internal static class CliConsoleWidth
{
    /// <summary>
    /// The width used when neither the console nor <c>COLUMNS</c> gives one:
    /// Spectre.Console's own default, so redirected output wraps the same
    /// whatever <c>TERM</c> is set to.
    /// </summary>
    public const int DefaultWidth = 80;

    /// <summary>The height used when the console reports a non-positive one.</summary>
    public const int DefaultHeight = 24;

    /// <summary>The environment variable consulted for a fallback width.</summary>
    public const string ColumnsVariable = "COLUMNS";

    /// <summary>
    /// Replaces a non-positive width or height on <paramref name="console"/>'s
    /// profile with a usable one.
    /// </summary>
    /// <param name="console">The console to repair.</param>
    /// <param name="columns">
    /// The value of <c>COLUMNS</c>; read from the environment when omitted.
    /// </param>
    /// <returns><paramref name="console"/>, for chaining.</returns>
    public static IAnsiConsole Ensure(IAnsiConsole console, string? columns = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        var profile = console.Profile;
        profile.Width = Resolve(
            profile.Width,
            columns ?? Environment.GetEnvironmentVariable(ColumnsVariable));
        if (profile.Height <= 0)
            profile.Height = DefaultHeight;
        return console;
    }

    /// <summary>
    /// Creates a console that writes plain text to <paramref name="writer"/>
    /// with a usable width.
    /// </summary>
    /// <param name="writer">Where the console writes.</param>
    /// <returns>The new console.</returns>
    public static IAnsiConsole Create(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        return Ensure(AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) }));
    }

    /// <summary>
    /// Picks the width to use from the detected width and <c>COLUMNS</c>.
    /// </summary>
    /// <param name="detectedWidth">The width the console detected.</param>
    /// <param name="columns">The value of <c>COLUMNS</c>, if any.</param>
    /// <returns>
    /// <paramref name="detectedWidth"/> when positive, else a positive
    /// <paramref name="columns"/>, else <see cref="DefaultWidth"/>.
    /// </returns>
    public static int Resolve(int detectedWidth, string? columns)
    {
        if (detectedWidth > 0)
            return detectedWidth;

        return int.TryParse(columns, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : DefaultWidth;
    }
}
