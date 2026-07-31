using System.Globalization;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// An <see cref="IDatasetKeyProvider"/> backed by a parsed <see cref="PermitFile"/>
/// and a Data Client hardware id. It locates the permit for a requested dataset
/// and unwraps its cell key with the hardware id.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7. The hardware id is the Data Client system
/// identifier that data permits are bound to; the same key decrypts a base
/// dataset and all of its incremental updates (§15-6.2).
/// </remarks>
public sealed class PermitKeyProvider : IDatasetKeyProvider
{
    private readonly PermitFile _permitFile;
    private readonly HardwareId _hardwareId;
    private readonly IReadOnlyDictionary<string, DatasetDiscoveryMetadata> _datasets;
    private readonly string? _productId;

    /// <summary>
    /// Creates a key provider over a permit file and hardware id.
    /// </summary>
    /// <param name="permitFile">The parsed permit file.</param>
    /// <param name="hardwareId">The Data Client system hardware id.</param>
    /// <param name="catalogue">
    /// The exchange catalogue whose edition and issue metadata constrain the
    /// permits.
    /// </param>
    /// <param name="productId">
    /// An optional product specification id (e.g. <c>S-101</c>) to restrict permit
    /// lookups to a single product section.
    /// </param>
    public PermitKeyProvider(
        PermitFile permitFile,
        HardwareId hardwareId,
        ExchangeCatalogue catalogue,
        string? productId = null)
    {
        _permitFile = permitFile ?? throw new ArgumentNullException(nameof(permitFile));
        if (!_permitFile.IsAuthenticated)
        {
            throw new ArgumentException(
                "The permit must be authenticated with PERMIT.SIGN before its keys can be used.",
                nameof(permitFile));
        }

        _hardwareId = hardwareId ?? throw new ArgumentNullException(nameof(hardwareId));
        ArgumentNullException.ThrowIfNull(catalogue);
        _datasets = catalogue.DatasetDiscoveryMetadata
            .ToDictionary(
                dataset => GetFileName(dataset.RelativePath),
                StringComparer.OrdinalIgnoreCase);
        _productId = productId;
    }

    /// <summary>
    /// Evaluates the permit that applies to a catalogue dataset.
    /// </summary>
    /// <param name="datasetFileName">The dataset file name, with or without a path.</param>
    /// <returns>The structured permit-policy outcome.</returns>
    public PermitEvaluationResult Evaluate(string datasetFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetFileName);

        var fileName = GetFileName(datasetFileName);
        if (!_datasets.TryGetValue(fileName, out var dataset) || !dataset.DataProtection)
        {
            var unprotected = dataset ?? new DatasetDiscoveryMetadata { FileName = fileName };
            return new PermitEvaluationResult(PermitEvaluationOutcome.NotProtected, unprotected, null);
        }

        if (!_permitFile.TryGetPermit(fileName, out var permit, _productId) || permit is null)
        {
            return new PermitEvaluationResult(
                PermitEvaluationOutcome.PermitNotFound,
                dataset,
                null,
                $"No dataset permit was found for protected dataset '{fileName}'.");
        }

        var identityDataset = ResolveIdentityDataset(permit, dataset);
        if (identityDataset is null)
        {
            return Reject(
                PermitEvaluationOutcome.BaseDatasetMissing,
                dataset,
                permit,
                $"Protected update '{fileName}' cannot be evaluated because its base dataset is absent.");
        }

        if (permit.EditionNumber is int permittedEdition)
        {
            if (identityDataset.EditionNumber is not int datasetEdition)
            {
                return Reject(
                    PermitEvaluationOutcome.EditionNumberMissing,
                    dataset,
                    permit,
                    $"Protected dataset '{fileName}' omits the edition number required by its permit.");
            }

            if (datasetEdition != permittedEdition)
            {
                return Reject(
                    PermitEvaluationOutcome.EditionMismatch,
                    dataset,
                    permit,
                    $"Protected dataset '{fileName}' is edition {datasetEdition}, but its permit is for edition {permittedEdition}.");
            }
        }

        var identityIssueDate = ParseDate(identityDataset.IssueDate);
        if (permit.EditionNumber is null && permit.IssueDate is DateOnly permittedIssueDate)
        {
            if (identityIssueDate is null)
            {
                return Reject(
                    PermitEvaluationOutcome.IssueDateMissing,
                    dataset,
                    permit,
                    $"Protected dataset '{fileName}' omits the issue date required by its permit.");
            }

            if (identityIssueDate.Value != permittedIssueDate)
            {
                return Reject(
                    PermitEvaluationOutcome.IssueDateMismatch,
                    dataset,
                    permit,
                    $"The base dataset for '{fileName}' was issued on {identityIssueDate:yyyy-MM-dd}, but its permit is for {permittedIssueDate:yyyy-MM-dd}.");
            }
        }

        var issueDate = ParseDate(dataset.IssueDate);
        if (issueDate is null)
        {
            return Reject(
                PermitEvaluationOutcome.IssueDateMissing,
                dataset,
                permit,
                $"Protected dataset '{fileName}' omits the issue date needed to enforce permit expiry.");
        }

        if (issueDate.Value > permit.Expiry)
        {
            return Reject(
                PermitEvaluationOutcome.IssuedAfterExpiry,
                dataset,
                permit,
                $"Protected dataset '{fileName}' was issued on {issueDate:yyyy-MM-dd}, after its permit expired on {permit.Expiry:yyyy-MM-dd}.");
        }

        return new PermitEvaluationResult(PermitEvaluationOutcome.Allowed, dataset, permit);
    }

    /// <inheritdoc />
    public bool TryGetCellKey(string datasetFileName, out byte[]? cellKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetFileName);

        var evaluation = Evaluate(datasetFileName);
        if (evaluation.Outcome == PermitEvaluationOutcome.NotProtected)
        {
            cellKey = null;
            return false;
        }

        if (!evaluation.IsAllowed || evaluation.Permit is null)
        {
            throw new DatasetPermitException(evaluation);
        }

        cellKey = evaluation.Permit.DecryptCellKey(_hardwareId);
        return true;
    }

    private static PermitEvaluationResult Reject(
        PermitEvaluationOutcome outcome,
        DatasetDiscoveryMetadata dataset,
        DataPermit permit,
        string detail) =>
        new(outcome, dataset, permit, detail);

    private DatasetDiscoveryMetadata? ResolveIdentityDataset(
        DataPermit permit,
        DatasetDiscoveryMetadata requestedDataset)
    {
        if (requestedDataset.UpdateNumber is null or 0)
        {
            return requestedDataset;
        }

        return _datasets.Values.FirstOrDefault(candidate =>
            candidate.UpdateNumber is null or 0 &&
            permit.AppliesTo(GetFileName(candidate.RelativePath)));
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('Z', 'z');
        return DateOnly.TryParseExact(
            trimmed,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOfAny(['/', '\\']);
        return separator >= 0 ? path[(separator + 1)..] : path;
    }
}
