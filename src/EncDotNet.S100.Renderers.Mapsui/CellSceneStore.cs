using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Core.Metadata;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using NetTopologySuite.IO;
using ZstdSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One tiled sub-layer of a stored cell scene: its scene and everything the
/// chart stack needs to place it, keyed by the tile disk namespace it was
/// drawn under.
/// </summary>
public sealed class StoredSceneLayer
{
    /// <summary>The sub-layer's stable key within its dataset.</summary>
    public required string LayerKey { get; init; }

    /// <summary>The sub-layer's layer name.</summary>
    public required string LayerName { get; init; }

    /// <summary>The product the layer was portrayed for (telemetry tag), or null.</summary>
    public string? Product { get; init; }

    /// <summary>The portrayal opacity of the layer.</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>The S-98 display plane of the sub-layer.</summary>
    public S98DisplayPlane Plane { get; init; }

    /// <summary>The sub-layer's priority within its plane.</summary>
    public int WithinPlanePriority { get; init; }

    /// <summary>The source dataset id of the stack item.</summary>
    public required string SourceDatasetId { get; init; }

    /// <summary>The source feature type of the sub-layer, or null.</summary>
    public string? SourceFeatureType { get; init; }

    /// <summary>The cell's ranking scale denominator on the stack item, or null.</summary>
    public int? SourceScaleDenominator { get; init; }

    /// <summary>The first half of the tile disk namespace.</summary>
    public required string ProductLayerSet { get; init; }

    /// <summary>The second half of the tile disk namespace.</summary>
    public required string StyleStateHash { get; init; }

    /// <summary>The scene.</summary>
    public required VectorScene Scene { get; init; }
}

/// <summary>
/// A vector cell's rendering as an earlier session left it: its sub-layer
/// scenes, extent, information and metadata, enough to draw the cell and
/// join the chart stack before it is parsed (map rendering slice 11).
/// </summary>
public sealed class StoredCellScene
{
    /// <summary>The dataset's metadata (extent, scales, identity).</summary>
    public required DatasetMetadata Metadata { get; init; }

    /// <summary>The dataset's information text.</summary>
    public required string Info { get; init; }

    /// <summary>The dataset's specification.</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>The rendered extent, in EPSG:3857 metres.</summary>
    public required MRect Extent { get; init; }

    /// <summary>The sub-layer keys of the rendering, or null.</summary>
    public IReadOnlyList<string>? LayerNames { get; init; }

    /// <summary>The cell's minimum display scale denominator, or null.</summary>
    public int? CellMinimumDisplayScale { get; init; }

    /// <summary>The cell's compilation scale denominator, or null.</summary>
    public int? CellCompilationScale { get; init; }

    /// <summary>The cell's maximum display scale denominator, or null.</summary>
    public int? CellMaximumDisplayScale { get; init; }

    /// <summary>The cell's data coverage in EPSG:3857, or null.</summary>
    public NetTopologySuite.Geometries.Geometry? CoverageGeometry { get; init; }

    /// <summary>The tiled sub-layers, in the rendering's order.</summary>
    public required IReadOnlyList<StoredSceneLayer> Layers { get; init; }

    /// <summary>
    /// Captures a render result for storing, or returns null when it can't be
    /// stored: a coverage product, a single-surface rendering, or a sub-layer
    /// without a tile disk namespace (for example a depth-adjusted portrayal).
    /// </summary>
    /// <param name="result">The render result.</param>
    /// <param name="metadata">The dataset's metadata.</param>
    /// <returns>The stored form, or null.</returns>
    public static StoredCellScene? TryCreate(MapsuiDatasetResult result, DatasetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(metadata);
        if (result.SceneLayers is not { Count: > 0 } layers || layers.Count != result.Layers.Count)
        {
            return null;
        }

        return new StoredCellScene
        {
            Metadata = metadata,
            Info = result.Info,
            Spec = result.Spec,
            Extent = result.Extent,
            LayerNames = result.LayerNames,
            CellMinimumDisplayScale = result.CellMinimumDisplayScale,
            CellCompilationScale = result.CellCompilationScale,
            CellMaximumDisplayScale = result.CellMaximumDisplayScale,
            CoverageGeometry = result.CoverageGeometry,
            Layers = layers,
        };
    }
}

/// <summary>
/// Cell scenes on disk (map rendering slice 11): a <see cref="ContentKind"/> of
/// the host's <see cref="ContentStore"/> holding one Zstandard-compressed
/// <see cref="StoredCellScene"/> per key.
/// </summary>
/// <remarks>
/// The host picks the key: it must identify every input of the rendering it
/// can know before the cell is parsed (the cell's bytes, the catalogues, the
/// presentation, the code). Each stored sub-layer carries the tile disk
/// namespace it was drawn under, so a cell drawn from its stored scene has the
/// same chart-tile <c>StackSlice</c> as when it was drawn from a parse, and
/// finds the tiles that session left on disk. Any read failure is a miss.
/// </remarks>
public sealed class CellSceneStore
{
    /// <summary>The kind name hosts open for this store (also its folder name).</summary>
    public const string KindName = "scenes";

    /// <summary>The envelope format version. Bump when the layout below changes.</summary>
    public const int FormatVersion = 2;

    /// <summary>The options hosts open this store's kind with.</summary>
    public static ContentKindOptions KindOptions { get; } = new()
    {
        Extension = ".scene",
        FormatVersion = FormatVersion * 1000 + VectorSceneSerializer.FormatVersion,
    };

    private const int CompressionLevel = 3;

    private readonly ContentKind _kind;
    private long _hits;
    private long _misses;
    private long _writes;

    /// <summary>Creates a store over <paramref name="kind"/>, opened as <see cref="KindName"/> with <see cref="KindOptions"/>.</summary>
    /// <param name="kind">The kind.</param>
    public CellSceneStore(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        _kind = kind;
    }

    /// <summary>Reads that found a stored scene.</summary>
    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>Reads that found none, or an unreadable one.</summary>
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>Scenes written (queued or inline).</summary>
    public long Writes => Interlocked.Read(ref _writes);

    /// <summary>Hashes arbitrary key text into a store key.</summary>
    /// <param name="identity">The text that identifies the rendering's inputs.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(string identity) => ContentKind.HashKey(identity);

    /// <summary>Reads the scene stored as <paramref name="key"/>, or null.</summary>
    /// <param name="key">The key, from <see cref="KeyFor"/>.</param>
    /// <param name="cancellationToken">Cancels the read; a cancelled read is a miss.</param>
    /// <returns>The scene, or null on a miss or an unreadable entry.</returns>
    public async ValueTask<StoredCellScene?> TryReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = await _kind.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry.State == ContentState.Present && entry.Bytes is { } bytes && TryDecode(bytes) is { } scene)
        {
            Interlocked.Increment(ref _hits);
            return scene;
        }

        Interlocked.Increment(ref _misses);
        return null;
    }

    /// <summary>
    /// Stores <paramref name="scene"/> as <paramref name="key"/>: encoded on the
    /// caller's thread, written by a background IO job (inline when the
    /// kind's write queue is full).
    /// </summary>
    /// <param name="key">The key, from <see cref="KeyFor"/>.</param>
    /// <param name="scene">The scene.</param>
    public void Write(string key, StoredCellScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var bytes = Encode(scene);
        if (_kind.QueueWrite(key, bytes) == ContentWriteResult.Full)
        {
            _kind.Write(key, bytes);
        }

        Interlocked.Increment(ref _writes);
    }

    /// <summary>Encodes a stored scene (exposed for tests and measurements).</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Encode(StoredCellScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            var metadata = DatasetMetadataSerializer.Serialize(scene.Metadata);
            w.Write7BitEncodedInt(metadata.Length);
            w.Write(metadata);
            w.Write(scene.Info);
            w.Write(scene.Spec.Name);
            w.Write(scene.Spec.Edition.ToString());
            w.Write(scene.Extent.MinX);
            w.Write(scene.Extent.MinY);
            w.Write(scene.Extent.MaxX);
            w.Write(scene.Extent.MaxY);
            w.Write(scene.LayerNames is not null);
            if (scene.LayerNames is { } names)
            {
                w.Write7BitEncodedInt(names.Count);
                foreach (var name in names)
                {
                    w.Write(name);
                }
            }

            WriteNullable(w, scene.CellMinimumDisplayScale);
            WriteNullable(w, scene.CellCompilationScale);
            WriteNullable(w, scene.CellMaximumDisplayScale);
            var coverage = scene.CoverageGeometry is { } geometry ? new WKBWriter().Write(geometry) : [];
            w.Write7BitEncodedInt(coverage.Length);
            w.Write(coverage);

            w.Write7BitEncodedInt(scene.Layers.Count);
            foreach (var layer in scene.Layers)
            {
                w.Write(layer.LayerKey);
                w.Write(layer.LayerName);
                WriteNullable(w, layer.Product);
                w.Write(layer.Opacity);
                w.Write7BitEncodedInt((int)layer.Plane);
                w.Write(layer.WithinPlanePriority);
                w.Write(layer.SourceDatasetId);
                WriteNullable(w, layer.SourceFeatureType);
                WriteNullable(w, layer.SourceScaleDenominator);
                w.Write(layer.ProductLayerSet);
                w.Write(layer.StyleStateHash);
                w.Flush();
                VectorSceneSerializer.Write(raw, layer.Scene);
            }
        }

        using var compressor = new Compressor(CompressionLevel);
        return compressor.Wrap(raw.GetBuffer().AsSpan(0, (int)raw.Length)).ToArray();
    }

    /// <summary>Decodes bytes from <see cref="Encode"/>, or returns null when they're unreadable.</summary>
    /// <param name="bytes">The compressed bytes.</param>
    /// <returns>The scene, or null.</returns>
    public static StoredCellScene? TryDecode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var decompressor = new Decompressor();
            using var raw = new MemoryStream(decompressor.Unwrap(bytes).ToArray(), writable: false);
            using var r = new BinaryReader(raw, Encoding.UTF8, leaveOpen: true);
            var metadata = DatasetMetadataSerializer.TryDeserialize(r.ReadBytes(r.Read7BitEncodedInt()));
            if (metadata is null)
            {
                return null;
            }

            var info = r.ReadString();
            var spec = new SpecRef(r.ReadString(), SpecVersion.Parse(r.ReadString()));
            var extent = new MRect(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            string[]? names = null;
            if (r.ReadBoolean())
            {
                names = new string[r.Read7BitEncodedInt()];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = r.ReadString();
                }
            }

            var minimum = ReadNullableInt(r);
            var compilation = ReadNullableInt(r);
            var maximum = ReadNullableInt(r);
            var coverageBytes = r.ReadBytes(r.Read7BitEncodedInt());
            var coverage = coverageBytes.Length == 0 ? null : new WKBReader().Read(coverageBytes);

            var layers = new StoredSceneLayer[r.Read7BitEncodedInt()];
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i] = new StoredSceneLayer
                {
                    LayerKey = r.ReadString(),
                    LayerName = r.ReadString(),
                    Product = ReadNullableString(r),
                    Opacity = r.ReadDouble(),
                    Plane = (S98DisplayPlane)r.Read7BitEncodedInt(),
                    WithinPlanePriority = r.ReadInt32(),
                    SourceDatasetId = r.ReadString(),
                    SourceFeatureType = ReadNullableString(r),
                    SourceScaleDenominator = ReadNullableInt(r),
                    ProductLayerSet = r.ReadString(),
                    StyleStateHash = r.ReadString(),
                    Scene = VectorSceneSerializer.Read(raw),
                };
            }

            return new StoredCellScene
            {
                Metadata = metadata,
                Info = info,
                Spec = spec,
                Extent = extent,
                LayerNames = names,
                CellMinimumDisplayScale = minimum,
                CellCompilationScale = compilation,
                CellMaximumDisplayScale = maximum,
                CoverageGeometry = coverage,
                Layers = layers,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static void WriteNullable(BinaryWriter w, string? value)
    {
        w.Write(value is not null);
        if (value is not null)
        {
            w.Write(value);
        }
    }

    private static void WriteNullable(BinaryWriter w, int? value)
    {
        w.Write(value.HasValue);
        if (value is { } v)
        {
            w.Write(v);
        }
    }

    private static string? ReadNullableString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    private static int? ReadNullableInt(BinaryReader r) => r.ReadBoolean() ? r.ReadInt32() : null;
}
