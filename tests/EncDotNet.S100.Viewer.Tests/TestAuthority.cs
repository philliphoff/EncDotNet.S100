using EncDotNet.S100.Datasets.Pipelines.Interoperability;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Test helper: builds a fresh
/// <see cref="IInteroperabilityAuthorityProvider"/> wrapping the
/// canonical S-98 authority. Each processor instantiated in a test
/// receives its own provider; tests that need to exercise runtime
/// authority swaps construct their own.
/// </summary>
internal static class TestAuthority
{
    public static IInteroperabilityAuthorityProvider NewS98Provider()
        => new InteroperabilityAuthorityProvider(new InteroperabilityAuthority());
}
