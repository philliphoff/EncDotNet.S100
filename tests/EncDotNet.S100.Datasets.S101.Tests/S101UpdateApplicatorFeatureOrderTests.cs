using System.Collections.ObjectModel;

namespace EncDotNet.S100.Datasets.S101.Tests;

/// <summary>
/// Checks that applying updates orders features exactly as the earlier list-based merge did,
/// which removed a deleted RCID from the order list and appended re-inserted ones.
/// </summary>
public class S101UpdateApplicatorFeatureOrderTests
{
    [Fact]
    public void ApplyChanges_RandomUpdates_MatchListBasedMerge()
    {
        var random = new Random(101);
        for (var trial = 0; trial < 2_000; trial++)
        {
            uint source = 0;
            var idSpace = random.Next(1, 8);

            // A base dataset, sometimes repeating an RCID.
            var baseFeatures = new List<S101FeatureRecord>();
            for (var i = random.Next(0, 8); i > 0; i--)
                baseFeatures.Add(Feature((uint)random.Next(idSpace), ++source, S101UpdateInstruction.Insert));

            var updateFeatures = new List<S101FeatureRecord>();
            for (var i = random.Next(0, 12); i > 0; i--)
                updateFeatures.Add(Feature((uint)random.Next(idSpace), ++source, (S101UpdateInstruction)random.Next(1, 4)));

            var actual = Doc(0, "1", baseFeatures).ApplyChanges(Doc(1, "2", updateFeatures)).Features
                .Select(f => (f.RecordId, f.FeatureIdentificationNumber));

            Assert.Equal(ListBasedMerge(baseFeatures, updateFeatures), actual);
        }
    }

    // The merge as it was before deletes left their RCIDs in the order: Remove searched the
    // list. FIDN identifies which record supplied a feature; a modify takes the update's FIDN.
    private static List<(uint, uint)> ListBasedMerge(List<S101FeatureRecord> baseFeatures, List<S101FeatureRecord> updateFeatures)
    {
        var order = new List<uint>();
        var byId = new Dictionary<uint, uint>();
        foreach (var f in baseFeatures)
        {
            if (!byId.ContainsKey(f.RecordId)) order.Add(f.RecordId);
            byId[f.RecordId] = f.FeatureIdentificationNumber;
        }

        foreach (var u in updateFeatures)
        {
            switch (u.UpdateInstruction)
            {
                case S101UpdateInstruction.Delete:
                    if (byId.Remove(u.RecordId)) order.Remove(u.RecordId);
                    break;
                case S101UpdateInstruction.Modify when byId.ContainsKey(u.RecordId):
                    byId[u.RecordId] = u.FeatureIdentificationNumber;
                    break;
                default:
                    if (!byId.ContainsKey(u.RecordId)) order.Add(u.RecordId);
                    byId[u.RecordId] = u.FeatureIdentificationNumber;
                    break;
            }
        }

        return [.. order.Select(id => (id, byId[id]))];
    }

    private static S101FeatureRecord Feature(uint rcid, uint source, S101UpdateInstruction instruction) => new()
    {
        RecordId = rcid,
        FeatureTypeCode = 1,
        FeatureIdentificationNumber = source,
        Attributes = [],
        SpatialAssociations = [],
        FeatureAssociations = [],
        InformationAssociations = [],
        RecordVersion = 1,
        UpdateInstruction = instruction,
    };

    private static S101Document Doc(int updateNumber, string profile, IEnumerable<S101FeatureRecord> features) => new()
    {
        Identification = new S101DatasetIdentification
        {
            DatasetName = $"TEST.{updateNumber:D3}",
            ApplicationProfile = profile,
            UpdateNumber = updateNumber,
        },
        StructureInfo = new S101DatasetStructureInfo(),
        FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
        CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
        CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
        Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
        Features = features.ToArray(),
        InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
        InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
    };
}
