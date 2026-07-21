using EncDotNet.S100.Datasets.S104;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// An S-104 tide time-series sampled at a geographic point by
/// <see cref="S104DatasetProcessor.SampleTide"/>, together with the
/// dataset-level metadata needed to rank one S-104 dataset against another
/// (finest grid resolution first, then latest issuance).
/// </summary>
/// <param name="SpacingDegrees">
/// The grid node spacing in decimal degrees (the finer of the two axes),
/// used as the primary tide-selection key.
/// </param>
/// <param name="IssueDate">
/// The dataset's <c>issueDate</c> parsed to a UTC instant, or <c>null</c> when
/// absent or unparseable; used as the secondary tide-selection key.
/// </param>
/// <param name="VerticalDatumCode">
/// The S-104 dataset's declared vertical datum as an S-100 register code
/// (source identifier 996), or <c>null</c> when absent.
/// </param>
/// <param name="Series">The sampled nearest-cell water-level time series.</param>
public sealed record S104TideProbe(
    double SpacingDegrees,
    DateTime? IssueDate,
    int? VerticalDatumCode,
    S104TimeSeries Series);
