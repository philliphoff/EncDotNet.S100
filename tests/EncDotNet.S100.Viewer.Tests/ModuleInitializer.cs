using System.Runtime.CompilerServices;
using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.Viewer.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Rendered view frames (ViewHost.CaptureFrame) are verified with the same
        // perceptual PNG comparer and diff writer as the chart renders.
        VerifyRenderHarness.Initialize();

        // Keep *.verified.png in the source tree (Snapshots/<TestClass>/), as the
        // visual-regression tests do, so they survive `dotnet clean`.
        DerivePathInfo((sourceFile, projectDirectory, type, method) =>
        {
            var directory = Path.Combine(projectDirectory, "Snapshots", type.Name);
            Directory.CreateDirectory(directory);
            return new PathInfo(directory: directory, typeName: type.Name, methodName: method.Name);
        });
    }
}
