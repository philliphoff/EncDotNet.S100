using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// Resolves the textual cross-product references carried by an
/// <see cref="S129UnderKeelClearancePlan"/> (S-129 Edition 2.0.0) against
/// candidate strongly-typed datasets supplied by the caller.
/// </summary>
/// <remarks>
/// <para>
/// In S-129 Edition 2.0.0 the links to the source S-421 route, S-102
/// bathymetry, and S-104 water-level products are carried as textual
/// producer identifiers — not GML <c>xlink:href</c> URLs — and are
/// preserved verbatim on the typed plan as
/// <see cref="S129ExternalReference"/> values. This resolver turns
/// those textual handles into typed
/// <see cref="S129ResolvedReference{T}"/> values when matching datasets
/// are supplied, or into <see cref="S129UnresolvedReference"/> entries
/// when they are not.
/// </para>
/// <para>
/// Resolution is best-effort and never throws. Callers are expected to
/// open the candidate datasets themselves (no implicit catalogue is
/// consulted; no I/O is performed) and may supply <c>null</c> for any
/// dataset they have not opened.
/// </para>
/// <para>
/// For the route: a candidate <see cref="S421Route"/> is considered a
/// match for the plan's
/// <see cref="S129UkcPlanMetadata.SourceRoute"/> when its
/// <see cref="S421Route.RouteId"/> or
/// <see cref="S421RouteInfo.Name"/> (or, failing those, its
/// <see cref="S421Route.Id"/>) equals the reference's
/// <see cref="S129ExternalReference.Identifier"/> (case-insensitive,
/// trimmed). When the reference carries a
/// <see cref="S129ExternalReference.Version"/>, the candidate's
/// <see cref="S421Route.EditionNumber"/> (rendered as a decimal
/// string) must also match.
/// </para>
/// <para>
/// For S-102 and S-104: S-129 Edition 2.0.0 does not declare textual
/// references to those products in any field of the current FC. If a
/// candidate <see cref="S102Dataset"/> / <see cref="S104Dataset"/> is
/// supplied alongside an S-129 plan, the resolver binds it as the
/// resolved bathymetry / water-level surface for the plan (the caller
/// is asserting "use this dataset"); when no candidate is supplied no
/// resolved reference is produced and an
/// <see cref="S129UnresolvedReference"/> with
/// <see cref="S129ReferenceResolutionReason.DatasetNotProvided"/> is
/// emitted.
/// </para>
/// </remarks>
public static class S129CrossProductResolver
{
    /// <summary>
    /// Resolves cross-product references for <paramref name="plan"/>
    /// against the supplied candidate datasets.
    /// </summary>
    /// <param name="plan">The S-129 UKC plan whose references to resolve.</param>
    /// <param name="bathymetry">
    /// The candidate S-102 dataset, or <c>null</c> if none is open.
    /// </param>
    /// <param name="waterLevel">
    /// The candidate S-104 dataset, or <c>null</c> if none is open.
    /// </param>
    /// <param name="route">
    /// The candidate S-421 route, or <c>null</c> if none is open.
    /// </param>
    /// <returns>
    /// A bag of resolved / unresolved references. The returned value is
    /// independent of <paramref name="plan"/>'s lifetime — it holds
    /// references back to the supplied dataset objects rather than
    /// copying them.
    /// </returns>
    public static S129ResolvedReferences Resolve(
        S129UnderKeelClearancePlan plan,
        S102Dataset? bathymetry = null,
        S104Dataset? waterLevel = null,
        S421Route? route = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var unresolved = ImmutableArray.CreateBuilder<S129UnresolvedReference>();

        S129ResolvedReference<S421Route>? resolvedRoute = ResolveRoute(plan, route, unresolved);
        S129ResolvedReference<S102Dataset>? resolvedBathymetry = ResolveBathymetry(plan, bathymetry, unresolved);
        S129ResolvedReference<S104Dataset>? resolvedWaterLevel = ResolveWaterLevel(plan, waterLevel, unresolved);

        return new S129ResolvedReferences(
            Bathymetry: resolvedBathymetry,
            WaterLevel: resolvedWaterLevel,
            Route: resolvedRoute,
            Unresolved: unresolved.ToImmutable());
    }

    private static S129ResolvedReference<S421Route>? ResolveRoute(
        S129UnderKeelClearancePlan plan,
        S421Route? candidate,
        ImmutableArray<S129UnresolvedReference>.Builder unresolved)
    {
        const string kind = "S-421 route";
        var reference = plan.Plan?.SourceRoute;

        if (candidate is null)
        {
            if (reference is not null)
                unresolved.Add(new S129UnresolvedReference(
                    reference, kind, S129ReferenceResolutionReason.DatasetNotProvided));
            return null;
        }

        if (reference is null)
        {
            // The plan does not name a source route; treat the supplied
            // candidate as an authoritative binding (caller is asserting
            // "use this route for this plan").
            var synthetic = new S129ExternalReference
            {
                Kind = kind,
                Identifier = candidate.RouteId ?? candidate.Info?.Name ?? candidate.Id,
                Version = candidate.EditionNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            return new S129ResolvedReference<S421Route>(synthetic, candidate);
        }

        if (!RouteMatches(candidate, reference))
        {
            unresolved.Add(new S129UnresolvedReference(
                reference, kind, S129ReferenceResolutionReason.IdentifierMismatch));
            return null;
        }

        return new S129ResolvedReference<S421Route>(reference, candidate);
    }

    private static bool RouteMatches(S421Route candidate, S129ExternalReference reference)
    {
        var id = reference.Identifier;
        if (string.IsNullOrWhiteSpace(id)) return false;

        bool idHit =
            Eq(candidate.RouteId, id) ||
            Eq(candidate.Info?.Name, id) ||
            Eq(candidate.Id, id);

        if (!idHit) return false;

        if (string.IsNullOrWhiteSpace(reference.Version)) return true;

        var candidateVersion = candidate.EditionNumber?.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        return Eq(candidateVersion, reference.Version);
    }

    private static bool Eq(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static S129ResolvedReference<S102Dataset>? ResolveBathymetry(
        S129UnderKeelClearancePlan plan,
        S102Dataset? candidate,
        ImmutableArray<S129UnresolvedReference>.Builder unresolved)
    {
        const string kind = "S-102 bathymetry";
        if (candidate is null)
        {
            unresolved.Add(new S129UnresolvedReference(
                ExternalReference: null, kind, S129ReferenceResolutionReason.DatasetNotProvided));
            return null;
        }

        // S-129 Edition 2.0.0 has no textual S-102 reference field; the
        // caller is asserting that this dataset is the bathymetric input
        // for the plan. Surface a synthetic external reference for
        // round-trip traceability.
        var synthetic = new S129ExternalReference
        {
            Kind = kind,
            Identifier = plan.DatasetIdentifier ?? plan.Plan?.Id ?? "(unspecified)",
        };
        _ = plan;
        return new S129ResolvedReference<S102Dataset>(synthetic, candidate);
    }

    private static S129ResolvedReference<S104Dataset>? ResolveWaterLevel(
        S129UnderKeelClearancePlan plan,
        S104Dataset? candidate,
        ImmutableArray<S129UnresolvedReference>.Builder unresolved)
    {
        const string kind = "S-104 water level";
        if (candidate is null)
        {
            unresolved.Add(new S129UnresolvedReference(
                ExternalReference: null, kind, S129ReferenceResolutionReason.DatasetNotProvided));
            return null;
        }

        var synthetic = new S129ExternalReference
        {
            Kind = kind,
            Identifier = plan.DatasetIdentifier ?? plan.Plan?.Id ?? "(unspecified)",
        };
        return new S129ResolvedReference<S104Dataset>(synthetic, candidate);
    }
}
