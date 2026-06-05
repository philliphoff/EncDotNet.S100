using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for the validation findings overlay: the click-to-zoom
/// command on <see cref="ValidationFindingViewModel"/>, the
/// <see cref="ValidationOverlayService"/> selection-driven layer
/// lifecycle, and the severity → colour mapping in
/// <see cref="ValidationOverlayBuilder"/>.
/// </summary>
public class ValidationOverlayTests
{
    // ── Test fakes ───────────────────────────────────────────────────

    private sealed class FakeMapHost : IMapHost
    {
        public List<MRect> ZoomCalls { get; } = new();
        public List<ILayer> Overlays { get; } = new();

        public void AddLayer(ILayer layer) { }
        public void RemoveLayer(ILayer layer) { }
        public void ReorderDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers) { }
        public void ZoomToExtent(MRect extent) => ZoomCalls.Add(extent);
        public void SetViewportToExtent(MRect mercatorExtent) { }
        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }
        public void AddOverlayLayer(ILayer layer) => Overlays.Add(layer);
        public void RemoveOverlayLayer(ILayer layer) => Overlays.Remove(layer);
        public System.Threading.Tasks.Task<byte[]?> RenderCurrentViewToPngAsync(int widthPx, int heightPx, double pixelDensity, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult<byte[]?>(null);
    }

    private static ValidationFinding Finding(
        ValidationSeverity severity = ValidationSeverity.Info,
        GeoPosition? point = null,
        BoundingBox? bbox = null)
        => new()
        {
            RuleId = "TEST-1",
            Severity = severity,
            Message = "msg",
            Point = point,
            BoundingBox = bbox,
        };

    private static ValidationReport ReportOf(params ValidationFinding[] findings)
        => new(findings.ToImmutableArray(), RulesEvaluated: findings.Length,
            RulesWithFindings: findings.Length);

    // ── ValidationFindingViewModel.HasSpatialLocation truth table ────

    [Fact]
    public void HasSpatialLocation_IsFalse_WhenNeitherPointNorBoundingBox()
    {
        var vm = new ValidationFindingViewModel(Finding());
        Assert.False(vm.HasSpatialLocation);
        Assert.False(vm.ZoomToFindingCommand.CanExecute(null));
    }

    [Fact]
    public void HasSpatialLocation_IsTrue_WithPointOnly()
    {
        var vm = new ValidationFindingViewModel(Finding(point: new GeoPosition(40, -70)));
        Assert.True(vm.HasSpatialLocation);
    }

    [Fact]
    public void HasSpatialLocation_IsTrue_WithBoundingBoxOnly()
    {
        var vm = new ValidationFindingViewModel(Finding(bbox: new BoundingBox(10, 20, 30, 40)));
        Assert.True(vm.HasSpatialLocation);
    }

    [Fact]
    public void HasSpatialLocation_IsTrue_WithBothPointAndBoundingBox()
    {
        var vm = new ValidationFindingViewModel(Finding(
            point: new GeoPosition(40, -70),
            bbox: new BoundingBox(10, 20, 30, 40)));
        Assert.True(vm.HasSpatialLocation);
    }

    // ── Command extent dispatch ──────────────────────────────────────

    [Fact]
    public void ZoomCommand_DispatchesBoundingBoxExtent()
    {
        var captured = new List<MRect>();
        var bbox = new BoundingBox(southLatitude: 10, westLongitude: 20, northLatitude: 30, eastLongitude: 40);
        var vm = new ValidationFindingViewModel(Finding(bbox: bbox), extent => captured.Add(extent));

        vm.ZoomToFindingCommand.Execute(null);

        var expected = MercatorRectOf(bbox);
        Assert.Single(captured);
        AssertRectClose(expected, captured[0]);
    }

    [Fact]
    public void ZoomCommand_DispatchesBoundingBox_WhenBothFieldsPresent()
    {
        var captured = new List<MRect>();
        var bbox = new BoundingBox(10, 20, 30, 40);
        var vm = new ValidationFindingViewModel(
            Finding(point: new GeoPosition(45, 25), bbox: bbox),
            extent => captured.Add(extent));

        vm.ZoomToFindingCommand.Execute(null);

        Assert.Single(captured);
        AssertRectClose(MercatorRectOf(bbox), captured[0]);
    }

    [Fact]
    public void ZoomCommand_DispatchesPaddedSquare_ForPointOnly()
    {
        var captured = new List<MRect>();
        var vm = new ValidationFindingViewModel(
            Finding(point: new GeoPosition(40, -70)),
            extent => captured.Add(extent));

        vm.ZoomToFindingCommand.Execute(null);

        Assert.Single(captured);
        var (cx, cy) = SphericalMercator.FromLonLat(-70, 40);
        var half = ValidationFindingViewModel.PointZoomHalfMetres;
        AssertRectClose(new MRect(cx - half, cy - half, cx + half, cy + half), captured[0]);
    }

    [Fact]
    public void ZoomCommand_NoOps_WhenNoSpatialLocation()
    {
        var captured = new List<MRect>();
        var vm = new ValidationFindingViewModel(Finding(), extent => captured.Add(extent));

        vm.ZoomToFindingCommand.Execute(null);

        Assert.Empty(captured);
        Assert.False(vm.ZoomToFindingCommand.CanExecute(null));
    }

    // ── ValidationOverlayService lifecycle ───────────────────────────

    [Fact]
    public void OverlayService_AddsNoLayer_WhenSelectionHasNoFindings()
    {
        var host = new FakeMapHost();
        var (vm, _) = MakeDatasetsViewModelWithEntry();

        using var svc = new ValidationOverlayService(host, vm);
        Assert.Empty(host.Overlays);
    }

    [Fact]
    public void OverlayService_AddsNoLayer_WhenFindingsHaveNoSpatialInfo()
    {
        var host = new FakeMapHost();
        var (vm, entry) = MakeDatasetsViewModelWithEntry();
        entry.SetValidationReport(ReportOf(Finding()));
        vm.SelectedEntry = entry;

        using var svc = new ValidationOverlayService(host, vm);
        Assert.Empty(host.Overlays);
    }

    [Fact]
    public void OverlayService_AddsLayer_WithFeaturePerSpatialFinding()
    {
        var host = new FakeMapHost();
        var (vm, entry) = MakeDatasetsViewModelWithEntry();
        entry.SetValidationReport(ReportOf(
            Finding(point: new GeoPosition(40, -70)),
            Finding(bbox: new BoundingBox(10, 20, 30, 40)),
            Finding()));
        vm.SelectedEntry = entry;

        using var svc = new ValidationOverlayService(host, vm);

        Assert.Single(host.Overlays);
        var layer = Assert.IsType<MemoryLayer>(host.Overlays[0]);
        Assert.Equal(2, CountFeatures(layer));
    }

    [Fact]
    public void OverlayService_RebuildsLayer_OnSelectionChange()
    {
        var host = new FakeMapHost();
        var loader = new FakeDatasetLoaderService();
        var vm = new DatasetsViewModel(loader);
        var withSpatial = new DatasetEntry("/tmp/a.gml", "S-125");
        withSpatial.SetValidationReport(ReportOf(Finding(point: new GeoPosition(40, -70))));
        var withoutSpatial = new DatasetEntry("/tmp/b.gml", "S-125");
        withoutSpatial.SetValidationReport(ReportOf(Finding()));
        vm.Entries.Add(withSpatial);
        vm.Entries.Add(withoutSpatial);

        using var svc = new ValidationOverlayService(host, vm);

        vm.SelectedEntry = withSpatial;
        Assert.Single(host.Overlays);

        vm.SelectedEntry = withoutSpatial;
        Assert.Empty(host.Overlays);

        vm.SelectedEntry = withSpatial;
        Assert.Single(host.Overlays);

        vm.SelectedEntry = null;
        Assert.Empty(host.Overlays);
    }

    [Fact]
    public void OverlayService_RemovesLayer_OnDispose()
    {
        var host = new FakeMapHost();
        var (vm, entry) = MakeDatasetsViewModelWithEntry();
        entry.SetValidationReport(ReportOf(Finding(point: new GeoPosition(40, -70))));
        vm.SelectedEntry = entry;

        var svc = new ValidationOverlayService(host, vm);
        Assert.Single(host.Overlays);

        svc.Dispose();
        Assert.Empty(host.Overlays);
    }

    // ── Builder severity → colour mapping ────────────────────────────

    [Fact]
    public void Builder_MapsSeveritiesToBadgePalette()
    {
        var error = ValidationOverlayBuilder.SeverityColor(ValidationSeverity.Error);
        var warning = ValidationOverlayBuilder.SeverityColor(ValidationSeverity.Warning);
        var info = ValidationOverlayBuilder.SeverityColor(ValidationSeverity.Info);

        Assert.Equal((0xD1, 0x34, 0x38), (error.R, error.G, error.B));
        Assert.Equal((0xCA, 0x50, 0x10), (warning.R, warning.G, warning.B));
        Assert.Equal((0x00, 0x7A, 0xCC), (info.R, info.G, info.B));
    }

    [Fact]
    public void Builder_SkipsFindings_WithoutSpatialInfo()
    {
        var layer = ValidationOverlayBuilder.Create();
        var vms = new[]
        {
            new ValidationFindingViewModel(Finding()),
            new ValidationFindingViewModel(Finding(point: new GeoPosition(40, -70))),
        };

        ValidationOverlayBuilder.Update(layer, vms);

        Assert.Equal(1, CountFeatures(layer));
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (DatasetsViewModel vm, DatasetEntry entry) MakeDatasetsViewModelWithEntry()
    {
        var loader = new FakeDatasetLoaderService();
        var vm = new DatasetsViewModel(loader);
        var entry = new DatasetEntry("/tmp/x.gml", "S-125");
        vm.Entries.Add(entry);
        return (vm, entry);
    }

    private static int CountFeatures(MemoryLayer layer)
    {
        int n = 0;
        foreach (var _ in layer.Features) n++;
        return n;
    }

    private static MRect MercatorRectOf(BoundingBox bbox)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(bbox.WestLongitude, bbox.SouthLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(bbox.EastLongitude, bbox.NorthLatitude);
        return new MRect(minX, minY, maxX, maxY);
    }

    private static void AssertRectClose(MRect expected, MRect actual, double tolerance = 1e-6)
    {
        Assert.InRange(actual.MinX, expected.MinX - tolerance, expected.MinX + tolerance);
        Assert.InRange(actual.MinY, expected.MinY - tolerance, expected.MinY + tolerance);
        Assert.InRange(actual.MaxX, expected.MaxX - tolerance, expected.MaxX + tolerance);
        Assert.InRange(actual.MaxY, expected.MaxY - tolerance, expected.MaxY + tolerance);
    }
}
