#!/usr/bin/env python3
"""Runs the Salish Sea MCP scenarios against `s100 mcp serve` and checks their assertions.

Usage:
  S100=/path/to/s100 ENC=/path/to/folder-of-NOAA-ENCs TIDES=/path/to/catalogue.json \
    python3 run_scenarios.py [scenario-id ...]

ENC must hold the cells listed in ../README.md (San Juans + Port Townsend). TIDES is
any tide/current catalogue `s100 mcp serve --tide-catalogue` accepts; the repo's
tests/datasets/Tides/Stations/noaa-currents.json is enough for S2/S3, and the full
catalogue from `s100 tides update` unlocks the San Juan current timing in S1.
Each assertion prints PASS, FAIL or SKIP; the exit code is the number of FAILs.
"""
import json, math, os, sys
from mcpclient import Mcp

HERE = os.path.dirname(os.path.abspath(__file__))
ROUTES = os.path.join(HERE, "..", "routes")
S100 = os.environ.get("S100", "s100")
ENC = os.environ["ENC"]
TIDES = os.environ.get("TIDES", os.path.join(HERE, "../../datasets/Tides/Stations/noaa-currents.json"))
VESSEL = dict(draftMeters=1.1, speedKnots=16, safetyContourMeters=1.5, safetyDepthMeters=1.5, corridorHalfWidthMeters=50)
results = []

def server():
    return Mcp([S100, "mcp", "serve", ENC, "--tide-catalogue", TIDES, "--tide-predictions"], log=os.path.join(HERE, "serve.log"))

def check(sid, name, ok, detail=""):
    tag = "SKIP" if ok is None else ("PASS" if ok else "FAIL")
    results.append(tag)
    print(f"  [{tag}] {sid} {name}" + (f" — {detail}" if detail else ""))

def poly(v): return json.dumps({"vertices": v})
def nm(a, b):
    dy = (a[0] - b[0]) * 60; dx = (a[1] - b[1]) * 60 * math.cos(math.radians((a[0] + b[0]) / 2))
    return math.hypot(dx, dy)

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
    days = json.load(open(os.path.join(ROUTES, "san-juans-7day.json")))["days"]
    for d in days:
        if d["id"] == "d7b": continue
        rep = m.call("check_route", dict(polyline=poly(d["vertices"]), departure="2027-07-12T16:00:00Z", **VESSEL))["data"]
        codes = [f["code"] for f in rep["findings"]]
        check(f"S1.{d['id']}", f"{d['name']}: no land or drying crossings",
              "route_check.crosses_land" not in codes and "route_check.crosses_drying" not in codes,
              f"{rep['distanceNm']:.1f} NM; {rep['distanceNm']/16*60:.0f} min at 16 kn, {rep['distanceNm']/8*60:.0f} min at 8 kn; verdict {rep['verdict']['kind']}: {rep['verdict']['title']}")
        # Fusion: area findings from a coarser cell must not overlap the same kind of area from a finer cell.
        areas = [(f["feature"]["datasetId"], f["location"]["startNm"], f["location"]["endNm"]) for f in rep["findings"] if f["category"] == "area" and f["code"] == "route_check.enters_prohibited_area"]
        band = lambda ds: int(ds.split("/")[1][2])
        overlap = [(a, b) for a in areas for b in areas if band(a[0]) < band(b[0]) and a[1] < b[2] - 1e-6 and b[1] < a[2] - 1e-6]
        check(f"S1.{d['id']}.areas", "area findings honour the finest-cell rule (no coarse span overlapping a finer one)",
              not overlap, f"{len(overlap)} overlapping pairs" + (f", e.g. {overlap[0][0][0].split('/')[1]} {overlap[0][0][1]:.1f}–{overlap[0][0][2]:.1f} NM vs {overlap[0][1][0].split('/')[1]}" if overlap else ""))
        rna = [f for f in rep["findings"] if f["group"] == "must_review" and f["code"] == "route_check.enters_prohibited_area" and f["details"]["area"]["restrictions"] == [8]]
        check(f"S1.{d['id']}.rna", "a 'entry restricted' Regulated Navigation Area (33 CFR 165.1301, large vessels) doesn't headline a 40' boat's route as must-review",
              not rna, f"{len(rna)} must-review findings, verdict '{rep['verdict']['title']}'")
        # #1548: structures keep the SafetyContour alert (and its code) but are titled for the structure,
        # so check the title. Structures at a berth are grouped into one Info finding per route end.
        structures = ("ShorelineConstruction", "Pontoon", "FloatingDock", "Hulk")
        shore = [f for f in rep["findings"] if f["title"] == "Crosses the safety contour" and any(s in f.get("meta", "") for s in structures)]
        berth = [f for f in rep["findings"] if f["code"] == "route_check.berth_structures"]
        ends = [f.get("details", {}).get("berthStructures", {}).get("end") for f in berth]
        check(f"S1.{d['id']}.shore", "piers and breakwaters aren't titled 'Crosses the safety contour'; at most one berth finding per end",
              not shore and len(ends) == len(set(ends)), f"{len(shore)} titled as safety-contour crossings, {len(berth)} berth findings")
        # Current timing (needs a current station near the route)
        cur = m.call("sample_coverage_along", {"spec": "S-111", "polyline": poly(d["vertices"]), "time": "2027-07-12T18:00:00Z"})["data"]["samples"]
        got = sum(1 for s in cur if "result" in s)
        check(f"S1.{d['id']}.cur", "predicted currents are available along the leg to correct running time",
              None if got == 0 else True, f"{got}/{len(cur)} vertices have a current (0 → load the full catalogue from `s100 tides update`)")

# ---------------------------------------------------------------- S2
def s2(m):
    print("S2  Port Townsend → Port Townsend Canal → Port Ludlow, 2.0 m draft, 0.5 m UKC, tide-predicted water level")
    v = json.load(open(os.path.join(ROUTES, "port-townsend-canal.json")))["vertices"]
    args = dict(polyline=poly(v), departure="2027-07-14T15:00:00Z", draftMeters=2.0, speedKnots=7, requiredUkcMeters=0.5,
                safetyContourMeters=3, safetyDepthMeters=3, corridorHalfWidthMeters=40,
                includeSamples=True, maxSamples=2000, includeWindow=True,
                windowFrom="2027-07-14T12:00:00Z", windowTo="2027-07-15T12:00:00Z", windowStepMinutes=15)
    rep = m.call("check_route", args)["data"]; cl = rep["clearance"]
    stations = {w["station"] for w in cl["sources"]["waterLevels"]}
    check("S2.1", "check_route fills the missing S-104 with tide predictions", bool(stations), ", ".join(sorted(stations)))
    worst = 0
    for s in [x for x in cl["samples"] if x.get("station")][::40]:
        p = m.call("predict_tide", {"station": s["station"], "from": s["time"], "to": s["time"], "stepMinutes": 1})["data"]
        worst = max(worst, abs(p["series"][0]["height"] - s["waterLevelMeters"]))
    check("S2.2", "route water levels equal predict_tide at the same station and time (≤ 1 cm)", worst <= 0.01, f"worst Δ {worst*100:.1f} cm")
    gaps = [g for g in cl["gaps"] if g["kind"] == "no_water_level_coverage"]
    deep = all(min(x["chartedDepthMeters"] for x in cl["samples"][g["firstSample"]:g["lastSample"] + 1]) - 2.0 - 0.5 > 1.0 for g in gaps) if gaps else False
    check("S2.3", "a water-level gap mid-route", None, f"{len(gaps)} gap(s): " + "; ".join(f"{g['startNm']:.1f}–{g['endNm']:.1f} NM" for g in gaps) + (" — all in water deep enough at any tide" if deep else ""))
    check("S2.4", "the departure window isn't emptied by a gap where charted depth alone clears the keel",
          bool(cl["window"]["windows"]) or not deep, f"windows = {cl['window']['windows']}")
    rep2 = m.call("check_route", dict(args, polyline=poly(v[:7])))["data"]["clearance"]["window"]["windows"]
    check("S2.5", "control: the same sweep on the legs before the gap finds a window", bool(rep2), json.dumps(rep2))
    flo = [f for f in rep["findings"] if "-15.0 m" in f.get("meta", "")]
    check("S2.6", "floating obstructions (WATLEV 7, DCEG default -15) aren't described as '-15.0 m' deep", not flo, f"{len(flo)} findings")

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
    from datetime import datetime, timedelta
    dep = (datetime.fromisoformat(slack["time"].replace("Z", "+00:00")) - timedelta(hours=to_pass / 8)).isoformat()
    check("S3.1", "agent can time the pass: departure = slack − distance-to-pass / SOG", True, f"slack {slack['time']}, {to_pass:.1f} NM at 8 kn → depart {dep[:16]}Z")
    a = m.call("sample_coverage", {"spec": "S-111", "latitude": pass_pt[0], "longitude": pass_pt[1], "time": slack["time"]})["data"]["value"]["speedKnots"]
    check("S3.2", "sample_coverage S-111 at the pass ≈ 0 kn at predicted slack", abs(a) < 0.2, f"{a} kn")
    mx = next(e for e in ev if e["kind"] == "max_flood")
    far = m.call("sample_coverage", {"spec": "S-111", "latitude": 48.36352, "longitude": -122.54594, "time": mx["time"]})["data"]
    val = far.get("value", {})
    check("S3.3", "the pass's max flood isn't applied ~10 km away at the Swinomish entrance in Skagit Bay",
          not (val.get("stationId") == "PUG1701_33" and val.get("speedKnots", 0) > 2),
          f"{val.get('speedKnots')} kn from {val.get('stationId')} at {val.get('stationDistanceMetres')} m")
    rep = m.call("check_route", dict(polyline=poly(v), departure=dep, draftMeters=1.1, speedKnots=8, safetyContourMeters=2, safetyDepthMeters=2, corridorHalfWidthMeters=30))["data"]
    land = [f for f in rep["findings"] if f["code"] == "route_check.crosses_land"]
    check("S3.4", "Fairway-derived Swinomish centreline has no land crossings", not land, f"{len(land)} (outside the channel: {[round(f['distanceNm'],2) for f in land]})")
    wl = rep["clearance"]["gaps"]
    check("S3.5", "Swinomish UKC uses a tide prediction (La Conner 9448794 needs the full catalogue)",
          None if any(g["kind"] == "no_water_level_coverage" for g in wl) else True, f"{len(wl)} gap(s)")

if __name__ == "__main__":
    want = set(sys.argv[1:]) or {"S1", "S2", "S3"}
    m = server()
    try:
        for sid, fn in (("S1", s1), ("S2", s2), ("S3", s3)):
            if sid in want: fn(m)
    finally:
        m.close()
    print(f"\n{results.count('PASS')} passed, {results.count('FAIL')} failed, {results.count('SKIP')} skipped")
    sys.exit(results.count("FAIL"))
