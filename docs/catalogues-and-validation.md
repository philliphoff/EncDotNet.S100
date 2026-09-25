# Custom catalogues and validation

## Why it matters

Every dataset is interpreted through two catalogues from its product
specification: the **feature catalogue** (S-100 Part 5), which defines its
feature types and attributes, and the **portrayal catalogue** (S-100 Part 9),
which defines how they're drawn. `EncDotNet.S100` bundles the official ones,
but you may need a newer edition, or your own symbology. This guide shows how to
use your own catalogues, how to check a dataset against its product's
validation rules, and how to add rules of your own.

## Quick win

Run a dataset's product validation rules and list what they find:

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

## Deep dive

### Your own feature catalogue

Load a feature catalogue with `S100FeatureCatalogue.FromStream`, then use it to
read features, or pair it with a dataset in a layer to render:

```csharp
using var featureCatalogue = S100FeatureCatalogue.FromStream(File.OpenRead("FeatureCatalogue.xml"));
using var dataset = S100Dataset.Open("navwarn_surface.gml");

foreach (var feature in featureCatalogue.EnumerateFeatures(dataset))
    Console.WriteLine($"{feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");
```

`FromStream` reads the whole stream, so you can dispose it straight away. The
catalogue should be for the dataset's product: feature type and attribute names
are looked up by code, so a catalogue for another product resolves nothing.

### Your own portrayal catalogue

A portrayal catalogue is a folder: `portrayal_catalogue.xml` plus the rules,
symbols, line styles, area fills and colour profiles it lists. Point
`S100PortrayalCatalogue.FromAssetSource` at the folder, or at a ZIP of it, and
render through a layer:

```csharp
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

A layer can set `FeatureCatalogue`, `PortrayalCatalogue`, both, or neither; any
catalogue left `null` is the bundled one. Layers also work with the composite
`RenderAsync(IReadOnlyList<S100Layer>, …)`, so each dataset in a composite can
have its own catalogues.

### Starting from the bundled catalogues

The easiest way to make your own catalogue is to change a copy of the bundled
one. In this repository they're under
`src/EncDotNet.S100.Specifications/content/<product>/`: `fc/` holds the feature
catalogue and `pc/` the portrayal catalogue folder. In an application using the
package, get the bundled feature catalogue with
`Specification.OpenFeatureCatalogueAsync` (namespace
`EncDotNet.S100.Specifications`):

```csharp
using EncDotNet.S100.Specifications;

await using (var bundled = await Specification.OpenFeatureCatalogueAsync("S-124"))
await using (var copy = File.Create("FeatureCatalogue.xml"))
    await bundled.CopyToAsync(copy);
```

### Running the bundled validation rules

`Validate()` runs the product's bundled rule pack, the same rules the S-100
Viewer shows, against the parsed dataset. Every product in the library has one
except S-401 inland ENC, which reuses the S-101 reader but not its rules, and
returns `null`.

- `null` means the product has no rule pack; a report with no findings
  (`IsValid`) means the rules ran and found nothing.
- `HasErrors` and `HasWarnings` summarise the report, and
  `FindingsOfSeverity(ValidationSeverity.Error)` filters it (namespace
  `EncDotNet.S100.Validation`).
- Each finding has a `RuleId` (for example `S124-R-1.1`), a `Severity`, a
  `Message`, and where possible a location (`Point` or `BoundingBox`) and the
  `RelatedFeatureId` it concerns.

The report is cached with the dataset, so calling `Validate()` again is cheap.

### Your own rules

Rules run against a product's typed model or dataset type. See
[Reading product data](reading-product-data.md) for how to get one. Build
rules with `ValidationRuleBuilder`: `Check` for one pass-or-fail condition, or
`Yield` to report several findings, such as one per offending feature:

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

Rule sets are immutable: `Add` and `Remove(ruleId)` return a new set and leave
the product's `Default` pack unchanged. Start from `Default` to extend the
product's rules, or from `ValidationRuleSet<T>.Empty` to run only your own. A
rule that throws becomes an `Error` finding instead of stopping the run.

Each product's rule pack is a static `Default` property on a class in the
product's `Validation` namespace, such as `S124NavigationalWarningRules` or
`S102DatasetRules`. The per-product READMEs list the rules.

## Troubleshooting

> [!IMPORTANT]
> A custom portrayal catalogue must include every file that
> `portrayal_catalogue.xml` lists. A missing rule file fails the render with a
> `FileNotFoundException` naming it, but a missing symbol is skipped and the image
> renders without it. If output looks incomplete, compare your folder with the
> bundled catalogue's.

> [!NOTE]
> `Validate()` returning `null` isn't a pass: it means no rules exist for the
> product. Check `report.IsValid` for a pass.

## Next step

- [Reading product data](reading-product-data.md) — the typed models and
  dataset types rules run against.
- [Top APIs](top-apis.md) — the main entry point in each package.
