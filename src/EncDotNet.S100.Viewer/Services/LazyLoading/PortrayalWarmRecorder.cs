using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Rendering.Engine;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Learns which cache entries a cell's background portrayal used (#1266): it
/// wraps the disk portrayal cache and the disk pattern-clip cache, passes every
/// call through, and notes the keys asked for inside a
/// <see cref="BeginRecording"/> scope. The warmer stores those keys in its
/// ledger and later asks <see cref="IsCached"/> whether they are all still
/// there, without knowing how processors and the renderer build their keys.
/// </summary>
/// <remarks>
/// The scope flows with the async call that opened it (an
/// <see cref="AsyncLocal{T}"/>), so concurrent portrayals each see only their
/// own keys. Outside a scope the wrappers behave exactly like the caches they
/// wrap. Recorded keys carry a prefix naming their cache.
/// </remarks>
internal sealed class PortrayalWarmRecorder
{
    private const string InstructionPrefix = "dlist:";
    private const string ClipPrefix = "clip:";

    private readonly DiskPortrayalInstructionCache _instructions;
    private readonly DiskPatternClipCache _clips;
    private readonly AsyncLocal<List<string>?> _current = new();

    /// <summary>Wraps the two disk caches.</summary>
    /// <param name="instructions">The portrayal (display-list) cache.</param>
    /// <param name="clips">The pattern-clip cache.</param>
    public PortrayalWarmRecorder(DiskPortrayalInstructionCache instructions, DiskPatternClipCache clips)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(clips);
        _instructions = instructions;
        _clips = clips;
        InstructionCache = new InstructionCacheWrapper(this);
        PatternClipCache = new ClipCacheWrapper(this);
    }

    /// <summary>The portrayal cache to give the warmer's pipeline factory.</summary>
    public IPortrayalInstructionCache InstructionCache { get; }

    /// <summary>The pattern-clip cache to give the warmer's renderer.</summary>
    public IPatternClipCache PatternClipCache { get; }

    /// <summary>
    /// Starts noting the keys asked for by this async flow. Call it inside the
    /// task that runs the portrayal, so the scope does not leak to the caller.
    /// </summary>
    /// <returns>The scope; its <see cref="Recording.Keys"/> lists the keys in the order first asked for.</returns>
    public Recording BeginRecording()
    {
        var keys = new List<string>();
        _current.Value = keys;
        return new Recording(this, keys);
    }

    /// <summary>Whether a recorded key still has an entry in its cache.</summary>
    /// <param name="recordedKey">A key from <see cref="Recording.Keys"/>.</param>
    /// <returns><see langword="true"/> when the entry's file exists.</returns>
    public bool IsCached(string recordedKey)
    {
        ArgumentNullException.ThrowIfNull(recordedKey);
        if (recordedKey.StartsWith(InstructionPrefix, StringComparison.Ordinal))
            return _instructions.Contains(recordedKey[InstructionPrefix.Length..]);
        if (recordedKey.StartsWith(ClipPrefix, StringComparison.Ordinal))
            return _clips.Contains(recordedKey[ClipPrefix.Length..]);
        return false;
    }

    private void Note(string prefix, string key)
    {
        if (_current.Value is not { } keys)
            return;
        var recorded = prefix + key;
        if (!keys.Contains(recorded, StringComparer.Ordinal))
            keys.Add(recorded);
    }

    /// <summary>A recording scope opened by <see cref="BeginRecording"/>.</summary>
    internal sealed class Recording : IDisposable
    {
        private readonly PortrayalWarmRecorder _owner;
        private readonly List<string> _keys;

        internal Recording(PortrayalWarmRecorder owner, List<string> keys)
        {
            _owner = owner;
            _keys = keys;
        }

        /// <summary>The keys asked for so far, in the order first asked for.</summary>
        public IReadOnlyList<string> Keys => _keys.ToArray();

        /// <inheritdoc />
        public void Dispose()
        {
            if (ReferenceEquals(_owner._current.Value, _keys))
                _owner._current.Value = null;
        }
    }

    private sealed class InstructionCacheWrapper(PortrayalWarmRecorder owner) : IPortrayalInstructionCache
    {
        public long Hits => owner._instructions.Hits;

        public long Misses => owner._instructions.Misses;

        public IReadOnlyList<DrawingInstruction> GetOrCompute(string key, Func<IReadOnlyList<DrawingInstruction>> factory)
        {
            owner.Note(InstructionPrefix, key);
            return owner._instructions.GetOrCompute(key, factory);
        }

        public ValueTask<IReadOnlyList<DrawingInstruction>> GetOrComputeAsync(
            string key,
            Func<CancellationToken, ValueTask<IReadOnlyList<DrawingInstruction>>> factory,
            CancellationToken cancellationToken = default)
        {
            owner.Note(InstructionPrefix, key);
            return owner._instructions.GetOrComputeAsync(key, factory, cancellationToken);
        }
    }

    private sealed class ClipCacheWrapper(PortrayalWarmRecorder owner) : IPatternClipCache
    {
        public long Hits => owner._clips.Hits;

        public long Misses => owner._clips.Misses;

        public IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> GetOrCompute(
            string key,
            Func<IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>> factory)
        {
            owner.Note(ClipPrefix, key);
            return owner._clips.GetOrCompute(key, factory);
        }
    }
}
