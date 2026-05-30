# Non-GML Validation — Design Note

> Status: **Design / Research** — no production code in this PR. This
> document is the contract that the per-spec implementation PRs (one
> per non-GML product) will build against. Open questions are
> collected in §10 and a recommended PR sequence is in §11.

## 0. Scope of this note

The validation framework in `src/EncDotNet.S100.Core/Validation/`
(`IValidationRule<TModel>`, `ValidationRuleSet<TModel>`,
`ValidationFinding`, `ValidationReport`, `ValidationContext`,
`ValidationRuleBuilder`) is in production today, but every shipped
rule pack targets a **GML-encoded** product:

| Pack                                 | Input model (TModel)                |
| ------------------------------------ | ----------------------------------- |
| `S122MarineProtectedAreaRules`       | `S122MarineProtectedAreasDataset`   |
| `S124NavigationalWarningRules`       | `S124NavigationalWarning`           |
| `S125AtonRules`                      | `S125AtonDataset`                   |
| `S127MarineServicesRules`            | `S127MarineServicesDataset`         |
| `S128CatalogueRules`                 | `S128CatalogueDataset`              |
| `S129UkcRules`                       | `S129UkcDataset`                    |
| `S131HarbourInfrastructureRules`     | `S131HarbourInfrastructureDataset`  |
| `S201AtonInformationRules`           | `S201AtonInformationDataset`        |
| `S411SeaIceRules`                    | `S411SeaIceDataset`                 |
| `S421RoutePlanRules`                 | `S421RoutePlan`                     |

All ten consume a strongly-typed `DataModel` projection produced
from the parsed `S{nnn}Dataset` via `S{Type}.From(raw, out var
diagnostics)`. The pattern works because the typed projection
already existed for every GML product (it shipped in PRs #69–77
before validation was layered on).

This note covers the remaining products, none of which fit that
mould:

- **Vector**:
  - **S-101** ENC — ISO 8211 record-encoded vector dataset.
  - **S-57** legacy ENC — ISO 8211 record-encoded vector dataset,
    translated to S-101 in-memory in this codebase.
- **Coverage** (HDF5 grids):
  - **S-102** Bathymetric Surface.
  - **S-104** Water Level Information for Surface Navigation.
  - **S-111** Surface Currents.

Out of scope:

- Writing any rule, rule pack, framework code, reader change,
  test, viewer wiring, or MCP surface — this is a design note.
- Tier-3 cross-dataset rules (e.g. S-104 vertical datum must
  match the sibling S-101 chart) — same posture as the GML packs;
  deferred and noted in §10.
- Performance benchmarking.

---

## 1. Background & motivation

### 1.1 What the GML pattern gives rule authors

Every GML pack reads off the typed `DataModel` projection. That
projection bakes three things into one type:

1. **Spec vocabulary.** Properties are named after the spec's
   feature classes and attributes (`warning.NavareaCode`, not
   `warning.Attributes["NAVAREA"]`).
2. **Resolved references.** xlinks are followed at projection time
   so rules walk object graphs, not anchor strings.
3. **Coerced values.** Strings that the schema says are integers,
   doubles, datetimes, or enums are parsed once, with parse failures
   filed as `ProjectionDiagnostic` entries rather than bubbling out
   of rules.

A rule then looks like:

```csharp
public static IValidationRule<S124NavigationalWarning> MinimumPartCount { get; } =
    ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("S124-R-1.1")
        .WithDescription("A navigational warning must contain at least one NavwarnPart.")
        .WithSeverity(ValidationSeverity.Error)
        .Yield((warning, _) => warning.Parts.Length > 0
            ? Array.Empty<ValidationFinding>()
            : new[] { /* finding */ });
```

The rule asks a spec-shaped question against typed model state and
emits findings carrying the rule id, severity, message, and optional
spatial / dataset / feature identity. It is short, testable, and
traceable to a spec clause.

Integration is a one-liner in the dataset processor (caching
included; see `S124DatasetProcessor.Validate()`):

```csharp
public override ValidationReport? Validate()
{
    if (!_validationCached)
    {
        _validationReport = ValidationRunner.Run(
            _dataset,
            static raw => S124NavigationalWarning.From(raw, out _),
            S124NavigationalWarningRules.Default);
        _validationCached = true;
    }
    return _validationReport;
}
```

### 1.2 Why the non-GML products are different

The non-GML readers diverge from this pattern in two distinct ways:

**Coverage readers (S-102 / S-104 / S-111) already produce a
typed dataset, but it is structural rather than projected.**
`S102DatasetReader.Read` walks the HDF5 file and returns
`S102Dataset { HorizontalCRS, Epoch, GeographicIdentifier,
IssueDate, Metadata, Coverages: BathymetryCoverage[] }`, with each
coverage carrying its own `Values: BathymetryValue[]` flat array.
That dataset *is* the spec-shaped view. The S-104 and S-111
equivalents follow the same pattern. The reader, however, throws
`S100DatasetSchemaException` /
`S100DatasetNotSupportedException` when the file is structurally
broken or carries an unsupported data-coding format — so most
"is the dataset structurally well-formed?" questions are answered
before `Validate()` could ever run. The interesting validation
surface is therefore (a) reader-throw conditions reported *as
findings* and (b) semantic / plausibility rules over the typed
dataset.

**Vector readers (S-101) emit an in-memory ISO 8211 graph, not a
projected model.** `S101DocumentReader.ReadFromStream` returns an
`S101Document` of `S101FeatureRecord` / `S101InformationRecord` /
`S101*Record` spatial records. Attributes are flat
`S101Attribute(NumericCode, Index, Value)` tuples — the numeric
codes are resolved through the bundled feature catalogue at
portrayal time, not at parse time, and there is no typed feature
hierarchy in the document. A rule that wants to ask "every
`DepthArea` must have a `DRVAL1` ≤ `DRVAL2`" must first map
acronyms to numeric codes through the FC and then sift
`document.Features` by `FeatureTypeCode`. That is more
plumbing than any GML rule author has to write today.

**S-57 piggybacks on S-101.** `S57DatasetProcessor` translates the
parsed `S57Document` to an `S101Document` in-memory via
`S57ToS101Translator` and renders it through the S-101 pipeline.
The translation is lossy by design (S-57's object catalogue does
not cleanly map onto S-101), so any rule that wants to ask about
"pre-translation" properties of an S-57 file (e.g. cell extent
record completeness, attribute domains S-57-specific to the S-57
object catalogue) must run before the translation, against the
`EncDotNet.S57.S57Document`.

### 1.3 Why this is a design note, not a pile of PRs

The questions to settle before any non-GML rule pack is written
are not "which rules?" but "what does the rule's input model look
like?" Two axes need a decision:

- **Vector axis** (S-101 / S-57): how do rules express themselves
  against an ISO 8211 graph?
- **Coverage axis** (S-102 / S-104 / S-111): how do rules express
  themselves against an HDF5-derived typed dataset, given that the
  reader has already crashed on the structural failures that look
  most like rules?

Get those wrong and you either fork the framework (one rule shape
per product), or rules end up so verbose they are not written, or
they bypass the framework altogether. Get them right and each
follow-up PR is "write the rules, plug into the processor,
ship" — exactly the cadence the GML packs achieved.

---

## 2. Candidate rule categories per spec

This section is not exhaustive — it is enough to demonstrate that
the input-model choice in §3 can actually express the rules each
spec wants. Counts are post-decision targets for the **first**
rule pack per spec (small on purpose), not totals.

### 2.1 S-102 — Bathymetric Surface (target: 6–8 rules in v1)

| Category | Example | Severity |
|---|---|---|
| Schema-shape (post-projection) | Every `BathymetryCoverage` has a non-empty `Values` array whose length equals `NumPointsLatitudinal × NumPointsLongitudinal`. | Error |
| Spec-semantic — NODATA fill | The NODATA depth value, when present, is exactly `1_000_000f` (S-102 §10c.x). | Error |
| Spec-semantic — root attributes | `HorizontalCRS` is set; if set, it resolves to a known EPSG code; `IssueDate` parses as ISO 8601. | Warning |
| Plausibility — depth range | All non-NODATA depth values lie in `[-50, 12_000]` metres (continental shelves to deepest trenches; covers UTM-mistakenly-stored-as-WGS84 typos). | Warning |
| Plausibility — grid extent | `OriginLatitude` and `OriginLongitude` are within `[-90, 90]` / `[-180, 180]`; spacing × num points yields an extent that doesn't wrap. | Error |
| Compound layout | `BathymetryValue` is the field-order `(Depth, Uncertainty)` PureHDF expects (rule fires if the reader's compound type discovery surfaced a swap). | Error |
| Tile coherency | When multiple `BathymetryCoverage` tiles are present, their cell spacing is identical (S-102 §3 tiling). | Warning |
| Projection-diagnostic surrogate | `S102-PROJ-SCHEMA` — reader threw `S100DatasetSchemaException`; carry `GroupPath` + `AttributeOrDataset` + `SpecReference`. | Error |

### 2.2 S-104 — Water Level Information (target: 6–8 rules in v1)

| Category | Example | Severity |
|---|---|---|
| Schema-shape | Every `WaterLevelCoverage` time step has a `Values` array of length `NumPointsLatitudinal × NumPointsLongitudinal`. | Error |
| Schema-shape — DCF gate | `DataCodingFormat` is in the supported set `{2, 3}`; the reader already throws `S100DatasetNotSupportedException` for unsupported variants, but the rule documents intent. | Error |
| Temporal — monotonicity | `Coverages` ordered by `TimePoint` are strictly increasing. | Warning |
| Temporal — even cadence | Successive `TimePoint` deltas vary by no more than ±10% (catches a missing or duplicated time step). | Warning |
| Spec-semantic — method string | `MethodWaterLevelProduct` is set when `Coverages.Count > 1` (model-driven datasets must describe the model). | Warning |
| Plausibility — range | All non-NODATA water level values lie in `[-15, 15]` metres. | Warning |
| Plausibility — extent | Same lat/lon range check as S-102. | Error |
| Projection-diagnostic surrogate | `S104-PROJ-SCHEMA`, `S104-PROJ-UNSUPPORTED`. | Error |

### 2.3 S-111 — Surface Currents (target: 6–8 rules in v1)

| Category | Example | Severity |
|---|---|---|
| Schema-shape | `Values` length matches `NumPointsLatitudinal × NumPointsLongitudinal` per coverage. | Error |
| Temporal | Monotonicity + cadence — same shape as S-104. | Warning |
| Spec-semantic — surface depth | `SurfaceCurrentDepth`, when present, lies in `[0, 1500]` metres (S-111 PS §). | Warning |
| Spec-semantic — type | `TypeOfCurrentData`, when present, is in the S-111 enumerated set. | Warning |
| Plausibility — speed | All non-NODATA current speeds lie in `[0, 15]` m/s. | Warning |
| Plausibility — direction | All non-NODATA directions lie in `[0, 360)` degrees. | Error |
| Plausibility — extent | Lat/lon range. | Error |
| Projection-diagnostic surrogate | `S111-PROJ-SCHEMA`, `S111-PROJ-UNSUPPORTED`. | Error |

### 2.4 S-101 — Electronic Navigational Chart (target: 8–10 rules in v1)

| Category | Example | Severity |
|---|---|---|
| Feature Catalogue conformance | Every `FeatureTypeCode` referenced by `Features` resolves through `FeatureTypeCatalogue` to an acronym defined in the bundled FC. | Error |
| Attribute Catalogue conformance | Every `S101Attribute.NumericCode` resolves through `AttributeTypeCatalogue` and is a valid attribute for its host feature per the FC. | Error |
| FOID uniqueness | `(ProducingAgency, FeatureIdentificationNumber, FeatureIdentificationSubdivision)` tuples are unique across `Features`. | Error |
| Spatial association completeness | Every `S101SpatialAssociation` references an `RCNM`/`RCID` that exists in the document's `Points`/`MultiPoints`/`CurveSegments`/`CompositeCurves`/`Surfaces` dictionaries. | Error |
| Ring closure | For every `S101SurfaceRecord`, the curves walked exterior-then-interior form closed rings (first point ≡ last point). | Error |
| Curve continuity | Adjacent curve segments inside a `CompositeCurve` share end points (no dangling endpoints). | Warning |
| Attribute domain conformance | Enumerated attributes carry values declared in the FC enum domain. | Warning |
| Plausibility — coordinates | All resolved (Y, X) coordinates lie in WGS-84 lat/lon range after applying `CoordinateMultiplicationFactor{X,Y}`. | Error |
| Information association referential | Every `S101InformationAssociation` resolves into `InformationTypes`. | Error |
| Projection-diagnostic surrogate | `S101-PROJ-PARSE` — `S101DocumentReader` reported a non-fatal parse warning (today's reader still throws on hard parse failure; rule placeholder until reader emits warnings). | Warning |

### 2.5 S-57 — legacy ENC (target: 3–4 rules in v1, deferred)

| Category | Example | Severity |
|---|---|---|
| Pre-translation — dataset identity | DSID record present and DSPM scale denominator non-zero. | Error |
| Pre-translation — coverage record | M_COVR / M_NSYS feature present (S-57 §4.6.1 meta object coverage). | Warning |
| Post-translation | Reuse the S-101 pack against the translated `S101Document` (everything that survives the translator). | inherited |

Most S-57 quality is best assessed *after* translation (because
the translator already filters the obviously-broken). The
S-57-specific pre-translation pack captures what gets lost in
translation.

---

## 3. The input-model decision (the central choice)

This is the section whose conclusions every subsequent PR depends on.

### 3.1 Vector — S-101

Four candidate input models:

**(a) Raw `S101Document`.**
Rules dive straight into `document.Features`,
`document.FeatureTypeCatalogue`, and `S101Attribute(NumericCode,
Index, Value)`. No new surface.
- ✅ Zero up-front work; rules ship today.
- ❌ Every rule re-implements `acronym → code` mapping, attribute
  lookup, complex-attribute marker-row unwrapping. Rule code
  drowns its intent in plumbing.
- ❌ No compile-time guarantee that a rule even references a
  spec-defined feature; typos surface only when the rule fails to
  match anything.

**(b) Thin spec-aligned façade.**
Layer an `S101FeatureView` (per feature) and an
`S101DocumentView` (per document) over `S101Document` that resolves
numeric codes through the bundled FC once and exposes:

```csharp
public sealed class S101FeatureView
{
    public string FeatureTypeAcronym { get; }   // e.g. "DepthArea"
    public Foid Foid { get; }                   // (agency, fidn, fids)
    public IReadOnlyList<S101AttributeView> Attributes { get; }
    public IReadOnlyList<S101AttributeView> ComplexAttributes(string acronym);
    public string? GetSimple(string acronym);
    public IEnumerable<SpatialAssociationView> SpatialAssociations { get; }
    public IEnumerable<FeatureAssociationView> FeatureAssociations { get; }
}

public sealed class S101DocumentView
{
    public S101Document Raw { get; }
    public IReadOnlyList<S101FeatureView> Features { get; }
    public IEnumerable<S101FeatureView> OfType(string acronym);
    public bool TryGetSpatial(S101SpatialAssociation a, out SpatialRecord r);
    public IReadOnlyList<S101DiagnosticView> Diagnostics { get; }  // parse warnings if any
}
```

Rules then read:

```csharp
foreach (var area in doc.OfType("DepthArea"))
{
    var lo = ParseDouble(area.GetSimple("DRVAL1"));
    var hi = ParseDouble(area.GetSimple("DRVAL2"));
    if (lo is not null && hi is not null && lo > hi)
        yield return new ValidationFinding { /* ... */ };
}
```

- ✅ Spec-vocabulary rule code.
- ✅ Cheap: the FC is already parsed for portrayal.
- ✅ Doesn't preclude (d) later — the façade is a strict superset of
  what (d) would offer for the common cases.
- ⚠️ Attribute *typing* is still rule-side (string `"5.0"` →
  `double 5.0`); a small `ParseDouble`/`ParseInt` helper bundled
  with the façade keeps that one-liner.
- ❌ Spatial geometry (resolved (lat, lon) tuples) is not exposed
  natively — rules wanting coordinates walk the document graph
  themselves. Acceptable for v1; promote to typed `Point` /
  `Curve` / `Surface` views in v-next.

**(c) `ILuaDataProvider`.**
Reuse the existing portrayal-side data provider
(`S101LuaDataProvider`).
- ✅ Already speaks the spec's vocabulary.
- ❌ Couples validation to portrayal infrastructure — a Lua engine
  must be instantiated to validate. Validation must run for
  consumers who never load a portrayal catalogue (CI lint,
  catalog tooling).
- ❌ The provider's API is shaped for Lua rule execution (one
  feature at a time, asks scoped at "current feature"); collection
  queries that validation wants (FOID uniqueness across all
  features) don't fit.

**(d) Full typed `DataModel` projection.**
Mirror the GML pattern: `S101FeatureCatalogueModel.From(document,
out diagnostics)` materialising `IReadOnlyList<DepthArea>`,
`IReadOnlyList<Sounding>`, etc.
- ✅ Maximum ergonomics — identical to GML rule code.
- ❌ Enormous up-front investment. S-101's catalogue declares
  dozens of feature classes, each with bespoke attribute sets, and
  the typed model has to be regenerated when the FC changes. This
  is a year-long projection effort the codebase has explicitly not
  funded (#69–77 deliberately excluded S-101).
- ❌ Premature: nothing in the v1 rule list (§2.4) needs typed
  per-class properties; everything wants
  "find me features of type X with attribute Y satisfying Z".
- ⚠️ When demand actually arrives — e.g. the FC-driven typed
  model lands for portrayal reasons — switching from (b) to (d) is
  mechanical because (b) hides the raw graph behind spec
  vocabulary already.

**Recommendation: (b).** Ship the façade in the S-101 PR
(V-4 in §11). Keep the door open for (d) — never expose raw
`S101FeatureRecord` through the façade in a way that would force
rules to break when the façade upgrades to typed feature classes.

### 3.2 Coverage — S-102 / S-104 / S-111

Three candidate input models:

**(a) Raw `IHdf5File` / `IHdf5Group`.**
Rules walk the HDF5 graph directly.
- ✅ Most natural for purely structural rules ("/BathymetryCoverage
  must exist").
- ❌ Skips the typed `S102Dataset` reader entirely, duplicating
  every traversal already performed at parse time. Rules end up as
  thin re-implementations of the reader.
- ❌ Sets a precedent that validation re-reads the file; for an
  S-102 tile with 4M+ values that's catastrophic.

**(b) `ICoverageSource`.**
Stay at the abstraction the renderers consume.
- ✅ Already exists.
- ❌ `ICoverageSource` is shaped for rendering — it exposes
  iterate-and-paint primitives, not root attribute audits or
  per-cell access. Most v1 rule categories (§2) don't fit.

**(c) Existing typed dataset (`S102Dataset` / `S104Dataset` /
`S111Dataset`).**
Rules read off the same model the renderer's `S102CoverageSource`
wraps.
- ✅ The dataset is already spec-shaped: root attributes are
  named properties, coverages are a typed list, each coverage has
  its `Values` flat array.
- ✅ Identical pattern to GML packs (`ValidationRuleSet<TypedModel>`).
- ✅ No re-read — the dataset is in memory once the processor's
  constructor returns.
- ⚠️ The typed dataset doesn't carry "where in the HDF5 file did
  this come from" metadata, so per-finding `RelatedFeatureId`
  reconstruction needs a small convention (covered in §4.3).
- ⚠️ Root-attribute presence rules need to be informational about
  the difference between "absent" and "present-and-empty". The
  current typed models use `string?` and `int?` to encode this,
  which is enough.

**Recommendation: (c).** Coverage rule packs land as
`ValidationRuleSet<S102Dataset>` / `ValidationRuleSet<S104Dataset>`
/ `ValidationRuleSet<S111Dataset>`. No new abstraction.

### 3.3 S-57

S-57's lifecycle in this codebase is **parse → translate to
`S101Document` → render**. Validation follows the same lifecycle:

- A small `S57PreTranslationRules.Default :
  ValidationRuleSet<S57Document>` checks the things that don't
  survive translation (§2.5 row 1–2). Implementation lives in
  `src/EncDotNet.S100.Datasets.S57/Validation/`.
- The post-translation `S101Document` is validated by
  `S101DatasetRules.Default` from V-4. `S57DatasetProcessor.Validate()`
  concatenates the two reports.
- No new façade. No new projection.

This keeps S-57 from becoming a third axis of validation work.
It also means S-57 validation is **gated on V-4** — the S-101 pack
must land first.

---

## 4. Framework constraints

The `IValidationRule<TModel>` surface was designed around GML
packs; this section confirms whether the same surface accommodates
non-GML packs or whether a v1 framework change is needed.

### 4.1 Sync `Evaluate`

The interface returns `IEnumerable<ValidationFinding>` synchronously.

- **Vector**: an S-101 document is wholly in memory; no I/O.
  Synchronous is correct.
- **Coverage**: by the time `Validate()` is called the typed dataset
  is already in memory (the processor's constructor read the HDF5
  file). `Values` is a flat array. Per-cell scans are CPU-bound;
  for a 4M-cell tile a "every NODATA must be exactly 1_000_000f"
  rule is one linear pass. No I/O. Synchronous is correct.

**No change needed.** Note that the runner already catches
exceptions and synthesises an `Error` finding, so a rule that
accidentally throws on a large dataset doesn't abort the report.

### 4.2 Tier-3 cross-dataset rules

Out of scope per the kickoff (consistent with the GML packs).
`ValidationContext.Services` remains the hatch when it's added;
nothing about the non-GML packs forces a change to the framework
hatch.

### 4.3 Per-finding payload conventions

Every shipped pack uses `RelatedFeatureId` differently. The
non-GML packs need conventions that downstream consumers (the
viewer's Validation tab, MCP) can reason about uniformly.

**S-101 / S-57 (post-translation):**

```
RelatedFeatureId = "{producingAgency}:{FIDN}.{FIDS}"
```

Matches the `Foid` triple in `S101Document`. The S-57 dataset
identifier is the file name (`DatasetId`).

For spatial-record findings (e.g. ring closure of a surface):

```
RelatedFeatureId = "surf:{RCID}"   // RCNM-tagged
RelatedFeatureId = "curve:{RCID}"
RelatedFeatureId = "point:{RCID}"
```

The tagged prefix removes ambiguity (RCIDs are not unique across
record-name buckets in S-101).

**Coverage:**

```
RelatedFeatureId = "{groupPath}"                         // per-coverage finding
RelatedFeatureId = "{groupPath}[row,col]"                // per-cell finding
RelatedFeatureId = "{groupPath}#timePoint"               // per-time-step finding
```

Where `groupPath` is the HDF5 group path the typed coverage
originated from (`"/BathymetryCoverage/BathymetryCoverage.01"`,
`"/WaterLevel/WaterLevel.04"`, etc.). The typed coverage records
do not currently carry this — see §10 Q-cov-1.

**Bounding box:**

For coverage rules whose finding is "this whole coverage tile is
out of plausible range", attach the tile's lat/lon extent as a
`BoundingBox`. For per-cell findings, attach a `Point` at the
cell centre.

### 4.4 `ValidationFinding` shape adequacy

The existing record covers everything in §2. No new fields.
Specifically:

- No need for a `TimePoint` field on `ValidationFinding` — time-step
  findings carry the time-step in the `Message` and in
  `RelatedFeatureId`. Promoting it to a structured field would
  drag the GML packs into adding null `TimePoint` values to every
  warning, which is the wrong trade.
- No need for `RuleCategory` — rule ids already encode the
  category by convention (`S102-R-*` semantic, `S102-PROJ-*`
  projection surrogate).

---

## 5. Projection-diagnostic equivalents

GML packs document a clean separation: projection-time failures
(`ProjectionDiagnostic` from `S{X}Dataset.From`) are surfaced
through the diagnostics out-parameter and are deliberately **not
duplicated as rules**. Non-GML products need an equivalent
discipline.

### 5.1 Coverage — wrap reader exceptions as findings

The HDF5 readers throw `S100DatasetSchemaException` and
`S100DatasetNotSupportedException` when the file is structurally
broken. These exceptions carry every datum a projection diagnostic
would (`Product`, `File`, `GroupPath`, `AttributeOrDataset`,
`SpecReference`).

The processor's `Validate()` override therefore wraps the reader
call:

```csharp
public override ValidationReport? Validate()
{
    if (!_validationCached)
    {
        try
        {
            _validationReport = S102DatasetRules.Default.Run(_dataset);
        }
        catch (S100DatasetSchemaException ex)
        {
            // Shouldn't normally fire — _dataset already read at ctor
            // time. Defensive only.
            _validationReport = ProjectionSurrogate(ex);
        }
        _validationCached = true;
    }
    return _validationReport;
}
```

But the realistic failure case is **the constructor itself** —
the HDF5 read throws and the processor never gets constructed.
The processor's caller (the viewer's dataset loader) catches the
exception today and surfaces it as a load error. The
*validation* surrogate is therefore only useful when:

1. The reader is later changed to return diagnostics-with-partial-data
   (a recommended follow-up; tracked as §10 Q-cov-2), or
2. The dataset loader caches the exception against the source and
   exposes a "validation report including the load failure" view.

**Recommendation:** ship the wrapper in V-1 for forward
compatibility but document that the realistic path runs against an
already-loaded dataset.

### 5.2 Vector — S-101 parser warnings

`S101DocumentReader` currently throws on hard parse failure but
does not yet surface non-fatal warnings (the diagnostics
namespace exists for telemetry, not validation). Two stances:

- **Stance A (V-4 only):** ship `S101-PROJ-*` rule ids that
  document the intended diagnostics surface, leave the rule body
  empty until `S101DocumentReader` emits warnings. The empty
  rules cost one entry in the rule set and zero findings.
- **Stance B (V-4 + reader change):** change
  `S101DocumentReader` to return `(S101Document, IReadOnlyList<
  S101ParseDiagnostic>)` and surface the diagnostics as findings.

**Recommendation: Stance A.** Reader changes belong in their own
PR; the design note's job is to commit to the rule-id namespace
so a future Stance B doesn't break consumers.

### 5.3 Naming

All projection-diagnostic surrogate rules use the prefix
`S{nnn}-PROJ-` (matching the GML packs' implicit convention that
`-R-` denotes a normative rule). Specifically:

```
S102-PROJ-SCHEMA, S102-PROJ-UNSUPPORTED
S104-PROJ-SCHEMA, S104-PROJ-UNSUPPORTED
S111-PROJ-SCHEMA, S111-PROJ-UNSUPPORTED
S101-PROJ-PARSE
S57-PROJ-PARSE
```

The viewer's Validation tab is free to filter on the `-PROJ-`
prefix to render projection diagnostics separately from normative
findings; this is a documentation convention, not a framework
change.

---

## 6. Per-spec v1 scope cuts

§2 sketched candidate categories. This section is what the **first
PR for each spec** actually ships.

### 6.1 S-102 v1 (V-1)

8 rules:

1. `S102-R-1.1` — Coverage `Values` length matches grid shape.
2. `S102-R-2.1` — NODATA fill, when present, is exactly `1_000_000f`.
3. `S102-R-3.1` — `HorizontalCRS`, when set, is a known EPSG code.
4. `S102-R-3.2` — `IssueDate`, when set, parses as ISO 8601.
5. `S102-R-4.1` — Coverage origin lat/lon within WGS-84 ranges.
6. `S102-R-4.2` — Coverage extent (`numPoints × spacing`) does not
   wrap the antimeridian or cross the pole.
7. `S102-R-5.1` — Depth values lie in `[-50, 12_000]` metres
   (plausibility; warning).
8. `S102-PROJ-SCHEMA` — defensive surrogate (§5.1).

Spec references in XML doc comments cite S-102 Edition 3.0.0 and
S-100 Part 10c §10.x where applicable.

### 6.2 S-104 v1 (V-2)

7 rules:

1. `S104-R-1.1` — Coverage `Values` length matches grid shape.
2. `S104-R-1.2` — `DataCodingFormat` ∈ `{2, 3}` (documents the
   supported set).
3. `S104-R-2.1` — Time points strictly increasing.
4. `S104-R-2.2` — Time-step cadence within ±10% (warning).
5. `S104-R-3.1` — `MethodWaterLevelProduct` set when
   `Coverages.Count > 1` (warning).
6. `S104-R-4.1` — Water level values in `[-15, 15]` metres
   (warning).
7. `S104-PROJ-SCHEMA`, `S104-PROJ-UNSUPPORTED`.

### 6.3 S-111 v1 (V-3)

7 rules:

1. `S111-R-1.1` — Coverage `Values` length matches grid shape.
2. `S111-R-2.1` — Time-step monotonicity + cadence (folded).
3. `S111-R-3.1` — `SurfaceCurrentDepth`, when present, in `[0, 1500]`
   metres (warning).
4. `S111-R-3.2` — `TypeOfCurrentData`, when present, in the S-111
   enumerated set.
5. `S111-R-4.1` — Speeds in `[0, 15]` m/s (warning).
6. `S111-R-4.2` — Directions in `[0, 360)` degrees.
7. `S111-PROJ-SCHEMA`, `S111-PROJ-UNSUPPORTED`.

### 6.4 S-101 v1 (V-4)

10 rules. Façade ships in the same PR.

1. `S101-R-1.1` — Every `FeatureTypeCode` resolves to an FC acronym.
2. `S101-R-1.2` — Every `S101Attribute.NumericCode` resolves and is
   valid for its host feature per the FC.
3. `S101-R-2.1` — FOID uniqueness across `Features`.
4. `S101-R-3.1` — Spatial associations resolve into the relevant
   record dictionary.
5. `S101-R-3.2` — Surface ring closure (exterior + interior).
6. `S101-R-3.3` — Composite curve continuity.
7. `S101-R-4.1` — Enumerated attribute values within FC domain.
8. `S101-R-5.1` — Resolved (lat, lon) within WGS-84 range.
9. `S101-R-5.2` — Information association referential integrity.
10. `S101-PROJ-PARSE` — placeholder per §5.2.

### 6.5 S-57 v1 (V-5, deferred)

3 pre-translation rules + delegation to the S-101 pack:

1. `S57-R-1.1` — DSID record present, DSPM scale denominator > 0.
2. `S57-R-1.2` — At least one M_COVR feature.
3. `S57-PROJ-PARSE`.

Plus `S101DatasetRules.Default` against the translated document,
findings rebadged with `RuleId = "S101-as-S57/" + originalRuleId`
so consumers can filter S-57 vs native S-101 findings.

---

## 7. Coverage-specific topics

### 7.1 Tile-aware iteration

S-102 may carry multiple `BathymetryCoverage` tiles (`s102-bathymetry`
skill §pitfalls). Rules iterate per tile and emit one finding per
offending tile (or per offending cell within a tile). `RelatedFeatureId`
follows §4.3 — `groupPath` distinguishes
`BathymetryCoverage.01` from `BathymetryCoverage.02`.

A per-dataset "all tiles share spacing" rule emits a single finding
referencing the dataset, not per tile.

### 7.2 Time-series groups (S-104 / S-111)

`WaterLevelCoverage` and `SurfaceCurrentCoverage` already model
each `Group_NNN` as one entry in the `Coverages` list with its
`TimePoint`. A per-time-step rule iterates `Coverages` and emits
one finding per offending step. An aggregate rule (monotonicity)
iterates the sequence once and emits at most one finding for the
first violation (later violations are usually cascade noise).

### 7.3 NODATA fill value

S-102 mandates `1_000_000f` exactly. Implementation note: the
typed `BathymetryValue { Depth, Uncertainty }` carries raw floats —
no normalisation. The NODATA rule walks the flat array, filters
on `Depth == 1_000_000f`, and emits a single dataset-level finding
when **none** is found and the dataset is sparse-by-extent (no
finding when every cell has a real depth).

The depth-range plausibility rule excludes `1_000_000f` from its
range check via the same equality.

S-104 / S-111 inherit the same NODATA convention via S-100 Part
10c; rules follow the same shape.

### 7.4 PureHDF compound layout

PureHDF maps HDF5 compound types into the C# struct by **field
order**, not field name. `BathymetryValue` (and the S-104 / S-111
equivalents) must declare fields in the same order as the file's
compound type. This is a reader-side correctness invariant; the
validation surrogate is a rule that compares the parsed first
non-NODATA `Depth` against the parsed `Uncertainty` plausibility
range (a swap reliably swaps the two range checks). The rule
fires "depth out of range AND uncertainty out of range" as a hint
to a layout regression. Lightweight, no new reader API.

### 7.5 Root-attribute completeness

Each coverage product has a small bag of root-level attributes
(`HorizontalCRS`, `Epoch`, `GeographicIdentifier`, `IssueDate`,
`Metadata`, plus product-specific extras). The S-100 Part 10c
table of "mandatory if present together" attributes drives a
**conditional** rule shape per product:

- `S102-R-3.x` family — covered above.
- `S104-R-3.x` family — `MethodWaterLevelProduct` conditional on
  `Coverages.Count > 1`.
- `S111-R-3.x` family — `TypeOfCurrentData` valid when present.

The rule shape is "if attribute A is set, attribute B must be set
too" / "if A is set, A must satisfy domain D". Easy to express
against the `string?` / `int?` typed dataset.

---

## 8. Vector-specific topics

### 8.1 Feature Catalogue conformance

The bundled S-101 FC is loaded via `FeatureCatalogueManager`
already (S-101 portrayal needs it). The S-101 pack takes a
`FeatureCatalogueManager` (or just the resolved
`FeatureCatalogueDecoder` for S-101) at façade-construction time
and uses it to:

- Resolve `FeatureTypeCode` → acronym (drives `S101-R-1.1`).
- Resolve `AttributeNumericCode` → acronym (drives `S101-R-1.2`).
- Check attribute validity per feature class (drives `S101-R-1.2`).
- Check enumerated attribute domains (drives `S101-R-4.1`).

**Implication for `Validate()`**: the S-101 processor's
`Validate()` override needs the same `FeatureCatalogueManager` it
already accepts in its constructor. No new dependencies; one
extra field plumbed into the façade constructor.

### 8.2 Spatial topology

`S101-R-3.x` rules walk the spatial-record dictionaries. The
façade exposes a `TryGetSpatial` helper so rules don't have to
remember which dictionary to look in. Ring closure walks the
ring's curves in order, resolving via the spatial registry; a
ring of fewer than three distinct points is reported as
`S101-R-3.2` regardless of closure.

### 8.3 Attribute domain conformance

The FC's enumerated domains are already loaded for portrayal
purposes. The pack reuses them — no separate enum loader, no
spec table embedded in the rule pack.

### 8.4 FOID uniqueness

`S101-R-2.1` is a single grouping pass over `Features`. A
duplicated FOID triple emits one finding per duplicate (i.e. the
first occurrence is the anchor; every subsequent occurrence is
flagged with `RelatedFeatureId` pointing at the duplicate).

### 8.5 What validation packs are NOT for

PR #132 fixed a Lua-rule expectation mismatch — the portrayal
rule expected one attribute shape; the dataset emitted another.
A validation rule would **not** have caught this; the dataset was
spec-conformant, the portrayal catalogue was wrong. Validation
packs are about **dataset** correctness, not catalogue
correctness. Portrayal catalogue correctness is the catalogue
test suite's job, not this design's job.

---

## 9. Packaging & integration

### 9.1 File layout

Match the GML packs exactly:

```
src/EncDotNet.S100.Datasets.S101/Validation/
  S101DatasetView.cs          # façade (per §3.1)
  S101FeatureView.cs
  S101AttributeView.cs
  S101DatasetRules.cs         # public static Default { get; }
src/EncDotNet.S100.Datasets.S102/Validation/
  S102DatasetRules.cs
src/EncDotNet.S100.Datasets.S104/Validation/
  S104DatasetRules.cs
src/EncDotNet.S100.Datasets.S111/Validation/
  S111DatasetRules.cs
src/EncDotNet.S100.Datasets.S57/Validation/
  S57PreTranslationRules.cs
```

### 9.2 Public surface

Each per-spec library exposes exactly one new public type
(`Sxxx{Domain}Rules`) plus, for S-101, the façade types.
Everything else (helpers, parsing utilities) is `internal`.

### 9.3 Processor `Validate()` integration

Coverage processors mirror the GML pattern, swapping
`ValidationRunner.Run(raw, project, ruleSet)` for a direct
`ruleSet.Run(_dataset)`:

```csharp
// S102DatasetProcessor
public ValidationReport? Validate()
{
    if (!_validationCached)
    {
        _validationReport = S102DatasetRules.Default.Run(_dataset);
        _validationCached = true;
    }
    return _validationReport;
}
```

S-101:

```csharp
// S101DatasetProcessor
public ValidationReport? Validate()
{
    if (!_validationCached)
    {
        var view = S101DatasetView.From(
            _dataset.Document,
            _featureCatalogueManager.GetDecoder("S-101"));
        _validationReport = S101DatasetRules.Default.Run(view);
        _validationCached = true;
    }
    return _validationReport;
}
```

S-57:

```csharp
// S57DatasetProcessor
public ValidationReport? Validate()
{
    if (!_validationCached)
    {
        var pre  = S57PreTranslationRules.Default.Run(_rawS57Document);
        var view = S101DatasetView.From(_translatedDataset.Document, decoder);
        var post = S101DatasetRules.Default.Run(view);
        _validationReport = ConcatReports(pre, post, rebadgePrefix: "S101-as-S57/");
        _validationCached = true;
    }
    return _validationReport;
}
```

`ConcatReports` is a small helper that lives next to
`ValidationRunner` in `EncDotNet.S100.Datasets.Pipelines`.

### 9.4 Caching semantics

Same as the GML packs: lazy, single-shot, invalidated only on
processor disposal. Validation is a pure function of the parsed
dataset (no palette, no time-step, no viewport dependency).

### 9.5 Viewer integration

The viewer's Validation tab consumes `IDatasetProcessor.Validate()`
already (PR #115 wiring). Non-GML reports surface in the same tab
with zero viewer changes. The Validation tab's filter UI may
choose to render `-PROJ-` rule ids in a separate sub-section; that
is a viewer-side enhancement orthogonal to this design.

### 9.6 MCP

Out of scope. The existing MCP `validate` per-dataset surface, if
any, picks up the new packs automatically because it goes through
the same `IDatasetProcessor.Validate()` shim.

---

## 10. Open questions

Pinned here, intentionally not resolved by this design:

### Q-cov-1: Per-finding HDF5 group path

Coverage findings want `RelatedFeatureId =
"{groupPath}[row,col]"`, but the typed `S102Dataset`,
`S104Dataset`, `S111Dataset` don't currently carry the source
group path on each coverage. Three resolutions:

1. Add a `string GroupPath` to `BathymetryCoverage` /
   `WaterLevelCoverage` / `SurfaceCurrentCoverage`. Mechanical.
2. Index coverages by position (`Coverages[i]` →
   `RelatedFeatureId = $"coverage[{i}]"`). Loses the spec-shaped
   path.
3. Recompute the path from product convention
   (`$"/BathymetryCoverage/BathymetryCoverage.{i+1:D2}"`). Brittle —
   real datasets sometimes use `BathymetryCoverage.001` (three-digit).

**Recommendation in v1: option 1.** It's the only one that
survives spec variants and round-trips into MCP / debugging. Add
the field in the V-1 PR alongside the rule pack.

### Q-cov-2: Reader-as-projection-diagnostics

Today the HDF5 readers throw on structural failure. Should they
instead return `(S102Dataset?, IReadOnlyList<ProjectionDiagnostic>)`
so a partially-broken file can still produce *some* findings?

This is a follow-up worth considering once the v1 packs ship and
real datasets surface failure modes. Deferred. Not blocking.

### Q-vector-1: Façade shape — per-feature vs whole-document

Captured in §3.1's recommendation. The whole-document façade
exposing typed feature collections (`view.DepthAreas`, etc.) was
considered and rejected for v1 because completeness becomes a
maintenance treadmill — the FC has dozens of feature classes and
the typed surface has to track every one. The per-feature view
plus `OfType(acronym)` filter is open-ended and always works.
Promote to typed collections later if a measurable ergonomics
gap appears.

### Q-vector-2: S-101 parser warnings

§5.2's Stance A vs Stance B. Default to Stance A; revisit when
the reader has a non-trivial diagnostics catalogue.

### Q-lint-mode

"Lint mode" vs "strict mode" — same rule emitting different
severities depending on the consumer. Not needed for any v1
consumer. Deferred. If added, lives on `ValidationContext`, not
on rules.

### Q-mcp-validate-all

Whether the MCP server should expose `validate_all` as a
batch-validates-everything tool. Tier-3 cross-dataset hook;
deferred per the kickoff.

### Q-perf-coverage

Per-cell scans on a 4M-cell S-102 tile are linear-time but
not free. v1 rules each do one pass; the rule set's worst case
is "(number of cell-touching rules) × (cells)". With 3–4
cell-touching rules per pack that's well under a second on a
typical tile. Measure on a real dataset before optimising;
specifically, see whether folding multiple range checks into a
single pass is worth a `IBatchValidationRule<TModel>` extension.
Not blocking.

### Q-bbox-cell

`ValidationFinding.Point` is a `GeoPosition`; the design uses
`Point` for per-cell findings (cell centre) and `BoundingBox` for
per-tile findings. Coverage cells are very small at typical S-102
resolutions and may render as a single pixel in the viewer. A
viewer-side enhancement may want to render per-cell findings as a
small circle marker rather than a point; that is a viewer
concern, not a framework change.

### Q-s57-rebadge

§9.3 sketches rebadging S-101 findings with an `"S101-as-S57/"`
prefix when produced from a translated S-57 document. Alternative:
keep the original rule id and add a `Source = "S101-as-S57"`
ambient context. The rebadge has the virtue of being visible in
the rule id alone; the ambient context preserves rule-id stability
for tooling that aggregates by rule id. Pin in V-5.

---

## 11. Sequencing — recommended PRs

Mirrors the dynamic-source D1/D2/D3 split: smallest landing PR
first, vector pack last because it's the largest body of work.

### V-1: S-102 rule pack + framework prep

- New: `S102DatasetRules.Default` with the 8 rules from §6.1.
- New: `BathymetryCoverage.GroupPath` field (Q-cov-1).
- New: `S102DatasetProcessor.Validate()` override.
- New: `ConcatReports` helper alongside `ValidationRunner`
  (used by V-5 too; landing here keeps V-5 a leaf PR).
- Tests: `tests/EncDotNet.S100.Pipelines.Tests/S102ValidationTests.cs`
  with synthetic `S102Dataset` fixtures (no real HDF5 file
  required for these rules).

Smallest semantic surface, no time axis, clearest one-to-one
mapping from the `s102-bathymetry` skill's "Review checklist"
items to rule ids.

### V-2: S-104 rule pack

- New: `S104DatasetRules.Default` with the 7 rules from §6.2.
- New: `WaterLevelCoverage.GroupPath` field.
- New: `S104DatasetProcessor.Validate()` override.
- Tests: per-PR fixture set.

Adds the time-axis rule patterns that V-3 reuses.

### V-3: S-111 rule pack

- New: `S111DatasetRules.Default` with the 7 rules from §6.3.
- New: `SurfaceCurrentCoverage.GroupPath` field.
- New: `S111DatasetProcessor.Validate()` override.
- Tests.

Largely template-of-V-2.

### V-4: S-101 façade + rule pack

- New: `S101DatasetView`, `S101FeatureView`, `S101AttributeView`
  in `src/EncDotNet.S100.Datasets.S101/Validation/`.
- New: `S101DatasetRules.Default` with the 10 rules from §6.4.
- New: `S101DatasetProcessor.Validate()` override.
- Tests: synthetic `S101Document` fixtures.

Largest PR. Façade design captured here is the contract; the
implementation PR may iterate on signatures.

### V-5: S-57 pre-translation pack + delegation

- New: `S57PreTranslationRules.Default` with the 3 rules from §6.5.
- New: `S57DatasetProcessor.Validate()` override.
- Resolves Q-s57-rebadge.
- Tests.

Gated on V-4.

### Out of this sequence

- Tier-3 cross-dataset rules (any product).
- Reader-as-diagnostics change (Q-cov-2).
- S-101 typed `DataModel` projection (the (d) option).
- MCP `validate_all`.
- Performance optimisation (`IBatchValidationRule<TModel>`).

Each is a future-tense follow-up with its own design.

---

## Appendix A — Cross-reference to skills

Every spec skill (`.github/skills/sXXX-*`) carries a "Review
checklist" that this design treats as the source of candidate
rules. Specifically:

- `s102-bathymetry` — §3 pitfalls (NODATA = 1_000_000f, tile
  spacing coherency, compound layout, root attribute pairing)
  populate §2.1 and §6.1.
- `s104-water-level` — DCF gating, time-step monotonicity,
  method-string completeness populate §2.2 and §6.2.
- `s111-surface-currents` — speed/direction plausibility, surface
  depth domain populate §2.3 and §6.3.
- `s101-enc` — FC conformance, FOID uniqueness, spatial
  association completeness populate §2.4 and §6.4.

Each implementation PR cites the relevant skill checklist item
in the rule's XML doc comment, and cites the relevant spec
clause in `RuleId` / `Description`.

---

## Appendix B — Why not extend the GML projection pattern to non-GML

A natural-sounding alternative is "do the same thing — build a
`From(raw, out diagnostics)` projection for every non-GML product
and validate the projection".

For coverage, that projection already exists — it IS the typed
dataset. Adding a second projection layer would be pure
ceremony.

For S-101, that projection is option (d) from §3.1, and the
reasons it doesn't fit v1 are captured there. The (b) façade
preserves the option of moving to (d) later without breaking any
rule that's been written against (b).

This appendix exists so the next person who asks "why didn't we
just do what the GML packs do?" has a written answer.
