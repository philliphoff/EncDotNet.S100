# Exchange-set fixtures

This folder contains synthetic CATALOG.XML fixtures used by
`ExchangeSetServiceLoaderTests` to exercise the viewer's
exchange-set loader (`EncDotNet.S100.Viewer.Services.ExchangeSetService`).

The fixtures are deliberately tiny and point at non-existent
dataset files because the tests inject a no-op
`IDatasetLoaderService` and so never open the referenced data.

| Fixture | Purpose |
|---|---|
| `Synthetic-Mixed/` | Three entries: two supported (S-101, S-102) and one unsupported (S-999). Drives the partial-failure status banner and the header-registration path. |
| `Synthetic-AllUnsupported/` | Two unsupported entries. Verifies that the loader disposes the set immediately and unregisters its header when nothing is dispatched. |
| `Synthetic-Renderable/` | Two supported, **shipped** GML datasets (S-124 + S-125, copied from `tests/datasets/S124` and `tests/datasets/S125`) plus one unsupported entry. Unlike the other fixtures, the referenced dataset files exist, so `RenderExchangeSetCommandTests` (in `EncDotNet.S100.Cli.Tests`) can drive `s100 render` compositing of a whole exchange set / directory / `.zip` (issue #407). |

For real-world catalogues see `tests/datasets/S101/CATALOG.XML`
(and the matching `tests/datasets/S101.zip`) which is consumed by the
existing `ExchangeCatalogueReader` / `FileSystemExchangeSetTests` /
`ZipExchangeSetTests` suites.
