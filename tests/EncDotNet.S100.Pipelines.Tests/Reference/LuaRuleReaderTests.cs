using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// <see cref="LuaRuleReader"/> and <see cref="XsltRuleReader"/> on small
/// hand-written rules.
/// </summary>
public class LuaRuleReaderTests
{
    private static readonly RuleVocabulary Vocabulary = new(
        new Dictionary<string, IReadOnlyList<ReferenceKind>>
        {
            ["SYMA01"] = [ReferenceKind.Symbol],
            ["SYMB01"] = [ReferenceKind.Symbol],
            ["SYMC01"] = [ReferenceKind.Symbol],
            ["BOTH01"] = [ReferenceKind.Symbol, ReferenceKind.LineStyle],
            ["HELP01"] = [ReferenceKind.Symbol],
        },
        new Dictionary<string, (string Id, ReferenceKind Kind)> { ["SYMA01"] = ("SYMA01", ReferenceKind.Symbol) },
        new HashSet<string> { "DIGIT" },
        new HashSet<string> { "DEPVS", "DEPDW" });

    private static List<RuleFact> Items(RuleFacts facts, string id) =>
        facts.Facts.Where(f => f.Kind == RuleFactKind.Item && f.Value == id).ToList();

    [Fact]
    public void Reads_nested_conditions_with_lines()
    {
        const string source = """
            function Thing(feature, featurePortrayal, contextParameters)
                if feature.PrimitiveType == PrimitiveType.Point then
                    if contextParameters.SimplifiedSymbols then
                        featurePortrayal:AddInstructions('PointInstruction:SYMA01')
                    elseif feature.colour[2] ~= 4 and not feature.height then
                        featurePortrayal:AddInstructions('PointInstruction:SYMB01')
                    else
                        featurePortrayal:AddInstructions('PointInstruction:SYMC01')
                    end
                end
            end
            """;

        var facts = LuaRuleReader.Read("Thing", source, Vocabulary);

        Assert.Contains("Thing", facts.FeatureEntryPoints);
        var a = Assert.Single(Items(facts, "SYMA01"));
        Assert.Equal(4, a.Line);
        Assert.Equal(
            [
                new RuleCondition { Kind = RuleConditionKind.Primitive, Operator = ConditionOperator.Equal, Value = "Point", Line = 2 },
                new RuleCondition { Kind = RuleConditionKind.ContextParameter, Operator = ConditionOperator.IsTrue, Subject = "SimplifiedSymbols", Line = 3 },
            ],
            a.Conditions);

        var b = Assert.Single(Items(facts, "SYMB01"));
        Assert.Contains(new RuleCondition { Kind = RuleConditionKind.Attribute, Operator = ConditionOperator.NotEqual, Subject = "colour", Index = 2, Value = "4", Line = 5 }, b.Conditions);
        Assert.Contains(new RuleCondition { Kind = RuleConditionKind.Attribute, Operator = ConditionOperator.Absent, Subject = "height", Line = 5 }, b.Conditions);
        Assert.DoesNotContain(b.Conditions, c => c.Subject == "SimplifiedSymbols");

        var c = Assert.Single(Items(facts, "SYMC01"));
        Assert.Equal(RuleConditionKind.Otherwise, c.Conditions[^1].Kind);
        Assert.Equal(7, c.Conditions[^1].Line);
    }

    [Fact]
    public void Else_of_a_lone_test_negates_it()
    {
        const string source = """
            if contextParameters.SimplifiedSymbols then
                x = 'SYMA01'
            else
                x = 'SYMB01'
            end
            if feature.a == 1 and feature.b == 2 then
            else
                y = 'SYMC01'
            end
            """;

        var facts = LuaRuleReader.Read("R", source, Vocabulary);

        var b = Assert.Single(Items(facts, "SYMB01")).Conditions.Single();
        Assert.Equal((RuleConditionKind.ContextParameter, ConditionOperator.IsFalse, "SimplifiedSymbols"), (b.Kind, b.Operator, b.Subject));
        var c = Assert.Single(Items(facts, "SYMC01")).Conditions.Single();
        Assert.Equal((RuleConditionKind.Expression, ConditionOperator.DoesNotHold), (c.Kind, c.Operator));
        Assert.Equal("feature.a == 1 and feature.b == 2", c.Text);
    }

    [Fact]
    public void Or_and_computed_values_stay_expressions()
    {
        const string source = """
            if feature.a == 1 or feature.b == 2 then x = 'SYMA01' end
            if depth >= contextParameters.SafetyContour then x = 'SYMB01' end
            """;

        var facts = LuaRuleReader.Read("R", source, Vocabulary);

        Assert.Equal(RuleConditionKind.Expression, Assert.Single(Items(facts, "SYMA01")).Conditions.Single().Kind);
        Assert.Equal("depth >= contextParameters.SafetyContour", Assert.Single(Items(facts, "SYMB01")).Conditions.Single().Text);
    }

    [Fact]
    public void Ignores_comments_requires_calls_and_debug_labels()
    {
        const string source = """
            -- PointInstruction:SYMA01 in a comment
            --[[ and SYMB01
            in a long comment ]]
            require 'HELP01'
            function F(feature)
                Debug.StartPerformance('Lua Code - HELP01')
                HELP01(feature)
                error('SYMC01 is not drawn')
            end
            """;

        var facts = LuaRuleReader.Read("R", source, Vocabulary);

        Assert.Empty(facts.Facts);
        Assert.Equal(["HELP01"], facts.Requires);
    }

    [Fact]
    public void Reads_families_fills_pens_and_viewing_groups()
    {
        const string source = """
            function F(feature, featurePortrayal, contextParameters)
                local viewingGroup = 27020
                local colour = 'DEPVS'
                if contextParameters.FourShades then colour = 'DEPDW' end
                featurePortrayal:AddInstructions('ColorFill:' .. colour)
                featurePortrayal:AddInstructions('ViewingGroup:13030;ColorFill:NODTA')
                featurePortrayal:SimpleLineStyle('dash',0.32,'CHGRD')
                featurePortrayal:AddInstructions('LineStyle:_simple_,,0.64,CSTLN')
                featurePortrayal:AddInstructions('PointInstruction:DIGIT' .. n)
                featurePortrayal:AddInstructions('LineInstruction:BOTH01')
            end
            """;

        var facts = LuaRuleReader.Read("F", source, Vocabulary).Facts;

        Assert.Equal(["27020", "13030"], facts.Where(f => f.Kind == RuleFactKind.ViewingGroup).Select(f => f.Value));
        Assert.Equal(["NODTA", "DEPVS", "DEPDW"], facts.Where(f => f.Kind == RuleFactKind.ColorFill).Select(f => f.Value));
        Assert.Contains(facts, f => f.Kind == RuleFactKind.ColorFill && f.Value == "DEPDW" && f.Conditions.Single().Subject == "FourShades");
        Assert.Equal(
            [("dash", 0.32, "CHGRD"), ("solid", 0.64, "CSTLN")],
            facts.Where(f => f.Kind == RuleFactKind.Pen).Select(f => (f.Dash, f.Width, f.Value)));
        Assert.Single(facts, f => f.Kind == RuleFactKind.Family && f.Value == "DIGIT");
        Assert.Equal(ReferenceKind.LineStyle, Assert.Single(facts, f => f.Value == "BOTH01").ItemKind);
    }

    [Fact]
    public void Feature_code_tests_read_as_feature_type_conditions()
    {
        const string source = "if feature.Code == 'DredgedArea' then x = 'AreaFillReference:SYMA01' end";

        var fact = Assert.Single(Items(LuaRuleReader.Read("R", source, Vocabulary), "SYMA01"));

        Assert.Equal(
            new RuleCondition { Kind = RuleConditionKind.FeatureType, Operator = ConditionOperator.Equal, Value = "DredgedArea", Line = 1 },
            fact.Conditions.Single());
    }

    [Fact]
    public void Xslt_reads_templates_tests_and_named_template_calls()
    {
        const string source = """
            <xsl:transform xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
              <xsl:include href="other.xsl"/>
              <xsl:template match="Thing[@primitive='Point' and shape=2]" priority="2">
                <pointInstruction>
                  <viewingGroup>27020</viewingGroup>
                  <xsl:choose>
                    <xsl:when test="colour = 3"><symbol reference="SYMA01"/></xsl:when>
                    <xsl:otherwise><symbol reference="SYMB01"/></xsl:otherwise>
                  </xsl:choose>
                </pointInstruction>
                <xsl:call-template name="helper"/>
              </xsl:template>
              <xsl:template match="Other|Thing[@primitive='Surface']">
                <areaInstruction><colorFill><color>DEPVS</color></colorFill></areaInstruction>
              </xsl:template>
              <xsl:template name="helper"><lineStyleReference reference="BOTH01"/></xsl:template>
            </xsl:transform>
            """;

        var facts = XsltRuleReader.Read("R", source, Vocabulary, new HashSet<string> { "Thing" });

        Assert.Equal(["other.xsl"], facts.Includes);
        var a = Assert.Single(facts.Facts, f => f.Value == "SYMA01");
        Assert.Equal("Thing", a.Owner);
        Assert.Contains(a.Conditions, c => c is { Kind: RuleConditionKind.Primitive, Value: "Point" });
        Assert.Contains(a.Conditions, c => c is { Kind: RuleConditionKind.Attribute, Subject: "shape", Value: "2" });
        Assert.Contains(a.Conditions, c => c is { Kind: RuleConditionKind.Expression, Text: "colour = 3" });
        Assert.Equal(RuleConditionKind.Otherwise, Assert.Single(facts.Facts, f => f.Value == "SYMB01").Conditions[^1].Kind);
        Assert.Contains(facts.Facts, f => f is { Kind: RuleFactKind.ViewingGroup, Value: "27020", Owner: "Thing" });

        // "Other" isn't a feature type, so its half of the match is unowned.
        Assert.Contains(facts.Facts, f => f is { Kind: RuleFactKind.ColorFill, Value: "DEPVS", Owner: "Thing" });
        Assert.Contains(facts.Facts, f => f is { Kind: RuleFactKind.ColorFill, Value: "DEPVS", Owner: null });

        Assert.Equal(new TemplateCall("Thing", "helper", [a.Conditions[0], a.Conditions[1]]).Template, Assert.Single(facts.Calls).Template);
        Assert.Equal(ReferenceKind.LineStyle, Assert.Single(facts.NamedTemplates["helper"].Facts).ItemKind);
    }

    [Fact]
    public void Xslt_that_is_not_well_formed_reads_as_empty()
    {
        var facts = XsltRuleReader.Read("R", "<xsl:transform", Vocabulary, null);

        Assert.Empty(facts.Facts);
    }
}
