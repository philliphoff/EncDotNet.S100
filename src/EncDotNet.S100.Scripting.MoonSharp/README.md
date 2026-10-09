# EncDotNet.S100.Scripting.MoonSharp

`EncDotNet.S100.Scripting.MoonSharp` implements the `ILuaEngine` and
`ILuaContext` interfaces from
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) with
[MoonSharp](https://github.com/moonsharp-devs/moonsharp), a Lua 5.2
interpreter written in .NET. The S-100 Part 9A Lua portrayal (S-101, S-131,
S-102 and others) runs on it. Reference it directly when you build a dataset
pipeline yourself or run Lua scripts through the core interfaces.

## Install

```bash
dotnet add package EncDotNet.S100.Scripting.MoonSharp
```

## Example: run a script

```csharp
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.MoonSharp;

ILuaEngine engine = new MoonSharpLuaEngine();
using ILuaContext lua = engine.CreateContext();

lua.Execute("function add(a, b) return a + b end");
object? sum = lua.Call("add", 2, 3);
```

## Main entry points

- `MoonSharpLuaEngine` creates Lua contexts. Each context runs in a sandbox
  with no OS, IO or debug access. `Modules` sets the MoonSharp core modules
  loaded into each new context; the default is that sandboxed set.
- `ILuaContext` (namespace `EncDotNet.S100.Scripting`) runs code, gets and
  sets globals, and calls functions. `SetModuleLoader` supplies the source for
  Lua `require()` calls, for example from a portrayal catalogue's rule files.
