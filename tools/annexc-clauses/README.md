# Annex C clause table

These scripts generate
[`S101ToS57AnnexCClauses.cs`](../../src/EncDotNet.S100.Datasets.S57/S101ToS57AnnexCClauses.cs),
the S-65 Annex C clause for each S-101 feature type, information type and
attribute, which `S101ToS57Translator` cites. Annex C is prose only, so the
table is built from its table of contents, matched by name against the bundled
S-101 Feature Catalogue. The scripts also list the attributes whose clause says,
without exception, that they "will not be converted".

Regenerate the table when Annex C or the bundled Feature Catalogue changes:

```bash
curl -L -o annexc.pdf "https://iho.int/uploads/user/pubs/standards/s-65/S-65%20Annex%20C_Ed%201.0.0_FINAL.pdf"
pdftotext -layout annexc.pdf annexc.txt
python3 -I tools/annexc-clauses/extract_clauses.py annexc.txt src/EncDotNet.S100.Specifications/content/S101/fc/FeatureCatalogue.xml notconv.tsv > clauses.tsv
python3 -I tools/annexc-clauses/generate_cs.py clauses.tsv notconv.tsv src/EncDotNet.S100.Datasets.S57/S101ToS57AnnexCClauses.cs
```

`pdftotext` is part of Poppler. Don't commit the PDF or the text: they are IHO
publications.
