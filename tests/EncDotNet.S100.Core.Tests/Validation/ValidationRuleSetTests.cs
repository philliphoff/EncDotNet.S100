using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Core.Tests.Validation;

/// <summary>
/// Tests for the spec-agnostic validation core: <see cref="ValidationRuleSet{TModel}"/>,
/// <see cref="ValidationReport"/>, and the fluent
/// <see cref="ValidationRuleBuilder"/>.
/// </summary>
public class ValidationRuleSetTests
{
    private sealed record Model(int Value, string? Label = null);

    // ── ValidationRuleSet.Run ────────────────────────────────────

    [Fact]
    public void Run_EmptyRuleSet_ReturnsEmptyReport()
    {
        var report = ValidationRuleSet<Model>.Empty.Run(new Model(1));

        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.RulesEvaluated);
        Assert.Equal(0, report.RulesWithFindings);
    }

    [Fact]
    public void Run_PassingRule_ProducesNoFindings()
    {
        var rule = ValidationRuleBuilder.RuleFor<Model>("test.pass")
            .WithDescription("Value must be positive.")
            .Check(m => m.Value > 0)
            .Build();
        var set = new ValidationRuleSet<Model>(rule);

        var report = set.Run(new Model(1));

        Assert.True(report.IsValid);
        Assert.Equal(1, report.RulesEvaluated);
        Assert.Equal(0, report.RulesWithFindings);
    }

    [Fact]
    public void Run_FailingRule_ProducesFinding()
    {
        var rule = ValidationRuleBuilder.RuleFor<Model>("test.fail")
            .WithDescription("Value must be positive.")
            .WithSeverity(ValidationSeverity.Error)
            .Check(m => m.Value > 0, failureMessage: "value is non-positive")
            .Build();
        var set = new ValidationRuleSet<Model>(rule);

        var report = set.Run(new Model(0));

        var finding = Assert.Single(report.Findings);
        Assert.Equal("test.fail", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("value is non-positive", finding.Message);
        Assert.True(report.HasErrors);
        Assert.False(report.IsValid);
        Assert.Equal(1, report.RulesWithFindings);
    }

    [Fact]
    public void Run_AllRulesEvaluatedEvenAfterFailure()
    {
        var r1 = ValidationRuleBuilder.RuleFor<Model>("r1")
            .WithSeverity(ValidationSeverity.Error)
            .Check(m => m.Value > 100, failureMessage: "too small (r1)")
            .Build();
        var r2 = ValidationRuleBuilder.RuleFor<Model>("r2")
            .WithSeverity(ValidationSeverity.Warning)
            .Check(m => m.Value > 50, failureMessage: "below warning threshold (r2)")
            .Build();

        var report = new ValidationRuleSet<Model>(r1, r2).Run(new Model(10));

        Assert.Equal(2, report.RulesEvaluated);
        Assert.Equal(2, report.RulesWithFindings);
        Assert.Equal(2, report.Findings.Length);
        Assert.True(report.HasErrors);
        Assert.True(report.HasWarnings);
    }

    [Fact]
    public void Run_ExceptionInRule_IsSurfacedAsErrorFinding()
    {
        var rule = ValidationRuleBuilder.RuleFor<Model>("test.boom")
            .WithDescription("Throws.")
            .Yield((_, _) => throw new InvalidOperationException("kaboom"))
            .Build();

        var report = new ValidationRuleSet<Model>(rule).Run(new Model(1));

        var finding = Assert.Single(report.Findings);
        Assert.Equal("test.boom", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains("kaboom", finding.Message);
    }

    [Fact]
    public void Run_ThrowsOnNullModel()
    {
        Assert.Throws<ArgumentNullException>(() => ValidationRuleSet<Model>.Empty.Run(null!));
    }

    [Fact]
    public void Run_PassesContextToRule_DefaultsWhenOmitted()
    {
        ValidationContext? captured = null;
        var rule = ValidationRuleBuilder.RuleFor<Model>("test.ctx")
            .Yield((_, ctx) =>
            {
                captured = ctx;
                return Array.Empty<ValidationFinding>();
            })
            .Build();

        new ValidationRuleSet<Model>(rule).Run(new Model(1));

        Assert.Same(ValidationContext.Default, captured);
    }

    [Fact]
    public void Run_HonoursExplicitContext()
    {
        var pinned = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset? observed = null;
        var rule = ValidationRuleBuilder.RuleFor<Model>("test.ctx-time")
            .Yield((_, ctx) =>
            {
                observed = ctx.ReferenceTime;
                return Array.Empty<ValidationFinding>();
            })
            .Build();

        new ValidationRuleSet<Model>(rule).Run(new Model(1),
            new ValidationContext { ReferenceTime = pinned });

        Assert.Equal(pinned, observed);
    }

    // ── ValidationRuleSet composition ─────────────────────────────

    [Fact]
    public void Add_AppendsRuleToSet()
    {
        var r1 = ValidationRuleBuilder.RuleFor<Model>("r1").Check(_ => true).Build();
        var r2 = ValidationRuleBuilder.RuleFor<Model>("r2").Check(_ => true).Build();

        var set = new ValidationRuleSet<Model>(r1).Add(r2);

        Assert.Equal(2, set.Rules.Length);
        Assert.Equal("r1", set.Rules[0].RuleId);
        Assert.Equal("r2", set.Rules[1].RuleId);
    }

    [Fact]
    public void Remove_DropsRulesByRuleId()
    {
        var r1 = ValidationRuleBuilder.RuleFor<Model>("keep").Check(_ => true).Build();
        var r2 = ValidationRuleBuilder.RuleFor<Model>("drop").Check(_ => true).Build();

        var set = new ValidationRuleSet<Model>(r1, r2).Remove("drop");

        Assert.Single(set.Rules);
        Assert.Equal("keep", set.Rules[0].RuleId);
    }

    // ── ValidationReport helpers ─────────────────────────────────

    [Fact]
    public void FindingsOfSeverity_FiltersCorrectly()
    {
        var findings = ImmutableArray.Create(
            new ValidationFinding { RuleId = "a", Severity = ValidationSeverity.Error, Message = "e" },
            new ValidationFinding { RuleId = "b", Severity = ValidationSeverity.Warning, Message = "w" },
            new ValidationFinding { RuleId = "c", Severity = ValidationSeverity.Info, Message = "i" });
        var report = new ValidationReport(findings, RulesEvaluated: 3, RulesWithFindings: 3);

        Assert.Single(report.FindingsOfSeverity(ValidationSeverity.Error));
        Assert.Single(report.FindingsOfSeverity(ValidationSeverity.Warning));
        Assert.Single(report.FindingsOfSeverity(ValidationSeverity.Info));
        Assert.True(report.HasErrors);
        Assert.True(report.HasWarnings);
    }

    [Fact]
    public void Report_Empty_IsValid()
    {
        Assert.True(ValidationReport.Empty.IsValid);
        Assert.False(ValidationReport.Empty.HasErrors);
        Assert.False(ValidationReport.Empty.HasWarnings);
    }
}
