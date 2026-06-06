# EncDotNet.S100.Portrayals

Parser for S-100 Portrayal Catalogues (S-100 Part 9).

## Overview

This library reads S-100 Portrayal Catalogue XML files and provides access to the symbols, styles, color profiles, and rules used to render S-100 data. Key types include:

- **`PortrayalCatalogue`** — the parsed model containing symbols, line styles, area fills, color profiles, rule files, viewing groups, and display modes.
- **`PortrayalCatalogueProvider`** — loads a catalogue and its referenced assets from an `IAssetSource`.
- **`PortrayalCatalogueReader`** — XML parser for portrayal catalogue files.
- **`PortrayalCatalogueManager`** — manages multiple portrayal catalogues. Implements `ICatalogueProvider<PortrayalCatalogueProvider>`, including `GetCatalogueHashAsync(spec)`, which returns a memoized lowercase-hex SHA-256 *aggregate* of the catalogue XML plus the bytes of every referenced asset it declares (rule files, symbols, line styles, area fills, colour profiles, pixmaps, style sheets). The hash is computed lazily, once per spec, and invalidated on `SetPath` / `SetSource`; a transient failure (null) is not permanently memoized. It is a content hash suitable as a cache-invalidation input.
- **`ColorProfileReader`**, **`LineStyleReader`**, **`AreaFillReader`** — parsers for individual portrayal components.
- **`ViewingGroup`**, **`DisplayMode`**, **`DisplayPlane`**, **`ContextParameter`** — display configuration types.

## Installation

```sh
dotnet add package EncDotNet.S100.Portrayals
```
