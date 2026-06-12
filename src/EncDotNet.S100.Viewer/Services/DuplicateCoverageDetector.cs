using System;
using System.IO;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Decides when a coverage dataset loaded from an exchange set duplicates
/// another already-loaded dataset's coverage and should therefore default
/// to hidden rather than stack on top of it.
/// </summary>
/// <remarks>
/// S-111 (and to a lesser extent S-104) exchange sets routinely bundle
/// several <em>products</em> over the same cell — for example the same
/// Rotterdam grid published as separate neap/spring tidal-regime and
/// depth-band variants (<c>S111-neap 0-5</c>, <c>S111-neap 0-10</c>,
/// <c>S111-neap 0-15</c>, …). Each product folder contains a file with the
/// <em>identical</em> dataset name (e.g.
/// <c>111NL00_ROTTERDAM_DCF2_20250322_2300.h5</c>) covering the same area
/// and the same time window. The time gate (which is purely temporal) lets
/// all co-temporal variants draw at once, so their current-vector arrows
/// stack on the same locations and the map looks like several time-steps
/// are rendered simultaneously.
///
/// Identical base file name is a strong, producer-independent signal that
/// two entries describe the <em>same</em> cell (distinct cells never share a
/// dataset name) — even when the variants are shipped as separate exchange
/// sets, each with its own <c>CATALOG.XML</c> and asset source, as the
/// Rotterdam NL set is. We keep the first such entry visible and load the
/// remaining duplicates hidden; the user can re-enable any of them from the
/// Datasets list. S-111 §10 / §12 (dataset naming and packaging).
/// </remarks>
internal static class DuplicateCoverageDetector
{
    /// <summary>
    /// True for the coverage product specs whose exchange sets are known to
    /// bundle multiple co-located, co-temporal product variants under one
    /// cell name. Vector specs and single-coverage specs are left untouched
    /// so their multi-file loads are never silently hidden.
    /// </summary>
    public static bool IsCollapsibleSpec(string? spec) =>
        spec is "S-111" or "S-104";

    /// <summary>
    /// True when two entries describe the same cell coverage: they share an
    /// identical base file name (case-insensitive), regardless of which
    /// product sub-folder or exchange set they came from.
    /// </summary>
    /// <remarks>
    /// The product variants are frequently published as <em>separate</em>
    /// exchange sets (each with its own <c>CATALOG.XML</c> and asset source),
    /// so source identity cannot be relied upon. For the collapsible coverage
    /// specs an identical dataset name is itself a strong same-cell signal:
    /// the S-111 / S-104 dataset name encodes producer, agency, area and
    /// reference time, so two distinct cells never share a name. Callers must
    /// gate this on <see cref="IsCollapsibleSpec"/> and on the two entries
    /// sharing the same product spec.
    /// </remarks>
    /// <param name="relativePathA">Source-relative path of the first entry.</param>
    /// <param name="relativePathB">Source-relative path of the second entry.</param>
    public static bool IsSameCoverage(string? relativePathA, string? relativePathB)
    {
        var fileA = SafeFileName(relativePathA);
        var fileB = SafeFileName(relativePathB);
        return fileA.Length > 0 &&
               string.Equals(fileA, fileB, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFileName(string? relativePath) =>
        string.IsNullOrEmpty(relativePath) ? string.Empty : Path.GetFileName(relativePath);
}
