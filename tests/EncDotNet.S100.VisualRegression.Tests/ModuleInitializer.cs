using System.Runtime.CompilerServices;
using EncDotNet.S100.VisualRegression;
using VerifyTests;

namespace EncDotNet.S100.VisualRegression.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyRenderHarness.Initialize();

        // Anchor *.verified.png snapshots in the source tree (Snapshots/<TestClass>/)
        // rather than in the build output directory, so they survive
        // `dotnet clean` and live in source control.
        DerivePathInfo((sourceFile, projectDirectory, type, method) =>
        {
            var directory = Path.Combine(projectDirectory, "Snapshots", type.Name);
            Directory.CreateDirectory(directory);
            return new PathInfo(directory: directory, typeName: type.Name, methodName: method.Name);
        });
    }
}
