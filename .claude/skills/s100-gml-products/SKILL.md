---
name: s100-gml-products
description: |
  Shared rules for every GML-encoded S-100 product in this repo (S-100
  Part 10b): S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201,
  S-411, S-412, S-421. Covers s100gml namespace tolerance, lat/lon axis
  order and the shared axis-order check, member/imember container
  shapes, xlink references, geometry-less features, the FeatureXML
  neutral form, XSLT portrayal via XslCompiledTransform, and the
  "upstream catalogue is byte-identical, adapt with an Adapter" rule.
  USE FOR: any change to a GML dataset reader, its FeatureXML source,
  its GmlPortrayalCatalogueBase subclass, or its bundled content/SXXX
  catalogue; adding a new GML product. Load it together with the
  product's own skill (s122-…, s124-…, etc.), which carries only that
  product's specifics. DO NOT USE FOR: ISO 8211 products (s101-enc,
  s57-enc), HDF5 coverages (s102/s104/s111), or the Lua engine itself
  (s100-framework).
---

# GML-encoded S-100 products (Part 10b)

The per-product skills assume these rules and only record where a
product deviates. If a rule here and a product skill disagree, the
product skill wins for that product.

## Shared code — use it, don't fork it

| Concern | Type (namespace `EncDotNet.S100.Features` unless noted) |
|---|---|
| `gml:pos` / `posList` / curve / ring parsing | `GmlCoordinateParser` (Core/Gml) |
| Axis-order decision per dataset | `GmlCoordinateParser.AssessAxisOrder` → `AxisOrderCheck` |
| Dataset envelope | `GmlCoordinateParser.ReadEnvelope` |
| Declared edition / identification | `GmlDatasetIdentification` |
| `DatasetMetadata` from features | `GmlDatasetMetadata.Create` |
| Feature / info-type contracts | `IS100Feature`, `IS100InformationType`, `IS100ComplexAttribute`, `GmlReference` |
| FeatureXML for XSLT rules | `GmlFeatureXmlSource<TFeature>` (`EncDotNet.S100.Pipelines.Vector`) |
| Portrayal catalogue | subclass `GmlPortrayalCatalogueBase` (`EncDotNet.S100.Portrayals`) |
| XSLT execution | `XsltRuleExecutor`, `IXsltRuleSource` (Core/Pipelines/Vector) |
| Drawing-instruction parsing | `DrawingInstructionParser` (Core/Pipelines/Vector) — shared with S-101/S-131 |

A new GML product should be a reader + model + a thin
`GmlPortrayalCatalogueBase` subclass. If you find yourself writing
coordinate parsing, namespace sniffing or a FeatureXML writer inside a
dataset project, move it into Core instead.

## Encoding rules

1. **Namespaces.** Tolerate every `s100gml` variant seen in published
   samples — `http://www.iho.int/s100gml/5.0` (canonical),
   `http://www.iho.int/s100gml/1.0`, and
   `http://www.iho.int/S100/profile/s100gml/1.0`. Never hard-code one.
   GML itself is `GmlNamespaces.Gml` (3.2).
2. **Axis order.** `EPSG:4326` positions are **lat lon** (Part 10b
   §6.2). Decide the order **once per dataset** with
   `AssessAxisOrder` (anchored on `gml:boundedBy/gml:Envelope`) and
   pass it to every parse call, so one dataset is never split between
   orders. Attach the returned `AxisOrderAssessment` to the dataset
   model so the viewer can surface it.
3. **Separators.** `posList` tokens may be whitespace- or
   comma-separated (producer bug seen in S-122/S-128 data); the shared
   parser handles both — don't regress it.
4. **Containers.** Support the XSD-canonical `<member>` / `<imember>`
   wrappers *and* the inline `<members>` / `<imembers>` containers.
   Iterate descendants of the dataset root and match by type code.
   (Known gap: the S-127 reader handles only `<member>` / `<imember>`.)
5. **Prefer namespace-driven feature recognition** — any member child
   in the application-schema namespace is a feature, and information
   types are discriminated against the FC's small, closed info-type
   set. S-127, S-128 and S-201 work this way. S-122, S-124, S-125,
   S-129 and S-411 still use hard-coded `FeatureTypeCodes` sets: don't
   copy that into a new reader, and keep those sets in step with the FC
   when you touch them.
6. **References.** Preserve every `xlink:href` with its role name
   (and `arcrole`/`xlink:title` where present) verbatim. Keep feature
   references and information references distinguishable all the way
   into FeatureXML so XSLT rules can resolve them.
7. **Geometry-less features are normal** (aggregations, associations,
   authorities, cancellations, container objects). Never synthesise
   geometry; renderers and pick must tolerate its absence.
8. **Time validity is UTC** (`fixedDateRange`, `periodicDateRange`,
   etc.). Do not coerce to local time at the source; convert only for
   display.
9. **Don't recompute producer values** — keep the reader FC-faithful;
   typed convenience views live in separate model types, not in the
   parser.

## Portrayal

- XSLT portrayal runs on .NET's `XslCompiledTransform` (XSLT 1.0 +
  `msxsl`). Keep rules within what it supports.
- FeatureXML must contain both `<Dataset><Features>` and, when the
  product has information types, `<Dataset><InformationTypes>` —
  upstream rules commonly walk `/Dataset/InformationTypes/*[@id=$ref]`.
- **Bundled catalogues under
  `src/EncDotNet.S100.Specifications/content/<SXXX>/` are
  byte-identical to upstream.** Never edit them in place. When the
  upstream rules need adapting to this codebase's display-list dialect,
  add a wrapper XSLT as an embedded resource in the **dataset project**
  (`src/EncDotNet.S100.Datasets.S411/Adapter/main.xsl`,
  `src/EncDotNet.S100.Datasets.S128/Adapter/outlineOnly.xsl`) and note it
  in the product skill (few `content/<SXXX>/` folders have a README).
- **Listed values** may arrive encoded as labels rather than codes. When
  the upstream XSLT matches numeric codes, normalise in the FeatureXML
  source (S-201's `S201ListedValueIndex` is the reference).
- Many upstream PCs ship a **Day-only** colour profile; Dusk/Night is
  then a no-op. Don't edit upstream `colorProfile.xml`; synthesise a
  palette in code if one is needed and document it.
- Exceptions: **S-131** portrays through Lua (Part 9A) via the shared
  Lua host API, so its catalogue implements `IVectorPortrayalCatalogue`
  directly and its processor skips `GmlDatasetProcessorBase`; **S-411**
  passes its original GML through to XSLT instead of using
  `GmlFeatureXmlSource`; **S-125** adds a supplemental S-101 Lua stage
  alongside its status-only XSLT catalogue.
- XSLT `xsl:include`s resolve through the catalogue base:
  `GmlPortrayalCatalogueBase` serves pre-fetched rule files
  case-insensitively (upstream includes often mismatch case), and
  `FetchRuleFallbackXmlResolver` fetches includes not listed in the
  catalogue.

## Tests and docs

- Every new public API or bug fix gets an xunit test in
  `tests/EncDotNet.S100.Datasets.SXXX.Tests/`. Synthetic GML fixtures
  live under `tests/datasets/SXXX/`. Check licensing before copying any
  third-party sample in; default is don't.
- Cite the product spec section (and Part 10b § for encoding concerns)
  in XML doc comments on spec-derived constants, element names and
  feature codes, and in PR descriptions.
