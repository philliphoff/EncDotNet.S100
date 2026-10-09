# Custom catalogues and validation

Every dataset is interpreted through two catalogues from its product
specification. The **feature catalogue** (S-100 Part 5) defines its feature
types and attributes. The **portrayal catalogue** (S-100 Part 9) defines how
they're drawn. `EncDotNet.S100` bundles the official catalogues, but you might
need a newer edition or your own symbology. This guide shows how to:

- use your own catalogues with `S100FeatureCatalogue`,
  `S100PortrayalCatalogue` and `S100Layer`
- check a dataset against its product's validation rules with `Validate()`
- add rules of your own with `ValidationRuleBuilder` and
  `ValidationRuleSet<T>`

The examples use files from
[`tests/datasets`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/datasets).

## Use your own feature catalogue

Load a feature catalogue with `S100FeatureCatalogue.FromStream`. Use it to read
features, or pair it with a dataset in a layer to render:

```csharp
using EncDotNet.S100;

using var featureCatalogue = S100FeatureCatalogue.FromStream(File.OpenRead("FeatureCatalogue.xml"));
using var dataset = S100Dataset.Open("navwarn_surface.gml");

foreach (var feature in featureCatalogue.EnumerateFeatures(dataset))
    Console.WriteLine($"{feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");
```

`FromStream` reads the whole stream, so you can dispose the stream as soon as
it returns. Use a catalogue for the dataset's product: feature type and
attribute names are looked up by code, so a catalogue for another product
resolves nothing.

## Use your own portrayal catalogue

A portrayal catalogue is a folder: `portrayal_catalogue.xml` plus the rules,
symbols, line styles, area fills and colour profiles it lists. Point
`S100PortrayalCatalogue.FromAssetSource` at the folder, or at a ZIP archive of
it, and render through a layer:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Core;

using var portrayalFolder = FileSystemAssetSource.Create("my-s124-portrayal");
using var dataset = S100Dataset.Open("navwarn_surface.gml");
using var renderer = new PngS100DatasetRenderer();

var layer = new S100Layer
{
    Dataset = dataset,
    PortrayalCatalogue = S100PortrayalCatalogue.FromAssetSource(portrayalFolder),
};
byte[] png = await renderer.RenderAsync(layer);
```

A layer can set `FeatureCatalogue`, `PortrayalCatalogue`, both or neither. Any
catalogue you leave `null` is the bundled one. The composite
`RenderAsync(IReadOnlyList<S100Layer>, …)` also takes layers, so each dataset
in a composite can have its own catalogues.

> [!IMPORTANT]
> Include every file that `portrayal_catalogue.xml` lists. A missing rule file
> fails the render with a `FileNotFoundException` that names it, but a missing
> symbol is skipped and the image renders without it. If the output looks
> incomplete, compare your folder with the bundled catalogue's.

## Start from a bundled catalogue

To make your own catalogue, start from a copy of the bundled one. In this
repository they're under
[`src/EncDotNet.S100.Specifications/content/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/src/EncDotNet.S100.Specifications/content),
one folder per product (for example `S124/`). In each, `fc/` holds the feature
catalogue and `pc/` the portrayal catalogue folder.

In an application that uses the package, get the bundled feature catalogue with
`Specification.OpenFeatureCatalogueAsync`:

```csharp
using EncDotNet.S100.Specifications;

await using (var bundled = await Specification.OpenFeatureCatalogueAsync("S-124"))
await using (var copy = File.Create("FeatureCatalogue.xml"))
    await bundled.CopyToAsync(copy);
```

## Validate a dataset

`Validate()` runs the product's bundled rule pack against the parsed dataset.
These are the same rules SoundCharts shows.

```csharp
using EncDotNet.S100;

using var dataset = S100Dataset.Open("navwarn_surface.gml");
var report = dataset.Validate();

if (report is null)
    Console.WriteLine("No validation rules for this product.");
else if (report.IsValid)
    Console.WriteLine("No findings.");
else
    foreach (var finding in report.Findings)
        Console.WriteLine($"{finding.Severity} {finding.RuleId}: {finding.Message}");
```

Every product in the library has a rule pack except S-401 inland ENC, which
uses the S-101 reader but not its rules.

> [!NOTE]
> `Validate()` returns `null` when the product has no rule pack. That isn't a
> pass. A report with no findings (`IsValid`) means the rules ran and found
> nothing.

The report has these members (namespace `EncDotNet.S100.Validation`):

- `HasErrors` and `HasWarnings` summarize the report.
  `FindingsOfSeverity(ValidationSeverity.Error)` filters it.
- Each finding has a `RuleId` (for example `S124-R-1.1`), a `Severity` and a
  `Message`. Where possible it also has a location (`Point` or `BoundingBox`)
  and the `RelatedFeatureId` it concerns.

The report is cached with the dataset, so calling `Validate()` again returns
the same report without running the rules again.

## Write your own rules

Rules run against a product's typed model or dataset type. To get one, see
[Reading product data](reading-product-data.md). Build rules with
`ValidationRuleBuilder`:

- `Check` reports one pass-or-fail condition.
- `Yield` reports any number of findings, such as one per offending feature.

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Datasets.S124.Validation;
using EncDotNet.S100.Validation;

var titled = ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("MY-1")
    .WithDescription("A warning names its general area.")
    .WithSeverity(ValidationSeverity.Warning)
    .Check(warning => !string.IsNullOrEmpty(warning.Preamble?.GeneralArea))
    .Build();

var partsHaveText = ValidationRuleBuilder.RuleFor<S124NavigationalWarning>("MY-2")
    .WithDescription("Every part carries warning text.")
    .WithSeverity(ValidationSeverity.Info)
    .Yield((warning, context) => warning.Parts
        .Where(part => string.IsNullOrEmpty(part.WarningInformation))
        .Select(part => new ValidationFinding
        {
            RuleId = "MY-2",
            Severity = ValidationSeverity.Info,
            Message = $"Part {part.Id} has no warning text.",
            RelatedFeatureId = part.Id,
        }))
    .Build();

var rules = S124NavigationalWarningRules.Default.Add(titled).Add(partsHaveText);

var warning = S124NavigationalWarning.From(S124Dataset.Open("navwarn_surface.gml"), out _);
foreach (var finding in rules.Run(warning).Findings)
    Console.WriteLine($"{finding.Severity} {finding.RuleId}: {finding.Message}");
```

A failed `Check` reports its description as the message, unless you pass a
`failureMessage`.

Rule sets are immutable. `Add` and `Remove(ruleId)` return a new set and leave
the product's `Default` pack unchanged. Start from `Default` to extend the
product's rules, or from `ValidationRuleSet<T>.Empty` to run only your own. A
rule that throws becomes an `Error` finding instead of stopping the run.

Each product's rule pack is a static `Default` property on a class in the
product's `Validation` namespace, such as `S124NavigationalWarningRules` or
`S102DatasetRules`. The per-product READMEs list the rules.

## Next steps

- [Reading product data](reading-product-data.md): the typed models and dataset
  types that rules run against.
- [Top APIs](top-apis.md): the main entry points in each package.
