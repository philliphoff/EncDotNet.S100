using SkiaSharp;
using VerifyTests;

namespace EncDotNet.S100.Testing.Rendering;

/// <summary>
/// Glue between <see cref="PerceptualImageComparer"/> /
/// <see cref="DiffImageWriter"/> and the Verify.NET snapshot framework.
/// </summary>
/// <remarks>
/// Call <see cref="Initialize"/> once from a <c>[ModuleInitializer]</c> in the
/// consuming test project to install:
/// <list type="bullet">
///   <item>A perceptual stream comparer registered for the <c>.png</c>
///         extension (replaces the default byte-equality comparer).</item>
///   <item>A mismatch handler that writes <c>*.diff.png</c> next to the
///         received / verified files for inspection.</item>
///   <item>Disables the GUI diff viewer launcher so CI runs never block.</item>
/// </list>
/// Per-test threshold overrides are available via
/// <see cref="UsePerceptualImageComparer(SettingsTask, PerceptualImageComparer)"/>.
/// </remarks>
public static class VerifyRenderHarness
{
    private static bool _initialized;

    /// <summary>
    /// Registers the perceptual PNG comparer and mismatch diff writer on the
    /// global Verify settings. Safe to call multiple times.
    /// </summary>
    /// <param name="defaultComparer">
    /// Optional comparer to use as the default when no per-test override is
    /// supplied. Defaults to <see cref="PerceptualImageComparer.Default"/>.
    /// </param>
    public static void Initialize(PerceptualImageComparer? defaultComparer = null)
    {
        if (_initialized) return;
        _initialized = true;

        // Disable the GUI diff tool launcher so headless / CI runs don't hang.
        Environment.SetEnvironmentVariable("DiffEngine_Disabled", "true");

        var comparer = defaultComparer ?? PerceptualImageComparer.Default;

        VerifierSettings.RegisterStreamComparer(
            "png",
            (received, verified, _) => CompareAsync(received, verified, comparer));

        VerifierSettings.OnVerifyMismatch(WriteDiffOnMismatch);
    }

    /// <summary>
    /// Overrides the perceptual comparer for a single Verify call. Use when a
    /// particular test needs looser or stricter thresholds than the global
    /// default.
    /// </summary>
    public static SettingsTask UsePerceptualImageComparer(
        this SettingsTask settings, PerceptualImageComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        return settings.UseStreamComparer((received, verified, _) =>
            CompareAsync(received, verified, comparer));
    }

    private static async Task<CompareResult> CompareAsync(
        Stream received, Stream verified, PerceptualImageComparer comparer)
    {
        byte[] receivedBytes = await ReadAllAsync(received);
        byte[] verifiedBytes = await ReadAllAsync(verified);

        var result = comparer.Compare(verifiedBytes, receivedBytes);
        return result.AreEqual
            ? CompareResult.Equal
            : CompareResult.NotEqual(result.Reason ?? "Images differ.");
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        if (stream is MemoryStream ms)
        {
            return ms.ToArray();
        }

        using var copy = new MemoryStream();
        if (stream.CanSeek) stream.Position = 0;
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static Task WriteDiffOnMismatch(FilePair pair, string? message, bool autoVerify)
    {
        try
        {
            if (!string.Equals(pair.Extension, "png", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            if (!File.Exists(pair.ReceivedPath) || !File.Exists(pair.VerifiedPath))
            {
                return Task.CompletedTask;
            }

            var receivedBytes = File.ReadAllBytes(pair.ReceivedPath);
            var verifiedBytes = File.ReadAllBytes(pair.VerifiedPath);

            var diffPath = Path.ChangeExtension(pair.ReceivedPath, ".diff.png");
            DiffImageWriter.Write(verifiedBytes, receivedBytes, diffPath);
        }
        catch
        {
            // Diff writing must never break a test run; swallow errors so the
            // primary mismatch message is what surfaces.
        }

        return Task.CompletedTask;
    }
}
