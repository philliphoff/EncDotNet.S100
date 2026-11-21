using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Datasets.S412.DataModel;

namespace EncDotNet.S100.Datasets.Pipelines.Spec;

/// <summary>
/// Describer strategy for S-412 Marine Weather Warnings. Returns the feature's
/// encoded attributes, as the generic GML describer does, with every nested
/// complex attribute (one <c>information</c> per language), plus a
/// <c>warning</c> block read from the typed model: the kind, the threshold and
/// Beaufort force with their feature catalogue labels, the message in each
/// language, the validity times and the data provider.
/// </summary>
internal sealed class S412FeatureDescriber : ISpecFeatureDescriber
{
    public string SpecName => "S-412";

    public ToolResult<DescribeFeatureResult> Describe(FeatureDescriberContext context)
    {
        if (context.Dataset.Data is not S412DatasetData s412)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new SpecNotSupportedForTool(context.Dataset.Spec, DescribeFeatureService.Name));
        }

        var feature = s412.Model.Features.FirstOrDefault(f => string.Equals(f.Id, context.FeatureId, StringComparison.Ordinal));
        if (feature is null)
        {
            return ToolResult<DescribeFeatureResult>.Err(
                new FeatureNotFound(context.Dataset.Id, context.FeatureId));
        }

        var model = S412MarineWeatherWarnings.From(s412.Model, out _);
        var warning = model.Warnings.FirstOrDefault(w => ReferenceEquals(w.Source, feature));

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = feature.Id,
            ["featureType"] = feature.FeatureType,
            ["geometryType"] = feature.GeometryType.ToString(),
            ["attributes"] = feature.Attributes,
            ["complexAttributes"] = feature.ComplexAttributes.Select(Complex).ToArray(),
            ["warning"] = warning is null ? null : Warning(warning),
        };

        var references = feature.References
            .Select(r =>
            {
                var target = r.Href.TrimStart('#');
                var resolved = s412.Model.InformationTypes.Any(i => i.Id == target)
                    || s412.Model.Features.Any(f => f.Id == target);
                return new Query.FeatureReference(r.Role, r.Href, resolved ? context.Dataset.Id : null, target, resolved);
            })
            .ToArray();

        return ToolResult<DescribeFeatureResult>.Ok(
            new DescribeFeatureResult(
                context.Dataset.Spec,
                feature.FeatureType,
                SpecPayloadJson.ToElement(payload),
                references));
    }

    private static Dictionary<string, object?> Complex(S412ComplexAttribute attribute) => new(StringComparer.Ordinal)
    {
        ["code"] = attribute.Code,
        ["subAttributes"] = attribute.SubAttributes,
        ["groups"] = attribute.Groups.Select(Complex).ToArray(),
    };

    private static Dictionary<string, object?> Warning(S412WeatherWarning warning)
    {
        var block = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = warning.Kind.ToString(),
            ["weatherWarningNumber"] = warning.WeatherWarningNumber,
            ["messages"] = Texts(warning.Messages),
            ["validFrom"] = warning.FixedDateRange?.Start,
            ["validTo"] = warning.FixedDateRange?.End,
            ["cancellationDate"] = warning.CancellationDate,
            ["inForceUntil"] = warning.InForceUntil,
            ["issuedAt"] = warning.IssuedAt,
            ["nextUpdateDateTime"] = warning.NextUpdateDateTime,
            ["dataProvider"] = warning.DataProvider is { } provider
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = provider.Id,
                    ["metarea"] = provider.MetareaCode is { } metarea ? S412Labels.MetareaName(metarea) : null,
                    ["countryName"] = provider.CountryName,
                    ["issuingServiceNames"] = Texts(provider.IssuingServiceNames),
                    ["agencyName"] = provider.AgencyName,
                }
                : null,
        };

        switch (warning)
        {
            case S412WindWarning wind:
                block["threshold"] = wind.Threshold is { } windThreshold ? Coded((int)windThreshold, windThreshold.Label()) : null;
                block["beaufortForce"] = wind.BeaufortForce is { } force ? Coded(force, S412Labels.BeaufortForceLabel(force)) : null;
                block["maximumWindSpeed"] = Texts(wind.MaximumWindSpeed);
                break;
            case S412SignificantWaveHeightWarning wave:
                block["threshold"] = wave.Threshold is { } waveThreshold ? Coded((int)waveThreshold, waveThreshold.Label()) : null;
                break;
            case S412IceAccretionWarning ice:
                block["iceAccretionRate"] = ice.Rate is { } rate ? Coded((int)rate, rate.Label()) : null;
                block["sourceOfIcing"] = ice.SourceOfIcing is { } source ? Coded((int)source, source.Label()) : null;
                break;
        }

        return block;
    }

    private static Dictionary<string, object?> Coded(int code, string? label) => new(StringComparer.Ordinal)
    {
        ["code"] = code,
        ["label"] = label,
    };

    private static object[] Texts(IReadOnlyList<S412Text> texts) =>
        [.. texts.Select(t => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["language"] = t.Language,
            ["text"] = t.Text,
            ["fileReference"] = t.FileReference,
        })];
}
