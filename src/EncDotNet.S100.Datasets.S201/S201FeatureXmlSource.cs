using System.Xml.Linq;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S201;

/// <summary>
/// Projects an <see cref="S201Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format consumed by the S-201 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// Extends the base GML projection with information references on
/// features (e.g. <c>AtoNStatus</c>, <c>Positioning</c>) and an
/// <c>InformationTypes</c> section that the bundled
/// <c>main_PaperChart.xsl</c> resolves via
/// <c>/Dataset/InformationTypes/*[@id=$ref]</c>. Feature-to-feature
/// xlink references (<c>theParentFeature</c> / <c>theSubordinateFeature</c>
/// from the S-201 Edition 2.0.0 <c>Structure/Equipment</c> aggregation)
/// are emitted as <c>&lt;Role featureRef="…"/&gt;</c> children so XSLT
/// rules can navigate equipment-on-structure relationships.
/// </remarks>
public sealed class S201FeatureXmlSource : GmlFeatureXmlSource<S201Feature>
{
    private readonly S201Dataset _dataset;

    /// <summary>Initializes a new <see cref="S201FeatureXmlSource"/>.</summary>
    public S201FeatureXmlSource(S201Dataset dataset)
        : base((dataset ?? throw new ArgumentNullException(nameof(dataset))).Features)
    {
        _dataset = dataset;
    }

    /// <summary>
    /// Translates listed-value attribute labels (e.g.
    /// <c>Port-Hand Lateral Mark</c>) to their FC numeric codes
    /// (e.g. <c>1</c>) so the upstream S-201 XSLT predicates — which match
    /// on numeric codes — fire correctly. Real-world S-201 producers
    /// emit labels; the bundled portrayal catalogue assumes codes. See
    /// S-201 Edition 2.0.0 §11.
    /// </summary>
    protected override string TransformAttributeValue(string code, string value)
        => S201ListedValueIndex.Normalize(code, value);

    /// <inheritdoc/>
    protected override void WriteFeatureExtensions(S201Feature feature, XElement featureElement)
    {
        SynthesiseDefaultPortrayalAttributes(feature, featureElement);

        if (!feature.InformationReferences.IsDefaultOrEmpty)
        {
            foreach (var infoRef in feature.InformationReferences)
            {
                featureElement.Add(new XElement(infoRef.Role,
                    new XAttribute("informationRef", infoRef.InformationRef)));
            }
        }

        if (!feature.FeatureReferences.IsDefaultOrEmpty)
        {
            foreach (var featureRef in feature.FeatureReferences)
            {
                featureElement.Add(new XElement(featureRef.Role,
                    new XAttribute("featureRef", featureRef.TargetRef)));
            }
        }
    }

    /// <summary>
    /// Synthesises spec-consistent default values for <c>buoyShape</c>,
    /// <c>beaconShape</c>, and <c>colour</c> when a feature carries a
    /// category attribute (e.g. <c>categoryOfLateralMark</c>) but omits
    /// the geometric/colour attributes the upstream portrayal catalogue
    /// requires to select a non-generic symbol. Without this, real-world
    /// datasets that encode only category information fall through to the
    /// generic <c>BOYGEN03</c>/default symbol (the "?" buoy).
    /// </summary>
    /// <remarks>
    /// <para>Defaults follow IALA-A conventions (the value emitted by
    /// most real-world S-201 producers via
    /// <c>marksNavigationalSystemOf=IALA A</c>):</para>
    /// <list type="bullet">
    ///   <item><description>Port-Hand Lateral Mark → can buoy, red.</description></item>
    ///   <item><description>Starboard-Hand Lateral Mark → conical buoy, green.</description></item>
    ///   <item><description>Preferred Channel to Starboard → can, red with green band.</description></item>
    ///   <item><description>Preferred Channel to Port → conical, green with red band.</description></item>
    /// </list>
    /// <para>Numeric codes correspond to the S-201 Edition 2.0.0 Feature
    /// Catalogue listed-value entries (<c>buoyShape</c>: 1=Conical,
    /// 2=Can, 4=Pillar; <c>colour</c>: 3=Red, 4=Green, 6=Yellow,
    /// 2=Black).</para>
    /// </remarks>
    private static void SynthesiseDefaultPortrayalAttributes(S201Feature feature, XElement featureElement)
    {
        // Translate category to FC numeric code (the FC labels are what real
        // datasets emit; TransformAttributeValue would have normalised them
        // for elements already in feature.Attributes, but here we read from
        // the source dictionary so we have to do the same lookup ourselves).
        string? Get(string code) =>
            feature.Attributes.TryGetValue(code, out var v)
                ? S201ListedValueIndex.Normalize(code, v)
                : null;

        bool Has(string code) =>
            featureElement.Element(code) is not null;

        void AddIfMissing(string code, string value)
        {
            if (!Has(code))
                featureElement.Add(new XElement(code, value));
        }

        switch (feature.FeatureType)
        {
            case "LateralBuoy":
            {
                var cat = Get("categoryOfLateralMark");
                switch (cat)
                {
                    case "1": // Port-Hand
                        AddIfMissing("buoyShape", "2"); // Can
                        AddIfMissing("colour", "3"); // Red
                        break;
                    case "2": // Starboard-Hand
                        AddIfMissing("buoyShape", "1"); // Conical
                        AddIfMissing("colour", "4"); // Green
                        break;
                    case "3": // Preferred Channel to Starboard (red with green band)
                        AddIfMissing("buoyShape", "2"); // Can
                        AddIfMissing("colour", "3");
                        break;
                    case "4": // Preferred Channel to Port (green with red band)
                        AddIfMissing("buoyShape", "1"); // Conical
                        AddIfMissing("colour", "4");
                        break;
                }
                break;
            }

            case "LateralBeacon":
            {
                var cat = Get("categoryOfLateralMark");
                switch (cat)
                {
                    case "1": AddIfMissing("colour", "3"); break;
                    case "2": AddIfMissing("colour", "4"); break;
                    case "3": AddIfMissing("colour", "3"); break;
                    case "4": AddIfMissing("colour", "4"); break;
                }
                break;
            }

            case "CardinalBuoy":
            {
                // IALA cardinal marks are pillar or spar with yellow + black bands.
                AddIfMissing("buoyShape", "4"); // Pillar
                AddIfMissing("colour", "2"); // Black (primary; band sequence varies)
                break;
            }

            case "IsolatedDangerBuoy":
            {
                AddIfMissing("buoyShape", "4"); // Pillar
                AddIfMissing("colour", "2"); // Black with red bands
                break;
            }

            case "SafeWaterBuoy":
            {
                AddIfMissing("buoyShape", "3"); // Spherical
                AddIfMissing("colour", "3"); // Red (with white vertical stripes)
                break;
            }

            case "SpecialPurposeGeneralBuoy":
            case "InstallationBuoy":
            {
                AddIfMissing("buoyShape", "4"); // Pillar
                AddIfMissing("colour", "6"); // Yellow
                break;
            }
        }
    }

    /// <inheritdoc/>
    protected override void WriteDatasetExtensions(XElement root)
    {
        var infoElement = new XElement("InformationTypes");

        foreach (var info in _dataset.InformationTypes)
        {
            var infoEl = new XElement(info.TypeCode, new XAttribute("id", info.Id));

            foreach (var (code, value) in info.Attributes)
                infoEl.Add(new XElement(code, value));

            foreach (var complex in info.ComplexAttributes)
                infoEl.Add(BuildComplexAttributeElement(complex));

            infoElement.Add(infoEl);
        }

        root.Add(infoElement);
    }
}
