"""Writes the CAP severity hatching symbols (CAPHATCH*.svg) into the directory
given, src/EncDotNet.S100.Datasets.Cap/Symbols: one tile of a square lattice
each, a diagonal line (crossed for Extreme) in the severity's colour class.

Usage: python3 -I make_hatch_symbols.py src/EncDotNet.S100.Datasets.Cap/Symbols
"""
import sys
out = sys.argv[1]
# name, token, spacing (mm), stroke (mm), cross-hatched, description
specs = [
    ("CAPHATCHEXT", "CAPEXT", 2.0, 0.25, True, "Extreme severity: dense cross-hatching."),
    ("CAPHATCHSEV", "CAPSEV", 2.5, 0.25, False, "Severe severity: dense diagonal hatching."),
    ("CAPHATCHMOD", "CAPMOD", 4.0, 0.2, False, "Moderate severity: diagonal hatching."),
    ("CAPHATCHMIN", "CAPMIN", 6.0, 0.2, False, "Minor severity: sparse diagonal hatching."),
    ("CAPHATCHUNK", "CAPUNK", 6.0, 0.15, False, "Unknown severity: sparse diagonal hatching."),
]
for name, token, s, w, cross, desc in specs:
    h = s / 2
    e = h * 1.1
    paths = f'<path class="s{token}" fill="none" stroke-width="{w}" d="M{-e:g},{e:g} L{e:g},{-e:g}"/>'
    if cross:
        paths += f'<path class="s{token}" fill="none" stroke-width="{w}" d="M{-e:g},{-e:g} L{e:g},{e:g}"/>'
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" version="1.2" width="{s:g}mm" height="{s:g}mm" viewBox="{-h:g} {-h:g} {s:g} {s:g}">'
           f'<title>{name}</title><desc>{desc} One tile of a {s:g} mm square lattice.</desc>'
           f'<rect class="symbolBox layout" fill="none" x="{-h:g}" y="{-h:g}" width="{s:g}" height="{s:g}"/>'
           f'{paths}<circle class="pivotPoint layout" fill="none" cx="0" cy="0" r="0.5"/></svg>\n')
    open(f"{out}/{name}.svg", "w").write(svg)
    print(name, s)
