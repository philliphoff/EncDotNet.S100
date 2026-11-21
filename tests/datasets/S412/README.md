# S-412 test datasets

| File | Source | Contents |
|---|---|---|
| `4122C00_TESTDS1.GML` | Upstream sample, [iho-ohi/S-412-Product-Specification](https://github.com/iho-ohi/S-412-Product-Specification) `Samples/` at commit `1e4fe55` (2026-04-02); unmodified apart from LF line endings | One `DataProvider` and one storm-force `WindWarning` area west of Ireland |
| `synthetic_all_warnings.gml` | Hand-written for issue #841 | One `DataProvider` and one feature of each warning type in Feature Catalogue 1.1.0 draft v009, with two-language messages |

The upstream sample is a draft and doesn't follow its own schema in places:
`dateEnd`, `cancellationDate` and `nextUpdateDateTime` are `20250101` rather
than a date or date-time, and the times of day are `0600` and `1800`. Readers
keep these values as written. Its `cancellationDate` (midnight on 1 January
2025) is before its range starts, so it's never in force at a time step.

In `synthetic_all_warnings.gml` each warning's `cancellationDate` falls on the
end of its range. From 7 November 2026 the warnings in force change at 00:00,
06:00 and 12:00, then at 00:00 and 18:00 on the 8th, and at 00:00 on the 9th
and 10th.
