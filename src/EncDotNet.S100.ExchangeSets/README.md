# EncDotNet.S100.ExchangeSets

Reader for S-100 Exchange Set catalogues and dataset/support file discovery.

## Overview

This library parses S-100 Exchange Set `CATALOG.XML` files and provides access to the datasets and support files within an exchange set. Key types include:

- **`ExchangeSet`** — opens and navigates an exchange set through an `IAssetSource`.
- **`ExchangeCatalogue`** — the parsed catalogue metadata.
- **`ExchangeCatalogueReader`** — XML parser for the exchange catalogue.
- **`DatasetDiscoveryMetadata`** — metadata for each dataset in the exchange set (file name, bounding box, product specification).
- **`SupportFileDiscoveryMetadata`** — metadata for support files.
- **`CatalogueDiscoveryMetadata`** — metadata for embedded catalogues.

## Installation

```sh
dotnet add package EncDotNet.S100.ExchangeSets
```
