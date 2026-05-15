# EncDotNet.S100.PerfRunner

Scripted performance scenario runner for EncDotNet.S100 pipelines and
renderers. Produces reproducible, comparable telemetry files that can be
summarised and diffed by the companion
[PerfReport](../EncDotNet.S100.PerfReport/) tool.

## Quick start

```bash
# Run the S-124 vector scenario with defaults
dotnet run --project tools/EncDotNet.S100.PerfRunner -- s124-vector

# List all available scenarios
dotnet run --project tools/EncDotNet.S100.PerfRunner -- list

# Full options
dotnet run --project tools/EncDotNet.S100.PerfRunner -- s101-portray-warm \
    --corpus tests/datasets \
    --out ./perf-runs \
    --warmup 3 \
    --iterations 20 \
    --tag branch=main \
    --tag commit=abc1234
```

## Scenarios

| Name | Description |
|------|-------------|
| `s101-portray-cold` | Single cold-start S-101 parse + portray. No warmup — captures first-pass Lua/XSLT compile cost. |
| `s101-portray-warm` | S-101 portrayal pipeline only (no render) with warmup. Pure pipeline throughput. |
| `s101-render-warm` | S-101 pipeline + Mapsui display-list render (headless). |
| `s102-coverage` | S-102 HDF5 bathymetry: coverage pipeline + render. |
| `s124-vector` | S-124 GML navigational warnings: XSLT-only vector pipeline. |
| `s201-vector` | S-201 GML AtoN information: XSLT-only vector pipeline. |
| `exchange-set-open` | Open a synthetic exchange set and walk all datasets. |

## Output

Each run produces two files in the output directory:

- `<timestamp>-<scenario>.jsonl` — newline-delimited JSON telemetry
  (spans + metrics).
- `<timestamp>-<scenario>.md` — markdown summary with iteration
  statistics.

### `.jsonl` schema (version 1)

Every line is a JSON object with a `kind` discriminator:

```jsonc
// First line — schema header
{"kind":"header","version":1,"startedAtUtc":"2026-05-09T05:00:00Z"}

// Span line
{"kind":"span","name":"s100.pipeline.vector.stage.lua",
 "traceId":"…","spanId":"…","parentSpanId":"…",
 "startUnixNs":…,"endUnixNs":…,"durationMs":13.4,
 "status":"Ok",
 "tags":{"s100.pipeline.stage":"lua","s100.product":"S-101"}}

// Metric line (histogram)
{"kind":"metric","name":"s100.pipeline.duration",
 "instrument":"histogram","unit":"ms",
 "tags":{"s100.product":"S-101"},
 "buckets":[{"sum":142.5,"count":20,"min":5.1,"max":12.3}]}

// Metric line (counter)
{"kind":"metric","name":"s100.symbol.cache.hit.count",
 "instrument":"counter","unit":"{hits}",
 "tags":{"s100.product":"S-101"},"value":48}
```

## Adding a new scenario

1. Create a class implementing `IPerfScenario` under `Scenarios/`.
2. Register it in `ScenarioRegistry.cs`:

```csharp
Register(() => new MyNewScenario());
```

3. Use `SharedInfrastructure.CreatePipelineFactory()` to get a factory
   pre-configured with all bundled portrayal catalogues, Lua engine,
   and CRS transforms.

## Notes

- The runner sets `ENC_DOTNET_OTEL_FILE` to capture telemetry from all
  `EncDotNet.S100.*` activity sources and meters.
- Cold scenarios run a single iteration with no warmup by design — they
  measure start-up overhead.
- Warm scenarios discard warmup iterations and report distribution stats
  so random noise is bounded.
- Adopting an OTLP collector is a future option; this in-process file
  exporter avoids an out-of-process dependency.
## Baseline runs

The `baseline` command runs **all** registered scenarios in series
with fixed parameters and writes results into a git-SHA-keyed
subdirectory:

```bash
# Run baseline with defaults (warmup 3, iterations 20)
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline

# Custom output directory
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline \
    --out tools/EncDotNet.S100.PerfRunner/baselines
```

### Output layout

```
baselines/
  CURRENT                          ← plain text file with the SHA
  <git-sha>/
    SUMMARY.md                     ← environment + per-scenario headline
    s101-portray-cold.jsonl
    s101-portray-cold.md
    s101-portray-warm.jsonl
    s101-portray-warm.md
    …
```

### Comparing your branch to baseline

```bash
# 1. On your branch, produce a fresh baseline
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline --out /tmp/perf

# 2. Diff each scenario against the committed baseline
BASELINE_SHA=$(cat tools/EncDotNet.S100.PerfRunner/baselines/CURRENT)
for s in s101-portray-cold s101-portray-warm s101-render-warm \
         s102-coverage s124-vector exchange-set-open; do
    dotnet run --project tools/EncDotNet.S100.PerfReport -- diff \
        tools/EncDotNet.S100.PerfRunner/baselines/$BASELINE_SHA/$s.jsonl \
        /tmp/perf/*/$s.jsonl
done
```

> **Noise floor caveat:** A single laptop run is informational, not
> authoritative. Timing values vary with background load, thermal
> throttling, and system state. See [CI gating](#ci-gating) for
> automated regression detection on every PR.

### Corpus

All default scenarios use synthetic fixtures under `tests/datasets/`.
For larger real-world datasets, run
`tools/EncDotNet.S100.PerfRunner/scripts/fetch-corpus.sh` and set
`ENC_DOTNET_PERF_CORPUS` to the cache directory. See
[`tests/perf/corpus/INDEX.md`](../../tests/perf/corpus/INDEX.md) for
the full corpus inventory.

## CI gating (interleaved + median/MAD)

The `.github/workflows/perf.yml` workflow runs on every PR to `main`:

1. Builds the **base** branch's PerfRunner into `/tmp/perf-bin/base/`
   and the **candidate** branch's into `/tmp/perf-bin/cand/` so both
   binaries are simultaneously available.
2. Calls `tools/perf/interleave.sh` which runs **5 rounds × 4
   iterations per side**, alternating which side leads each round
   (random order) so both base and candidate observe the same noise
   distribution. Each measured iteration is wrapped in a
   `perf.iteration` activity tagged with `perf.scenario`,
   `perf.round`, `perf.iter`, and `perf.side`.
3. Runs `perfreport gate` with median + MAD evaluation
   (`--threshold 10 --min-abs 100 --mad-k 3.0 --retry-zone-mult 2.0`).
   Scenarios in the suspicious zone are written to
   `${out}.suspicious.txt` rather than failing immediately.
4. If any scenarios are suspicious, re-runs interleave for **only
   those scenarios** for 5 more rounds, then re-gates with
   `--retry-zone-mult 1.0` (no second retry).
5. Posts a markdown summary to the PR.

### Per-iteration baseline flags

The `baseline` command supports the orchestrator contract:

| Flag | Purpose |
|------|---------|
| `--append` | Append to existing per-scenario `.jsonl` files instead of overwriting; suppresses per-scenario `.md` and `SUMMARY.md` regeneration. |
| `--round-tag <N>` | Stamp every `perf.iteration` activity with `perf.round=<N>`. |
| `--side <baseline\|candidate>` | Stamp every iteration with `perf.side`. |
| `--scenarios <csv>` | Restrict the run to the named scenarios (used by retry). |
| `--out-subdir <name>` | Override the git-SHA-derived subdirectory so the orchestrator owns the layout. |

To update the committed baseline after merging perf improvements:

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline
# Review baselines/<new-sha>/SUMMARY.md, then commit and update CURRENT.
```

## Notes
