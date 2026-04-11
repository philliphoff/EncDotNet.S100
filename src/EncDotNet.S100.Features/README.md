# EncDotNet.S100.Features

Parser for S-100 Feature Catalogue XML files (ISO 19110 / S-100 Part 5).

## Overview

This library reads Feature Catalogue XML documents and produces a structured model including:

- **`FeatureCatalogue`** — the root parsed model containing feature types, information types, attributes, roles, and associations.
- **`FeatureCatalogueReader`** — XML parser that reads a feature catalogue from a stream or file.
- **`FeatureType`**, **`InformationType`** — definitions of feature and information types.
- **`SimpleAttribute`**, **`ComplexAttribute`**, **`ListedValue`** — attribute definitions and enumerated values.
- **`FeatureAssociation`**, **`InformationAssociation`**, **`Role`** — inter-feature relationships.

## Installation

```sh
dotnet add package EncDotNet.S100.Features
```
