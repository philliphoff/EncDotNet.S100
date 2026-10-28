# NMEA 0183 fixtures

Recordings for `EncDotNet.S100.Nmea.Tests` and
`EncDotNet.S100.DynamicSources.Nmea.Tests`, one sentence per line with CR LF
endings, as multiplexers log them. Lines starting with `#` are comments the
replay driver skips.

| File | Lines | What it holds |
| --- | --- | --- |
| `ownship-weather.nmea` | 24 | A full instrument stream: `RMC`, `VTG`, `GGA`, `HDT`, `HDG`, `VHW`, `MWV` R and T, `MWD`, `MTW`, two `XDR` (air temperature and pressure; an engine temperature), a proprietary `PGRMZ`, an AIS `!AIVDM`, a tag block, and four bad lines: a wrong checksum, a sentence cut off mid-field, a cut-off sentence glued to a whole one (the whole one is kept), and noise. |
| `apparent-wind.nmea` | 9 | `RMC`, `HDT`, `VHW`, `MWV` R and `MTW` only: true wind has to be derived. |
| `relative-wind.nmea` | 6 | `RMC`, `MWV` R (one with status `V`, one in m/s) and `MTW`, but no heading: relative wind only. |

**These fixtures are hand-written.** No instrument was available to record
from. They are real-world shaped: talkers `GP` (GNSS), `HE` (gyro), `HC`
(magnetic compass), `II` (integrated instruments), `WI` (weather instrument),
field layouts from NMEA 0183 v4.11, and checksums computed for every line
except the deliberately bad one. The scene is consistent: a vessel in
Massachusetts Bay (42°21' N, 070°39' W, near NDBC buoy 44013) heading 090° T
at 6.0 kn through the water and 6.5 kn over the ground on 095°, variation
14.5° W, in a true wind over the ground from 200° at 14.0 kn. That gives the
apparent wind (083.0° R, 13.8 kn) and the water-referenced true wind (107.4° T
relative to the bow, 14.4 kn) in the files. A small script generated them;
the vector sums are in `TrueWindTests`.

A real recording can be added beside these. The tests don't depend on dates:
the source stamps sentences with the time it receives them.
