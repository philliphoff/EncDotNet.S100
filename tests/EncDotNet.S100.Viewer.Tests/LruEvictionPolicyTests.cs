using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for <see cref="LruEvictionPolicy{TKey}"/>, the pure
/// least-recently-used bookkeeping behind lazy-load eviction (issue #458).
/// </summary>
public sealed class LruEvictionPolicyTests
{
    [Fact]
    public void Touch_ReportsNewlyAdded()
    {
        var lru = new LruEvictionPolicy<string>();

        Assert.True(lru.Touch("a"));
        Assert.False(lru.Touch("a"));
        Assert.Equal(1, lru.Count);
    }

    [Fact]
    public void SelectEvictions_UnderBudget_ReturnsNothing()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a");
        lru.Touch("b");

        Assert.Empty(lru.SelectEvictions(retentionBudget: 5));
    }

    [Fact]
    public void SelectEvictions_OverBudget_EvictsColdestFirst()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a"); // coldest
        lru.Touch("b");
        lru.Touch("c"); // warmest

        var victims = lru.SelectEvictions(retentionBudget: 1);

        // Two over budget: the two coldest, coldest first.
        Assert.Equal(new[] { "a", "b" }, victims);
    }

    [Fact]
    public void Touch_MovesKeyToWarmEnd()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a");
        lru.Touch("b");
        lru.Touch("c");
        lru.Touch("a"); // 'a' is now warmest; 'b' is coldest

        var victims = lru.SelectEvictions(retentionBudget: 2);

        Assert.Equal(new[] { "b" }, victims);
    }

    [Fact]
    public void SelectEvictions_NeverEvictsProtectedKeys()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a");
        lru.Touch("b");
        lru.Touch("c");

        var protectedKeys = new HashSet<string> { "a" };
        var victims = lru.SelectEvictions(retentionBudget: 2, protectedKeys: protectedKeys);

        // One over budget: the coldest is 'a' but it is protected, so the next
        // coldest unprotected key ('b') is evicted instead.
        Assert.DoesNotContain("a", victims);
        Assert.Equal(new[] { "b" }, victims);
    }

    [Fact]
    public void SelectEvictions_DoesNotRemoveFromTracking()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a");
        lru.Touch("b");

        _ = lru.SelectEvictions(retentionBudget: 0);

        // Selection is advisory; the caller removes explicitly.
        Assert.Equal(2, lru.Count);
    }

    [Fact]
    public void Remove_StopsTracking()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a");
        lru.Touch("b");

        lru.Remove("a");

        Assert.False(lru.Contains("a"));
        Assert.Equal(1, lru.Count);
    }

    [Fact]
    public void SelectEvictions_NegativeBudget_TreatedAsZero()
    {
        var lru = new LruEvictionPolicy<string>();
        lru.Touch("a");

        Assert.Equal(new[] { "a" }, lru.SelectEvictions(retentionBudget: -5));
    }
}
