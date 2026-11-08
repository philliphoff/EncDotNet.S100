# EncDotNet.S100.Datasets.S412

`EncDotNet.S100.Datasets.S412` reads WMO S-412 Marine Weather Warnings
datasets: GML files (S-100 Part 10b) with areas where wind, significant wave
height, visibility, thunderstorms or ice accretion meet warning criteria during
a period. It projects a dataset into typed warnings and portrays it with the
bundled S-412 portrayal catalogue. Reference it when you need typed access to
S-412 warnings. To open and render any product, including S-412, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

> [!WARNING]
> S-412 is a draft. It isn't approved in the IHO GI Registry yet, and WMO
> targets operational use in 2029. This package follows the Feature Catalogue
> 1.1.0 draft (v009) and the draft portrayal catalogue from
> [iho-ohi/S-412-Product-Specification](https://github.com/iho-ohi/S-412-Product-Specification),
> and its types can change when the specification does.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S412
```

## Read a dataset

Open the dataset, then project it into the typed model:

```csharp
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Datasets.S412.DataModel;

var dataset = S412Dataset.Open("path/to/warnings.gml");
var model = S412MarineWeatherWarnings.From(dataset, out var diagnostics);

foreach (var warning in model.Warnings)
{
    var message = warning.Messages.FirstOrDefault(m => m.Language == "eng")?.Text;
    Console.WriteLine($"{warning.Kind} {warning.WeatherWarningNumber}: " +
        $"{warning.FixedDateRange?.Start:u} to {warning.FixedDateRange?.End:u}. {message}");

    if (warning is S412WindWarning wind)
        Console.WriteLine($"  {wind.Threshold}, Beaufort {wind.BeaufortForce}");
}
```

## Main types

- **`S412Dataset`**: the parsed dataset. `Features` holds the warnings and
  `InformationTypes` the `DataProvider` records. `Open` takes a path or a
  stream. `ReadMetadata` (static, for a path or stream, or on an open dataset)
  returns the declared product specification and the WGS 84 extent of the
  warning areas without running portrayal.
- **`S412Feature`**: a warning feature with its type code, area, simple
  attributes, complex attributes and references. **`S412InformationType`** is a
  `DataProvider`. **`S412ComplexAttribute`** holds a complex attribute's
  sub-attributes; its `Groups` hold nested complex attributes, such as one
  `information` per language.
- **`S412MarineWeatherWarnings`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S412PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules and colour profile.

S-100 Part 10b 5.0 writes an enumeration as its label with a `code` attribute,
such as `<windWarningThreshold code="3">storm force</windWarningThreshold>`.
The reader keeps the code (`"3"`) as the attribute's value: it's what the
feature catalogue lists and what the portrayal rules test.

## Typed data model

`S412MarineWeatherWarnings`, in the `EncDotNet.S100.Datasets.S412.DataModel`
namespace, is a read-only projection of `S412Dataset`. The portrayal pipeline
doesn't use it.

- **`Warnings`** lists the warnings in document order. `S412WeatherWarning` is
  the abstract base, with the attributes every warning inherits from
  `WeatherWarning`: `FixedDateRange` (with `Start` and `End` as UTC moments),
  `Messages`, `IssueDate`, `IssueTime`, `WeatherWarningNumber`,
  `CancellationDate`, `NextUpdateDateTime`, `InteroperabilityIdentifier` and
  the resolved `DataProvider`. The concrete classes are `S412WindWarning`
  (`Threshold`, `BeaufortForce`, `MaximumWindSpeed`),
  `S412SignificantWaveHeightWarning` (`Threshold`), `S412IceAccretionWarning`
  (`Rate`, `SourceOfIcing`), `S412RestrictedVisibilityWarning` and
  `S412ThunderstormWarning`. `Kind` names the type.
- **`DataProviders`** lists the `S412DataProvider` records: the issuing
  service's METAREA, country and names, the contact information and the
  preparing service.

Values that don't parse, enumeration codes the feature catalogue doesn't list,
and `dataProviderInformation` references that don't resolve become
`ProjectionDiagnostic` entries; `From` doesn't throw for them. The upstream
sample writes some dates and times outside the schema (`20250101` for a
date-time, `0600` for a time of day): a date is read where one is given, and a
time of day that isn't an S-100 time leaves `FixedDateRange.Start` or `End`
`null`, with a diagnostic.

## Portrayal

The bundled catalogue draws each warning as a translucent fill with an outline,
coloured by warning type and threshold. The library puts S-412 on the S-98
cautions and warnings plane, as it does S-124 navigational warnings, whatever
plane the rules name.

The draft catalogue's `main.xsl` includes a significant wave height rule file
that isn't there, and the file that is there matches the wrong feature and
attribute names. `S412PortrayalCatalogue` serves a corrected copy at load time,
without editing the bundled files. The catalogue's
[README](../EncDotNet.S100.Specifications/content/S412/README.md) lists this
correction and the other known gaps in the draft.

## Not yet supported

- There's no validation rule pack. `s100 validate` reports only the shared
  checks, such as axis order.
- The SoundCharts timeline doesn't yet hide warnings outside their
  `fixedDateRange` or after their `cancellationDate`.
