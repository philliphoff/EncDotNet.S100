using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Navigation.Tests;

public sealed class PublicApiTests
{
    [Fact]
    public void Public_api_matches_baseline() =>
        PublicApiBaseline.Verify(typeof(RouteChecker).Assembly);
}
