# CAP test fixtures

Marine weather warnings for the CAP processor (issue #1069). Four are real
National Weather Service alerts, a US Government work in the public domain,
copied byte for byte (or, where noted, cut down to their marine entries
without changing them). One is hand-written.

The NWS endpoints (`api.weather.gov`, `alerts.weather.gov`) weren't reachable
from the build environment, so the real alerts come from public mirrors that
saved them from the NWS alerts API. Each mirror is named below; the alert
content is the NWS's own.

| File | Bytes | Form | What | Source |
|---|---|---|---|---|
| `nws-gale-warning-pzz560.xml` | 3,637 | CAP 1.2 XML | **Real.** Gale Warning, NWS San Francisco, sent 2026-09-12 08:47 PDT, for zone PZZ560 (Pigeon Point to Point Pinos). **Zone codes only.** Its `expires` (17:00 PDT on the 12th) is before its `onset` (15:00 PDT on the 13th); the hazard's end is the `eventEndingTime` parameter, 21:00 PDT on the 13th. | Verbatim copy of `testing/NWS_GaleWarning.xml` in [tannerryan/cap](https://github.com/tannerryan/cap) (a snapshot of the NWS alerts API, per that folder's README). |
| `nws-marine-weather-statement-anz530.xml` | 5,990 | CAP 1.2 XML | **Real.** Marine Weather Statement, NWS Baltimore/Washington, sent 2026-09-12 09:42 EDT, for eleven Chesapeake Bay and Tidal Potomac zones, ANZ530 to ANZ542 (including ANZ535). **Zone codes only.** Minor. | Verbatim copy of `testing/NWS_MarineWeatherStatement.xml` in [tannerryan/cap](https://github.com/tannerryan/cap). |
| `nws-alerts-marine-20260828.geojson` | 10,337 | NWS GeoJSON | **Real.** Two alerts from `api.weather.gov/alerts/active?status=actual` at 2026-08-28 15:38Z: a Special Marine Warning for Delaware Bay **with a polygon** (Severe, 15:12Z to 16:15Z), and a Small Craft Advisory for PZZ650 (Santa Barbara Channel) with zone codes only, onset 22:00Z, ending 10:00Z on the 29th. | The two marine features of `domains/globalfeed/testdata/nws_active_unfiltered_trimmed.json` in [branden-thompson/watchpost](https://github.com/branden-thompson/watchpost), a trimmed probe of the NWS API that keeps each alert unchanged. The features are unchanged; the collection keeps the API's `@context`, `type`, `title` and `updated`; the other alerts and the mirror's own `_note` are left out. Re-indented. |
| `nws-alerts-marine-20260603.atom` | 12,911 | Atom (NWS entries) | **Real.** The NWS Atom feed (`api.weather.gov/alerts.atom`) at 2026-06-03 16:45Z, cut to its three marine entries: a Special Marine Warning off the Florida Keys **with a `cap:polygon`** (GMZ032, GMZ043, GMZ053), and two Small Craft Advisories in the Gulf with zone codes only (GMZ475; GMZ452, GMZ455, GMZ472). Each Small Craft Advisory's `expires` is before its `onset`; `eventEndingTime` gives the end. | The feed head and the three marine `entry` elements of `examples/data/public-data/nws-cap-alerts-sample.xml` in [fulmenhq/sumpter](https://github.com/fulmenhq/sumpter), byte for byte; its three other entries (Flash Flood, Red Flag, Air Quality) are left out. |
| `hand-written-circle.xml` | 1,924 | CAP 1.2 XML | **Hand-written, synthetic.** An Extreme Hazardous Seas Warning, status `Exercise`, with a 30 km `<circle>` at 38.5°N 74.5°W from 15:00Z to 18:00Z on 2026-08-28 (so it overlaps the Delaware Bay warning), and a second area naming ANZ450 and the fictitious ANZ999, to test codes the zone layer doesn't have. Not an NWS product. | Written for these tests. |

Between them they cover every severity: Extreme (circle), Severe (both
Special Marine Warnings), Moderate (gale, Small Craft Advisories) and Minor
(Marine Weather Statement).

The zone codes resolve against the marine zone layer bundled with
`EncDotNet.S100.Datasets.Cap` (`src/EncDotNet.S100.Datasets.Cap/Resources/MarineZones.geojson`),
built from the full NWS coastal and offshore marine zone shapefiles by
`tools/cap` (see its README).
