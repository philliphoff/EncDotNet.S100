using System.Globalization;
using System.Text;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// A Lua state set up for one dataset and one set of mariner settings that
/// portrays chosen features on demand, by passing their ids to the
/// catalogue's <c>PortrayalMain(featureIDs)</c> (S-100 Part 9a). Opened by
/// <see cref="LuaRuleExecutor.OpenSessionAsync"/>.
/// </summary>
/// <remarks>
/// Not thread-safe: portray from one thread at a time. A feature portrayed
/// twice is portrayed again, so callers keep what they've already portrayed.
/// </remarks>
public sealed class LuaPortrayalSession : IDisposable
{
    private readonly ILuaContext _lua;
    private readonly ILuaDataProvider _provider;
    private readonly string _productTag;
    private readonly IFeatureAnchorProvider? _anchorProvider;
    private readonly ISpatialReferenceResolver? _spatialReferences;
    private int _emitted;

    internal LuaPortrayalSession(
        ILuaContext lua,
        ILuaDataProvider provider,
        string productTag,
        IFeatureAnchorProvider? anchorProvider,
        ISpatialReferenceResolver? spatialReferences)
    {
        _lua = lua;
        _provider = provider;
        _productTag = productTag;
        _anchorProvider = anchorProvider;
        _spatialReferences = spatialReferences;
    }

    /// <summary>Portrays features and returns their drawing instructions.</summary>
    /// <param name="featureIds">
    /// The features' ids, as the dataset's drawing instructions reference
    /// them (<see cref="DrawingInstruction.FeatureReference"/>). Numeric ids
    /// are passed to Lua as numbers, as the S-101 host gives them.
    /// </param>
    /// <returns>Their drawing instructions, in emission order.</returns>
    public IReadOnlyList<DrawingInstruction> Portray(IReadOnlyCollection<string> featureIds)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        if (featureIds.Count == 0)
        {
            return [];
        }

        var script = new StringBuilder("PortrayalMain({");
        foreach (var id in featureIds)
        {
            if (double.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                script.Append(number.ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                script.Append('\'').Append(LuaSource.EscapeLiteral(id)).Append('\'');
            }

            script.Append(',');
        }

        script.Append("})");
        try
        {
            _lua.Execute(script.ToString());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{_productTag} Lua portrayal failed: {_lua.DescribeError(ex)}", ex);
        }

        var emitted = LuaRuleExecutor.Deduplicate(_provider.EmittedInstructions, _emitted);
        _emitted = _provider.EmittedInstructions.Count;
        var parsed = new List<DrawingInstruction>();
        foreach (var e in emitted)
        {
            parsed.AddRange(DrawingInstructionParser.Parse(
                e.FeatureRef, e.InstructionString, _anchorProvider?.GetAnchor(e.FeatureRef), _spatialReferences));
        }

        return parsed;
    }

    /// <inheritdoc />
    public void Dispose() => _lua.Dispose();
}
