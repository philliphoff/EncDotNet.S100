# EncDotNet.S100.Scripting.LuaCSharp

`EncDotNet.S100.Scripting.LuaCSharp` implements the `ILuaEngine` and
`ILuaContext` interfaces from
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) with
[Lua-CSharp](https://github.com/nuskey8/Lua-CSharp), a Lua 5.2 interpreter
written in C#. It's the default engine for the S-100 Part 9a Lua portrayal
(S-101, S-57 through the S-101 rules, S-401, S-131, S-125 and S-102).
[`EncDotNet.S100.Scripting.MoonSharp`](../EncDotNet.S100.Scripting.MoonSharp/README.md)
implements the same interfaces and stays available for comparison. Reference
this package directly when you build a dataset pipeline yourself or run Lua
scripts through the core interfaces.

## Install

```bash
dotnet add package EncDotNet.S100.Scripting.LuaCSharp
```

## Example: run a script

```csharp
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.LuaCSharp;

ILuaEngine engine = new LuaCSharpLuaEngine();
using ILuaContext lua = engine.CreateContext();

lua.Execute("function add(a, b) return a + b end");
object? sum = lua.Call("add", 2, 3);
```

## Main entry points

- `LuaCSharpLuaEngine` creates Lua contexts. Each context runs in a sandbox:
  the basic, `bit32`, coroutine, math, package, string and table libraries are
  open; `io`, `os` and `debug` aren't. The context has no file system, so
  `dofile`, `loadfile` and `require` can't read files, and `print` output is
  discarded.
- `ILuaContext` (namespace `EncDotNet.S100.Scripting`) runs code, gets and
  sets globals, and calls functions. `SetModuleLoader` supplies the source for
  Lua `require()` calls, for example from a portrayal catalogue's rule files.
  `CallCancellable` stops a running call when its `CancellationToken` is
  cancelled.
- `LuaEngineKind` and `LuaEngineKinds` (namespace `EncDotNet.S100.Scripting`)
  name the engines, for a host that lets the user choose one.
  `LuaEngineFactory` in the [`EncDotNet.S100`](../EncDotNet.S100/README.md)
  package creates either.

## Behaviour

Lua-CSharp's API is asynchronous. The context completes each call before it
returns, because the host functions it registers are synchronous.

Host functions are .NET delegates passed to `SetGlobal`. The common
`Func`/`Action` shapes over `double`, `string` and reference types are called
directly; any other delegate works through `Delegate.DynamicInvoke`, which is
slower. A missing or `nil` argument becomes the parameter type's default.

An error description from `DescribeError` ends with the Lua stack, whose lines
give the chunk and line, for example `PortrayalAPI.lua:121`. Module chunks are
named after the module.

Two shims restore Lua 5.2 behaviour that Lua-CSharp 0.5.8 deviates from and
that the S-100 Part 9a framework depends on. Each lives in its own file under
`Compatibility/` and can be deleted when a Lua-CSharp release fixes the
deviation:

- `EqDispatchShim`: the `__eq` metamethod runs only when both operands are
  tables (or both userdata) with the same handler. Lua-CSharp also calls it
  for `nil`, numbers and tables with a different handler.
- `RequireShim`: `require` records a module whose chunk returns nothing, so the
  chunk runs once. Lua-CSharp runs it again on every `require`.

The engines differ where Lua leaves behaviour to the implementation. The one
that changes portrayal output: `table.sort` isn't stable in reference Lua or
in Lua-CSharp, but is in MoonSharp, so a sectored light whose sectors share a
range can list its colours in a different order. Lua-CSharp's order matches
reference Lua.
