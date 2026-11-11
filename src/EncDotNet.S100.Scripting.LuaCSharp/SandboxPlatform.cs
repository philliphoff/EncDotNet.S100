using Lua;
using Lua.IO;
using Lua.Platforms;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// The <see cref="LuaPlatform"/> every context runs on: no file system, no
/// environment variables, no process exit, and standard streams that discard
/// output and read as empty.
/// </summary>
/// <remarks>
/// Lua-CSharp routes <c>dofile</c>, <c>loadfile</c>, the file searcher of
/// <c>require</c>, <c>print</c> and the <c>io</c>/<c>os</c> libraries
/// through the platform. The engine doesn't open <c>io</c>, <c>os</c> or
/// <c>debug</c>; this platform makes sure the functions that remain can't
/// reach the disk, the console or the process either. That matches
/// MoonSharp's hard sandbox, except that MoonSharp's <c>print</c> writes to
/// the console. Here it's discarded, so a stray <c>print</c> in a rule can't
/// corrupt a host that owns standard output, such as <c>s100 mcp serve</c>.
/// </remarks>
internal static class SandboxPlatform
{
    /// <summary>Creates a sandboxed platform for one Lua state.</summary>
    /// <remarks>
    /// One per state, because Lua-CSharp's streams buffer and a
    /// <see cref="LuaState"/> may run on any thread.
    /// </remarks>
    public static LuaPlatform Create() =>
        new(
            FileSystem: NoFileSystem.Instance,
            OsEnvironment: NoOsEnvironment.Instance,
            StandardIO: new NullStandardIO(),
            TimeProvider: TimeProvider.System);

    private sealed class NoFileSystem : ILuaFileSystem
    {
        public static readonly NoFileSystem Instance = new();

        public bool IsReadable(string path) => false;

        public ValueTask<ILuaStream> Open(string path, LuaFileOpenMode mode, CancellationToken cancellationToken) =>
            throw Denied(path);

        public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken) =>
            throw Denied(oldName);

        public ValueTask Remove(string path, CancellationToken cancellationToken) => throw Denied(path);

        public string GetTempFileName() => throw Denied("a temporary file");

        public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken) =>
            throw Denied("a temporary file");

        private static NotSupportedException Denied(string path) =>
            new($"The Lua sandbox has no file system; '{path}' can't be opened.");
    }

    private sealed class NoOsEnvironment : ILuaOsEnvironment
    {
        public static readonly NoOsEnvironment Instance = new();

        public string? GetEnvironmentVariable(string name) => null;

        public ValueTask Exit(int exitCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The Lua sandbox can't exit the process.");

        public double GetTotalProcessorTime() => 0;
    }

    private sealed class NullStandardIO : ILuaStandardIO
    {
        public ILuaStream Input { get; } = ILuaStream.CreateFromString(string.Empty);

        public ILuaStream Output { get; } = ILuaStream.CreateFromStream(Stream.Null, LuaFileOpenMode.Write);

        public ILuaStream Error { get; } = ILuaStream.CreateFromStream(Stream.Null, LuaFileOpenMode.Write);
    }
}
