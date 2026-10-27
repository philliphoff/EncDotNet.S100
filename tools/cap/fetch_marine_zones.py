"""Fetches every NWS marine zone (coastal mz18mr25 and offshore oz18mr25) as one
GeoJSON feature per UGC code, into the directory given as the first argument.

weather.gov wasn't reachable when the layer was built, so this reads the KDE
foss-public-alert-server mirror, whose generate_UGC.py wrote each feature of
the NWS shapefiles unchanged (pyshp's __geo_interface__: the NWS geometry and
DBF attributes) to data/UGC/<UGC>.geojson. Every marine UGC prefix is probed
for zones 000 to 999; a missing file is a code with no zone.

Usage: python3 -I fetch_marine_zones.py <out-dir>
Set SSL_CERT_FILE for a proxy's CA bundle.
"""
import concurrent.futures
import json
import os
import ssl
import sys
import urllib.error
import urllib.request

BASE = "https://raw.githubusercontent.com/KDE/foss-public-alert-server/master/foss_public_alert_server/alertHandler/data/UGC/"
PREFIXES = ["AM", "AN", "GM", "LC", "LE", "LH", "LM", "LO", "LS", "PH", "PK", "PM", "PS", "PZ", "SL"]
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
ctx = ssl.create_default_context(cafile=os.environ.get("SSL_CERT_FILE"))


def fetch(ugc):
    path = os.path.join(out, ugc + ".geojson")
    if os.path.exists(path):
        return ugc, True
    for attempt in range(4):
        try:
            with urllib.request.urlopen(BASE + ugc + ".geojson", context=ctx, timeout=30) as r:
                data = r.read()
            with open(path, "wb") as f:
                f.write(data)
            return ugc, True
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return ugc, False
        except Exception:
            pass
    return ugc, None


ids = [f"{p}Z{n:03d}" for p in PREFIXES for n in range(1000)]
found = failed = 0
with concurrent.futures.ThreadPoolExecutor(48) as pool:
    for ugc, ok in pool.map(fetch, ids):
        if ok:
            found += 1
        elif ok is None:
            failed += 1
            print("FAILED", ugc, flush=True)
print("found", found, "failed", failed)
