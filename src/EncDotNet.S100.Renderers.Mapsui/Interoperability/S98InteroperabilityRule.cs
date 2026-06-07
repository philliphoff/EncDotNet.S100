using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// One S-98 inter-product rule: a guarded transformation of the
/// sorted layer stack. PR-L2 ships five rules (see
/// <see cref="S98DefaultRules"/>); future PRs may load additional
/// rules from a parsed Interoperability Catalogue.
/// </summary>
/// <remarks>
/// <para>
/// A rule is a pair of pure delegates: <see cref="Condition"/>
/// decides whether the rule applies given the current set of loaded
/// datasets, and <see cref="Effect"/> transforms the sorted stack
/// when <see cref="Condition"/> returns <c>true</c>. Rules execute
/// in the order they appear in
/// <see cref="IInteroperabilityAuthority.ApplyRules"/>'s rule
/// collection (declaration order); each rule's output is the next
/// rule's input.
/// </para>
/// <para>
/// Rules are pure functions of <c>(stack, context)</c> — they must
/// not mutate the input list, the layers it references, or any
/// shared state. The default implementations in
/// <see cref="S98DefaultRules"/> follow this contract.
/// </para>
/// </remarks>
/// <param name="RuleId">
/// Stable identifier, e.g. <c>"R-101-102-B"</c>. Used in diagnostics
/// and tests; must be unique within a rule collection.
/// </param>
/// <param name="SpecCitation">
/// Free-text citation of the spec clause(s) this rule derives from,
/// e.g. <c>"S-98 Ed.2.0.0 Annex A §8.4.1 + Part B §B-3.1.2"</c>.
/// </param>
/// <param name="Condition">
/// Predicate over the rule context; <c>true</c> means the rule's
/// <see cref="Effect"/> should be applied. Must be pure.
/// </param>
/// <param name="Effect">
/// Transforms the sorted layer stack. Receives the (possibly
/// already-transformed) stack and the rule context; must return a
/// new list — input must not be mutated.
/// </param>
public sealed record S98InteroperabilityRule(
    string RuleId,
    string SpecCitation,
    Func<S98RuleContext, bool> Condition,
    Func<IReadOnlyList<LayerStackEntry>, S98RuleContext, IReadOnlyList<LayerStackEntry>> Effect);
