# EncDotNet.S100.Datasets.S124

Library for reading and portraying [IHO S-124](https://iho.int/en/s-124-navigational-warnings) (Navigational Warnings) datasets.

S-124 provides a standard data model for distributing navigational warnings (NAVAREA, coastal, local) as GML-encoded datasets conforming to the S-100 framework.

## Features

- Parse S-124 GML datasets (S-100 Part 10b encoding)
- Extract navigational warning features (`NavwarnPart`, `NavwarnAreaAffected`, `TextPlacement`)
- Convert to S-100 Part 9 FeatureXML for portrayal pipeline consumption
- XSLT-based portrayal via the S-124 Portrayal Catalogue
