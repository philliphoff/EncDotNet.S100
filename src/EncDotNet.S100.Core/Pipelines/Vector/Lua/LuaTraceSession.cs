using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// A Lua portrayal session with a line hook installed, which portrays chosen
/// features one at a time and records which lines of the catalogue's rule
/// files ran for each. Opened by
/// <see cref="LuaRuleExecutor.OpenTraceSessionAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// A line is recorded each time Lua starts running it, as Lua's line hook
/// reports it: on entering a new line, and again when a loop jumps back to
/// one. Lines are named by rule file and line number, the way the chart
/// reference's rule reader names the facts it records
/// (<c>RuleUse.RuleFile</c> and <c>RuleUse.Line</c>), so the two can be
/// matched. Lines outside the catalogue's rule files, such as the host's own
/// scripts, aren't recorded.
/// </para>
/// <para>
/// Not thread-safe: trace from one thread at a time. A feature traced twice
/// is portrayed again.
/// </para>
/// </remarks>
public sealed class LuaTraceSession : IDisposable
{
    private readonly LuaPortrayalSession _session;
    private readonly ILuaContext _lua;
    private readonly Dictionary<string, string> _ruleFiles;
    private List<LuaTraceLine>? _lines;

    internal LuaTraceSession(LuaPortrayalSession session, ILuaContext lua, IEnumerable<string> ruleFileNames)
    {
        _session = session;
        _lua = lua;

        // The hook names a module after require's argument, which the rules
        // may spell in another case than the catalogue's file name.
        _ruleFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ruleFileNames)
        {
            _ruleFiles[Path.GetFileName(name)] = Path.GetFileNameWithoutExtension(name);
        }

        _lua.TrySetLineHook(OnLine);
    }

    /// <summary>
    /// Portrays features one at a time and returns, for each, its drawing
    /// instructions and the rule lines that ran.
    /// </summary>
    /// <param name="featureIds">
    /// The features' ids, as for <see cref="LuaPortrayalSession.Portray"/>.
    /// </param>
    /// <returns>One trace per feature, in the order given.</returns>
    public IReadOnlyList<LuaFeatureTrace> Trace(IReadOnlyCollection<string> featureIds)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        var traces = new List<LuaFeatureTrace>(featureIds.Count);
        foreach (var id in featureIds)
        {
            var lines = new List<LuaTraceLine>();
            _lines = lines;
            IReadOnlyList<DrawingInstruction> instructions;
            try
            {
                instructions = _session.Portray([id]);
            }
            finally
            {
                _lines = null;
            }

            traces.Add(new LuaFeatureTrace(id, instructions, lines));
        }

        return traces;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lua.TrySetLineHook(null);
        _session.Dispose();
    }

    private void OnLine(string chunk, int line)
    {
        if (_lines is { } lines && _ruleFiles.TryGetValue(chunk, out var file))
        {
            lines.Add(new LuaTraceLine(file, line));
        }
    }
}

/// <summary>A feature's drawing instructions and the rule lines that ran to make them.</summary>
/// <param name="FeatureId">The feature's id, as passed to <see cref="LuaTraceSession.Trace"/>.</param>
/// <param name="Instructions">The feature's drawing instructions, in emission order.</param>
/// <param name="Lines">The rule lines that ran, in the order they ran.</param>
public sealed record LuaFeatureTrace(
    string FeatureId,
    IReadOnlyList<DrawingInstruction> Instructions,
    IReadOnlyList<LuaTraceLine> Lines);

/// <summary>A line of a portrayal rule file that ran.</summary>
/// <param name="File">
/// The rule file's name without its extension, as the portrayal catalogue
/// lists it, for example <c>LateralBeacon</c> for <c>LateralBeacon.lua</c>.
/// </param>
/// <param name="Line">The line number, from 1, in the catalogue's file.</param>
public readonly record struct LuaTraceLine(string File, int Line);
