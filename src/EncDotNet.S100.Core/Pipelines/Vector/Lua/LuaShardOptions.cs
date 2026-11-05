using System.Globalization;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Whether, and how, <see cref="LuaRuleExecutor"/> splits one cell's
/// <c>PortrayalMain</c> run (S-100 Part 9a) across several Lua states that run
/// at the same time (map render design, slice 2). Off by default.
/// </summary>
/// <remarks>
/// <para>
/// Each shard is its own Lua state, set up exactly as for a full run, that
/// portrays a subset of the cell's features. Features that read each other's
/// Lua state while they're portrayed (a <c>TextPlacement</c> and the features
/// that place text on it, co-located lights) stay in one shard, and the shards'
/// output is merged back into the order a full run emits it, so the result is
/// the same as a full run. Sharding needs the product's data provider to group
/// those features (<see cref="ILuaShardGrouping"/>); a provider that doesn't
/// always gets a full run.
/// </para>
/// <para>
/// Sharding changes neither the output nor the portrayal cache keys, so a
/// cache warmed with sharding off serves a run with it on.
/// </para>
/// </remarks>
public sealed record LuaShardOptions
{
    /// <summary>
    /// The environment variable that turns sharding on for hosts that read
    /// <see cref="FromEnvironment"/>: the number of shards, for example
    /// <c>4</c>. Unset, empty or <c>1</c> leaves it off.
    /// </summary>
    public const string ShardsEnvironmentVariable = "S100_LUA_SHARDS";

    /// <summary>
    /// The environment variable that sets
    /// <see cref="MinFeaturesPerShard"/> for <see cref="FromEnvironment"/>.
    /// </summary>
    public const string MinFeaturesEnvironmentVariable = "S100_LUA_SHARD_MIN_FEATURES";

    /// <summary>
    /// The environment variable that lists products to keep unsharded for
    /// <see cref="FromEnvironment"/>, comma separated (for example
    /// <c>S-401,S-57</c>).
    /// </summary>
    public const string DisabledProductsEnvironmentVariable = "S100_LUA_SHARDS_DISABLED";

    /// <summary>Sharding off: every cell gets one full run.</summary>
    public static LuaShardOptions Disabled { get; } = new();

    /// <summary>
    /// The most shards one cell is split into. <c>1</c> (the default) turns
    /// sharding off.
    /// </summary>
    public int MaxShards { get; init; } = 1;

    /// <summary>
    /// The fewest features a shard gets. A cell with fewer than twice this
    /// many features gets a full run, and a larger one gets no more shards
    /// than this allows, so a shard's portrayal work stays well above the cost
    /// of setting up its Lua state.
    /// </summary>
    public int MinFeaturesPerShard { get; init; } = 500;

    /// <summary>
    /// The most shards of one cell that run at once. The default matches the
    /// viewer's visible-load concurrency, so one cell's shards can't take
    /// more cores than visible loads already may.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>
    /// Products (by spec, for example <c>S-401</c>) whose cells always get a
    /// full run, so a catalogue whose rules turn out to keep state across
    /// features can be switched off without turning sharding off for the
    /// rest. Compared ignoring case.
    /// </summary>
    public IReadOnlySet<string> DisabledProducts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether these options split cells at all.</summary>
    public bool IsEnabled => MaxShards > 1;

    /// <summary>
    /// These options for one product: <see cref="Disabled"/> when the product
    /// is in <see cref="DisabledProducts"/>, otherwise these options.
    /// </summary>
    /// <param name="product">The product spec, for example <c>S-101</c>.</param>
    public LuaShardOptions ForProduct(string product)
    {
        ArgumentException.ThrowIfNullOrEmpty(product);
        return IsEnabled && !DisabledProducts.Contains(product) ? this : Disabled;
    }

    /// <summary>
    /// Reads the options from <see cref="ShardsEnvironmentVariable"/>,
    /// <see cref="MinFeaturesEnvironmentVariable"/> and
    /// <see cref="DisabledProductsEnvironmentVariable"/>. Returns
    /// <see cref="Disabled"/> when the shard count is unset or not a number
    /// above one.
    /// </summary>
    public static LuaShardOptions FromEnvironment()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(ShardsEnvironmentVariable), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var shards) || shards <= 1)
        {
            return Disabled;
        }

        var options = new LuaShardOptions { MaxShards = shards };
        if (int.TryParse(Environment.GetEnvironmentVariable(MinFeaturesEnvironmentVariable), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var minFeatures) && minFeatures > 0)
        {
            options = options with { MinFeaturesPerShard = minFeatures };
        }

        var disabled = Environment.GetEnvironmentVariable(DisabledProductsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(disabled))
        {
            options = options with
            {
                DisabledProducts = new HashSet<string>(
                    disabled.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase),
            };
        }

        return options;
    }
}
