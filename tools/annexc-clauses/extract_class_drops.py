import re, sys, xml.etree.ElementTree as ET
# Lists, per S-101 feature or information type, the attributes its Annex C
# clause says "will not be converted" (§3-§24), as tab-separated
# (class code, attribute code, clause). Sentences that apply only to one
# branch of a conditional conversion ("When converting to ...", "Where ...")
# are skipped, as are those that go on to list exceptions ("except for"). A
# sentence that names a sub-attribute ("complex attribute speed, sub-attribute
# speed minimum") drops the sub-attribute only.
text, fc = sys.argv[1], sys.argv[2]
lines = open(text).read().splitlines()

root = ET.parse(fc).getroot()
def norm(s): return re.sub(r'[^a-z]', '', s.lower())
classes, attributes = {}, {}
for el in root.iter():
    tag = el.tag.rsplit('}', 1)[-1]
    if tag not in ('S100_FC_FeatureType', 'S100_FC_InformationType', 'S100_FC_SimpleAttribute', 'S100_FC_ComplexAttribute'):
        continue
    name = code = None
    for c in el:
        if c.tag.endswith('}name'): name = c.text
        if c.tag.endswith('}code'): code = c.text
    if not (name and code): continue
    target = classes if tag in ('S100_FC_FeatureType', 'S100_FC_InformationType') else attributes
    target.setdefault(norm(name), code)

# Split the body (after the table of contents) into clauses.
clauses, current, body = [], None, []
for i, l in enumerate(lines):
    if i < 1000: continue
    m = re.match(r'^\s*(\d{1,2}\.\d{1,2})\s{2,}\S', l)
    if m and '....' not in l and 3 <= int(m.group(1).split('.')[0]) <= 24:
        if current: clauses.append((current, body))
        current, body = m.group(1), []
    elif current:
        body.append(l)
if current: clauses.append((current, body))

out = set()
for num, body in clauses:
    joined = ' '.join(x.strip() for x in body)
    joined = re.sub(r'S-65 Annex C\s+May 2025\s+Edition 1\.0\.0', ' ', joined)
    joined = re.sub(r'\d*\s*S-101 to S-57 ENC Conversion Guidance\s*\d*', ' ', joined)
    joined = re.sub(r'\s+', ' ', joined)
    m = re.search(r'S-101 (?:Geo|Meta) Feature:\s*(.+?)\s*\(', joined) or re.search(r'S-101 Information Type:\s*(.+?)\s*\(', joined)
    if not m: continue
    cls = classes.get(norm(m.group(1)))
    if not cls: continue
    for sentence in re.split(r'(?<=\.)\s+|•|\bo\s', joined):
        if 'will not be converted' not in sentence: continue
        s = sentence.strip()
        if re.match(r'^(When|Where|If|For [A-Z])', s): continue
        if 'except' in s.split('will not be converted')[1][:40]: continue
        head = s.split('will not be converted')[0]
        if 'sub-attribute' in head:
            head = 'attributes ' + head.split('sub-attribute', 1)[1].lstrip('s ')
        hm = re.search(r'attributes?\s+(.+?)\s*(?:\(|$)', head)
        if not hm: continue
        names = re.split(r',\s*|\s+and\s+|\s+or\s+', hm.group(1))
        for n in names:
            n = re.sub(r'\b(the|complex|simple|attribute|attributes|sub-attribute|sub-attributes|S-101)\b', '', n).strip()
            code = attributes.get(norm(n))
            if code: out.add((cls, code, num))
for cls, code, num in sorted(out):
    print(f'{cls}\t{code}\t{num}')
