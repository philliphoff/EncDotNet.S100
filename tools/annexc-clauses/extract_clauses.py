import re, sys, xml.etree.ElementTree as ET
toc, fc = sys.argv[1], sys.argv[2]
lines = open(toc).read().splitlines()
entries = {}
for i, l in enumerate(lines[:1000]):
    m = re.match(r'^\s*(\d+(?:\.\d+)+)\s+(.+?)\s*\.{3,}', l)
    if m:
        num, title = m.group(1), m.group(2)
        title = re.sub(r'\s*\(see S-4.*', '', title).strip()
        entries.setdefault(num, title)
ns = {'S100FC': 'http://www.iho.int/S100FC/5.0'}
root = ET.parse(fc).getroot()
def norm(s): return re.sub(r'[^a-z]', '', s.lower())
names = {}
for tag in ['featureType', 'informationType', 'simpleAttribute', 'complexAttribute']:
    for ft in root.iter():
        if ft.tag.endswith('}S100_FC_' + tag[0].upper() + tag[1:]):
            name = code = None
            for c in ft:
                if c.tag.endswith('}name'): name = c.text
                if c.tag.endswith('}code'): code = c.text
            if name and code: names.setdefault((tag, norm(name)), code)
out = []
for num, title in entries.items():
    sec = int(num.split('.')[0])
    kinds = ['featureType'] if 3 <= sec <= 23 else ['informationType'] if sec == 24 else ['simpleAttribute'] if sec in (27, 28) else ['complexAttribute'] if sec == 29 else []
    t = re.sub(r'\(.*?\)', '', title).strip()
    for k in kinds:
        code = names.get((k, norm(t)))
        if code: out.append((k, code, num, title))
for k, code, num, title in out:
    print(f'{k}\t{code}\t{num}\t{title}')

# Second pass: which section 27/28 attributes Annex C says are never converted.
text = lines
starts = {}
for i, l in enumerate(text):
    m = re.match(r'^\s*(2[78]\.\d+)\s+\S', l)
    if m and '....' not in l and i > 1000:
        starts[m.group(1)] = i
with open(sys.argv[3], 'w') as f:
    for k, code, num, title in out:
        if k != 'simpleAttribute' or num not in starts: continue
        i = starts[num]
        para = ' '.join(x.strip() for x in text[i+1:i+8])
        para = re.sub(r'\s+', ' ', para)
        first = para.split('. ')[0]
        if 'will not be converted' in first and not first.startswith('Unless') and 'With the exception' not in first:
            f.write(f'{code}\t{num}\n')
