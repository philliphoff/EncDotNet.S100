# EncDotNet.S100.Hdf5.PureHdf

HDF5 file reader implementation using [PureHDF](https://github.com/Apollo3zehn/PureHDF), a fully managed .NET HDF5 library with no native dependencies.

## Overview

This library implements the `IHdf5File` and `IHdf5Group` abstractions defined in `EncDotNet.S100.Core` using PureHDF. It provides:

- **`PureHdfFile`** — opens HDF5 files from a file path or stream and exposes groups, attributes, and datasets through the core abstractions.

This is the recommended HDF5 provider for all platforms, as it requires no native libraries.

## Installation

```sh
dotnet add package EncDotNet.S100.Hdf5.PureHdf
```
