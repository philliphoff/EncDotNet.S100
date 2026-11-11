# Coded surface bulletin test fixtures

WPC coded surface bulletins for the surface analysis reader (issue #1295) and its portrayal (#1300;
design note `vector-weather.md` §5, §8.3). Three are real bulletins from the NWS
Weather Prediction Center, a US Government work in the public domain, copied
byte for byte. Two are hand-written.

| File | Bytes | What | Source |
|---|---|---|---|
| `codsus-hr-2026110400.txt` | 3,748 | **Real.** High-resolution (0.1°) coded surface analysis `ASUS02 KWBC 040000`, valid 2026-11-04 00Z, issued 834 PM EST Tue Nov 3 2026. Every front type, 15 highs and 29 lows. `HIGHS` and `LOWS` wrap mid-pair (`993 4521792 982` / `5341590 …`); points at 179.2°W and 179.5°W. | IEM AFOS archive (`mesonet.agron.iastate.edu/cgi-bin/afos/retrieve.py?pil=CODSUS`), which keeps NOAAport's text unchanged; cut at its `$$`. |
| `codsus-2026110400.txt` | 2,695 | **Real.** The standard (1°) bulletin of the same analysis, `ASUS01 KWBC 040134`. | Same archive. |
| `codsrp-2026110504.txt` | 2,111 | **Real.** The short-range coded forecast bulletin `FSUS02 KWBC 050402` (`CODSRP`), issued 1101 PM EST Wed Nov 4 2026, with `12HR PROG VALID 051200Z` and `24HR PROG VALID 060000Z` sections at 1°. | `tgftp.nws.noaa.gov/data/raw/fs/fsus02.kwbc.cod.srp.txt`. |
| `synthetic-antimeridian.txt` | 386 | **Hand-written, synthetic.** A standard bulletin issued 1030 PM EST Wed Dec 31 2025 (03:30Z on 2026-01-01) and valid `123121Z`, so 2025-12-31 21Z, the year before the issue time's in UTC, with an intensity on every front (`WK`, `MDT`, `STG`), a cold front and a stationary front crossing 180° (`50180 48183`, 181°W–200°W are east longitudes), a low wrapped mid-pair, `TROF` starting mid-line, a trough wrapped mid-line, an unknown code (`DRYLN`) and the `**` end marker. Not a WPC product. | Written for these tests. |
| `synthetic-atlantic-low.txt` | 375 | **Hand-written, synthetic.** A high-resolution analysis valid 2026-11-04 18Z, drawn after the design mock-ups' fronts frame (issue #1300): a 996 hPa low at 45°N 50°W, an occluded front to a triple point at 42.5°N 45.5°W, a cold front south-west from it ending in a stationary front, a warm front east, a trough, and a 1032 hPa high at 37°N 30°W. Fronts listed so their pips fall on the side they move towards. Not a WPC product. | Written for the portrayal goldens. |

WPC's own format pages ("Reading the Coded Surface Bulletin", the
high-resolution and short-range forecast versions) are quoted in the tests
for their worked examples.
