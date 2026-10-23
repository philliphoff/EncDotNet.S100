using System.Reflection;
using PublicApiGenerator;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Compares an assembly's public API with a checked-in baseline file, so a
/// change to the public surface shows up in review as a diff of that file.
/// </summary>
/// <remarks>
/// <para>
/// The surface is generated with PublicApiGenerator as C# declarations, one
/// type per block, and stored at <c>src/&lt;AssemblyName&gt;/PublicApi.txt</c>.
/// A test project opts in with <c>&lt;EnablePublicApiBaseline&gt;true&lt;/EnablePublicApiBaseline&gt;</c>
/// (which links this file and references the generator) and one test that
/// calls <see cref="Verify(Assembly)"/>.
/// </para>
/// <para>
/// To accept a change, run the test with <c>S100_UPDATE_API_BASELINE=1</c>:
/// it rewrites the baseline instead of failing. See
/// <c>docs/design/api-conventions.md</c>, "Public API baselines".
/// </para>
/// </remarks>
internal static class PublicApiBaseline
{
    /// <summary>The environment variable that makes <see cref="Verify(Assembly)"/> rewrite the baseline.</summary>
    public const string UpdateVariable = "S100_UPDATE_API_BASELINE";

    private const string BaselineFileName = "PublicApi.txt";

    /// <summary>
    /// Fails when <paramref name="assembly"/>'s public API differs from its
    /// baseline, or rewrites the baseline when <see cref="UpdateVariable"/> is set.
    /// </summary>
    /// <param name="assembly">The library assembly whose public surface is locked.</param>
    public static void Verify(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var name = assembly.GetName().Name!;
        var path = Path.Combine(FindRepositoryRoot(), "src", name, BaselineFileName);
        var actual = Normalize(assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            // Assembly attributes (InternalsVisibleTo, TargetFramework and
            // the like) are build plumbing, not API.
            ExcludeAttributes = ["System.Runtime.CompilerServices.InternalsVisibleToAttribute"],
            IncludeAssemblyAttributes = false,
        }));

        if (IsUpdateRequested())
        {
            File.WriteAllText(path, actual);
            return;
        }

        if (!File.Exists(path))
        {
            Assert.Fail(
                $"No public API baseline for {name} at {path}. Create it with " +
                $"{UpdateVariable}=1 dotnet test tests/{name}.Tests");
        }

        var expected = Normalize(File.ReadAllText(path));
        if (expected == actual)
        {
            return;
        }

        Assert.Fail(
            $"The public API of {name} no longer matches {Path.GetRelativePath(FindRepositoryRoot(), path)}.\n" +
            $"If the change is intended, update the baseline with\n" +
            $"  {UpdateVariable}=1 dotnet test tests/{name}.Tests\n" +
            $"and commit it with the change.\n\n" +
            Diff(expected, actual));
    }

    private static bool IsUpdateRequested()
    {
        var value = Environment.GetEnvironmentVariable(UpdateVariable);
        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    // Line endings follow the checkout (autocrlf), so compare and write LF.
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    // A longest-common-subsequence line diff. Each changed line is prefixed
    // with the type it belongs to, because member lines such as
    // "public string Name { get; }" repeat across types.
    private static string Diff(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var lines = new List<string>();
        string? type = null;
        void Add(char sign, string line) =>
            lines.Add(IsTypeLine(line) || type is null ? $"{sign} {line.Trim()}" : $"{sign} {line.Trim()}    (in {type})");

        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                type = IsTypeLine(a[x]) ? TypeName(a[x]) : type;
                x++;
                y++;
            }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                Add('+', b[y]);
                type = IsTypeLine(b[y]) ? TypeName(b[y]) : type;
                y++;
            }
            else
            {
                Add('-', a[x]);
                type = IsTypeLine(a[x]) ? TypeName(a[x]) : type;
                x++;
            }
        }

        return string.Join('\n', lines);
    }

    // Types sit at one level of indentation inside their namespace block.
    private static bool IsTypeLine(string line) =>
        line.StartsWith("    public ", StringComparison.Ordinal) && !line.StartsWith("     ", StringComparison.Ordinal);

    private static string TypeName(string line)
    {
        var declaration = line.Trim();
        var colon = declaration.IndexOf(" : ", StringComparison.Ordinal);
        return colon < 0 ? declaration : declaration[..colon];
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EncDotNet.S100.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not find EncDotNet.S100.slnx above {AppContext.BaseDirectory}.");
    }
}
