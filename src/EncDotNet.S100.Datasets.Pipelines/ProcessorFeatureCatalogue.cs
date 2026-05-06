using System;
using System.IO;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Shared bootstrap for processor-side Feature Catalogue loading. Each
/// processor calls <see cref="TryLoadDecoder"/> in its constructor with
/// the host-supplied <c>Func&lt;string,Stream?&gt;</c> resolver; when the
/// resolver returns a stream, the FC is parsed once and wrapped in a
/// <see cref="FeatureCatalogueDecoder"/> that survives for the lifetime
/// of the processor. When no resolver / no stream is supplied (e.g. unit
/// tests, CLI tools without the bundled <c>EncDotNet.S100.Specifications</c>
/// dependency), the result is null and pick output falls back to raw codes.
/// </summary>
internal static class ProcessorFeatureCatalogue
{
    public static FeatureCatalogueDecoder? TryLoadDecoder(
        Func<string, Stream?>? resolver,
        string productSpec)
    {
        if (resolver is null) return null;

        Stream? stream;
        try { stream = resolver(productSpec); }
        catch { return null; }

        if (stream is null) return null;

        try
        {
            using (stream)
            {
                var fc = FeatureCatalogueReader.Read(stream);
                return new FeatureCatalogueDecoder(fc);
            }
        }
        catch
        {
            // FC parse failures must not break dataset loading; pick output
            // simply degrades to raw codes.
            return null;
        }
    }
}
