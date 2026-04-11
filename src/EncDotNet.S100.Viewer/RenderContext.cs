using System;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Base class for spec-specific render contexts passed to dataset processors.
/// </summary>
internal abstract record RenderContext;

internal sealed record S101RenderContext : RenderContext;

internal sealed record S102RenderContext : RenderContext;

internal sealed record S111RenderContext(DateTime? TimeStep = null) : RenderContext;
