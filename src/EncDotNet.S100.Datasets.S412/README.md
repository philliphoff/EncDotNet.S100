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
date-time, `0600` for a time of day). A date given for a date-time is read as
the start of that day. A time of day written as hours and minutes (`hhmm`)
rather than the complete `hhmmss` the encoding guide asks for is read on the
minute, with an `attribute.time.incomplete` diagnostic.

## Time

A warning is in force from the start of its `fixedDateRange` to its end, both
included, and ended from its `cancellationDate`. This is the rule S-124
navigational warnings and CAP weather warnings use (`WarningValidity` in
`EncDotNet.S100.Core`), so all three start and end alike. A date with no time
of day is midnight UTC at the start of that day.

- **`S412WeatherWarning.StatusAt(time)`** gives a warning's status: not yet in
  force, in force, or ended. `InForceUntil` is the earlier of the range end and
  the cancellation date, and `IssuedAt` combines `issueDate` and `issueTime`.
- **`S412MarineWeatherWarnings.DrawnAt(time)`** lists the warnings in force at
  a time, or every warning when the time is `null`.
- **`Changes`** lists the instants the set of warnings in force changes.
  `TimeCoverage` runs from the earliest start to the latest end, and
  `S412Dataset.ReadMetadata` reports it.

The processor in `EncDotNet.S100.Datasets.Pipelines` (`S412DatasetProcessor`)
is time-aware. Its time steps are `Changes`, each held until the next, and at a
time step it draws and picks only the warnings in force. `s100 info` lists the
steps, `s100 render --time-step` draws one, and a host's timeline steps through
them. With no time step every warning is drawn.

The upstream sample's `cancellationDate` (midnight on 1 January 2025) comes
before its range starts (06:00 that day), so it's never in force at any time
step. It's drawn only when no time step is given.

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

## Query with MCP

`identify_features` finds a warning inside its area, and `describe_feature`
returns its encoded attributes, with every language of nested attributes such
as `weatherWarningMessage`, plus a `warning` block: the kind, the threshold,
Beaufort force, icing rate and source as a code with the feature catalogue's
label (`gale force`, `gale`), the message and maximum wind speed in each
language, the validity times and the data provider. `S412Labels` gives the
same labels in code. See the
[MCP server reference](../../docs/mcp-server.md).

## Validate

`S412MarineWeatherWarningRules`, in the
`EncDotNet.S100.Datasets.S412.Validation` namespace, is the default rule set
for `S412MarineWeatherWarnings`. `s100 validate`, `S100Dataset.Validate()` and
the SoundCharts Validation tab run it.

```csharp
using EncDotNet.S100.Datasets.S412;
using EncDotNet.S100.Datasets.S412.DataModel;
using EncDotNet.S100.Datasets.S412.Validation;

var model = S412MarineWeatherWarnings.From(S412Dataset.Open("path/to/warnings.gml"), out _);
var report = S412MarineWeatherWarningRules.Validate(model);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId} {finding.RelatedFeatureId}: {finding.Message}");
```

The draft's validation annex (Annex B) doesn't list any S-412 checks yet. The
rules check what the draft does make normative: the feature catalogue's
bindings and listed values, which the GML schema repeats, the encoding guide's
time rules (DCEG §2), and the S-100 Part 10b geometry rules. Rule IDs have the
form `S412-R-{theme}.{n}`, numbered by the data quality themes in the draft's
validation annex: 1 completeness, 2 logical consistency, 3 spatial
consistency, 5 temporal accuracy and 6 thematic accuracy. The annex asks for
every check to be a warning while S-412 is in development, so every rule is a
warning. Each finding names its feature and is located on the warning area.

| Rule ID | Checks |
|---|---|
| `S412-R-1.1` | A warning has the attributes bound `1..1`: `fixedDateRange`, `issueDate`, `weatherWarningNumber` and `cancellationDate`, plus `windWarningThreshold` and `maximumWindSpeed` (wind), `significantWaveHeightWarningThreshold` (wave height) and `iceAccretionRate` (ice accretion). |
| `S412-R-1.2` | A `fixedDateRange` has its `dateStart`, `dateEnd`, `timeOfDayStart` and `timeOfDayEnd`. |
| `S412-R-1.3` | A `DataProvider` has an `issuingService` with a `metareaName` and `issuingServiceName`, a `contactInformation` with an `agencyName`, and a `preparationService` with an `issuingServiceName`. |
| `S412-R-1.4` | Each `information` in a message or maximum wind speed has a `text` or a `fileReference`. |
| `S412-R-2.1` | `dataProviderInformation` refers to a `DataProvider` in the dataset. |
| `S412-R-2.2` | Identifiers (`gml:id`) are unique. |
| `S412-R-3.1` | A warning is a surface with a closed exterior ring of at least four positions. |
| `S412-R-3.2` | Positions are within the WGS 84 latitude and longitude ranges. |
| `S412-R-5.1` | Dates and times are readable as their schema types, a time of day has seconds (`hhmmss`), and a date-time has a time. |
| `S412-R-5.2` | A `fixedDateRange` doesn't end before it starts. |
| `S412-R-5.3` | A warning isn't cancelled at or before its date range starts, which would mean it's never in force. |
| `S412-R-5.4` | `nextUpdateDateTime` isn't before the warning was issued. |
| `S412-R-6.1` | Enumeration values are listed in the feature catalogue, including a `DataProvider`'s `metareaName` (1 to 14). |
| `S412-R-6.2` | A wind warning's `beaufortForce` matches its `windWarningThreshold`: force 7 for near gale, 8 or 9 for gale, 10 or 11 for storm, and 12 for hurricane force. |

The upstream sample gets seven warnings: no `maximumWindSpeed`, four
incomplete dates and times, a cancellation before the warning starts, and
Beaufort force 8 with a storm-force threshold.
