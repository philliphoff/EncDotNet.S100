"""Builds the symbol dictionary prototype's data from the bundled S-100 catalogues.

Usage: python3 -I extract.py <content-dir> <out.json>
"""
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

CONTENT, OUT = sys.argv[1], sys.argv[2]
SPECS = ['S101', 'S401', 'S201', 'S124', 'S411', 'S421', 'S122', 'S127', 'S128', 'S129', 'S131', 'S125', 'S111']
SPEC_NAMES = {
    'S101': 'Electronic Navigational Chart', 'S401': 'Inland ENC', 'S201': 'Aids to Navigation Information',
    'S124': 'Navigational Warnings', 'S411': 'Sea Ice', 'S421': 'Route Plan', 'S122': 'Marine Protected Areas',
    'S127': 'Marine Traffic Management', 'S128': 'Catalogue of Nautical Products', 'S129': 'Under Keel Clearance Management',
    'S131': 'Marine Harbour Infrastructure', 'S125': 'Marine Navigational Services', 'S111': 'Surface Currents',
}


def local(tag):
    return tag.rsplit('}', 1)[-1]


def child_text(el, name):
    for c in el:
        if local(c.tag) == name:
            return (c.text or '').strip()
    return ''


def desc_of(el):
    for c in el:
        if local(c.tag) == 'description':
            return child_text(c, 'name'), child_text(c, 'description')
    return '', ''


SVG_STRIP = [
    (re.compile(r'<\?xml[^>]*\?>'), ''),
    (re.compile(r'<\?xml-stylesheet[^>]*\?>'), ''),
    (re.compile(r'<!--.*?-->', re.S), ''),
    (re.compile(r'<metadata>.*?</metadata>', re.S), ''),
    (re.compile(r'<title>.*?</title>', re.S), ''),
    (re.compile(r'<desc>.*?</desc>', re.S), ''),
    (re.compile(r'<(rect|circle)[^>]*class="[^"]*layout[^"]*"[^>]*/>'), ''),
    (re.compile(r'\s+'), ' '),
]


def clean_svg(text):
    for rx, rep in SVG_STRIP:
        text = rx.sub(rep, text)
    return text.strip()


def svg_box(svg):
    m = re.search(r'viewBox="([^"]+)"', svg)
    return [float(v) for v in m.group(1).replace(',', ' ').split()] if m else [0, 0, 10, 10]


def svg_inner(svg):
    m = re.search(r'<svg[^>]*>(.*)</svg>', svg, re.S)
    return m.group(1) if m else ''


def parse_css(path):
    rules = {}
    if not os.path.exists(path):
        return rules
    text = re.sub(r'/\*.*?\*/', '', open(path, encoding='utf-8').read(), flags=re.S)
    for sel, body in re.findall(r'([^{}]+)\{([^}]*)\}', text):
        for s in sel.split(','):
            s = s.strip()
            if s.startswith('.'):
                rules[s] = ' '.join(body.split())
    return rules


def color_profile(path):
    """token -> {palette: hex}"""
    out = {}
    if not os.path.exists(path):
        return out
    root = ET.parse(path).getroot()
    for pal in root.iter():
        if local(pal.tag) != 'palette':
            continue
        name = pal.get('name', '').lower()
        for item in pal:
            if local(item.tag) != 'item':
                continue
            tok = item.get('token')
            for c in item:
                if local(c.tag) == 'srgb':
                    r, g, b = (int(child_text(c, k)) for k in ('red', 'green', 'blue'))
                    out.setdefault(tok, {})[name] = f'#{r:02x}{g:02x}{b:02x}'
    return out


def parse_fc(path):
    feats, attrs = {}, {}
    if not os.path.exists(path):
        return feats, attrs
    root = ET.parse(path).getroot()
    for el in root.iter():
        t = local(el.tag)
        if t == 'S100_FC_FeatureType':
            code = child_text(el, 'code')
            prims = [ (c.text or '').strip() for c in el if local(c.tag) == 'permittedPrimitives']
            feats[code] = {
                'code': code, 'name': child_text(el, 'name'), 'alias': child_text(el, 'alias'),
                'def': child_text(el, 'definition'), 'prims': prims, 'abstract': el.get('isAbstract') == 'true',
            }
        elif t == 'S100_FC_SimpleAttribute':
            code = child_text(el, 'code')
            vals = {}
            for lv in el.iter():
                if local(lv.tag) == 'listedValue':
                    vals[child_text(lv, 'code')] = child_text(lv, 'label')
            attrs[code] = {'name': child_text(el, 'name'), 'vals': vals}
    return feats, attrs


COND_RX = re.compile(r'feature\.(\w+)(\[\d+\])?\s*(==|~=|<=|>=|<|>)\s*(-?\d+(?:\.\d+)?)')
CTX = {
    'SimplifiedSymbols': 'simplified symbols', 'PlainBoundaries': 'plain boundaries', 'RadarOverlay': 'radar overlay',
    'FullLightLines': 'full light sectors', 'IgnoreScaleMinimum': 'ignore scale minimum', 'ShallowWaterDangers': 'shallow water dangers',
    'FourShades': 'four depth shades', 'NationalLanguage': 'national language',
}


def humanise(cond, attrs):
    cond = cond.strip()
    cond = re.sub(r'\bthen\s*$', '', cond).strip()

    def rep(m):
        a, idx, op, v = m.groups()
        name = attrs.get(a, {}).get('name') or a
        label = attrs.get(a, {}).get('vals', {}).get(v)
        val = label.lower() if label else v
        op = {'==': '=', '~=': '≠'}.get(op, op)
        return f'{name.lower()}{idx or ""} {op} {val}'

    cond = COND_RX.sub(rep, cond)
    cond = re.sub(r'feature\.PrimitiveType\s*==\s*PrimitiveType\.(\w+)', lambda m: f'drawn as {m.group(1).lower()}', cond)
    cond = re.sub(r'not\s+contextParameters\.(\w+)', lambda m: f'{CTX.get(m.group(1), m.group(1))} off', cond)
    cond = re.sub(r'contextParameters\.(\w+)', lambda m: f'{CTX.get(m.group(1), m.group(1))} on', cond)
    cond = re.sub(r'feature\.(\w+)', lambda m: (attrs.get(m.group(1), {}).get('name') or m.group(1)).lower(), cond)
    cond = cond.replace(' and ', ', ').replace('~= nil', 'given').replace('== nil', 'not given')
    return cond[:140]



def negate_branch(lines, j, ind, attrs):
    """Describes the else branch at line j: the negation of a lone if, or 'none of the above'."""
    elseifs = 0
    for k in range(j - 1, -1, -1):
        l = lines[k]
        st = l.strip()
        if not st or st.startswith('--'):
            continue
        kind = len(l) - len(l.lstrip('\t '))
        if kind < ind:
            break
        if kind == ind and st.startswith('elseif'):
            elseifs += 1
        elif kind == ind and re.match(r'if\b', st):
            if elseifs:
                return 'none of the cases above'
            cond = re.sub(r'^if\s+', '', st)
            m = re.fullmatch(r'(not\s+)?contextParameters\.(\w+)\s*then', cond)
            if m:
                return f"{CTX.get(m.group(2), m.group(2))} {'on' if m.group(1) else 'off'}"
            return 'not (' + humanise(cond, attrs) + ')'
    return 'otherwise'

def lua_refs(text, items, attrs):
    """item -> list of humanised condition chains"""
    lines = text.split('\n')
    out = {}
    for i, line in enumerate(lines):
        toks = set(re.findall(r"[A-Za-z0-9_]+", line)) & items
        if not toks or line.strip().startswith('--'):
            continue
        indent = len(line) - len(line.lstrip('\t '))
        chain = []
        for j in range(i - 1, -1, -1):
            l = lines[j]
            s = l.strip()
            if not s or s.startswith('--'):
                continue
            ind = len(l) - len(l.lstrip('\t '))
            if ind < indent and re.match(r'(if|elseif|else)\b', s):
                if s.startswith('else') and not s.startswith('elseif'):
                    chain.append(negate_branch(lines, j, ind, attrs))
                else:
                    chain.append(humanise(re.sub(r'^(if|elseif)\s+', '', s), attrs))
                indent = ind
            elif ind < indent and re.match(r'function\b', s):
                break
        cond = '; '.join(reversed([c for c in chain if c]))
        for t in toks:
            lst = out.setdefault(t, [])
            if cond and cond not in lst and len(lst) < 6:
                lst.append(cond)
            elif not cond and '' not in lst:
                lst.append('')
    return out


def render_line(ls_path, sym_svgs):
    """Line style sample as an SVG fragment in mm, ~ two intervals long."""
    root = ET.parse(ls_path).getroot()
    if local(root.tag) == 'compositeLineStyle':
        return None
    interval = float(child_text(root, 'intervalLength') or 0)
    width, color = 0.32, 'CHBLK'
    for c in root:
        if local(c.tag) == 'pen':
            width = float(c.get('width', 0.32))
            color = child_text(c, 'color')
    dashes = [(float(child_text(d, 'start')), float(child_text(d, 'length'))) for d in root if local(d.tag) == 'dash']
    syms = []
    for s in root:
        if local(s.tag) == 'symbol':
            syms.append((s.get('reference'), float(child_text(s, 'position') or 0)))
    length = 30.0
    reps = max(1, int(length // interval) + 1) if interval > 0 else 1
    parts = []
    if not dashes:
        parts.append(f'<line x1="0" y1="0" x2="{length}" y2="0" stroke-width="{width}" class="s{color}"/>')
    for r in range(reps):
        off = r * interval
        for st, ln in dashes:
            x1, x2 = off + st, min(off + st + ln, length)
            if x1 < length:
                parts.append(f'<line x1="{x1:.2f}" y1="0" x2="{x2:.2f}" y2="0" stroke-width="{width}" class="s{color}"/>')
        for ref, pos in syms:
            x = off + pos
            if x < length and ref in sym_svgs:
                parts.append(f'<g transform="translate({x:.2f} 0)">{svg_inner(sym_svgs[ref])}</g>')
    return {'svg': ''.join(parts), 'box': [0, -3, length, 6], 'color': color}


def render_fill(af_path, sym_svgs):
    root = ET.parse(af_path).getroot()
    ref = None
    v1 = v2 = None
    for c in root:
        n = local(c.tag)
        if n == 'symbol':
            ref = c.get('reference')
        elif n in ('v1', 'v2'):
            vec = (float(child_text(c, 'x') or 0), float(child_text(c, 'y') or 0))
            if n == 'v1':
                v1 = vec
            else:
                v2 = vec
    if not ref or ref not in sym_svgs or not v1:
        return None
    inner = svg_inner(sym_svgs[ref])
    v2 = v2 or (0, v1[0])
    size = 40.0
    parts = []
    for i in range(-2, 4):
        for j in range(-2, 4):
            x = i * v1[0] + j * v2[0]
            y = i * v1[1] + j * v2[1]
            if -10 < x < size + 10 and -10 < y < size + 10:
                parts.append(f'<g transform="translate({x:.2f} {y:.2f})">{inner}</g>')
    return {'svg': ''.join(parts), 'box': [0, 0, size, size], 'ref': ref}


data = {'specs': [], 'css': {}}
for spec in SPECS:
    base = os.path.join(CONTENT, spec)
    pc_path = os.path.join(base, 'pc', 'portrayal_catalogue.xml')
    if not os.path.exists(pc_path):
        continue
    pc = ET.parse(pc_path).getroot()
    feats, attrs = parse_fc(os.path.join(base, 'fc', 'FeatureCatalogue.xml'))

    css = {pal: parse_css(os.path.join(base, 'pc', 'Symbols', f'{pal}SvgStyle.css')) for pal in ('day', 'dusk', 'night')}
    profile = color_profile(os.path.join(base, 'pc', 'ColorProfiles', 'colorProfile.xml'))
    data['css'][spec] = {'classes': css, 'tokens': profile}

    # viewing groups -> name and display mode
    vg_name, vg_layer, layer_name, mode_layers = {}, {}, {}, {}
    for el in pc.iter():
        t = local(el.tag)
        if t == 'viewingGroup' and el.get('id'):
            vg_name[el.get('id')] = desc_of(el)
        elif t == 'viewingGroupLayer' and el.get('id'):
            layer_name[el.get('id')] = desc_of(el)[0]
            for c in el:
                if local(c.tag) == 'viewingGroup':
                    vg_layer.setdefault((c.text or '').strip(), el.get('id'))
        elif t == 'displayMode' and el.get('id'):
            mode_layers[el.get('id')] = [(c.text or '').strip() for c in el if local(c.tag) == 'viewingGroupLayer']

    def display_cat(vg):
        layer = vg_layer.get(str(vg))
        if layer is None:
            return None
        for m in ('DisplayBase', 'StandardDisplay', 'OtherInformation'):
            if layer in mode_layers.get(m, []):
                return {'DisplayBase': 'Base', 'StandardDisplay': 'Standard', 'OtherInformation': 'Other'}[m]
        return None

    items = {}
    sym_svgs = {}
    for el in pc.iter():
        t = local(el.tag)
        if t not in ('symbol', 'lineStyle', 'areaFill') or not el.get('id'):
            continue
        name, desc = desc_of(el)
        fn = child_text(el, 'fileName')
        kind = {'symbol': 'point', 'lineStyle': 'line', 'areaFill': 'area'}[t]
        items[el.get('id')] = {'id': el.get('id'), 'kind': kind, 'desc': desc or name, 'file': fn}
        if t == 'symbol':
            p = os.path.join(base, 'pc', 'Symbols', fn)
            if os.path.exists(p):
                sym_svgs[el.get('id')] = clean_svg(open(p, encoding='utf-8').read())

    for iid, it in items.items():
        if it['kind'] == 'point' and iid in sym_svgs:
            svg = sym_svgs[iid]
            it['box'] = svg_box(svg)
            it['svg'] = svg_inner(svg)
        elif it['kind'] == 'line':
            p = os.path.join(base, 'pc', 'LineStyles', it['file'])
            r = render_line(p, sym_svgs) if os.path.exists(p) and p.endswith('.xml') else None
            if r:
                it['svg'], it['box'] = r['svg'], r['box']
        elif it['kind'] == 'area':
            p = os.path.join(base, 'pc', 'AreaFills', it['file'])
            r = render_fill(p, sym_svgs) if os.path.exists(p) and p.endswith('.xml') else None
            if r:
                it['svg'], it['box'] = r['svg'], r['box']

    # pattern-fill helper symbols (e.g. AIRARE02P) are shown through their fill
    helper = {it['ref'] for it in []}

    # rules -> feature/item links
    ids = set(items)
    links = {}  # feature -> item -> conds
    fills, pens = {}, {}
    feat_vg = {}
    prefixes = {re.sub(r'\d+$', '', i) for i in ids if re.search(r'\d$', i)}
    for rf in sorted(f for f in glob.glob(os.path.join(base, 'pc', 'Rules', '**', '*'), recursive=True) if os.path.isfile(f) and f.endswith(('.lua', '.xsl'))):
        fname = os.path.splitext(os.path.basename(rf))[0]
        text = open(rf, encoding='utf-8', errors='ignore').read()
        owner = fname if fname in feats else None
        if rf.endswith('.lua'):
            refs = lua_refs(text, ids, attrs)
            m = re.search(r'local viewingGroup\s*=\s*(\d+)', text)
            if m and owner:
                feat_vg[owner] = m.group(1)
        else:
            refs = {i: [] for i in set(re.findall(r'[A-Za-z0-9_]+', text)) & ids}
            for stem in re.findall(r'(?:Symbols|LineStyles|AreaFills)/([\w-]+)\.(?:svg|xml)', text):
                for i, it in items.items():
                    if os.path.splitext(it['file'])[0] == stem:
                        refs.setdefault(i, [])
            for tm in re.findall(r'<xsl:template match="(?:\w+:)?([A-Za-z_]+)', text):
                if tm in feats and owner is None:
                    owner = tm
            m = re.search(r'<viewingGroup>(\d+)</viewingGroup>', text)
            if m and owner:
                feat_vg.setdefault(owner, m.group(1))
        # composed families, e.g. 'SOUNDG' .. digit
        for pfx in prefixes:
            if re.search(r"['\"]" + re.escape(pfx) + r"['\"]?\s*\.\.", text) or re.search(r"['\"]" + re.escape(pfx) + r"%", text):
                for i in ids:
                    if re.sub(r'\d+$', '', i) == pfx and i not in refs:
                        refs[i] = ['composed by the rule from the value (digits, depth)']
        key = owner or f'({fname})'
        fl = fills.setdefault(key, [])
        toks = re.findall(r'ColorFill:([A-Z]+)', text)
        if "ColorFill:' .." in text:
            toks += [t for t in re.findall(r"'([A-Z]{5})'", text) if t in profile]
        for tok in toks:
            if tok not in fl:
                fl.append(tok)
        pn = pens.setdefault(key, [])
        for dash, w, tok in re.findall(r'LineStyle:_simple_,(\w*),([\d.]+),([A-Z]+)', text):
            pen = [dash or 'solid', float(w), tok]
            if pen not in pn:
                pn.append(pen)
        if not refs:
            continue
        dest = links.setdefault(key, {})
        for i, conds in refs.items():
            lst = dest.setdefault(i, [])
            for c in conds:
                if c not in lst and len(lst) < 6:
                    lst.append(c)

    # shared helper rule files: attribute their items to every feature that requires them
    direct = {}
    for rf in glob.glob(os.path.join(base, 'pc', 'Rules', '*.lua')):
        fname = os.path.splitext(os.path.basename(rf))[0]
        text = open(rf, encoding='utf-8', errors='ignore').read()
        direct[fname] = re.findall(r"require\s*\(?\s*'([^']+)'", text)
    requires = {}
    for fname in direct:
        if fname not in feats:
            continue
        seen, stack = set(), list(direct[fname])
        while stack:
            r = stack.pop()
            if r in seen or r == 'Default':
                continue
            seen.add(r)
            stack.extend(direct.get(r, []))
        for req in seen:
            requires.setdefault(req, []).append(fname)
    for helper_name, users in requires.items():
        key = f'({helper_name})'
        for u in users:
            for tok in fills.get(key, []):
                if tok not in fills.setdefault(u, []):
                    fills[u].append(tok)
            for pen in pens.get(key, []):
                if pen not in pens.setdefault(u, []):
                    pens[u].append(pen)
        if key in links:
            for u in users:
                dest = links.setdefault(u, {})
                for i, conds in links[key].items():
                    dest.setdefault(i, ['via ' + helper_name + (': ' + conds[0] if conds and conds[0] else '')])

    used_by = {}
    for f, its in links.items():
        if f.startswith('('):
            continue
        for i in its:
            used_by.setdefault(i, []).append(f)

    features = []
    for code, f in sorted(feats.items(), key=lambda kv: kv[1]['name'].lower()):
        if f['abstract']:
            continue
        its = links.get(code, {})
        vg = feat_vg.get(code)
        order = sorted(its, key=lambda i: ({'point': 0, 'line': 1, 'area': 2}[items[i]['kind']], i))
        features.append({
            'code': code, 'name': f['name'], 'alias': f['alias'], 'def': f['def'], 'prims': f['prims'],
            'vg': vg, 'vgName': vg_name.get(vg, ('', ''))[0] if vg else '', 'vgSection': vg_name.get(vg, ('', ''))[1] if vg else '',
            'cat': display_cat(vg) if vg else None,
            'items': [{'id': i, 'conds': its[i]} for i in order],
            'fills': fills.get(code, [])[:6], 'pens': pens.get(code, [])[:4],
        })

    out_items = []
    for iid, it in sorted(items.items()):
        if 'svg' not in it:
            continue
        out_items.append({'id': iid, 'kind': it['kind'], 'desc': it['desc'], 'box': it['box'], 'svg': it['svg'],
                          'usedBy': sorted(set(used_by.get(iid, [])))})

    data['specs'].append({'id': spec, 'title': SPEC_NAMES.get(spec, spec), 'features': features, 'items': out_items})
    print(spec, 'features', len(features), 'with symbols', sum(1 for f in features if f['items']),
          'items', len(out_items), 'linked', sum(1 for i in out_items if i['usedBy']), file=sys.stderr)

json.dump(data, open(OUT, 'w', encoding='utf-8'), separators=(',', ':'), ensure_ascii=False)
print('bytes', os.path.getsize(OUT), file=sys.stderr)
