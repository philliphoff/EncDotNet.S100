using EncDotNet.S100.DataModel;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Core.Tests.Validation;

/// <summary>
/// Tests focused on the fluent <see cref="ValidationRuleBuilder"/> entry point.
/// </summary>
public class ValidationRuleBuilderTests
{
    private sealed record Model(double Latitude, double Longitude);

    [Fact]
    public void Check_FailureMessageDefaultsToDescription()
    {
        var rule = ValidationRuleBuilder.RuleFor<Model>("R1")
            .WithDescription("Latitude must be in [-90, 90].")
            .Check(m => m.Latitude is >= -90 and <= 90)
            .Build();

        var findings = rule.Evaluate(new Model(91, 0), ValidationContext.Default).ToArray();

        var f = Assert.Single(findings);
        Assert.Equal("Latitude must be in [-90, 90].", f.Message);
    }

    [Fact]
    public void Check_LocatorAttachesPoint()
    {
        var rule = ValidationRuleBuilder.RuleFor<Model>("R-loc")
            .WithDescription("Latitude must be in [-90, 90].")
            .Check(
                predicate: m => m.Latitude is >= -90 and <= 90,
                failureMessage: "bad latitude",
                locator: m => new GeoPosition(m.Latitude, m.Longitude))
            .Build();

        var findings = rule.Evaluate(new Model(91, 10), ValidationContext.Default).ToArray();

        var f = Assert.Single(findings);
        Assert.NotNull(f.Point);
        Assert.Equal(91, f.Point!.Value.Latitude);
        Assert.Equal(10, f.Point.Value.Longitude);
    }

    [Fact]
    public void Yield_AllowsMultipleFindings()
    {
        // One finding per coordinate component out of range
        var rule = ValidationRuleBuilder.RuleFor<Model>("R-multi")
            .WithSeverity(ValidationSeverity.Warning)
            .Yield((m, _) =>
            {
                var list = new List<ValidationFinding>();
                if (m.Latitude is < -90 or > 90)
                    list.Add(new ValidationFinding
                    {
                        RuleId = "R-multi",
                        Severity = ValidationSeverity.Warning,
                        Message = "latitude out of range",
                    });
                if (m.Longitude is < -180 or > 180)
                    list.Add(new ValidationFinding
                    {
                        RuleId = "R-multi",
                        Severity = ValidationSeverity.Warning,
                        Message = "longitude out of range",
                    });
                return list;
            })
            .Build();

        var findings = rule.Evaluate(new Model(91, 181), ValidationContext.Default).ToArray();

        Assert.Equal(2, findings.Length);
        Assert.All(findings, f => Assert.Equal(ValidationSeverity.Warning, f.Severity));
    }

    [Fact]
    public void Build_WithoutBody_Throws()
    {
        var builder = ValidationRuleBuilder.RuleFor<Model>("R-empty")
            .WithDescription("missing body");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_CarriesRuleIdAndDefaultSeverity()
    {
        var rule = ValidationRuleBuilder.RuleFor<Model>("R-meta")
            .WithDescription("desc")
            .WithSeverity(ValidationSeverity.Info)
            .Check(_ => true)
            .Build();

        Assert.Equal("R-meta", rule.RuleId);
        Assert.Equal("desc", rule.Description);
        Assert.Equal(ValidationSeverity.Info, rule.DefaultSeverity);
    }

    [Fact]
    public void RuleFor_ThrowsOnNullRuleId()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationRuleBuilder.RuleFor<Model>(null!));
    }
}
