using System.Text.RegularExpressions;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Removes calls that do nothing from the S-100 Part 9a scripting framework
/// and rule sources before they are loaded, without changing the files on disk.
/// </summary>
/// <remarks>
/// <para>
/// <c>S100Scripting.lua</c> defines the type checks (<c>CheckType</c>,
/// <c>CheckSelf</c> and the rest) as empty vararg functions unless
/// <c>TypeSystemChecks(true)</c> turns them on. The framework and the rules call
/// them hundreds of thousands of times per cell. They also call
/// <c>Debug.StartPerformance</c> and <c>Debug.StopPerformance</c>, which the
/// framework routes to <c>HostDebuggerEntry</c>, and every host ignores those
/// actions. Under MoonSharp each call allocates a call frame, an argument array
/// and a vararg table, so these calls were about 18% of the bytes a cell
/// allocates (#1275).
/// </para>
/// <para>
/// Only a call that is a whole line is removed, and only when every argument is
/// a plain name or a literal. Evaluating those has no side effects, so removing
/// the call cannot skip an attribute lookup or an error that an argument
/// expression would raise. The line itself is kept, so Lua error messages
/// still name the same line numbers.
/// </para>
/// </remarks>
internal static partial class LuaNoOpCallStripper
{
    // An argument with no side effects: a name, a string literal or a number.
    private const string Argument =
        @"(?:[A-Za-z_][A-Za-z0-9_]*|'[^'\\\r\n]*'|""[^""\\\r\n]*""|-?[0-9]+(?:\.[0-9]+)?)";

    [GeneratedRegex(
        @"^([ \t]*)(?:CheckType|CheckTypeOrNil|CheckSelf|CheckNotSelf|CheckTypeOneOf|CheckTypeOneOfOrNil" +
        @"|Debug\.StartPerformance|Debug\.StopPerformance|Debug\.ResetPerformance)" +
        @"\((?:[ \t]*" + Argument + @"(?:[ \t]*,[ \t]*" + Argument + @")*)?[ \t]*\)[ \t]*;?[ \t]*(?:--[^\r\n]*)?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NoOpCall();

    /// <summary>
    /// Whether the calls can be removed from <paramref name="sources"/>: no
    /// source turns the type checks on, so the checks stay empty.
    /// </summary>
    public static bool CanStrip(IEnumerable<string> sources) =>
        !sources.Any(s => s.Contains("TypeSystemChecks(true)", StringComparison.Ordinal));

    /// <summary>Returns <paramref name="source"/> with the no-op calls removed.</summary>
    public static string Strip(string source) => NoOpCall().Replace(source, "$1");
}
