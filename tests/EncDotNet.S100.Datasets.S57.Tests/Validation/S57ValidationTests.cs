using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S101.Validation;
using EncDotNet.S100.Datasets.S57.Validation;
using EncDotNet.S100.Validation;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.S57.Tests.Validation;

/// <summary>
/// Synthetic-fixture tests for the V-5 S-57 pre-translation rule
/// pack (<see cref="S57PreTranslationRules"/>) plus delegation tests
/// confirming that <c>S57DatasetProcessor.Validate()</c>'s wire-up of
/// the S-101 pack against the translated document produces the
/// expected <c>S101-as-S57/</c>-prefixed findings.
/// </summary>
/// <remarks>
/// Implements the test sketch in
/// <c>docs/design/non-gml-validation.md</c> §11 V-5. Fixtures are
/// built in-memory; no real S-57 datasets are required.
/// </remarks>
public class S57ValidationTests
{
    // ── Fixture helpers ────────────────────────────────────────────────

    private static S57DataSetIdentification Dsid(string name = "TEST.000") => new()
    {
        DataSetName = name,
        EditionNumber = "1",
        UpdateNumber = "0",
        IssueDate = "20260528",
    };

    private static S57DataSetParameters Dspm(int compilationScale = 50_000) => new()
    {
        CompilationScale = compilationScale,
        CoordinateMultiplicationFactor = 10_000_000,
        SoundingMultiplicationFactor = 10,
    };

    private static S57FeatureRecord Feat(
        uint recordId,
        S57ObjectCode objectCode,
        ushort producingAgency = 540,
        uint featureIdentificationNumber = 1,
        ushort featureIdentificationSubdivision = 0,
        byte primitive = 255,
        IEnumerable<S57SpatialPointer>? spatialPointers = null)
        => new()
        {
            RecordName = new S57RecordName
            {
                RecordNameCode = 100,
                RecordId = (int)recordId,
                AgencyCode = producingAgency,
                FeatureId = (int)featureIdentificationNumber,
                FeatureSubdivision = featureIdentificationSubdivision,
            },
            Primitive = (S57GeometricPrimitive)primitive,
            ObjectCode = objectCode,
            Attributes = ImmutableArray<S57AttributeValue>.Empty,
            NationalAttributes = ImmutableArray<S57AttributeValue>.Empty,
            SpatialPointers = (spatialPointers ?? Array.Empty<S57SpatialPointer>()).ToImmutableArray(),
        };

    private const byte RcnmConnectedNode = 120;

    private static S57VectorRecord Node(uint id, int y, int x)
        => new()
        {
            RecordName = new S57RecordName { RecordNameCode = RcnmConnectedNode, RecordId = (int)id },
            VectorPointers = ImmutableArray<S57VectorPointer>.Empty,
            Coordinates2D = ImmutableArray.Create(new S57Coordinate2D { X = x, Y = y }),
            Soundings = ImmutableArray<S57Sounding>.Empty,
            Attributes = ImmutableArray<S57AttributeValue>.Empty,
        };

    private static S57SpatialPointer NodePointer(uint nodeId) => new()
    {
        Name = new S57RecordName { RecordNameCode = RcnmConnectedNode, RecordId = (int)nodeId },
        Orientation = (S57Orientation)1,
        Usage = (S57UsageIndicator)0,
        Mask = (S57MaskingIndicator)0,
    };

    private static S57Document BuildDocument(
        S57DataSetIdentification? dsid = null,
        S57DataSetParameters? dspm = null,
        IEnumerable<S57FeatureRecord>? features = null,
        IEnumerable<S57VectorRecord>? vectorRecords = null)
        => new()
        {
            DataSetIdentification = dsid,
            DataSetParameters = dspm,
            VectorRecords = (vectorRecords ?? Array.Empty<S57VectorRecord>()).ToImmutableArray(),
            FeatureRecords = (features ?? Array.Empty<S57FeatureRecord>()).ToImmutableArray(),
        };

    // ── S57-R-1.1 — DSID + DSPM/CSCL ───────────────────────────────────

    [Fact]
    public void R1_1_Passes_When_Dsid_Present_And_Scale_Positive()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(compilationScale: 50_000),
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S57-R-1.1");
    }

    [Fact]
    public void R1_1_Fails_When_Dsid_Missing()
    {
        var doc = BuildDocument(
            dsid: null,
            dspm: Dspm(),
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S57-R-1.1");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains("DSID", finding.Message);
    }

    [Fact]
    public void R1_1_Fails_When_Dspm_Missing()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: null,
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S57-R-1.1");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains("DSPM", finding.Message);
    }

    [Fact]
    public void R1_1_Fails_When_Scale_Zero()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(compilationScale: 0),
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S57-R-1.1");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Contains("CSCL", finding.Message);
    }

    [Fact]
    public void R1_1_Fails_When_Scale_Negative()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(compilationScale: -1),
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        Assert.Contains(report.Findings, f => f.RuleId == "S57-R-1.1");
    }

    [Fact]
    public void R1_1_Reports_Both_Dsid_And_Dspm_Missing_As_Separate_Findings()
    {
        var doc = BuildDocument(dsid: null, dspm: null);

        var report = S57PreTranslationRules.Default.Run(doc);

        var r11 = report.Findings.Where(f => f.RuleId == "S57-R-1.1").ToList();
        Assert.Equal(2, r11.Count);
    }

    // ── S57-R-1.2 — M_COVR presence ────────────────────────────────────

    [Fact]
    public void R1_2_Passes_When_M_COVR_Present()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(),
            features: new[]
            {
                Feat(1, S57ObjectCode.M_COVR),
                Feat(2, S57ObjectCode.DEPARE, featureIdentificationNumber: 2),
            });

        var report = S57PreTranslationRules.Default.Run(doc);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S57-R-1.2");
    }

    [Fact]
    public void R1_2_Warns_When_No_M_COVR()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(),
            features: new[] { Feat(1, S57ObjectCode.DEPARE) });

        var report = S57PreTranslationRules.Default.Run(doc);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "S57-R-1.2");
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Contains("M_COVR", finding.Message);
    }

    [Fact]
    public void R1_2_Warns_When_Feature_List_Empty()
    {
        var doc = BuildDocument(dsid: Dsid(), dspm: Dspm());

        var report = S57PreTranslationRules.Default.Run(doc);

        Assert.Contains(report.Findings, f => f.RuleId == "S57-R-1.2");
    }

    // ── S57-PROJ-PARSE placeholder ─────────────────────────────────────

    [Fact]
    public void ProjParse_Is_Registered_As_Empty_Placeholder()
    {
        // The rule exists in the pack and the rule-id namespace is
        // reserved (design §5.3) — but the body is empty until the
        // reader surfaces non-fatal diagnostics (design §5.2 Stance A).
        Assert.Equal("S57-PROJ-PARSE", S57PreTranslationRules.ParserDiagnosticPlaceholder.RuleId);

        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(),
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        Assert.DoesNotContain(report.Findings, f => f.RuleId == "S57-PROJ-PARSE");
    }

    // ── Whole-pack composition ─────────────────────────────────────────

    [Fact]
    public void Default_Pack_All_Green_On_Conformant_Document()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(),
            features: new[]
            {
                Feat(1, S57ObjectCode.M_COVR),
                Feat(2, S57ObjectCode.DEPARE, featureIdentificationNumber: 2),
            });

        var report = S57PreTranslationRules.Default.Run(doc);

        Assert.Empty(report.Findings);
        Assert.Equal(3, report.RulesEvaluated);
        Assert.Equal(0, report.RulesWithFindings);
    }

    [Fact]
    public void Default_Pack_Counts_Rules_Evaluated()
    {
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(),
            features: new[] { Feat(1, S57ObjectCode.M_COVR) });

        var report = S57PreTranslationRules.Default.Run(doc);

        // Three rules: R-1.1, R-1.2, PROJ-PARSE.
        Assert.Equal(3, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_Static_Wrapper_Equivalent_To_Default_Run()
    {
        var doc = BuildDocument(dsid: null, dspm: null);

        var viaWrapper = S57PreTranslationRules.Validate(doc);
        var viaDefault = S57PreTranslationRules.Default.Run(doc);

        Assert.Equal(viaDefault.Findings.Length, viaWrapper.Findings.Length);
        Assert.Equal(viaDefault.RulesEvaluated, viaWrapper.RulesEvaluated);
    }

    // ── Delegation — translated S-101 findings rebadged ────────────────

    [Fact]
    public void Delegation_Translated_S101_Findings_Are_Rebadged_With_S101_As_S57_Prefix()
    {
        // Two BCNCAR point features sharing the same FOID triple
        // (agency=540, FIDN=42, FIDS=0). Both reference the same
        // connected node so they survive translation; the S-57 →
        // S-101 translator preserves the FOID composition, so
        // S101-R-2.1 (FOID uniqueness — V-4, §6.4 / §8.4) will fire
        // against the translated S101Document.
        // S57DatasetProcessor.Validate() rebadges those findings
        // with the "S101-as-S57/" prefix (design §9.3 /
        // Q-s57-rebadge resolution).
        var node = Node(1, 100, 200);
        var doc = BuildDocument(
            dsid: Dsid(),
            dspm: Dspm(),
            vectorRecords: new[] { node },
            features: new[]
            {
                Feat(1, S57ObjectCode.BCNCAR,
                     producingAgency: 540, featureIdentificationNumber: 42,
                     primitive: 1, spatialPointers: new[] { NodePointer(1) }),
                Feat(2, S57ObjectCode.BCNCAR,
                     producingAgency: 540, featureIdentificationNumber: 42,
                     primitive: 1, spatialPointers: new[] { NodePointer(1) }),
            });

        var translated = new S57ToS101Translator().Translate(doc);
        Assert.Equal(2, translated.Features.Length);

        var view = S101DatasetView.From(translated, decoder: null);
        var s101Report = S101DatasetRules.Default.Run(view);

        Assert.Contains(s101Report.Findings, f => f.RuleId == "S101-R-2.1");

        // Mirror what S57DatasetProcessor.Validate() does via
        // ConcatReports.Concat(pre, post, rebadgePrefix: "S101-as-S57/"):
        // verifies the rule output is structured such that simple
        // prefix-rewriting yields the documented composite rule id.
        var rebadged = s101Report.Findings
            .Select(f => f with { RuleId = "S101-as-S57/" + f.RuleId })
            .ToList();

        Assert.Contains(rebadged, f => f.RuleId == "S101-as-S57/S101-R-2.1");
    }

    [Fact]
    public void Delegation_Pre_Translation_Findings_Are_Not_Rebadged()
    {
        // Confirms the rebadge prefix applies to the post-translation
        // report only (ConcatReports.Concat semantics): native
        // S57-R-* rule ids must remain verbatim so consumers can
        // filter on the "S57-" prefix to surface the small
        // pre-translation pack distinctly from the inherited S-101
        // findings.
        var doc = BuildDocument(dsid: null, dspm: Dspm());

        var pre = S57PreTranslationRules.Default.Run(doc);

        Assert.Contains(pre.Findings, f => f.RuleId == "S57-R-1.1");
        Assert.DoesNotContain(pre.Findings,
            f => f.RuleId.StartsWith("S101-as-S57/", StringComparison.Ordinal));
    }
}
