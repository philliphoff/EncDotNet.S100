# C# coding style guide

This is the **normative** coding style guide for the C#/.NET code in
EncDotNet.S100. It exists so that style is *explicit and documented* rather
than inferred from surrounding code. New code must follow it, and existing
code is migrated toward it opportunistically.

> **Scope.** This document covers *language, formatting, and naming* style.
> For *API-shape* decisions (collection return types, `class` vs `record`,
> quantity types) see the companion [API design
> conventions](design/api-conventions.md). For test conventions see
> [`CONTRIBUTING.md`](../CONTRIBUTING.md).

> **Enforcement.** Where a rule below can be checked mechanically it is
> intended to be encoded in `.editorconfig` / analyzers and enforced in CI
> (see the tracking issue). Until that encoding lands, treat this document as
> the source of truth. When the two disagree, the encoded rule wins and this
> document should be corrected.

## 1. Guiding principles

1. **Consistency over personal preference.** Match the established style even
   if you would personally choose differently. A uniform codebase is easier to
   read, review, and refactor mechanically.
2. **The compiler is the first reviewer.** The build sets
   `TreatWarningsAsErrors=true` (see `Directory.Build.props`); a warning is a
   failure. Do not suppress warnings to make the build pass — fix the cause.
3. **Nullability is part of the type.** Nullable reference types are enabled
   solution-wide. Model presence/absence honestly instead of defeating the
   analyzer.
4. **Prefer the standard tooling.** Formatting is whatever `dotnet format`
   produces from the shared `.editorconfig`; do not hand-format against it.

## 2. Files, namespaces, and usings

- **One top-level type per file**, and the file name matches the type
  (`SpecRef.cs` contains `SpecRef`). Small tightly-coupled helpers (a private
  nested type, a partial file) are the exception.
- **File-scoped namespaces** only:

  ```csharp
  namespace EncDotNet.S100.Core;
  ```

  Block-scoped (`namespace X { ... }`) namespaces are not used anywhere in the
  codebase and must not be introduced.
- **`ImplicitUsings` is enabled**, so do not add `using` directives for the
  implicit set (`System`, `System.Linq`, `System.Collections.Generic`, etc.).
  Add only the usings the implicit set does not cover.
- Place `using` directives at the top of the file, `System.*` first, then
  other namespaces, each group alphabetically ordered. Remove unused usings
  (they are warnings, and warnings fail the build).
- Use **UTF-8**, LF or platform-native line endings per `.editorconfig`, a
  trailing newline, and no trailing whitespace.

## 3. Formatting

- **4 spaces** per indent level. No tabs.
- **Allman braces** — the opening brace goes on its own line for types,
  methods, properties, and control-flow blocks:

  ```csharp
  public SpecRef(string name, SpecVersion edition)
  {
      Name = SpecName.Normalize(name);
      Edition = edition;
  }
  ```

- **Always brace multi-line blocks.** A single-statement `if` whose body is on
  the *same line* is acceptable for terse guard clauses
  (`if (firstDot <= 0) return false;`), but once the body wraps to the next
  line it must be braced. Do not mix a braced and unbraced arm in the same
  `if`/`else`.
- One statement per line; one declaration per line.
- Keep lines reasonably short (roughly 100–120 columns). Prose in doc comments
  in this codebase wraps around 72–76 columns — match the surrounding file.
- Use a single blank line to separate members and logical groups; never stack
  multiple consecutive blank lines.
- Use **expression-bodied members** for one-liners where they read well
  (`public override string ToString() => $"{Name}/{Edition}";`). Use a block
  body when the logic spans multiple statements.

## 4. Naming

| Element | Convention | Example |
|---|---|---|
| Namespace, type, method, property, event, enum member | `PascalCase` | `CoveragePipeline`, `TryParse` |
| Interface | `PascalCase` prefixed with `I` | `IAssetSource` |
| Type parameter | `PascalCase` prefixed with `T` | `TModel`, `TKey` |
| Local variable, parameter | `camelCase` | `versionPart`, `edition` |
| Private / internal instance field | `_camelCase` | `_assetSource` |
| Constant, `static readonly` | `PascalCase` | `MaxDepthBands` |
| Async method | `PascalCase` suffixed `Async` | `OpenFeatureCatalogueAsync` |

- Do **not** prefix with `this.` to disambiguate fields — the `_` field prefix
  already makes fields visually distinct, and `this.` is effectively unused in
  the codebase.
- Prefer descriptive names over abbreviations, except well-known domain terms
  (`Fc`, `Pc`, `Crs`, `Utm`, S-100 attribute acronyms). Spec-derived
  identifiers should match the spec's casing where practical and cite the
  section (see §8).

## 5. Language features and idioms

- **`var`** is the default for local declarations — it is used pervasively.
  Reach for an explicit type only when it materially improves readability or
  the right-hand side does not make the type obvious.
- Use **target-typed `new`** where the type is already stated
  (`SpecRef value = new(name, edition);` / `= []` for an empty collection).
- Prefer **collection expressions** (`[]`, `[a, b, c]`) and range/index
  operators (`s[..sep]`, `afterPrefix[(firstDot + 1)..]`) — both are used
  throughout.
- Prefer **pattern matching** and switch expressions over long `if`/`else if`
  ladders and `switch` statements where they read more clearly.
- Prefer **string interpolation** (`$"..."`) over concatenation. Pass an
  explicit `StringComparison` to string comparisons/`StartsWith`/`IndexOf`
  where culture matters (`StringComparison.OrdinalIgnoreCase` for
  identifiers/tokens).
- Use `is null` / `is not null` for reference equality checks.

## 6. Nullability and argument validation

- Never use the null-forgiving operator `!` to silence the analyzer. If a value
  is genuinely never null, express that in the type; if it can be null, handle
  it. `!` is reserved for the rare, commented case where an invariant cannot be
  encoded.
- Validate public-entry-point arguments up front:

  ```csharp
  ArgumentNullException.ThrowIfNull(source);
  ArgumentException.ThrowIfNullOrWhiteSpace(name);
  ```

- Follow the `Parse` / `TryParse` pair pattern for parsing: `TryParse` returns
  `bool` and never throws on malformed input; `Parse` delegates to it and
  throws `FormatException` (see `SpecRef`).
- Throw the most specific standard exception (`ArgumentException`,
  `ArgumentOutOfRangeException`, `InvalidOperationException`,
  `FormatException`, `NotSupportedException`) with a message that names the
  offending value.

## 7. Documentation comments

- **Every public and protected member carries XML doc comments** — at minimum
  `<summary>`, plus `<param>`, `<returns>`, and `<exception>` where
  applicable. This is required, not optional.
- Use `<see cref="..."/>` for cross-references, `<c>...</c>` for inline code,
  and `<remarks>`/`<para>` for rationale. Explain *why*, not just *what*, for
  non-obvious design choices (see `SpecRef` for the house style).
- Internal/private members are documented when the intent is non-obvious.
- **Implementation comments are for clarification only.** Do not narrate code
  that speaks for itself. Prefer a comment that captures a non-obvious
  invariant, a spec reference, or a "do not change this without …" warning.

## 8. Spec-derived code

- For constants, enums, attribute names, group paths, and element names taken
  from an IHO S-100 product specification, cite the spec and section in the XML
  doc comment (e.g. `S-104 §10.2.3 WaterLevel attribute names`).
- Consult the matching per-spec skill/instruction file before writing
  spec-semantic code (see the routing table in
  [`CONTRIBUTING.md`](../CONTRIBUTING.md#spec-routing-skills--instructions)).

## 9. Async

- Suffix async methods with `Async` and return `Task`/`Task<T>`/`ValueTask<T>`.
- Accept and honor a `CancellationToken` on async APIs that do I/O; flow it
  through to the calls you make.
- Do not expose `async void` except for event handlers.
- Avoid `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` — they invite
  deadlocks. Make the call chain async instead.

## 10. Dependencies and build

- NuGet versions are managed centrally in `Directory.Packages.props` via
  Central Package Management. Do **not** add `Version` attributes to individual
  `.csproj` files.
- Do not add `#pragma warning disable` or `<NoWarn>` to work around analyzer
  findings without a comment justifying the specific, local reason.
- Keep code cross-platform (CI builds on `ubuntu-latest`). Gate any
  platform-specific API to the appropriate runtime identifier.

---

*This guide is Phase 1 ("Define") of the coding-style initiative. Phase 2
encodes the mechanically-checkable rules here into `.editorconfig`/analyzers,
Phase 3 enforces them in CI, and Phase 4 refactors existing code into
compliance.*
