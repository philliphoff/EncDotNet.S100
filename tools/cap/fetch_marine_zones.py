"""Downloads one release of the NWS coastal (mzDDmmYY.zip) and offshore
(ozDDmmYY.zip) marine zone shapefiles from https://www.weather.gov/gis/MarineZones
into the directory given as the first argument, for build_marine_zones.py.

The release is the date part of the file names the page lists, for example
16ap26 for mz16ap26.zip and oz16ap26.zip. Use the newest one the page offers.
The zips are untrusted downloads: give a fresh, empty directory outside the
repository.

Usage: python3 -I fetch_marine_zones.py <out-dir> <release>
Set SSL_CERT_FILE for a proxy's CA bundle.
"""
import os
import ssl
import sys
import urllib.request

BASE = "https://www.weather.gov/source/gis/Shapefiles/WSOM/"
USER_AGENT = "EncDotNet.S100 fixture capture (+https://github.com/philliphoff/EncDotNet.S100)"

out, release = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
ctx = ssl.create_default_context(cafile=os.environ.get("SSL_CERT_FILE"))

for kind in ("mz", "oz"):
    name = f"{kind}{release}.zip"
    request = urllib.request.Request(BASE + name, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, context=ctx, timeout=120) as r:
        data = r.read()
    with open(os.path.join(out, name), "wb") as f:
        f.write(data)
    print(name, len(data))
