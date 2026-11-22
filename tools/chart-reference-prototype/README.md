# Chart reference prototype (reference only)

The Python extractor and page template behind the Chart reference prototype
(https://claude.ai/artifact/Peujh6ZNQhhoJ4MsNAJT1i). They are the reference
for the C# rule readers in #1634. This branch isn't meant to merge.

```
python3 -I extract.py src/EncDotNet.S100.Specifications/content data.json
```

`extract.py` reads each spec's feature catalogue, portrayal catalogue and
rules. For Lua rules, it finds the item names each rule names and walks up
the enclosing `if`/`elseif`/`else` blocks to build their conditions. It
follows `require` transitively and links prefix families (`'SOUNDG' ..`). It
also picks up `ColorFill:` and simple `LineStyle:` instructions. For XSLT
rules, it reads `<symbol reference>`, symbol file paths and the template's
`match`. The design is in `docs/design/symbol-dictionary.md` §2–3 (branch
`docs/symbol-dictionary-design`, PR #1640).
