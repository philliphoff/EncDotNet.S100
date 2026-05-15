using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Core.Tests;

public class DataModelTests
{
    // ── ProjectionDiagnostic ─────────────────────────────────────

    [Fact]
    public void Diagnostic_ToString_IncludesSeverityAndMessage()
    {
        var d = new ProjectionDiagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = "Something amiss.",
            Code = "test.code",
            RelatedId = "ID1",
            RelatedAttribute = "attr",
        };

        var s = d.ToString();
        Assert.Contains("Warning", s);
        Assert.Contains("Something amiss.", s);
        Assert.Contains("ID1", s);
    }

    // ── AttributeParser ──────────────────────────────────────────

    [Fact]
    public void TryParseInt_ReturnsValueForValidInput()
    {
        var ctx = NewContext();
        var v = AttributeParser.TryParseInt("42", ctx, "id", "attr");
        Assert.Equal(42, v);
        Assert.Empty(ctx.Diagnostics);
    }

    [Fact]
    public void TryParseInt_ReturnsNullAndEmitsDiagnosticForBadInput()
    {
        var ctx = NewContext();
        var v = AttributeParser.TryParseInt("not-a-number", ctx, "id", "attr");
        Assert.Null(v);
        var d = Assert.Single(ctx.Diagnostics);
        Assert.Equal("attribute.parse.int", d.Code);
        Assert.Equal("id", d.RelatedId);
        Assert.Equal("attr", d.RelatedAttribute);
    }

    [Fact]
    public void TryParseInt_ReturnsNullForNullAndDoesNotEmit()
    {
        var ctx = NewContext();
        var v = AttributeParser.TryParseInt(null, ctx, "id", "attr");
        Assert.Null(v);
        Assert.Empty(ctx.Diagnostics);
    }

    [Fact]
    public void TryParseDouble_UsesInvariantCulture()
    {
        var ctx = NewContext();
        Assert.Equal(3.14, AttributeParser.TryParseDouble("3.14", ctx, "id", "a"));
        Assert.Empty(ctx.Diagnostics);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void TryParseBool_AcceptsCommonForms(string input, bool expected)
    {
        var ctx = NewContext();
        Assert.Equal(expected, AttributeParser.TryParseBool(input, ctx, null, null));
        Assert.Empty(ctx.Diagnostics);
    }

    [Fact]
    public void TryParseBool_EmitsDiagnosticForGarbage()
    {
        var ctx = NewContext();
        Assert.Null(AttributeParser.TryParseBool("maybe", ctx, "id", "a"));
        Assert.Equal("attribute.parse.bool", Assert.Single(ctx.Diagnostics).Code);
    }

    [Fact]
    public void TryParseDateTimeOffset_ParsesIso8601()
    {
        var ctx = NewContext();
        var v = AttributeParser.TryParseDateTimeOffset("2026-03-15T12:00:00Z", ctx, null, null);
        Assert.NotNull(v);
        Assert.Equal(2026, v.Value.Year);
        Assert.Equal(TimeSpan.Zero, v.Value.Offset);
        Assert.Empty(ctx.Diagnostics);
    }

    // ── XlinkResolver ────────────────────────────────────────────

    [Fact]
    public void Xlink_Resolves_HashPrefixedHref()
    {
        var target = new TestTarget("f1");
        var resolver = XlinkResolver.Build(new[]
        {
            new KeyValuePair<string, object>("f1", target),
        });
        var ctx = new ProjectionContext(resolver);

        var resolved = resolver.Resolve<TestTarget>("#f1", "someRole", ctx, null);
        Assert.Same(target, resolved);
        Assert.Empty(ctx.Diagnostics);
    }

    [Fact]
    public void Xlink_Resolves_PlainHref()
    {
        var target = new TestTarget("f1");
        var resolver = XlinkResolver.Build(new[]
        {
            new KeyValuePair<string, object>("f1", target),
        });
        var ctx = new ProjectionContext(resolver);

        Assert.Same(target, resolver.Resolve<TestTarget>("f1", "r", ctx, null));
    }

    [Fact]
    public void Xlink_EmitsDiagnosticForUnresolvedHref()
    {
        var resolver = XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>());
        var ctx = new ProjectionContext(resolver);

        var v = resolver.Resolve<TestTarget>("#missing", "role", ctx, "source");
        Assert.Null(v);
        var d = Assert.Single(ctx.Diagnostics);
        Assert.Equal("xlink.unresolved", d.Code);
        Assert.Equal("source", d.RelatedId);
    }

    [Fact]
    public void Xlink_EmitsDiagnosticForWrongTargetType()
    {
        var target = new TestTarget("f1");
        var resolver = XlinkResolver.Build(new[]
        {
            new KeyValuePair<string, object>("f1", target),
        });
        var ctx = new ProjectionContext(resolver);

        var v = resolver.Resolve<OtherTarget>("#f1", "role", ctx, "src");
        Assert.Null(v);
        Assert.Single(ctx.Diagnostics);
    }

    // ── ExtraAttributes ──────────────────────────────────────────

    [Fact]
    public void ExtraAttributes_ExcludeKnown_RemovesKnownKeysCaseInsensitive()
    {
        var attrs = System.Collections.Immutable.ImmutableDictionary
            .CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        attrs["foo"] = "1";
        attrs["bar"] = "2";
        attrs["baz"] = "3";

        var extras = ExtraAttributes.ExcludeKnown(attrs.ToImmutable(), "FOO", "BAR");
        Assert.Single(extras);
        Assert.Equal("3", extras["baz"]);
    }

    // ── GeoPosition ──────────────────────────────────────────────

    [Fact]
    public void GeoPosition_RecordEquality()
    {
        var a = new GeoPosition(51.5, -0.1);
        var b = new GeoPosition(51.5, -0.1);
        Assert.Equal(a, b);
        Assert.NotEqual(a, new GeoPosition(0, 0));
    }

    // ── helpers ──────────────────────────────────────────────────

    private static ProjectionContext NewContext() =>
        new(XlinkResolver.Build(Array.Empty<KeyValuePair<string, object>>()));

    private sealed record TestTarget(string Id);
    private sealed record OtherTarget(string Id);
}
