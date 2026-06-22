using System.Runtime.CompilerServices;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Guards the GPU-residency registry teardown in <see cref="S100VectorTileRenderer"/>
/// that frees a torn-down tiled ("B") layer's GPU objects — its texture cache and
/// its rotated off-screen composite (<c>RotationSurface</c>/<c>RotationImage</c>)
/// — on the render thread rather than letting the GC finalizer thread reclaim them
/// off-thread. The latter races a live render thread inside the native Skia GPU
/// backend and crashes the process (observed when switching the render subsystem
/// from "B" tiled to "A" Mapsui, which re-portrays and swaps in fresh layers).
///
/// A real GPU <see cref="SKSurface"/>/<see cref="GRContext"/> is unavailable in a
/// headless test, but the registry's lifecycle management is identical for CPU- and
/// GPU-backed resources, so these exercise it with CPU-backed surfaces and a
/// sentinel context. The whole class touches the process-wide registry, so each
/// test clears it afterwards for isolation.
/// </summary>
public sealed class GpuRegistryTeardownTests
{
    // Register an entry whose owning layer is reachable ONLY from the (weak)
    // registry once this method returns, so a GC can collect it. Kept out-of-line
    // so the layer is not rooted by the caller's stack frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterWithCollectableLayer(TileCache cache, object context, SKSurface surface, SKImage image)
    {
        var layer = new MemoryLayer("dead");
        S100VectorTileRenderer.RegisterGpuEntryForTest(layer, cache, context, surface, image);
    }

    [Fact]
    public void ReconcileGpuCaches_OwningLayerCollected_DisposesRotationCompositeAndRemovesEntry()
    {
        try
        {
            var context = new object();
            var cache = new TileCache(1024 * 1024);
            var surface = SKSurface.Create(new SKImageInfo(8, 8));
            var image = surface.Snapshot();

            RegisterWithCollectableLayer(cache, context, surface, image);
            Assert.Equal(1, S100VectorTileRenderer.GpuRegistryEntryCountForTest);

            // Drop the only (weak) path to the layer.
            for (var i = 0; i < 4; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            S100VectorTileRenderer.ReconcileGpuCachesForTest(context);

            // The dead layer's entry is gone and its GPU rotation composite was
            // disposed on this (render) thread — never left for the finalizer.
            Assert.Equal(0, S100VectorTileRenderer.GpuRegistryEntryCountForTest);
            Assert.True(IsDisposed(surface));
            Assert.True(IsDisposed(image));
        }
        finally
        {
            S100VectorTileRenderer.ClearGpuRegistryForTest();
        }
    }

    [Fact]
    public void ReconcileGpuCaches_LiveLayer_RetainsEntryAndDoesNotDisposeRotationComposite()
    {
        var layer = new MemoryLayer("live");
        var surface = SKSurface.Create(new SKImageInfo(8, 8));
        var image = surface.Snapshot();
        try
        {
            var context = new object();
            var cache = new TileCache(1024 * 1024);
            S100VectorTileRenderer.RegisterGpuEntryForTest(layer, cache, context, surface, image);

            for (var i = 0; i < 4; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            S100VectorTileRenderer.ReconcileGpuCachesForTest(context);

            // A live layer keeps its entry and its (still-needed) GPU resources.
            Assert.Equal(1, S100VectorTileRenderer.GpuRegistryEntryCountForTest);
            Assert.False(IsDisposed(surface));
            Assert.False(IsDisposed(image));
        }
        finally
        {
            GC.KeepAlive(layer);
            S100VectorTileRenderer.ClearGpuRegistryForTest();
        }
    }

    [Fact]
    public void ReconcileGpuCaches_DeadLayerDifferentContext_RemovesEntryButLeavesResourcesForOwningContext()
    {
        var surface = SKSurface.Create(new SKImageInfo(8, 8));
        var image = surface.Snapshot();
        try
        {
            var owningContext = new object();
            var otherContext = new object();
            var cache = new TileCache(1024 * 1024);

            RegisterWithCollectableLayer(cache, owningContext, surface, image);

            for (var i = 0; i < 4; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            // Reconciling under a *different* context must not free resources bound
            // to the owning context (freeing under the wrong GPU context crashes);
            // the dead entry is dropped so the owning context can reclaim them.
            S100VectorTileRenderer.ReconcileGpuCachesForTest(otherContext);

            Assert.Equal(0, S100VectorTileRenderer.GpuRegistryEntryCountForTest);
            Assert.False(IsDisposed(surface));
            Assert.False(IsDisposed(image));
        }
        finally
        {
            surface.Dispose();
            image.Dispose();
            S100VectorTileRenderer.ClearGpuRegistryForTest();
        }
    }

    // SkiaSharp surfaces the native handle as IntPtr.Zero once disposed.
    private static bool IsDisposed(SKObject obj) => obj.Handle == nint.Zero;
}
