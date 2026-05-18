using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for the validation surface added to
/// <see cref="DatasetEntry"/> and the
/// <see cref="ValidationFindingViewModel"/> wrapper. These tests do not
/// touch Avalonia — they exercise pure view-model logic so they can run
/// on any CI without a display.
/// </summary>
public class DatasetEntryValidationTests
{
    private static ValidationFinding Finding(string ruleId, ValidationSeverity severity,
        string message = "msg", string? relatedFeatureId = null)
        => new()
        {
            RuleId = ruleId,
            Severity = severity,
            Message = message,
            RelatedFeatureId = relatedFeatureId,
        };

    private static ValidationReport ReportOf(params ValidationFinding[] findings)
        => new(findings.ToImmutableArray(), RulesEvaluated: findings.Length,
            RulesWithFindings: findings.Length);

    [Fact]
    public void Defaults_HaveNoValidationReport_AndShowNoRulePackEmptyState()
    {
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");

        Assert.Null(entry.Validation);
        Assert.False(entry.HasValidationRulePack);
        Assert.False(entry.HasValidationFindings);
        Assert.Equal(0, entry.ValidationFindingCount);
        Assert.Empty(entry.Findings);
        Assert.Contains("S-125", entry.ValidationEmptyStateMessage);
    }

    [Fact]
    public void SetValidationReport_WithErrorsAndWarnings_PopulatesCountsAndBadgeFlags()
    {
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");
        var observed = new List<string?>();
        entry.PropertyChanged += (_, e) => observed.Add(e.PropertyName);

        entry.SetValidationReport(ReportOf(
            Finding("S125-R-1.1", ValidationSeverity.Error),
            Finding("S125-R-1.2", ValidationSeverity.Warning),
            Finding("S125-R-1.3", ValidationSeverity.Warning),
            Finding("S125-R-1.4", ValidationSeverity.Info)));

        Assert.True(entry.HasValidationRulePack);
        Assert.True(entry.HasValidationFindings);
        Assert.Equal(4, entry.ValidationFindingCount);
        Assert.Equal(1, entry.ValidationErrorCount);
        Assert.Equal(2, entry.ValidationWarningCount);
        Assert.Equal(1, entry.ValidationInfoCount);
        Assert.True(entry.BadgeIsError);
        Assert.False(entry.BadgeIsWarning);
        Assert.False(entry.BadgeIsInfo);
        Assert.Equal(4, entry.Findings.Count);
        Assert.Contains(nameof(DatasetEntry.ValidationFindingCount), observed);
        Assert.Contains(nameof(DatasetEntry.HasValidationFindings), observed);
    }

    [Fact]
    public void SetValidationReport_WithOnlyWarnings_SelectsWarningBadge()
    {
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");

        entry.SetValidationReport(ReportOf(
            Finding("R1", ValidationSeverity.Warning),
            Finding("R2", ValidationSeverity.Info)));

        Assert.False(entry.BadgeIsError);
        Assert.True(entry.BadgeIsWarning);
        Assert.False(entry.BadgeIsInfo);
    }

    [Fact]
    public void SetValidationReport_WithOnlyInfo_SelectsInfoBadge()
    {
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");

        entry.SetValidationReport(ReportOf(Finding("R1", ValidationSeverity.Info)));

        Assert.False(entry.BadgeIsError);
        Assert.False(entry.BadgeIsWarning);
        Assert.True(entry.BadgeIsInfo);
    }

    [Fact]
    public void SetValidationReport_Empty_ShowsNoFindingsEmptyState()
    {
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");

        entry.SetValidationReport(ValidationReport.Empty);

        Assert.True(entry.HasValidationRulePack);
        Assert.False(entry.HasValidationFindings);
        Assert.Equal(0, entry.ValidationFindingCount);
        // The "no findings" message must not include the spec name —
        // that's reserved for the "no rule pack" empty state.
        Assert.DoesNotContain("S-125", entry.ValidationEmptyStateMessage);
    }

    [Fact]
    public void SetValidationReport_Null_ResetsToNoRulePackState()
    {
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");
        entry.SetValidationReport(ReportOf(Finding("R1", ValidationSeverity.Error)));
        Assert.True(entry.HasValidationFindings);

        entry.SetValidationReport(null);

        Assert.Null(entry.Validation);
        Assert.False(entry.HasValidationRulePack);
        Assert.False(entry.HasValidationFindings);
        Assert.Equal(0, entry.ValidationFindingCount);
        Assert.Empty(entry.Findings);
        Assert.Contains("S-125", entry.ValidationEmptyStateMessage);
    }

    [Theory]
    [InlineData(ValidationSeverity.Error, "Error", "ErrorCircle", true, false, false)]
    [InlineData(ValidationSeverity.Warning, "Warning", "Warning", false, true, false)]
    [InlineData(ValidationSeverity.Info, "Info", "Info", false, false, true)]
    public void ValidationFindingViewModel_MapsSeverityToIconAndFlags(
        ValidationSeverity severity, string expectedClass, string expectedIcon,
        bool isError, bool isWarning, bool isInfo)
    {
        var vm = new ValidationFindingViewModel(Finding("R1", severity));

        Assert.Equal(expectedClass, vm.SeverityClass);
        Assert.Equal(expectedIcon, vm.SeverityIcon);
        Assert.Equal(isError, vm.IsError);
        Assert.Equal(isWarning, vm.IsWarning);
        Assert.Equal(isInfo, vm.IsInfo);
    }

    [Fact]
    public void ValidationFindingViewModel_NoRelatedFeature_HidesRelatedLine()
    {
        var vm = new ValidationFindingViewModel(Finding("R1", ValidationSeverity.Error));

        Assert.False(vm.HasRelatedFeature);
        Assert.Equal(string.Empty, vm.RelatedFeatureLine);
    }

    [Fact]
    public void ValidationFindingViewModel_WithRelatedFeature_FormatsLine()
    {
        var vm = new ValidationFindingViewModel(
            Finding("R1", ValidationSeverity.Warning, relatedFeatureId: "BUOYAGE.42"));

        Assert.True(vm.HasRelatedFeature);
        Assert.Contains("BUOYAGE.42", vm.RelatedFeatureLine);
    }
}
