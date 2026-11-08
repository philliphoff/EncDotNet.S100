# S-412 test datasets

| File | Source | Contents |
|---|---|---|
| `4122C00_TESTDS1.GML` | Upstream sample, [iho-ohi/S-412-Product-Specification](https://github.com/iho-ohi/S-412-Product-Specification) `Samples/` at commit `1e4fe55` (2026-04-02); unmodified | One `DataProvider` and one storm-force `WindWarning` area west of Ireland |
| `synthetic_all_warnings.gml` | Hand-written for issue #841 | One `DataProvider` and one feature of each warning type in Feature Catalogue 1.1.0 draft v009, with two-language messages |

The upstream sample is a draft and doesn't follow its own schema in places:
`dateEnd`, `cancellationDate` and `nextUpdateDateTime` are `20250101` rather
than a date or date-time, and the times of day are `0600` and `1800`. Readers
keep these values as written.
