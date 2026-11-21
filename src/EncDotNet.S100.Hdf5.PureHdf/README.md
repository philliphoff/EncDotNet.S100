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

## Example: write a file

Build the file as an `Hdf5WriteGroup` tree (from `EncDotNet.S100.Core`), then
write it. A struct dataset is a compound; name its members with
`Hdf5MemberAttribute`:

```csharp
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;

var root = new Hdf5WriteGroup().SetAttribute("productSpecification", "INT.IHO.S-104.2.0");
root.Group("WaterLevel").SetDataset("values", [new Value { Height = 1.5f }]);
PureHdfWriter.Write(root, "path/to/dataset.h5");

struct Value
{
    [Hdf5Member("waterLevelHeight")] public float Height;
}
```

Strings are variable-length UTF-8 and enums are HDF5 enumerations. Pass
dimensions to `SetDataset` for a grid. Product writers such as
`S104StationSeriesWriter` build the tree for you. PureHDF's writer needs
runtime code generation, so writing doesn't work under NativeAOT; reading
does.

## Main entry points

- `PureHdfFile.Open` opens an HDF5 file from a path or a stream. `Root` is the
  root group.
- `IHdf5Group` (from `EncDotNet.S100.Core`, namespace `EncDotNet.S100.Hdf5`)
  opens child groups and reads attributes, datasets and compound datasets.
- `PureHdfWriter.Write` writes an `Hdf5WriteGroup` tree to a path or a stream.
