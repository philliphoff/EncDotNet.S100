# Documentation

Conceptual guides, design notes, and per-product reference material for
**EncDotNet.S100** — a managed, cross-platform implementation of the
IHO [S-100](https://iho.int/en/s-100-edition-5-2-0) Universal
Hydrographic Data Model for .NET, together with a cross-platform
desktop viewer. Every supported product ships a reader, a portrayal
pipeline, and a normative validation rule pack.

## Supported products

| Standard | Subject | Encoding | Portrayal | Validation pack | Library |
|---|---|---|---|---|---|
| **S-101** | Electronic Navigational Charts | ISO 8211 | Lua (Part 9A) | ✅ | [Datasets.S101](../src/EncDotNet.S100.Datasets.S101/README.md) |
| **S-102** | Bathymetric Surfaces | HDF5 | Coverage (Lua) | ✅ | [Datasets.S102](../src/EncDotNet.S100.Datasets.S102/README.md) |
| **S-104** | Water Level Information | HDF5 | Coverage | ✅ | [Datasets.S104](../src/EncDotNet.S100.Datasets.S104/README.md) |
| **S-111** | Surface Currents | HDF5 | Coverage arrows | ✅ | [Datasets.S111](../src/EncDotNet.S100.Datasets.S111/README.md) |
| **S-122** | Marine Protected Areas | GML | XSLT | ✅ | [Datasets.S122](../src/EncDotNet.S100.Datasets.S122/README.md) |
| **S-124** | Navigational Warnings | GML | XSLT | ✅ | [Datasets.S124](../src/EncDotNet.S100.Datasets.S124/README.md) |
| **S-125** | Marine Aids to Navigation | GML | XSLT | ✅ | [Datasets.S125](../src/EncDotNet.S100.Datasets.S125/README.md) |
| **S-127** | Marine Resources & Services | GML | XSLT | ✅ | [Datasets.S127](../src/EncDotNet.S100.Datasets.S127/README.md) |
| **S-128** | Catalogue of Nautical Products | GML | XSLT | ✅ | [Datasets.S128](../src/EncDotNet.S100.Datasets.S128/README.md) |
| **S-129** | Under Keel Clearance Management | GML | XSLT | ✅ | [Datasets.S129](../src/EncDotNet.S100.Datasets.S129/README.md) |
| **S-131** | Marine Harbour Infrastructure | GML | Lua (Part 9A) | ✅ | [Datasets.S131](../src/EncDotNet.S100.Datasets.S131/README.md) |
| **S-201** | Aids to Navigation Information (IALA) | GML | XSLT | ✅ | [Datasets.S201](../src/EncDotNet.S100.Datasets.S201/README.md) |
| **S-411** | Sea Ice Information | GML | XSLT | ✅ | [Datasets.S411](../src/EncDotNet.S100.Datasets.S411/README.md) |
| **S-421** | Route Plans | GML | XSLT | ✅ | [Datasets.S421](../src/EncDotNet.S100.Datasets.S421/README.md) |
| **S-57** *(legacy)* | Electronic Navigational Charts (Ed 3.1) | ISO 8211 | via S-101 pipeline | ✅ (delegated) | [Datasets.S57](../src/EncDotNet.S100.Datasets.S57/README.md) |

## Guides

- [Typed data models](typed-data-models.md) — strongly-typed object-graph projections layered on top of the schema-agnostic feature bags exposed by each dataset reader.
- [Observability](observability.md) — span tree, metrics catalogue, and OpenTelemetry / Aspire recipes for inspecting pipeline activity.
- [MCP server](mcp-server.md) — Model Context Protocol surface (`list_datasets`, `describe_feature`, `sample_coverage`, `render_to_image`) used by AI agents to query loaded datasets.

## Design notes

The `design/` folder collects the contracts that shipped implementations
were built against. They cover rationale and open questions rather
than current-state behaviour (which lives in the per-library READMEs).

- [Dynamic feature sources](design/dynamic-feature-source.md) — push-driven point/track/area features (own-ship, AIS, route preview, sensor overlays) that sit alongside static datasets in the rendering surface.
- [Own-ship vessel symbology](design/own-ship-symbology.md) — true-scale hull + arrowhead + CCRP cross renderer that consumes the dynamic-feature-source abstraction.
- [AIS dynamic feature source](design/ais-source.md) — three-layer split (driver → `IAisMessageSource` → `AisDynamicFeatureSource`), aisstream.io WebSocket driver, and per-class AIS target rendering.
- [S-98 interoperability](design/s98-interoperability.md) — display-plane plumbing and inter-product rules that make multi-product paint stacks deterministic and spec-driven.
