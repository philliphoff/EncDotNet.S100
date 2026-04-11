# EncDotNet.S100.Portrayals

Parser for S-100 Portrayal Catalogues (S-100 Part 9).

## Overview

This library reads S-100 Portrayal Catalogue XML files and provides access to the symbols, styles, color profiles, and rules used to render S-100 data. Key types include:

- **`PortrayalCatalogue`** — the parsed model containing symbols, line styles, area fills, color profiles, rule files, viewing groups, and display modes.
- **`PortrayalCatalogueProvider`** — loads a catalogue and its referenced assets from an `IAssetSource`.
- **`PortrayalCatalogueReader`** — XML parser for portrayal catalogue files.
- **`PortrayalCatalogueManager`** — manages multiple portrayal catalogues.
- **`ColorProfileReader`**, **`LineStyleReader`**, **`AreaFillReader`** — parsers for individual portrayal components.
- **`ViewingGroup`**, **`DisplayMode`**, **`DisplayPlane`**, **`ContextParameter`** — display configuration types.

## Installation

```sh
dotnet add package EncDotNet.S100.Portrayals
```
