using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The area findings (#956): S-101 restricted, military practice, caution,
/// anchorage and traffic areas and S-122 protected areas the route's
/// corridor meets, from <see cref="RouteAreaCheck"/> over the loaded data,
/// judged at the ETAs where the vessel is in each area, or at the view time
/// where the legs have no speed. Runs with or without a draft.
/// </summary>
/// <remarks>
/// It shares the <see cref="RouteCheckSourceCache"/> with
/// <see cref="RouteCheckFindingProducer"/>, so the S-101 cells' indexes are
/// built once. Each finding refers to the area's feature, so pick and
/// describe can open it.
/// </remarks>
internal sealed class AreaFindingProducer : IRouteFindingProducer
{
    private readonly RouteCheckSourceCache _sources;
    private readonly RouteAreaOptions? _options;

    /// <param name="sources">The sources shared with the chart checks, or <c>null</c> for its own.</param>
    /// <param name="options">Tuning, or <c>null</c> for the defaults with the viewer's wording.</param>
    public AreaFindingProducer(RouteCheckSourceCache? sources = null, RouteAreaOptions? options = null)
    {
        _sources = sources ?? new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        _options = options;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Legs.Count == 0)
            return Task.FromResult<IReadOnlyList<RouteFinding>>([]);

        var options = _options ?? RouteAreaOptions.Default with { Formatter = new ViewerFindingFormatter(request) };
        return Task.Run<IReadOnlyList<RouteFinding>>(
            () => new RouteAreaCheck(options)
                .Check(request.Plan, request.Settings, _sources.Get(request.Datasets).Areas!, cancellationToken)
                .Findings,
            cancellationToken);
    }
}
