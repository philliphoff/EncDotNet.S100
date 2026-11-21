#!/usr/bin/env python3
"""Runs the Salish Sea MCP scenarios against `s100 mcp serve` and checks their assertions.

Usage:
  S100=/path/to/s100 ENC=/path/to/folder-of-NOAA-ENCs TIDES=/path/to/catalogue.json \
    python3 run_scenarios.py [--json results.json] [scenario-id ...]

Needs s100 0.30 or later (the `sample` tool, and check_route's vessel, limits,
samples and window objects). ENC must hold the cells listed in ../README.md.
TIDES is any catalogue `s100 mcp serve --tide-catalogue` accepts; the repo's
tests/datasets/Tides/Stations/noaa-currents.json is enough for S2 and S3, and
the full catalogue from `s100 tides update` unlocks the San Juan current
steps in S1. Each assertion prints PASS, FAIL or SKIP; the exit code is the
number of FAILs.
"""
import json, math, os, random, sys
from datetime import datetime, timedelta
import numpy as np
from mcpclient import Mcp

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(HERE, "..", "routes")
S100 = os.environ.get("S100", "s100")
ENC = os.environ["ENC"]
TIDES = os.environ.get("TIDES", os.path.join(HERE, "../../datasets/Tides/Stations/noaa-currents.json"))
VESSEL = dict(draftMeters=1.1, speedKnots=16, safetyContourMeters=1.5, safetyDepthMeters=1.5)
LIMITS = dict(corridorHalfWidthMeters=50)
BERTH_NM = 0.3   # findings this close to either end count as the marina or anchorage approach
SJ_BOX = dict(south=48.33, west=-123.25, north=48.80, east=-122.45)
results = []

def server():
    return Mcp([S100, "mcp", "serve", ENC, "--tide-catalogue", TIDES, "--tide-predictions"], log=os.path.join(HERE, "serve.log"))

def check(sid, name, ok, detail=""):
    tag = "SKIP" if ok is None else ("PASS" if ok else "FAIL")
    results.append({"id": sid, "name": name, "result": tag, "detail": detail})
    print(f"  [{tag}] {sid} {name}" + (f" — {detail}" if detail else ""))

def poly(v): return json.dumps({"vertices": v})
def nm(a, b):
    dy = (a[0] - b[0]) * 60; dx = (a[1] - b[1]) * 60 * math.cos(math.radians((a[0] + b[0]) / 2))
    return math.hypot(dx, dy)
def t(s): return datetime.fromisoformat(s.replace("Z", "+00:00"))

# ---------------------------------------------------------------- S1
def s1(m):
    print("S1  7-day San Juan loop from Cap Sante Marina (40' twin-diesel, 1.1 m draft)")
    r = m.call("search_features", {"text": "Cap Sante Marina"})
    check("S1.1", "'Cap Sante Marina' isn't a charted name; the agent must resolve it another way",
          r["data"].get("totalCount", 0) == 0, f"{r['data'].get('totalCount')} matches")
    # #1549: finestOnly keeps only the finest cell's copy. Objects are counted by position,
    # because the two beacons at the entrance share a type and a name.
    r = m.call("search_features", {"text": "Friday Harbor Marina Entrance", "pageSize": 50, "finestOnly": True})
    pos = {(round((f["bounds"]["southLatitude"] + f["bounds"]["northLatitude"]) / 2, 5),
            round((f["bounds"]["westLongitude"] + f["bounds"]["eastLongitude"]) / 2, 5)) for f in r["data"]["features"]}
    check("S1.2", "search_features with finestOnly returns one hit per real-world object",
          r["data"]["totalCount"] == len(pos), f"{r['data']['totalCount']} hits for {len(pos)} distinct objects")
    days = [d for d in json.load(open(os.path.join(ROUTES, "san-juans-7day.json")))["days"] if d["id"] != "d7b"]
    # Navigability grid: agreement with the routes, and connectivity between stops.
    g = m.call("get_navigability_grid", dict(SJ_BOX, draftMeters=1.1, safetyContourMeters=1.5, cellMeters=50))["data"]
    grid = np.load(g["file"]["path"]); G = g["grid"]
    rc = lambda p: (int((p[0] - G["south"]) / G["latitudeStep"]), int((p[1] - G["west"]) / G["longitudeStep"]))
    check("S1.grid", "get_navigability_grid covers the San Juans in one call", g["counts"]["uncharted"] == 0,
          f"{G['rows']}×{G['columns']} cells at 50 m, {g['counts']['navigable']:,} navigable, {g['counts']['hazard']:,} hazard")
    for d in days:
        v = d["vertices"]
        ends = [v[1], v[-2]]; snapped = []
        for k, p in enumerate(ends):   # snap to the nearest navigable cell by hand until nearest_navigable_point exists (#1559)
            r, c = rc(p)
            if grid["class"][r, c] != 2:
                idx = np.argwhere(grid["class"] == 2); j = np.argmin((idx[:, 0] - r) ** 2 + (idx[:, 1] - c) ** 2)
                rr, cc = idx[j]; ends[k] = [G["south"] + (rr + .5) * G["latitudeStep"], G["west"] + (cc + .5) * G["longitudeStep"]]
                snapped.append(f"{nm(p, ends[k]) * 1852:.0f} m")
        conn = m.call("get_navigability_grid", dict(SJ_BOX, draftMeters=1.1, safetyContourMeters=1.5, cellMeters=50, format="none",
                                                     connect=ends))["data"]["connection"]
        check(f"S1.{d['id']}.connect", f"{d['name']}: the grid joins the first and last open-water waypoints",
              conn.get("connected"), (f"snapped by the harness: {', '.join(snapped)}; " if snapped else "") + json.dumps({k: conn[k] for k in conn if k in ("connected", "reason")}))
        rep = m.call("check_route", dict(polyline=poly(v), departure="2027-07-12T16:00:00Z", vessel=VESSEL, limits=LIMITS,
                                         samples=dict(include=True, max=2000)))["data"]
        total = rep["distanceNm"]
        must = [f for f in rep["findings"] if f["group"] == "must_review"]
        codes = [f["code"] for f in must]
        check(f"S1.{d['id']}", f"{d['name']}: no land or drying crossings",
              "route_check.crosses_land" not in codes and "route_check.crosses_drying" not in codes,
              f"{total:.1f} NM; {total/16*60:.0f} min at 16 kn, {total/8*60:.0f} min at 8 kn; verdict {rep['verdict']['kind']}: {rep['verdict']['title']}")
        away = [f for f in must if f["category"] != "area" and BERTH_NM < f["distanceNm"] < total - BERTH_NM]
        check(f"S1.{d['id']}.depth", f"no must-review chart findings more than {BERTH_NM} NM from the berths",
              not away, "; ".join(f"{f['distanceNm']:.2f} NM {f['meta'][:70]}" for f in away[:3]))
        areas = [(f["feature"]["datasetId"], f["location"]["startNm"], f["location"]["endNm"]) for f in rep["findings"] if f["category"] == "area" and f.get("feature")]
        band = lambda ds: int(ds.split("/")[1][2])
        overlap = [(a, b) for a in areas for b in areas if band(a[0]) < band(b[0]) and a[1] < b[2] - 1e-6 and b[1] < a[2] - 1e-6]
        check(f"S1.{d['id']}.areas", "area findings honour the finest-cell rule (no coarse span overlapping a finer one)",
              not overlap, f"{len(overlap)} overlapping pairs")
        rna = [f for f in must if f["category"] == "area" and 8 in f["details"]["area"].get("restrictions", []) and 7 not in f["details"]["area"].get("restrictions", [])]
        check(f"S1.{d['id']}.rna", "an 'entry restricted' RNA (33 CFR 165.1301, large vessels) isn't must-review",
              not rna, f"{len(rna)} must-review; verdict '{rep['verdict']['title']}'")
        # #1548: structures keep the SafetyContour alert (and its code) but are titled for the structure,
        # and pontoons, piers and wharves within the berth radius are grouped into one Info finding per end.
        structures = ("ShorelineConstruction", "Pontoon", "FloatingDock", "Hulk")
        titled = [f for f in rep["findings"] if f["title"] == "Crosses the safety contour" and any(s in f.get("meta", "") for s in structures)]
        scf = lambda f: (f.get("details") or {}).get("safetyContourFeature") or {}
        near_end = lambda f: min(f["distanceNm"], total - f["distanceNm"]) * 1852 <= 100   # default limits.berthRadiusMeters
        berth = [f for f in must if scf(f).get("isBerth") and near_end(f)]
        beyond = [f for f in must if scf(f).get("isBerth") and not near_end(f)]
        ends = [f["details"]["berthStructures"]["end"] for f in rep["findings"] if f["code"] == "route_check.berth_structures"]
        cut = sum(1 for f in must if scf(f).get("isStructure") and not scf(f).get("isBerth") and scf(f).get("cutByTrack"))
        check(f"S1.{d['id']}.shore", "structures aren't titled 'Crosses the safety contour'; berth structures within the radius are one Info finding per end",
              not titled and not berth and len(ends) == len(set(ends)),
              f"{len(titled)} titled as safety-contour crossings, {len(berth)} berth crossings within 100 m, {len(beyond)} beyond it, {len(ends)} berth finding(s), {cut} non-berth structure(s) cut by the track")
        # sample(depth) agrees with check_route's clearance samples
        samp = [s for s in rep["clearance"].get("samples", []) if s.get("chartedDepthMeters") is not None]
        random.seed(d["id"]); pick = random.sample(samp, min(15, len(samp)))
        bad = 0
        for s in pick:
            sd = m.call("sample", {"quantity": "depth", "latitude": s["latitude"], "longitude": s["longitude"]})["data"]
            dv = (sd.get("depth") or {}).get("depthMeters")
            if dv is None or dv < s["chartedDepthMeters"] - 0.05: bad += 1
        check(f"S1.{d['id']}.sample", "sample(depth) at clearance samples is no shoaler than check_route's sample (which keeps the shoalest of its stretch)",
              bad == 0, f"{len(pick) - bad}/{len(pick)} agree")
        cur = m.call("sample", {"quantity": "current", "polyline": poly(v), "time": "2027-07-12T18:00:00Z"})["data"]["samples"]
        got = sum(1 for s in cur if "result" in s)
        check(f"S1.{d['id']}.cur", "predicted currents are available along the leg to correct running time",
              None if got == 0 else True, f"{got}/{len(cur)} vertices have a current (0 → load the full catalogue from `s100 tides update`)")

# ---------------------------------------------------------------- S2
def s2(m):
    print("S2  Port Townsend → Port Townsend Canal → Port Ludlow, 2.0 m draft, 0.5 m UKC, tide-predicted water level")
    v = json.load(open(os.path.join(ROUTES, "port-townsend-canal.json")))["vertices"]
    args = dict(polyline=poly(v), departure="2027-07-14T15:00:00Z",
                vessel=dict(draftMeters=2.0, speedKnots=7, requiredUkcMeters=0.5, safetyContourMeters=3, safetyDepthMeters=3),
                limits=dict(corridorHalfWidthMeters=40), samples=dict(include=True, max=2000),
                window={"from": "2027-07-14T12:00:00Z", "to": "2027-07-15T12:00:00Z", "stepMinutes": 15})
    rep = m.call("check_route", args)["data"]; cl = rep["clearance"]
    stations = {w["station"] for w in cl["sources"]["waterLevels"]}
    check("S2.1", "check_route fills the missing S-104 with tide predictions", bool(stations), ", ".join(sorted(stations)))
    worst = 0
    for s in [x for x in cl["samples"] if x.get("station")][::40]:
        p = m.call("predict_tide", {"station": s["station"], "from": s["time"], "to": s["time"], "stepMinutes": 1})["data"]
        worst = max(worst, abs(p["series"][0]["height"] - s["waterLevelMeters"]))
    check("S2.2", "route water levels equal predict_tide at the same station and time (≤ 1 cm)", worst <= 0.01, f"worst Δ {worst*100:.1f} cm")
    s = next(x for x in cl["samples"] if x.get("station"))
    sd = m.call("sample", {"quantity": "depth", "latitude": s["latitude"], "longitude": s["longitude"], "time": s["time"]})["data"]
    wl = (sd.get("waterLevel") or {}).get("heightMeters")
    check("S2.2b", "sample(depth, time) gives the same water level as the route sample", wl is not None and abs(wl - s["waterLevelMeters"]) <= 0.01,
          f"sample {wl} m vs route {s['waterLevelMeters']} m")
    gaps = [g for g in cl["gaps"] if g["kind"] == "no_water_level_coverage"]
    check("S2.3", "a water-level gap mid-route", None, f"{len(gaps)} gap(s): " + "; ".join(f"{g['startNm']:.1f}–{g['endNm']:.1f} NM" for g in gaps))
    check("S2.4", "the departure window isn't emptied by a gap where charted depth alone clears the keel",
          bool(cl["window"]["windows"]), f"windows = {cl['window']['windows']}")
    rep2 = m.call("check_route", dict(args, polyline=poly(v[:7])))["data"]["clearance"]["window"]["windows"]
    check("S2.5", "control: the same sweep on the legs before the gap finds a window", bool(rep2), json.dumps(rep2))
    flo = [f for f in rep["findings"] if "-15.0 m" in f.get("meta", "")]
    named = sum(1 for f in rep["findings"] if "floating" in f.get("meta", ""))
    check("S2.6", "floating obstructions (WATLEV 7) are described as floating, not '-15.0 m'", not flo and named > 0, f"{len(flo)} '-15.0 m', {named} 'floating'")

# ---------------------------------------------------------------- S3
def s3(m):
    print("S3  Day-7 variant through Deception Pass at slack, then the Swinomish Channel")
    days = json.load(open(os.path.join(ROUTES, "san-juans-7day.json")))["days"]
    v = next(d for d in days if d["id"] == "d7b")["vertices"]
    ev = m.call("predict_current", {"station": "Deception Pass", "from": "2027-07-18T12:00:00Z", "to": "2027-07-19T03:00:00Z", "mode": "events"})["data"]["events"]
    slack = next(e for e in ev if e["kind"].startswith("slack"))
    pass_pt = (48.4065, -122.6440)
    i = min(range(len(v)), key=lambda k: nm(v[k], pass_pt))
    to_pass = sum(nm(v[k], v[k + 1]) for k in range(i))
    dep = t(slack["time"]) - timedelta(hours=to_pass / 8)
    check("S3.1", "by hand: departure = slack − distance-to-pass / SOG", True, f"slack {slack['time']}, {to_pass:.1f} NM at 8 kn → depart {dep:%H:%M}Z")
    a = m.call("sample", {"quantity": "current", "latitude": pass_pt[0], "longitude": pass_pt[1], "time": slack["time"]})["data"]["value"]["speedKnots"]
    check("S3.2", "sample(current) at the pass ≈ 0 kn at predicted slack", abs(a) < 0.2, f"{a} kn")
    mx = next(e for e in ev if e["kind"] == "max_flood")
    far = m.call("sample", {"quantity": "current", "latitude": 48.36352, "longitude": -122.54594, "time": mx["time"]})["data"]
    val = far.get("value", {})
    check("S3.3", "the pass's max flood isn't applied ~10 km away at the Swinomish entrance in Skagit Bay",
          not (val.get("stationId") == "PUG1701_33" and val.get("speedKnots", 0) > 2),
          f"{val.get('speedKnots')} kn from {val.get('stationId')}" if val else far.get("code", ""))
    rep = m.call("check_route", dict(polyline=poly(v), departure=dep.isoformat(),
                                     vessel=dict(draftMeters=1.1, speedKnots=8, safetyContourMeters=2, safetyDepthMeters=2),
                                     limits=dict(corridorHalfWidthMeters=30),
                                     window={"from": "2027-07-18T12:00:00Z", "to": "2027-07-19T03:00:00Z", "stepMinutes": 5,
                                             "constraints": {"applyCurrents": True, "gates": [{"name": "Deception Pass", "latitude": pass_pt[0], "longitude": pass_pt[1],
                                                                                                "station": "PUG1701_33", "slackWithinMinutes": 15}]}}))["data"]
    land = [f for f in rep["findings"] if f["code"] == "route_check.crosses_land"]
    check("S3.4", "Fairway-derived Swinomish centreline has no land crossings", not land, f"{len(land)}")
    wl = rep["clearance"]["gaps"]
    check("S3.5", "Swinomish UKC uses a tide prediction (La Conner 9448794 needs the full catalogue)",
          None if any(g["kind"] == "no_water_level_coverage" for g in wl) else True, f"{len(wl)} gap(s)")
    tm = rep.get("timing") or {}
    wins = tm.get("windows", [])
    hit = any(t(w["from"]) <= dep <= t(w["to"]) for w in wins)
    check("S3.6", "check_route's tidal-gate window contains the hand-computed departure", hit,
          "; ".join(f"{t(w['from']):%H:%M}–{t(w['to']):%H:%M}Z best {t(w['best']):%H:%M}" for w in wins))
    g = (tm.get("planned") or {}).get("gates", [{}])[0]
    check("S3.7", "at that departure the gate ETA is within 15 min of slack", g.get("nearestSlack") is not None and abs((t(g["eta"]) - t(g["nearestSlack"])).total_seconds()) <= 900,
          f"ETA {g.get('eta','')[:16]}, slack {g.get('nearestSlack','')[:16]}")
    legs = (tm.get("planned") or {}).get("legs", [])
    delta = sum(l["runningMinutes"] - l["runningMinutesAtSpeed"] for l in legs)
    check("S3.8", "applyCurrents changes running time where a current is known", any("meanAlongCurrentKnots" in l for l in legs),
          f"{tm.get('planned',{}).get('runningMinutes')} min with currents, {delta:+.1f} min vs speed alone; current on {sum(1 for l in legs if 'meanAlongCurrentKnots' in l)}/{len(legs)} legs")

if __name__ == "__main__":
    args = sys.argv[1:]; out = None
    if "--json" in args:
        out = args[args.index("--json") + 1]; del args[args.index("--json"):args.index("--json") + 2]
    want = set(args) or {"S1", "S2", "S3"}
    m = server()
    try:
        for sid, fn in (("S1", s1), ("S2", s2), ("S3", s3)):
            if sid in want: fn(m)
    finally:
        m.close()
    tally = {k: sum(1 for r in results if r["result"] == k) for k in ("PASS", "FAIL", "SKIP")}
    print(f"\n{tally['PASS']} passed, {tally['FAIL']} failed, {tally['SKIP']} skipped")
    if out: json.dump(results, open(out, "w"), indent=1)
    sys.exit(tally["FAIL"])
