# EncDotNet.S100.Scripting.MoonSharp

Lua scripting engine implementation using [MoonSharp](https://github.com/moonsharp-devs/moonsharp), a pure .NET Lua 5.2 interpreter.

## Overview

This library implements the `ILuaEngine` and `ILuaContext` abstractions defined in `EncDotNet.S100.Core` using MoonSharp. Scripts run in a sandboxed environment with no OS, IO, or debug access. Key types include:

- **`MoonSharpLuaEngine`** — `ILuaEngine` implementation that creates Lua execution contexts.
- **`MoonSharpLuaContext`** — `ILuaContext` implementation for loading and executing Lua scripts.
- **`DelegateScriptLoader`** — custom MoonSharp script loader for resolving Lua modules from an `IAssetSource`.

## Installation

```sh
dotnet add package EncDotNet.S100.Scripting.MoonSharp
```
