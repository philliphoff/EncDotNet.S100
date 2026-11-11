# S-100 chrome theme variants — feasibility spike

**Status:** read-only research spike. No code changes, no commits. The output
of this note is a go / no-go recommendation for the user to decide whether
to invest in Phase 2.

**Question:** can we add **S100Day**, **S100Dusk**, and **S100Night** as
Avalonia `ThemeVariant`s that recolour the entire viewer chrome (title bar,
panels, buttons, charts, dialogs) using S-100-tuned palettes, sitting
alongside Avalonia's stock `Light` and `Dark`?

**Headline answer:** **Go — minimum viable scope.** Ship `S100Night` as
the only new chrome variant in a single PR. Light and Dark chrome stay
as-is; Day and Dusk S-100 chrome variants are deferred. Chrome drives
map palette by default (S100Night → Night map; Light/Dark → Day map),
with the map palette dropdown still freely editable for one-off
overrides. Estimate ~3.25 engineer-days. The plumbing is straightforward
(Avalonia 11 supports custom `ThemeVariant`s with inheritance fallback;
ShadUI 0.1.6 publishes ~30 `Color` keys we override sparsely), and the
maintenance footprint of *one* extra variant is bounded.

---

## 1. Avalonia 11's custom-variant story

Avalonia 11 (we're on 11.3.x — see `Directory.Packages.props`) supports
arbitrary theme variants out of the box. The relevant primitive is
`Avalonia.Styling.ThemeVariant`, source at
[`src/Avalonia.Base/Styling/ThemeVariant.cs`][1]:

```csharp
public sealed record ThemeVariant
{
    public ThemeVariant(object key, ThemeVariant? inheritVariant) { … }
    public object Key { get; }
    public ThemeVariant? InheritVariant { get; }

    public static ThemeVariant Default { get; }
    public static ThemeVariant Light   { get; }
    public static ThemeVariant Dark    { get; }
}
```

[1]: https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.11/src/Avalonia.Base/Styling/ThemeVariant.cs

Two facts matter:

1. **Custom keys are first-class.** A new `ThemeVariant("S100Night", …)` is
   a valid value for `Application.RequestedThemeVariant` and propagates
   through `ActualThemeVariant` exactly like the built-ins. The
   [theme variants guide][2] explicitly says variant keys can be anything
   the developer chooses; `Default` is reserved as the fallback bucket.

2. **`InheritVariant` is the killer feature.** A custom variant can name
   another variant as its inheritance root: when a `DynamicResource` lookup
   misses in the custom variant's dictionary, Avalonia walks the inherit
   chain instead of falling back to `Default`. So `S100Night` declared as
   `new ThemeVariant("S100Night", ThemeVariant.Dark)` automatically picks
   up every ShadUI Dark color we don't explicitly override. We can ship a
   sparse override dictionary — only the dozen-or-so keys we actually want
   to retune — without losing styling for anything else.

[2]: https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants

The minimum viable resource dictionary for one custom variant is roughly
this (App.axaml-level):

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.ThemeDictionaries>
      <ResourceDictionary x:Key="S100Night">
        <Color x:Key="WindowBackgroundColor">#0A0000</Color>
        <Color x:Key="ForegroundColor">#FFB0A0</Color>
        <!-- … other overrides … -->
      </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

Plus, in code, registration of the variant + its inherit chain (the XAML key
`"S100Night"` is matched by string against the `Key` of whatever
`ThemeVariant` instance we hand to `RequestedThemeVariant`). The inherit
behaviour does require constructing the variant programmatically:

```csharp
public static readonly ThemeVariant S100Night = new("S100Night", ThemeVariant.Dark);
public static readonly ThemeVariant S100Dusk  = new("S100Dusk",  ThemeVariant.Dark);
public static readonly ThemeVariant S100Day   = new("S100Day",   ThemeVariant.Light);
Application.Current!.RequestedThemeVariant = S100Night;
```

That's the entire registration story. There is no allow-list; no opt-in API
ShadUI or any other library has to provide.

---

## 2. ShadUI's theming model

Source: [`accntech/shad-ui`][3] on GitHub (and shipped binaries at
`~/.nuget/packages/shadui/0.1.6/`). The relevant files:

- `src/ShadUI/Themes/ShadTheme.axaml`
- `src/ShadUI/Themes/Light.axaml`
- `src/ShadUI/Themes/Dark.axaml`
- `src/ShadUI/Controls/Constants.axaml`
- `src/ShadUI/Controls/Resources.axaml` (merges every per-control
  resource file)

[3]: https://github.com/accntech/shad-ui/tree/main/src/ShadUI/Themes

`ShadTheme.axaml` is dead simple:

```xml
<Styles x:Class="ShadUI.ShadTheme" …>
  <Styles.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://ShadUI/Controls/Constants.axaml" />
        <ResourceInclude Source="avares://ShadUI/Controls/Resources.axaml" />
      </ResourceDictionary.MergedDictionaries>
      <ResourceDictionary.ThemeDictionaries>
        <ResourceInclude Source="/Themes/Light.axaml" x:Key="Light" />
        <ResourceInclude Source="/Themes/Dark.axaml"  x:Key="Dark" />
      </ResourceDictionary.ThemeDictionaries>
    </ResourceDictionary>
  </Styles.Resources>
  <StyleInclude Source="avares://ShadUI/Controls/Styles.axaml" />
</Styles>
```

Two consequences:

- **ShadUI is unaware of any variant outside Light/Dark.** It has *no*
  conditional logic, no `[Theme]` attribute scan, no extensibility hook to
  register a third variant. Light and Dark are hardcoded `x:Key`s in the
  package's own `ThemeDictionaries`.
- **But that doesn't matter,** because Avalonia's resource resolution walks
  *every* `ThemeDictionaries` in the merged tree. If we add an
  `S100Night` dictionary at App level, controls inside ShadUI's templates
  using `{DynamicResource WindowBackgroundColor}` resolve against the
  *combined* set: ShadUI's Light + ShadUI's Dark + our S100Night. When the
  current variant is `S100Night`, our entry wins; for any key we omit,
  Avalonia falls through `InheritVariant.Dark` to ShadUI's Dark
  dictionary.

### ShadUI resource keys

Counted from `Light.axaml` / `Dark.axaml`: ~30 distinct `Color` keys
(many with `…Color60`, `…Color20` opacity siblings derived from a base).
Grouped:

| Category               | Keys                                                                                                                |
|------------------------|---------------------------------------------------------------------------------------------------------------------|
| Surface / window       | `WindowBackgroundColor`, `TitleBarBackgroundColor`, `SidebarBackgroundColor`, `CardBackgroundColor`, `BackgroundColor` |
| Dialog                 | `DialogBackgroundColor`, `DialogOverlayColor`, `BusyAreaOverlayColor`                                                |
| Text                   | `ForegroundColor`, `ForegroundLeadColor`, `MutedColor`                                                               |
| Border / outline       | `BorderColor`, `BorderColor60`, `BorderColor30`, `OutlineColor`                                                      |
| Theme accent (button)  | `PrimaryColor` + Color75/50/10, `PrimaryForegroundColor`, `SecondaryColor` + 75/50, `SecondaryForegroundColor`       |
| Destructive            | `DestructiveColor` + 75/50/10, `DestructiveForegroundColor`                                                          |
| Status                 | `InfoColor`, `SuccessColor`, `WarningColor`, `ErrorColor` (each with 60/20/10/5 alpha siblings)                      |
| Ghost / hover / select | `GhostColor`, `GhostHoverColor`, `GhostHoverColor50`, `WindowButtonHoverColor`, `SelectionColor`                     |
| Switch / tab           | `SwitchBackgroundColor`, `SwitchForegroundColor`, `TabItemSelectedColor`, `TabItemsBackgroundColor`                  |

Important: ShadUI publishes **`Color`** keys, not `IBrush` keys. Per-control
templates instantiate brushes from the colors via ordinary
`<SolidColorBrush Color="{DynamicResource WindowBackgroundColor}" />`
patterns. Our overrides therefore need to be `Color` values too.

`AccentBrush` — heavily used in the viewer — is **not** a ShadUI key. It's
a viewer-owned `SolidColorBrush` defined in `MainWindow.axaml` and mutated
by `MainWindow.axaml.cs` when `SettingsViewModel.AccentColor` changes. It
sits outside ShadUI's theme system today; that's actually convenient,
because the user-chosen accent should stay constant across chrome variants.

### Fallback behaviour for keys we don't override

Confirmed by reading ThemeDictionaries semantics: missing keys traverse the
`InheritVariant` chain. So a sparse S100Night dictionary that overrides
only `WindowBackgroundColor`, `TitleBarBackgroundColor`,
`SidebarBackgroundColor`, `CardBackgroundColor`, `DialogBackgroundColor`,
`ForegroundColor`, `ForegroundLeadColor`, `MutedColor`, `BorderColor`,
`OutlineColor`, `BusyAreaOverlayColor`, `TabItemsBackgroundColor` (≈12
keys) gets us a coherent night palette, with Status/Switch/Tab/Ghost
inheriting from Dark. This is the **minimum viable** scope.

---

## 3. Mapping S-100 portrayal palette → ShadUI keys

There is no clean spec-derived mapping. The S-100 portrayal colour profile
(`src/EncDotNet.S100.Specifications/content/S101/pc/ColorProfiles/colorProfile.xml`)
defines tokens for *map* features — `CHBLK`, `CHWHT`, `LANDA`, `DEPVS`,
`UIBCK`, `UINFD`, etc. The four `UI…` tokens (`UIBCK`, `UINFD`, `UINFF`,
`UINFR`) are explicitly the closest things to chrome colours, but they're
intended for the **"information area"** the ECDIS pen draws inside the map
viewport, not for OS-level chrome like a title bar or a checkbox track.
The token set has nothing for "panel border radius accent", "input focus
ring", "dialog overlay alpha".

So the mapping is unavoidably **hand-picked**, taking inspiration from
S-100 conventions (white-on-black at night, warm-dim at dusk, flat white
for day) rather than mechanically derived. The palettes below are sketches
to anchor discussion — not a spec.

| Key                       | Avalonia Light   | Avalonia Dark   | S100Day (proposed) | S100Dusk (proposed) | S100Night (proposed) |
|---------------------------|------------------|-----------------|--------------------|---------------------|----------------------|
| `WindowBackgroundColor`   | `#FFFFFF`        | `#0A0A0A`       | `#FAFAF7`          | `#1F1A14`           | `#000000`            |
| `TitleBarBackgroundColor` | `#FFFFFF`        | `#0A0A0A`       | `#F0F0EC`          | `#231D16`           | `#0A0000`            |
| `SidebarBackgroundColor`  | `#FFFFFF`        | `#0A0A0A`       | `#F5F5F0`          | `#1A1610`           | `#080000`            |
| `CardBackgroundColor`     | `#FFFFFF`        | `#171717`       | `#FFFFFF`          | `#2A2218`           | `#100404`            |
| `DialogBackgroundColor`   | `#F2F2F2`        | `#0A0A0A`       | `#F5F5F0`          | `#231D16`           | `#0A0000`            |
| `ForegroundColor`         | `#18181B`        | `#E5E5E5`       | `#18181B`          | `#D9C4A8`           | `#FFB0A0`            |
| `ForegroundLeadColor`     | `#71717A`        | `#A1A1AA`       | `#5A5A60`          | `#A8927A`           | `#C88478`            |
| `MutedColor`              | `#71717A`        | `#A0A0A9`       | `#71717A`          | `#8A7560`           | `#8A4848`            |
| `BorderColor`             | `#CECECE`        | `#353535`       | `#D8D6D0`          | `#3A3024`           | `#280808`            |
| `OutlineColor`            | `#E5E5E5`        | `#3E3E3E`       | `#E0DED8`          | `#3F3528`           | `#301010`            |
| `BusyAreaOverlayColor`    | `#F2F2F2`        | `#1971717A`     | `#F2F2F2`          | `#19A8927A`         | `#19FF6464`          |
| `TabItemsBackgroundColor` | `#E8E8E8`        | `#27272A`       | `#EAE8E2`          | `#2F271D`           | `#1A0606`            |

Notes on the palettes:

- **S100Day** is barely distinguishable from Avalonia Light (slightly warmer
  off-white). Honest assessment: this variant earns its keep almost entirely
  for *consistency* — i.e. so the chrome-variant selector has a Day option
  matching the map-palette selector — not because it adds visible value.
  We could legitimately decide S100Day = ThemeVariant.Light and skip the
  custom dictionary entirely.
- **S100Dusk** is a warm-dim brown/amber palette — there's no Avalonia
  equivalent. This one *does* add real value if you use the viewer at
  twilight on a real bridge. It's the second-most-justifiable variant.
- **S100Night** is the standard ECDIS night: near-black background,
  red-shifted foreground (`#FFB0A0` ≈ a muted salmon, not pure red, to
  remain readable), all status colours wash out toward red. This is the
  variant that actually preserves dark-adapted vision and is the entire
  *raison d'être* of the spike.
- Status colours (`InfoColor`, `SuccessColor`, `WarningColor`,
  `ErrorColor`) deserve special thought for Night: pure blue `#2979FF`
  destroys night vision worse than chrome white does. A correct Night
  palette pushes Info → dim cyan or even amber, Success → muted green,
  Warning → amber, Error → deep red. Spec doesn't dictate this — we're
  inventing it. Visual review with a hardware partner would matter.
- We should **not** override `PrimaryColor` / `SecondaryColor`. Those are
  ShadUI's "filled button" colours and pull double-duty as accent surfaces.
  The viewer already routes its accent through the `AccentBrush` resource,
  which is independent of theme. Keeping S100 chrome variants
  accent-agnostic is a feature.

---

## 4. Existing in-repo theme-aware code

The viewer's binary `IsDarkTheme` model is small but pervasive enough that
adding 3 more variants requires care.

### `IThemeService` / `ThemeService`

```csharp
internal interface IThemeService
{
    bool IsDarkTheme { get; }
    bool ToggleTheme();
}
```

The contract is binary. Adding 3+ variants requires either:
- **Migrate to enum** (`ChromeTheme.Light | Dark | S100Day | S100Dusk |
  S100Night`) and break the contract, or
- **Add a parallel enum-flavoured API** (`ChromeTheme Current { get; }`,
  `void SetTheme(ChromeTheme)`) and leave `IsDarkTheme` as a derived
  convenience for callers that only need the binary signal.

Recommend the second: derived `IsDarkTheme` keeps `MeasureOverlayAppearanceProvider`
and `CompassRoseView` working unchanged in their first cut. `IsDarkTheme`
returns `true` for `Dark`, `S100Dusk`, and `S100Night`; `false` for
`Light` and `S100Day`. That's a reasonable simplification: the overlay and
compass were tuned for "is the canvas dark?", which is exactly what these
variants express.

### `MeasureOverlayAppearanceProvider`

Reads `_settings.AccentColor` + `_theme.IsDarkTheme`, raises a single
`Changed` event when either source updates. Already subscribes to
`Application.ActualThemeVariantChanged`, so the moment we set
`RequestedThemeVariant = S100Night`, the overlay re-evaluates. **No
behavioural change required**, assuming we keep the binary `IsDarkTheme`
projection. (Phase 3 polish: derive a more nuanced "overlay tint" from the
variant — pure white text overlays look harsh on a near-black night chrome
because they're brighter than the ECDIS palette wants.)

### `CompassRoseView`

Currently does its own variant check:

```csharp
var isDark = ActualThemeVariant == ThemeVariant.Dark;
```

That **breaks** the moment we set the variant to `S100Night`, because
`ActualThemeVariant != ThemeVariant.Dark`. The fix is mechanical:

```csharp
var t = ActualThemeVariant;
var isDark = t == ThemeVariant.Dark || t.Key is "S100Night" or "S100Dusk";
```

…or better, route through `IThemeService.IsDarkTheme` so this lives in one
place. Since `CompassRoseView` is a `Control`, not a VM, we'd have to inject
or look up the service — the cleaner fix is probably to keep the inline
check but generalise it once.

### Post-PR-N1 chart VMs

After PR-N1 lands, station chart view-models subscribe to
`ActualThemeVariantChanged` and recolour their LiveCharts2 series. Same
fix: replace any equality check against `ThemeVariant.Dark` with
"is this a dark variant" predicate. The chart re-render path already
exists; we're just feeding it a finer signal.

### Other subtle consumers

- `MainWindow.axaml` defines `AccentBrush` *outside* any
  `ThemeDictionaries`. It will resolve correctly under all variants — but
  it is **not** variant-aware, which is what we want (accent is a
  separate axis from chrome variant).
- `App.axaml` Slider template overrides use `{DynamicResource
  AccentBrush}` — variant-agnostic, fine.
- `ViewerSettings.ColorProfile` is a string already (`"Day"`, `"Dusk"`,
  `"Night"`). A new `ChromeTheme` setting needs its own key; reusing
  `ColorProfile` would couple the two axes prematurely (see §5).

---

## 5. Linkage with the map palette

After discussion, the model is: **chrome theme is primary; map palette
follows chrome by default, but the user can override the map palette to
any value to test arbitrary combinations.** Reversed from the original
draft — chrome is the driver, not the map.

Rationale (paraphrasing the user): Light / Dark are *system-level*
preferences ("I want all my apps to look consistent on this machine"),
not operational nautical preferences. A user switching the machine to
Dark mode is not declaring intent to navigate at night. So Light and
Dark chrome both map to **Day** map palette by default. The S-100
chrome variants (just S100Night for the first cut) *are* operational
preferences and so do drive the map palette.

### Default coupling

| Chrome variant   | Default map palette |
|------------------|---------------------|
| `Light`          | `Day`               |
| `Dark`           | `Day`               |
| `S100Night`      | `Night`             |
| *(future)* `S100Dusk` | `Dusk`         |
| *(future)* `S100Day`  | `Day`          |

Note: there is no chrome variant that defaults the map to anything other
than its same-named palette (or Day for Light/Dark). The mapping is
one-way deterministic on chrome change.

### Override semantics

- The map palette has its own UI control (today's `SettingsViewModel.
  SelectedPalette` dropdown) that stays visible. Users can change it
  freely at any time.
- When the user picks a chrome variant, the map palette is **reset to
  the default for that variant** (per the table above), overwriting any
  prior manual map-palette selection. This is the "set chrome → map
  follows" half.
- Once chrome is set, the user can re-select a different map palette
  and it sticks until the next chrome change. We do **not** introduce a
  persistent "decouple" toggle — the override is implicit (just change
  the map dropdown after picking chrome).
- This is honest about the user's mental model: chrome is the rare,
  deliberate switch ("I'm now operating at night"); map palette is the
  fine-tuning ("…and I want to see what this chart looks like in Dusk
  mode under that chrome").

### Future: an explicit "dark map" palette

Deferred. The user noted that *if* Dark chrome eventually wants a map
that's muted to match (rather than the standard Day palette under a
dark frame, which can be jarringly bright), we'd add a new map palette
that's a desaturated Day rather than Night. That's a separate piece of
work; out of scope here.

### Persistence

`ViewerSettings.ColorProfile` already persists `Day | Dusk | Night` as
a string for the map palette. Add a new `ViewerSettings.ChromeTheme`
string (e.g. `"Light" | "Dark" | "S100Night"`) that persists the chrome
variant. The two are independent settings — we do *not* reuse
`ColorProfile` for chrome.

When the user changes chrome, both settings update in a single
transaction (chrome → `ChromeTheme`; default-mapped map palette →
`ColorProfile`). When the user changes only the map palette, only
`ColorProfile` updates. On startup, both are restored independently —
so a user who picked S100Night chrome and then overrode to Day map
gets exactly that combination back next launch.

### Implementation note

`SettingsViewModel.SelectedPalette` is already the right setter
location for map-palette changes (it raises `PaletteChanged` which
`DatasetLoaderService` listens to for re-render). Add a sibling
`SelectedChromeTheme` property; its setter:

1. Writes `ViewerSettings.ChromeTheme`.
2. Sets `Application.Current!.RequestedThemeVariant`.
3. Sets `SelectedPalette` to the default for that chrome variant
   (which in turn triggers the existing map re-render path).

Step 3 is what makes the coupling work. It's a normal `SelectedPalette`
write, so the existing change-notification chain handles map re-render
exactly as it does today.

---

## 6. Effort estimate

Estimates are engineer-days for an engineer already familiar with the
viewer. Includes implementation, tests, doc updates. Excludes design review
and visual QA across platforms.

| Phase                                                                                  | Min viable (Night only) | Full (Day + Dusk + Night) | Polish (status colours, focus rings, edge cases) |
|----------------------------------------------------------------------------------------|-------------------------|---------------------------|---------------------------------------------------|
| Register `ThemeVariant` instances + `App.axaml` `ThemeDictionaries` skeleton           | 0.25                    | 0.5                       | —                                                 |
| Author override resource dictionary (12 keys per variant)                              | 0.5                     | 1.5                       | +1 (status palette, opacity siblings)             |
| Migrate `IThemeService` (add enum-flavoured API; keep `IsDarkTheme` derived)           | 0.5                     | 0.75                      | —                                                 |
| Wire `SettingsViewModel` chrome-drives-map coupling + `ChromeTheme` persistence | 0.5                     | 0.5                       | —                                                 |
| Touch up `CompassRoseView` + `MeasureOverlayAppearanceProvider` for non-Dark "darks"   | 0.25                    | 0.25                      | +0.5 (variant-specific overlay tints)             |
| Update PR-N1 chart VMs to react to non-Dark dark variants                              | 0.25                    | 0.25                      | —                                                 |
| Settings UI (dropdown / radio for chrome theme; visibility of override toggle)         | 0.25                    | 0.5                       | —                                                 |
| Strings.resx entries + tooltips (per repo viewer rules)                                | 0.25                    | 0.5                       | —                                                 |
| Manual visual QA across macOS/Windows/Linux                                            | 0.5                     | 1                         | +1                                                |
| **Total**                                                                              | **~3.25 days**          | **~5.75 days**            | **+~2.5 days**                                    |

These are honest "engineer-day" estimates, not optimistic ones. The
visual-QA item is the one most likely to balloon: every dialog / popup /
data-grid / focus state in the app needs eyeballing under the new variants
because no automated coverage exists today (see §7).

---

## 7. Risks and open questions

1. **No visual regression coverage.** Panel inventory PR #125 noted this
   gap. Adding three new variants triples the surface that has to be
   manually verified each time ShadUI updates. *Mitigation:* commission a
   visual-regression harness *before* merging Phase 2; or accept that
   chrome variants will rot quietly.

2. **ShadUI updates can introduce new keys we don't override.** Each new
   key with no S100 override silently inherits from Dark (which may or
   may not be appropriate for Night). *Mitigation:* add a CI check that
   diffs ShadUI's `Light.axaml` between releases and flags new keys; or
   accept gradual drift.

3. **Status colours under Night are an open palette question.** Pure blue
   Info on a near-black background reflects exactly the night-vision
   problem ECDIS Night mode exists to solve. We have to pick replacement
   hues without an IHO spec to cite. *Open question:* do we want a
   maritime-domain expert (or at least an existing reference ECDIS) to
   sanity-check Dusk/Night status hues before we ship?

4. **Day chrome adds limited value.** Avalonia Light covers ~95% of the
   user-visible Day experience already. Authoring an S100Day dictionary
   exists mostly to make the selector symmetric. *Mitigation:* alias
   S100Day → ThemeVariant.Light at the variant level (no separate
   dictionary). The user's slaving UI then maps `SelectedPalette.Day` →
   `ThemeVariant.Light`, `Dusk` → `S100Dusk`, `Night` → `S100Night`.

5. **Settings persistence migration.** `ViewerSettings.ColorProfile`
   already persists Day/Dusk/Night. A separate `ChromeTheme` setting (when
   decoupled) is new and needs default-value handling for users upgrading
   from versions that didn't have it. Trivial but easy to forget.

6. **`AccentBrush` interaction with red-friendly Night.** The user can
   currently pick a bright cyan accent. Under S100Night that bright cyan
   is exactly what Night chrome is designed to suppress. *Open question:*
   do we (a) honour the user's accent regardless, (b) silently desaturate
   it under Night, or (c) show a warning in settings? Recommend (a): the
   user owns their accent. Document the gotcha.

7. **`ThemeVariantScope` for one-off light surfaces.** The map canvas is
   always inside its own scope (Mapsui owns its own rendering). But
   pop-out windows, file dialogs, and OS-native dialogs may not honour
   custom variants on every platform. *Open question:* what does macOS's
   native title bar do under `ThemeVariant("S100Night", inherit: Dark)`?
   Looking at `PlatformThemeVariant` operator: a custom variant inheriting
   from Dark casts to `PlatformThemeVariant.Dark`, so OS-level chrome
   should follow Dark. Worth verifying on hardware.

8. **LiveCharts2 + Mapsui are independent of `ThemeVariant` until we wire
   them.** Mapsui's tile / vector layers don't read Avalonia resources at
   all — they're driven by the existing `PaletteChanged` re-render path,
   which is map-palette-axis, not chrome-axis. Slaving the two axes (§5)
   is what reconciles that. After PR-N1, chart VMs subscribe to chrome
   theme changes; the new variants are fine for them as long as
   `IsDarkTheme` stays a meaningful predicate.

9. **`CompassRoseView`'s inline `ThemeVariant.Dark` check is one of
   probably several.** A pre-merge audit (`grep -rn "ThemeVariant\."` in
   `src/EncDotNet.S100.Viewer`) would surface them all. The spike found
   one; there may be 2–4 more.

10. **The "S100" prefix on the ThemeVariant key is permanent.** If we
    ever rebrand or consolidate, persisted user settings reference these
    strings. Worth getting the names right the first time.

---

## 8. Recommendation

**Go — minimum viable scope.** Ship a single PR that adds `S100Night` as
the only new chrome variant, with chrome-drives-map coupling.

Scope of the first PR:

- **One new chrome variant: `S100Night`.** Registered as
  `new ThemeVariant("S100Night", inheritVariant: ThemeVariant.Dark)`.
  Roughly 12 overridden keys (the surface/text/border set in §3); status
  colours inherit from Dark for now (call out in the PR description as a
  known follow-up — Info blue on near-black needs revisiting once we see
  it in real use).

- **No bespoke S100Day or S100Dusk in this PR.** Avalonia Light covers
  Day. Dark covers Dusk well enough to defer the work. The chrome
  selector exposes three options: Light, Dark, S100Night.

- **Wire chrome-drives-map coupling per §5.** Picking S100Night also
  sets the map palette to Night; picking Light or Dark sets it to Day.
  The map palette dropdown stays visible and editable for the
  override-after-the-fact case. New `ViewerSettings.ChromeTheme`
  setting persists chrome independently of `ColorProfile`.

- **Migrate `IThemeService` to expose both the variant and a derived
  `IsDarkTheme`** (true for `Dark` and `S100Night`). `CompassRoseView`
  and any other inline `ThemeVariant.Dark` equality checks switch to
  the `IsDarkTheme` predicate so they don't go pale-on-pale under
  S100Night.

- **Strings.resx entries + tooltips** for the new selector per the
  viewer instructions file. (`viewer.instructions.md` requires every
  user-visible string in resx and every button to have a tooltip.)

- **Manual visual QA on macOS** (the maintainer's primary OS) is
  enough for a first ship. Windows/Linux are tested when a user on
  those platforms exercises the feature. Don't block on a regression
  harness — see §7.1.

**Estimate:** ~3.25 engineer-days, per §6 "Min viable" column.

### What to defer

- **S100Dusk.** Promote to a custom variant in a follow-up if users
  report Dark-as-Dusk looks wrong (probable: Dusk wants warm tones,
  Dark is cool-neutral). By then we'll know which keys actually
  matter.
- **S100Day.** Skip indefinitely; an extra dictionary nobody can
  distinguish from Avalonia Light is pure overhead.
- **A muted "dark map" palette** (per the user's note in §5). Separate
  piece of work, separate spec discussion.
- **Bespoke night-tuned status colours** (Info / Success / Warning /
  Error). Ship inheriting from Dark; refine in a follow-up after
  visual review.
- **Visual regression harness.** Worth doing eventually but not as a
  blocker for one variant.

### What would change the call

This is a Go because the cost is bounded (~3 days), the win is real
(actual dark-adapted vision support, the only thing S-100 Night
genuinely *needs* that Avalonia Dark doesn't provide), and the
maintenance footprint of *one* extra variant is modest. If the team
ever decides to go all the way to all three S-100 chrome variants plus
night-tuned status colours plus visual regression coverage, the cost
becomes more like 8–10 engineer-days for a relatively small UX win, at
which point "ship Phase 1 only" becomes the defensible answer. The
recommendation above explicitly avoids that escalation by capping
Phase 2 scope at S100Night-only.

---

## Appendix: sources

- Avalonia 11 theme variants guide:
  <https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-theme-variants>
- `Avalonia.Styling.ThemeVariant` source (11.3.11):
  <https://github.com/AvaloniaUI/Avalonia/blob/release/11.3.11/src/Avalonia.Base/Styling/ThemeVariant.cs>
- ShadUI 0.1.6 ShadTheme + Light + Dark dictionaries:
  <https://github.com/accntech/shad-ui/tree/main/src/ShadUI/Themes>
- Local NuGet cache:
  `~/.nuget/packages/shadui/0.1.6/lib/netstandard2.1/ShadUI.dll`
- In-repo theme-aware code surveyed:
  - `src/EncDotNet.S100.Viewer/Services/IThemeService.cs`
  - `src/EncDotNet.S100.Viewer/Services/ThemeService.cs`
  - `src/EncDotNet.S100.Viewer/Services/MeasureOverlayAppearanceProvider.cs`
  - `src/EncDotNet.S100.Viewer/Views/CompassRoseView.cs`
  - `src/EncDotNet.S100.Viewer/App.axaml`
  - `src/EncDotNet.S100.Viewer/MainWindow.axaml` (`AccentBrush` resource)
  - `src/EncDotNet.S100.Viewer/ViewerSettings.cs`
  - `src/EncDotNet.S100.Viewer/ViewModels/SettingsViewModel.cs`
  - `src/EncDotNet.S100.Viewer/Services/DatasetLoaderService.cs`
    (`PaletteChanged` re-render hook)
- S-100 portrayal palette source:
  `src/EncDotNet.S100.Specifications/content/S101/pc/ColorProfiles/colorProfile.xml`
