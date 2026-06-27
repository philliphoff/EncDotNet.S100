using System;
using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class PickReportViewModelTests
{
    private static PickAttribute Leaf(string code, string value, string? name = null) =>
        new() { Code = code, Name = name, RawValue = value, DisplayValue = null, Children = [] };

    [Fact]
    public void SetPick_PopulatesAllFieldsAndMarksHasPick()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "DepthArea",
            featureTypeName: "Depth Area",
            featureRef: "42",
            datasetFileName: "test.000",
            productSpec: "S-101",
            attributes: new[]
            {
                Leaf("DRVAL1", "10.0", "Depth Range Value 1"),
                Leaf("DRVAL2", "20.0", "Depth Range Value 2"),
            });

        Assert.True(vm.HasPick);
        Assert.Equal("DepthArea", vm.FeatureType);
        Assert.Equal("Depth Area", vm.FeatureTypeName);
        Assert.Equal("42", vm.FeatureRef);
        Assert.Equal("test.000", vm.DatasetFileName);
        Assert.Equal("S-101", vm.ProductSpec);
        Assert.Equal(2, vm.Attributes.Count);
        Assert.True(vm.HasAttributes);
        Assert.Equal("DRVAL1", vm.Attributes[0].Code);
        Assert.Equal("Depth Range Value 1", vm.Attributes[0].DisplayName);
        Assert.Equal("10.0", vm.Attributes[0].RawValue);
    }

    [Fact]
    public void SetPick_LiftsResolvedFileReferenceIntoReferencedTexts()
    {
        var vm = new PickReportViewModel();

        var fileRef = new PickAttribute
        {
            Code = "fileReference",
            Name = "File Reference",
            RawValue = "CAUTION.TXT",
            DisplayValue = null,
            ExternalText = "ANCHORING RESTRICTED\n\nBeware of strong currents.",
            Children = [],
        };

        vm.SetPick("CautionArea", "Caution Area", "7", "test.000", "S-101",
            new[] { Leaf("objectName", "Caution Area"), fileRef });

        // The resolved file reference is pulled out of the attribute table…
        var row = Assert.Single(vm.Attributes);
        Assert.Equal("objectName", row.Code);

        // …and surfaced as a referenced-text card with a promoted heading.
        Assert.True(vm.HasReferencedText);
        var card = Assert.Single(vm.ReferencedTexts);
        Assert.Equal("ANCHORING RESTRICTED", card.Title);
        Assert.Equal("CAUTION.TXT", card.FileName);
        Assert.Equal("Beware of strong currents.", card.Body);
        Assert.Equal(fileRef.ExternalText, card.ClipboardText);
    }

    [Fact]
    public void SetPick_UnresolvedFileReferenceStaysInAttributeTable()
    {
        var vm = new PickReportViewModel();

        var fileRef = new PickAttribute
        {
            Code = "fileReference",
            Name = "File Reference",
            RawValue = "MISSING.TXT",
            DisplayValue = null,
            Children = [],
        };

        vm.SetPick("CautionArea", "Caution Area", "7", "test.000", "S-101", new[] { fileRef });

        Assert.False(vm.HasReferencedText);
        Assert.Empty(vm.ReferencedTexts);
        var row = Assert.Single(vm.Attributes);
        Assert.Equal("MISSING.TXT", row.RawValue);
    }

    [Fact]
    public void Clear_EmptiesReferencedTexts()
    {
        var vm = new PickReportViewModel();
        var fileRef = new PickAttribute
        {
            Code = "fileReference",
            RawValue = "CAUTION.TXT",
            ExternalText = "Hazard.\nDetails.",
            Children = [],
        };
        vm.SetPick("CautionArea", null, "7", "test.000", "S-101", new[] { fileRef });
        Assert.True(vm.HasReferencedText);

        vm.Clear();

        Assert.False(vm.HasReferencedText);
        Assert.Empty(vm.ReferencedTexts);
    }

    [Fact]
    public void SetPick_WithEmptyAttributes_ReportsHasAttributesFalse()
    {
        var vm = new PickReportViewModel();

        vm.SetPick(
            featureType: "Authority",
            featureTypeName: null,
            featureRef: "auth.1",
            datasetFileName: "container.gml",
            productSpec: "S-127",
            attributes: System.Array.Empty<PickAttribute>());

        Assert.True(vm.HasPick);
        Assert.False(vm.HasAttributes);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void SetPick_PreservesReferencedTextThroughReformat()
    {
        var vm = new PickReportViewModel();

        var fileRef = new PickAttribute
        {
            Code = "fileReference",
            Name = "File Reference",
            RawValue = "CAUTION.TXT",
            DisplayValue = null,
            ExternalText = "Beware of strong currents.",
            Children = [],
        };

        vm.SetPick("CautionArea", "Caution Area", "7", "test.000", "S-101", new[] { fileRef });

        Assert.Empty(vm.Attributes);
        var card = Assert.Single(vm.ReferencedTexts);
        Assert.Equal("CAUTION.TXT", card.FileName);
        Assert.Equal("Beware of strong currents.", card.Title);
        Assert.Equal("Beware of strong currents.", card.ClipboardText);
    }

    [Fact]
    public void SetPick_ReplacesPreviousPickContents()
    {
        var vm = new PickReportViewModel();

        vm.SetPick("First", null, "1", "a.gml", "S-101", new[] { Leaf("X", "1") });
        vm.SetPick("Second", null, "2", "b.gml", "S-124", new[] { Leaf("Y", "2") });

        Assert.Equal("Second", vm.FeatureType);
        Assert.Equal("2", vm.FeatureRef);
        Assert.Equal("b.gml", vm.DatasetFileName);
        Assert.Equal("S-124", vm.ProductSpec);
        Assert.Single(vm.Attributes);
        Assert.Equal("Y", vm.Attributes[0].Code);
    }

    [Fact]
    public void Clear_ResetsAllState()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("DepthArea", "Depth Area", "42", "test.000", "S-101",
            new[] { Leaf("DRVAL1", "10.0") });

        vm.Clear();

        Assert.False(vm.HasPick);
        Assert.False(vm.HasAttributes);
        Assert.Null(vm.FeatureType);
        Assert.Null(vm.FeatureTypeName);
        Assert.Null(vm.FeatureRef);
        Assert.Null(vm.DatasetFileName);
        Assert.Null(vm.ProductSpec);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void ClearCommand_InvokesClear()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("X", null, "1", null, null, System.Array.Empty<PickAttribute>());

        Assert.True(vm.HasPick);
        vm.ClearCommand.Execute(null);

        Assert.False(vm.HasPick);
    }

    [Fact]
    public void SetPicks_WithLocation_PopulatesLocationFields()
    {
        var vm = new PickReportViewModel();

        vm.SetPicks(
            new[]
            {
                new PickHit { FeatureType = "DepthArea", FeatureRef = "42" },
            },
            System.Array.Empty<DynamicPickHit>(),
            new PickLocation(47.601234, -122.334567));

        Assert.True(vm.HasLocation);
        Assert.Equal(new PickLocation(47.601234, -122.334567), vm.Location);
        Assert.False(string.IsNullOrEmpty(vm.LocationDisplay));
        Assert.True(vm.CopyLocationCommand.CanExecute(null));
    }

    [Fact]
    public void SetPicks_WithoutLocation_LeavesLocationEmpty()
    {
        var vm = new PickReportViewModel();

        vm.SetPicks(new[] { new PickHit { FeatureType = "DepthArea", FeatureRef = "42" } });

        Assert.False(vm.HasLocation);
        Assert.Null(vm.Location);
        Assert.Null(vm.LocationDisplay);
        Assert.False(vm.CopyLocationCommand.CanExecute(null));
    }

    [Fact]
    public void Clear_ResetsLocation()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(
            new[] { new PickHit { FeatureType = "DepthArea", FeatureRef = "42" } },
            System.Array.Empty<DynamicPickHit>(),
            new PickLocation(10.0, 20.0));

        vm.Clear();

        Assert.False(vm.HasLocation);
        Assert.Null(vm.Location);
        Assert.False(vm.CopyLocationCommand.CanExecute(null));
    }

    [Fact]
    public void CopyLocationCommand_RaisesRequestWithDecimalDegrees()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(
            new[] { new PickHit { FeatureType = "DepthArea", FeatureRef = "42" } },
            System.Array.Empty<DynamicPickHit>(),
            new PickLocation(47.601234, -122.334567));

        string? copied = null;
        vm.CopyLocationRequested += (_, text) => copied = text;

        vm.CopyLocationCommand.Execute(null);

        Assert.Equal("47.601234, -122.334567", copied);
    }

    [Fact]
    public void HasPickPropertyChanged_FiresOnSetPickAndClear()
    {
        var vm = new PickReportViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PickReportViewModel.HasPick))
                changed.Add(e.PropertyName);
        };

        vm.SetPick("X", null, "1", null, null, System.Array.Empty<PickAttribute>());
        vm.Clear();

        Assert.Equal(2, changed.Count);
    }

    private static PickHit Hit(string type, string typeName, string id, params PickAttribute[] attrs) =>
        new()
        {
            FeatureType = type,
            FeatureTypeName = typeName,
            FeatureRef = id,
            DatasetFileName = "ds.gml",
            ProductSpec = "S-101",
            Attributes = attrs,
        };

    [Fact]
    public void SetPicks_MultipleHits_PopulatesAndSelectsFirst()
    {
        var vm = new PickReportViewModel();

        var hits = new[]
        {
            Hit("DepthArea", "Depth Area", "1", Leaf("DRVAL1", "10")),
            Hit("LandArea", "Land Area", "2", Leaf("CATLAR", "1")),
            Hit("BuoyLateral", "Lateral Buoy", "3"),
        };

        vm.SetPicks(hits);

        Assert.True(vm.HasPick);
        Assert.True(vm.HasMultipleHits);
        Assert.Equal(3, vm.Hits.Count);
        Assert.Same(hits[0], vm.SelectedHit);
        Assert.Equal("DepthArea", vm.FeatureType);
        Assert.Equal("Depth Area", vm.FeatureTypeName);
        Assert.Equal("1", vm.FeatureRef);
        Assert.Single(vm.Attributes);
    }

    [Fact]
    public void SetPicks_SingleHit_HasMultipleHitsFalse()
    {
        var vm = new PickReportViewModel();

        vm.SetPicks(new[] { Hit("DepthArea", "Depth Area", "1") });

        Assert.True(vm.HasPick);
        Assert.False(vm.HasMultipleHits);
        Assert.Single(vm.Hits);
    }

    [Fact]
    public void SetPicks_EmptyList_ClearsPanel()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(new[] { Hit("X", null!, "1") });
        Assert.True(vm.HasPick);

        vm.SetPicks(System.Array.Empty<PickHit>());

        Assert.False(vm.HasPick);
        Assert.False(vm.HasMultipleHits);
        Assert.Empty(vm.Hits);
        Assert.Null(vm.SelectedHit);
    }

    [Fact]
    public void SelectedHit_Change_UpdatesDetailFields()
    {
        var vm = new PickReportViewModel();
        var hits = new[]
        {
            Hit("DepthArea", "Depth Area", "1", Leaf("DRVAL1", "10")),
            Hit("LandArea", "Land Area", "2", Leaf("CATLAR", "1"), Leaf("OBJNAM", "Foo")),
        };
        vm.SetPicks(hits);

        vm.SelectedHit = hits[1];

        Assert.Equal("LandArea", vm.FeatureType);
        Assert.Equal("Land Area", vm.FeatureTypeName);
        Assert.Equal("2", vm.FeatureRef);
        Assert.Equal(2, vm.Attributes.Count);
        Assert.Equal("CATLAR", vm.Attributes[0].Code);
        Assert.True(vm.HasAttributes);
    }

    [Fact]
    public void Clear_AfterMultiHit_ResetsHitListAndSelection()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(new[]
        {
            Hit("A", null!, "1"),
            Hit("B", null!, "2"),
        });

        vm.Clear();

        Assert.False(vm.HasPick);
        Assert.False(vm.HasMultipleHits);
        Assert.Empty(vm.Hits);
        Assert.Null(vm.SelectedHit);
    }

    [Fact]
    public void HitDisplayLabel_PrefersFeatureTypeName_FallsBackToCode()
    {
        var withName = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureTypeName = "Depth Area",
            FeatureRef = "1",
        };
        var withoutName = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureTypeName = null,
            FeatureRef = "1",
        };

        Assert.Equal("Depth Area", withName.DisplayLabel);
        Assert.Equal("DepthArea", withoutName.DisplayLabel);
    }

    private static FeatureReference Ref(string role, string target, string? arcRole = null)
        => new() { Role = role, TargetRef = target, ArcRole = arcRole };

    [Fact]
    public void SetPicks_PopulatesReferencesFromSelectedHit()
    {
        var vm = new PickReportViewModel();
        var hit = new PickHit
        {
            FeatureType = "LightLateral", FeatureTypeName = "Lateral Light", FeatureRef = "L1",
            References = new[] { Ref("AtonStatus", "S1"), Ref("SpatialAccuracy", "A2") },
        };

        vm.SetPicks(new[] { hit });

        Assert.True(vm.HasReferences);
        Assert.Equal(2, vm.References.Count);
        Assert.Equal("AtonStatus", vm.References[0].Role);
        Assert.Equal("S1", vm.References[0].TargetRef);
    }

    [Fact]
    public void ChangingSelectedHit_RefreshesReferences()
    {
        var vm = new PickReportViewModel();
        var hits = new[]
        {
            new PickHit
            {
                FeatureType = "LightLateral", FeatureRef = "L1",
                References = new[] { Ref("AtonStatus", "S1") },
            },
            new PickHit
            {
                FeatureType = "DepthArea", FeatureRef = "D1",
                References = System.Array.Empty<FeatureReference>(),
            },
        };
        vm.SetPicks(hits);
        Assert.True(vm.HasReferences);

        vm.SelectedHit = hits[1];

        Assert.False(vm.HasReferences);
        Assert.Empty(vm.References);
    }

    [Fact]
    public void Clear_EmptiesReferences()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(new[]
        {
            new PickHit
            {
                FeatureType = "X", FeatureRef = "1",
                References = new[] { Ref("a", "b") },
            },
        });

        vm.Clear();

        Assert.False(vm.HasReferences);
        Assert.Empty(vm.References);
    }

    [Fact]
    public void NavigateCommand_RoutesParameterToHandler()
    {
        var vm = new PickReportViewModel();
        FeatureReference? captured = null;
        vm.NavigateRequested += (_, r) => captured = r;

        var reference = Ref("role", "target");
        Assert.True(vm.NavigateCommand.CanExecute(reference));
        vm.NavigateCommand.Execute(reference);

        Assert.NotNull(captured);
        Assert.Equal("target", captured!.TargetRef);
    }

    [Fact]
    public void NavigateCommand_NullParameter_DoesNothing()
    {
        var vm = new PickReportViewModel();
        var invoked = false;
        vm.NavigateRequested += (_, _) => invoked = true;

        Assert.False(vm.NavigateCommand.CanExecute(null));
        vm.NavigateCommand.Execute(null);

        Assert.False(invoked);
    }

    private sealed class StubMarinerSettings : IMarinerSettingsProvider
    {
        public MarinerSettings Current { get; private set; }
        public StubMarinerSettings(MarinerSettings initial) => Current = initial;
        public event Action<MarinerSettings>? Changed;
        public void Update(MarinerSettings value)
        {
            Current = value;
            Changed?.Invoke(value);
        }
    }

    [Fact]
    public void SetPick_FormatsDepthAttribute_InCurrentDepthUnit()
    {
        var mariner = new StubMarinerSettings(new MarinerSettings { DepthUnit = DepthUnit.Feet });
        var vm = new PickReportViewModel(timeFormat: null, marinerSettings: mariner);

        vm.SetPick(
            featureType: "DepthArea",
            featureTypeName: "Depth Area",
            featureRef: "1",
            datasetFileName: "x.000",
            productSpec: "S-101",
            attributes: new[]
            {
                new PickAttribute
                {
                    Code = "DRVAL1",
                    Name = "Depth Range Min",
                    RawValue = "10",
                    DisplayValue = "10",
                    DepthMetresValue = 10.0,
                    Children = Array.Empty<PickAttribute>(),
                },
            });

        // 10 m → ~32.8 ft via DepthFormatting.
        Assert.Contains("ft", vm.Attributes[0].DisplayValue!);
    }

    [Fact]
    public void DepthUnitChange_Rewrites_DepthAttribute_DisplayValue_Live()
    {
        var mariner = new StubMarinerSettings(new MarinerSettings { DepthUnit = DepthUnit.Metres });
        var vm = new PickReportViewModel(timeFormat: null, marinerSettings: mariner);

        vm.SetPick(
            featureType: "DepthArea",
            featureTypeName: "Depth Area",
            featureRef: "1",
            datasetFileName: "x.000",
            productSpec: "S-101",
            attributes: new[]
            {
                new PickAttribute
                {
                    Code = "VALDCO",
                    Name = "Value of Depth Contour",
                    RawValue = "5",
                    DisplayValue = "5 m",
                    DepthMetresValue = 5.0,
                    Children = Array.Empty<PickAttribute>(),
                },
            });

        Assert.Contains("m", vm.Attributes[0].DisplayValue!);

        mariner.Update(new MarinerSettings { DepthUnit = DepthUnit.Feet });

        Assert.Contains("ft", vm.Attributes[0].DisplayValue!);
    }

    // PR-D4: dynamic-source pick coverage.

    private static DynamicPickHit DynamicHit(string id = "ais.test", string label = "MV ALPHA") => new()
    {
        SourceId = "ais",
        SourceDisplayName = "AIS",
        FeatureId = id,
        Kind = "vessel.ais.cargo",
        DisplayLabel = label,
        LastUpdated = DateTimeOffset.UtcNow,
        Latitude = 47.6,
        Longitude = -122.3,
    };

    [Fact]
    public void SetPicks_WithOnlyDynamicHits_MarksHasPickAndPopulatesDynamicSection()
    {
        var vm = new PickReportViewModel();

        vm.SetPicks(Array.Empty<PickHit>(), new[] { DynamicHit() });

        Assert.True(vm.HasPick);
        Assert.True(vm.HasDynamicHits);
        Assert.False(vm.HasDatasetPick);
        Assert.Single(vm.DynamicHits);
        Assert.Equal("MV ALPHA", vm.DynamicHits[0].DisplayLabel);
        Assert.Null(vm.SelectedHit);
        // Dataset detail fields stay clear when no dataset hit is present.
        Assert.Null(vm.FeatureType);
        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void SetPicks_WithBoth_KeepsDatasetSelectionAndDynamicList()
    {
        var vm = new PickReportViewModel();
        var datasetHit = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureRef = "42",
            Attributes = Array.Empty<PickAttribute>(),
        };

        vm.SetPicks(new[] { datasetHit }, new[] { DynamicHit() });

        Assert.True(vm.HasPick);
        Assert.True(vm.HasDatasetPick);
        Assert.True(vm.HasDynamicHits);
        Assert.Same(datasetHit, vm.SelectedHit);
        Assert.Equal("DepthArea", vm.FeatureType);
        Assert.Single(vm.DynamicHits);
    }

    [Fact]
    public void Clear_AlsoClearsDynamicHits()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(Array.Empty<PickHit>(), new[] { DynamicHit() });
        Assert.True(vm.HasDynamicHits);

        vm.Clear();

        Assert.False(vm.HasPick);
        Assert.False(vm.HasDynamicHits);
        Assert.Empty(vm.DynamicHits);
    }

    [Fact]
    public void SetPicks_WithBothEmpty_ClearsState()
    {
        var vm = new PickReportViewModel();
        vm.SetPicks(new[] { new PickHit { FeatureType = "X", FeatureRef = "1" } }, Array.Empty<DynamicPickHit>());
        Assert.True(vm.HasPick);

        vm.SetPicks(Array.Empty<PickHit>(), Array.Empty<DynamicPickHit>());

        Assert.False(vm.HasPick);
    }

    private static DynamicPickHit Hit(string featureId) => new()
    {
        SourceId = "ais.aisstream",
        SourceDisplayName = "AIS",
        FeatureId = featureId,
        DisplayLabel = featureId,
        LastUpdated = DateTimeOffset.UtcNow,
        Latitude = 50.0,
        Longitude = -1.0,
    };

    [Theory]
    [InlineData("ais:123456789", true, 123456789u)]
    [InlineData("ais:1", true, 1u)]
    [InlineData("ais:0", false, 0u)]
    [InlineData("ownship", false, 0u)]
    [InlineData("ais:notanumber", false, 0u)]
    [InlineData("", false, 0u)]
    public void TryGetAisMmsi_ParsesOnlyAisFeatureIds(string featureId, bool expected, uint expectedMmsi)
    {
        var ok = PickReportViewModel.TryGetAisMmsi(Hit(featureId), out var mmsi);

        Assert.Equal(expected, ok);
        if (expected)
            Assert.Equal(expectedMmsi, mmsi);
    }

    [Fact]
    public void TakeHelmCommand_OnAisHit_RaisesRequestedWithMmsi()
    {
        var vm = new PickReportViewModel();
        uint? raised = null;
        vm.TakeHelmRequested += (_, mmsi) => raised = mmsi;

        var hit = Hit("ais:987654321");
        Assert.True(vm.TakeHelmCommand.CanExecute(hit));
        vm.TakeHelmCommand.Execute(hit);

        Assert.Equal(987654321u, raised);
    }

    [Fact]
    public void TakeHelmCommand_OnNonAisHit_IsDisabledAndDoesNotRaise()
    {
        var vm = new PickReportViewModel();
        var raised = false;
        vm.TakeHelmRequested += (_, _) => raised = true;

        var hit = Hit("ownship");
        Assert.False(vm.TakeHelmCommand.CanExecute(hit));
        vm.TakeHelmCommand.Execute(hit);

        Assert.False(raised);
    }

    [Fact]
    public void IsAisTarget_TracksFeatureId()
    {
        Assert.True(Hit("ais:42").IsAisTarget);
        Assert.False(Hit("ownship").IsAisTarget);
    }

    [Fact]
    public void Identity_WithNamedFeature_LeadsWithNameAndClassPill()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("BeaconLateral", "Beacon, Lateral", "555", "101GB00502793.000", "S-101",
            new[] { Leaf("name", "Number 10"), Leaf("colour", "3", "Colour") });

        Assert.Equal("Number 10", vm.PrimaryLabel);
        Assert.Equal("Beacon, Lateral", vm.SecondaryLabel);
        Assert.True(vm.HasSecondaryLabel);
        Assert.Equal("ID 555 · S-101 · 101GB00502793.000", vm.IdentityCaption);
    }

    [Fact]
    public void Identity_WithoutName_LeadsWithClassAndNoPill()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("DepthArea", "Depth Area", "300", "test.000", "S-101",
            new[] { Leaf("DRVAL1", "10.0") });

        Assert.Equal("Depth Area", vm.PrimaryLabel);
        Assert.Null(vm.SecondaryLabel);
        Assert.False(vm.HasSecondaryLabel);
        Assert.Equal("ID 300 · S-101 · test.000", vm.IdentityCaption);
    }

    [Fact]
    public void IdentityCaption_OmitsMissingSegments()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("Authority", null, "auth.1", null, null, System.Array.Empty<PickAttribute>());

        Assert.Equal("ID auth.1", vm.IdentityCaption);
    }

    [Fact]
    public void CopyIdentityCommand_RaisesRequestWithNameAndCaption()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("BeaconLateral", "Beacon, Lateral", "555", "f.000", "S-101",
            new[] { Leaf("name", "Number 10") });

        string? captured = null;
        vm.CopyIdentityRequested += (_, text) => captured = text;

        Assert.True(vm.CopyIdentityCommand.CanExecute(null));
        vm.CopyIdentityCommand.Execute(null);

        Assert.Equal("Number 10\nID 555 · S-101 · f.000", captured);
    }

    [Fact]
    public void Identity_ClearedAfterClear()
    {
        var vm = new PickReportViewModel();
        vm.SetPick("BeaconLateral", "Beacon, Lateral", "555", "f.000", "S-101",
            new[] { Leaf("name", "Number 10") });

        vm.Clear();

        Assert.Null(vm.PrimaryLabel);
        Assert.Null(vm.IdentityCaption);
        Assert.False(vm.CopyIdentityCommand.CanExecute(null));
    }
}
