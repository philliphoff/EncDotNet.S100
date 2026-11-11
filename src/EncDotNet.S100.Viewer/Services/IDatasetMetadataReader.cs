using EncDotNet.S100.Core;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Reads a dataset's cheap "peek" <see cref="DatasetMetadata"/> (declared
/// spec, geographic extent, and — where the encoding models them — display
/// scale and temporal coverage) from a file path <em>without</em> a full
/// parse or portrayal run, so a host can place a dataset on the map and
/// frame a viewport before deciding to load it in full (issue #467 WS3).
/// </summary>
/// <remarks>
/// Implementations are expected to be backed by a cross-session cache so a
/// previously-probed dataset costs nothing on a later session. A dataset
/// whose product is not cheaply probeable — or that fails to parse — yields
/// <see langword="null"/>; callers must degrade gracefully (fall back to a
/// full load) rather than treat a <see langword="null"/> as authoritative.
/// </remarks>
internal interface IDatasetMetadataReader
{
    /// <summary>
    /// Reads the metadata for the dataset at <paramref name="path"/>, or
    /// returns <see langword="null"/> when the path is empty, its product is
    /// not cheaply probeable, or the read fails.
    /// </summary>
    /// <param name="path">Absolute path to the dataset file.</param>
    /// <returns>The dataset's metadata, or <see langword="null"/>.</returns>
    DatasetMetadata? TryRead(string path);
}
