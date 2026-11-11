# EncDotNet.S100.Hdf5.PureHdf

`EncDotNet.S100.Hdf5.PureHdf` implements the `IHdf5File` and `IHdf5Group`
interfaces from [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) with
[PureHDF](https://github.com/Apollo3zehn/PureHDF), a managed .NET HDF5 library
with no native dependencies. The dataset pipelines use it to open S-102, S-104
and S-111 files. Reference it directly when you call an HDF5 product reader
yourself, or read HDF5 attributes and datasets through the core interfaces.
It's the only HDF5 backend in this repository.

## Install

```bash
dotnet add package EncDotNet.S100.Hdf5.PureHdf
```

## Example: read a root attribute

```csharp
using EncDotNet.S100.Hdf5.PureHdf;

using var file = PureHdfFile.Open("path/to/dataset.h5");
string spec = file.Root.ReadStringAttribute("productSpecification");
Console.WriteLine(spec);
```

## Main entry points

- `PureHdfFile.Open` opens an HDF5 file from a path or a stream. `Root` is the
  root group.
- `IHdf5Group` (from `EncDotNet.S100.Core`, namespace `EncDotNet.S100.Hdf5`)
  opens child groups and reads attributes, datasets and compound datasets.
