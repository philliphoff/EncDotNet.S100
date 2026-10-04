# Design brief: protected (encrypted) datasets and key management in SoundCharts

Status: brainstorm / pre-design (2026-10-04). Input for a design pass that should produce mocks of the UX, including alternatives where noted.

## 1. Why now

The SDK can read S-100 Part 15 protected exchange sets end to end (`docs/protected-exchange-sets.md`):

- authenticate `PERMIT.XML` against `PERMIT.SIGN` and a trusted Scheme Administrator (SA) certificate (`PermitSignatureVerifier`);
- evaluate each dataset's permit (`PermitKeyProvider.Evaluate`), unwrap its cell key with the system's **hardware ID**, and decrypt as it reads (`S100ExchangeSet.WithDecryption`);
- verify catalogue signatures over the *plaintext* (`Part15SignatureContentResolver`);
- recover a hardware ID from a **user permit** plus a manufacturer key (`UserPermit.DecryptHardwareId`), or create a user permit (`UserPermit.Create`).

The viewer exposes none of this. Commercial S-101 (and, later, other S-1xx products) will ship protected, so a viewer that can't handle protected data can't open real charts. This brief covers what the viewer needs: **detect** protected data, **show** its state clearly, and **manage** the keys and trust that unlock it.

## 2. What exists today (measured from code)

| Area | Today | Where |
|---|---|---|
| Detection | The catalogue parser reads `dataProtection` per dataset (`DatasetDiscoveryMetadata.DataProtection`). The Library indexer stores it as a `dataProtection=true` property, so it shows up only as a raw "Data protection: true" row in item details. Nothing else in the viewer reads it. | `ExchangeSetItemReader`, `Library_Property_dataProtection` |
| Opening a protected set | The viewer opens it like any other set and tries to parse ciphertext. The user gets a generic load failure that says nothing about protection or keys. | `ExchangeSetService.OpenAsync`, `LoadFailureClassifier` |
| Signatures | An exchange-set header badge (`SignatureStatus`: Verified / Unsigned / Invalid / Untrusted / Mixed / Error). Verification runs with `AllowUntrustedCertificates = true`, so **the viewer has no trust store**: "Verified" means "mathematically valid", not "from a trusted SA". Protected datasets are usually signed over plaintext, which can't be checked without keys. | `ExchangeSetService.VerifySignaturesAsync`, `ExchangeSetHeader` |
| Settings | No identity, key, permit or certificate settings. Settings are JSON (`ViewerSettings`), which is not a suitable place for secrets. | `SettingsView`, `ViewerSettings` |
| CLI | `s100 render` skips protected datasets with a warning ("this CLI has no decryption keys"). | `RenderCommand` |
| MCP | Library and dataset tools exist (`describe_library_item`, `set_dataset_state`, …). None of them know about protection. | `McpTools` |
| Caches | Disk caches for tiles, S-57 catalogues and dataset metadata (#693). | `Services/Caching` |
| S-57 / S-63 | S-63 signature verification only. The upstream `EncDotNet.S57` 0.7.0 package **cannot decrypt S-63**, so S-63 is out of scope here (see §4 J). | `S57ExchangeSetVerification` |
| Sample data | **There are no real protected datasets available.** SDK tests generate protected sets in memory. The docs contain a "test data server" recipe (SA cert, data-server cert, encrypted S-101 cell, signed catalogue, permit). | `ProtectionTests`, `docs/protected-exchange-sets.md` |

## 3. Vocabulary the design should settle

The SDK terms are precise but unfamiliar. The UI needs user-facing words for each of them, and the design should pick and stick to one set.

| SDK term | What the user needs to know | Candidate UI words |
|---|---|---|
| Protected dataset (`dataProtection`) | It's encrypted; you need a licence to view it. | "Protected", "Licensed", "Encrypted" |
| Hardware ID (`HW_ID`) | This computer's secret identity. Licences are made for it. **A secret.** | "System ID", "Device key", "Hardware ID" |
| User permit | The shareable code you send to your chart supplier to buy licences. Derived from the hardware ID; safe to share. | "User permit", "Licence request code" |
| Manufacturer key / ID (`M_KEY`, `M_ID`) | Identifies the software maker in the IHO scheme. Only needed to recover a hardware ID from a user permit. | Hidden behind "Advanced" |
| `PERMIT.XML` + `PERMIT.SIGN` | The licence file: one entry per dataset, with expiry and edition. | "Permit", "Licence file" |
| Data server | Who issued the permit (e.g. a chart distributor). | "Issued by …" |
| Scheme Administrator certificate | The trust root that vouches for data servers. Normally the IHO's. | "Trusted authority", "Scheme Administrator" |
| Permit evaluation outcome | Why a dataset will or won't open. | See §4 B |

Two UX states must not be confused:

- **Locked**: protected and we have no usable key (no permit, refused permit, or no hardware ID).
- **Unlocked**: protected, permit allows it, and decryption works. It renders like any other dataset.

## 4. Problem areas, with ideas

### A. Detecting protected data

- Sources of truth: the catalogue's `dataProtection` flag per dataset (before any load), the presence of `PERMIT.XML`/`PERMIT.SIGN` at the set root, and permits the user has already imported (§4 E).
- Detect **at index time** (Library) and **at open time** (Datasets panel). Nothing should need a failed load to discover that data is protected.
- A set can be mixed: protected cells next to unprotected support files, or protected S-101 next to an unprotected S-102. The state is per dataset, with a roll-up per exchange set ("8 of 12 protected · 6 unlocked").
- Wrong-key failures are detected late (on decrypt), and roughly 1 in 256 wrong keys "succeed" and produce garbage that fails to parse. A load failure on a protected dataset should be classified as "probably the wrong key", not as a generic parse error.

### B. Showing protection state

Per dataset, the states the design needs (mapped from `PermitEvaluationOutcome` and the exceptions):

| State | Cause | What the user can do |
|---|---|---|
| Not protected | `NotProtected` | — |
| Unlocked | `Allowed`, and decryption succeeded | — (maybe show the permit's expiry) |
| Locked: no system ID | No hardware ID configured | Set up this computer |
| Locked: no permit | `PermitNotFound`, or no `PERMIT.XML` at all | Import a permit / contact the supplier |
| Locked: permit not trusted | `PERMIT.SIGN` fails: `CertificateUntrusted`, `SignatureInvalid`, `NotSigned`, `CertificateExpired` | Add a trusted authority / get a fresh permit |
| Locked: permit is for another edition | `EditionMismatch`, `EditionNumberMissing`, `IssueDateMismatch`, `IssueDateMissing` | Get updated permits |
| Locked: permit expired | `IssuedAfterExpiry` (the data was issued after the permit ran out) | Renew the subscription |
| Locked: base missing | `BaseDatasetMissing` (a protected update without its base) | Add the base cell |
| Can't decrypt | `DatasetDecryptionException`, or garbage after decrypt | Check the system ID; the permit may be for another computer |

Where this state should appear (each is a mock target):

1. **Library rows**: a lock glyph or tag beside the existing tags (`LibraryItemTag`: Update / Loaded / On pan / Failed / Expired). Locked might be a new tag kind. Should "Unlocked" be visible at all, or only "Locked"?
2. **Library item details**: replace the raw "Data protection: true" row with a "Protection" group (state, issuer, expiry, edition covered, reason).
3. **Datasets panel exchange-set header**: a protection badge next to the signature badge, with a roll-up ("6/8 unlocked"). One combined "trust and licence" badge is an alternative.
4. **Dataset rows / inspector**: a locked dataset still gets a row (greyed, lock icon, reason, primary action). Consider a new **Protection** inspector tab (next to Dataset / Layers / Validation) with a per-dataset permit table for the set.
5. **Map**: a locked dataset's coverage is known from the catalogue. Show it as a hatched or locked outline so the user sees *where* they have data they can't view, instead of silent blank sea. This needs to fit with the existing Library coverage overlay and the S-128 outline style (#756).
6. **Load failure**: replace the generic failure dialog with an actionable, protection-aware message, and **one** notification per exchange set, not one per cell.
7. **Signatures**: once keys exist, plaintext signatures become checkable. Once trusted authorities exist, "Verified" can mean "trusted". The badge vocabulary may need "Verified (trusted authority)" vs "Valid signature, untrusted source", plus "Can't check: locked".

### C. Setting up this computer (identity)

The viewer acts as a Part 15 **data client**. It needs a hardware ID.

- **Paths to a hardware ID**:
  1. Enter an existing hardware ID (32 hex characters), e.g. one issued alongside test data or used by another system the user licenses.
  2. Enter a user permit plus the manufacturer key (Advanced; mainly for testing).
  3. **Generate** one for this install. Then show the derived **user permit** with Copy and "Save as file" so the user can send it to a supplier. Note: a real user permit needs an IHO-registered manufacturer ID and key. SoundCharts is not registered, so generation is realistic only for test schemes or after registration. The design should leave room for it without promising it.
- The hardware ID is a **secret**. Store it in the OS credential store (macOS Keychain, Windows DPAPI/Credential Manager, libsecret), never in the settings JSON. Mask it by default and require a deliberate reveal or copy.
- Should more than one identity be allowed (e.g. permits bought for two different systems)? A wrong hardware ID can't be detected until decrypt, so the viewer could try each identity until one decrypts. This is ambiguous UX and should be decided explicitly (§8).
- Backup and restore: losing the hardware ID loses every licence bought for it. Consider export/import of the identity (password-protected), with a strong warning.

### D. Trusted authorities (Scheme Administrator certificates)

- The trust store: a list of trusted SA certificates (subject, issuer, thumbprint, validity, where it came from), with Add from file, Remove and View.
- Never auto-trust a certificate found *inside* an exchange set. The SDK docs are explicit that the trust root is configuration. If a permit is signed by an unknown authority, the UX can offer "Trust this authority?" but it must show the fingerprint and make clear this is a security decision.
- A **test-mode** affordance for development SAs (like the one our fixture generator creates). A visible marker such as "Test authority", with test-signed data badged as such on the map or in the header.
- The same store should drive exchange-set signature verification. That retires `AllowUntrustedCertificates = true` as the only mode (§4 B.7). This is a behaviour change for unprotected sets, so the design should cover how "Untrusted" reads for the large body of unsigned or self-signed sample data users already have.

### E. Permits

- Permits normally ship inside the exchange set (`PERMIT.XML` at the root). Suppliers also send **permit-only updates**: a new `PERMIT.XML` for data the user already has, e.g. after renewal or for a new edition.
- Ideas:
  - A **Permits** list: issuer (data server name and ID), issue date, number of datasets covered, earliest and latest expiry, authentication state, and source (embedded in a set vs imported).
  - **Import permit…** (file picker or drag-drop onto the window). After import, re-evaluate every protected dataset in the Library and the map, and say what changed ("Unlocked 14 datasets").
  - Permit detail: per-dataset rows (cell name, edition, expiry, "in Library?", "on map?").
- Precedence when both exist: the permit in the set vs an imported one. The newest valid permit should probably win per dataset; the design should state that rule.
- **Expiry**: Part 15 checks expiry against the dataset's *issue date*, not today. Existing data keeps working; only new editions or updates issued after expiry are refused. So "Expired" means "you won't be able to open new updates", not "your charts stopped working". The wording should reflect that. A "Permit expires in 23 days" warning, as a Library tag or notification, is useful for subscriptions.

### F. The first-run / first-encounter flow

The most important flow to mock: **a user opens a protected exchange set with nothing configured.**

- Today: a load failure. Proposed: the set opens, unprotected parts load, protected datasets appear as locked rows with outlines on the map, and one notification says "8 datasets are protected. [Set up licences]".
- "Set up licences" opens a short guided flow: (1) this computer's ID → (2) trusted authority → (3) permit (pre-filled from the set if present) → result: "6 unlocked, 2 need updated permits". Then the locked datasets load **in place**, with no reopen.
- Alternatives to mock: a modal wizard (like the Add Online Catalogue wizard) vs an inline panel in the Datasets inspector vs a Settings page with deep links.
- Returning users with everything set up should see **no** friction: protected data just loads, perhaps with a small lock-open glyph.

### G. Where key management lives

Options for the design to compare:

1. A new **Settings → Licences & trust** page with three sections: This computer, Trusted authorities, Permits.
2. A dedicated **Licences** window or panel (closer to the ECDIS "permit manager" convention), opened from the menu, from badges and from notifications.
3. A split: identity and trust in Settings (set once), permits beside the Library (they relate to data).

The macOS app menu (`NativeMenuBuilder`) probably needs entries such as "Import Permit…" and "Licences…".

### H. Library integration

- Filters: "Protected", "Locked", "Permit expiring".
- Bulk actions on locked items: "Copy list of cells needing permits", which a user could send to a supplier. This is a realistic request that ECDIS users make.
- Online sources (#655/#670/#680) may list protected datasets. Download still works; the item then becomes Local + Locked. Should a locked online item even offer Get? (Probably yes, with a lock cue.)
- `s100 feed serve/export` (#680) must not re-publish decrypted content. Protected items stay encrypted, or are excluded.

### I. Safety, privacy and leakage

These aren't visual, but they change the UX, so the design should account for them:

- **Disk caches** (tiles, metadata, #693): caching rendered tiles of licensed data on disk is a licence and leakage risk. Options: no disk cache for protected datasets (slower), or an encrypted cache tied to the identity. If this costs speed, the user may notice it.
- **MCP**: tools must never return the hardware ID, manufacturer key or cell keys. Protection *state* is fine to expose (`describe_library_item` gains a protection block). Should an MCP client be able to import a permit? Probably yes; should it be able to set the identity? Probably no.
- **Screenshots, feedback reports and crash logs** (`FeedbackReport`, `CrashLog`): redact secrets and never attach dataset bytes.
- **Not-for-navigation** framing stays. Unlocking licensed data doesn't make SoundCharts a type-approved ECDIS. Does the protected-data UX need to restate that?

### J. S-63 (S-57) out of scope, for now

- Users with S-63 ENC permits (`PERMIT.TXT`, cell permits) will expect the same UX. The upstream package can't decrypt S-63 today.
- Proposal: detect S-63 encrypted cells and show them as "Locked: S-63 encryption isn't supported yet" (not a parse error). Design the identity and permit screens so that an S-63 permit type could be added later. S-63 uses the same HW_ID concept, with a different permit format.

## 5. Constraints the design must respect

- Desktop Avalonia app (macOS first; Windows and Linux too). Light and dark chrome themes. Existing components: the Library panel, Datasets panel and inspector tabs, notifications with actions, wizard dialogs (`WizardStyles`), Settings.
- Secrets live in the OS credential store and are never shown in full without a deliberate action.
- No real protected data exists. Every screen must be demonstrable with **our own generated fixtures** (§6, §9).
- Part 15 decides permit validity by issue date, not wall-clock time. Wording about "expired" must stay accurate.
- Keep the number of new concepts small. Most users will never touch protected data, so the feature should be invisible until it's needed.

## 6. Scenarios to mock against

All of these come from a fixture generator (§9). Names are suggestions:

1. **First encounter**: `PROT-Basic`, one protected S-101 cell plus an unprotected S-102 tile, permit embedded, nothing configured in the viewer.
2. **Happy path**: the same set after setup. It just loads; the lock-open state is visible but quiet.
3. **Mixed outcomes**: `PROT-Mixed`, 8 cells: 5 allowed, 1 with no permit, 1 for another edition, 1 issued after expiry. Covers the roll-up and per-row reasons.
4. **Wrong computer**: a permit made for a different hardware ID, so every cell fails to decrypt. Covers the "check your system ID" guidance.
5. **Untrusted issuer**: a permit signed by an SA that isn't in the trust store. Covers the "Trust this authority?" decision.
6. **Permit-only update**: an existing set with no permit, then the user drops a separate `PERMIT.XML` and the cells unlock in place.
7. **Library scale**: a local collection of ~200 cells, ~150 protected, mixed states. Covers filters, tags, map outlines and "copy cells needing permits".
8. **S-63 set**: an S-57 set with S-63 encrypted cells → "not supported yet".
9. **Expiring soon**: a permit expiring in 10 days (view time / test clock via MCP `set_test_clock`).

## 7. Directions to explore (alternatives, not a decision)

- **A. "Quiet lock"**: protection is just another dataset state, expressed through the existing tag, badge and notification components. Key management is a Settings page. Minimal new surface.
- **B. "Licence manager"**: a dedicated Licences window as the hub (identity, authorities, permits, and per-dataset coverage of your licences), modelled on ECDIS permit managers. Everything else links into it.
- **C. "Coverage-first"**: the map is the primary surface. Locked areas are hatched; clicking one explains why and offers the fix; Library and panels are secondary.

The mocks should show A and B side by side for the first-encounter flow (scenario 1) and the mixed set (scenario 3), and C's map treatment regardless of which direction wins.

## 8. Questions for design

1. Which user-facing words: "Protected / Licensed / Encrypted", "System ID / Hardware ID", "Permit / Licence"?
2. Should *unlocked* protected data carry any persistent marker, or only locked data?
3. One combined "trust + licence" badge on the exchange-set header, or two badges?
4. Wizard, inline panel, or Settings deep links for first setup?
5. Multiple identities: allowed? If yes, how does the user see which identity unlocked which dataset?
6. How prominent should "Trust this authority?" be? It's a security decision made by people who probably don't know what a Scheme Administrator is.
7. How do we say "permit expired, but your current charts still work"?
8. What does a hatched locked coverage look like next to the Library coverage overlay and S-128 outlines, in day/dusk/night palettes?
9. Does protected data change the not-for-navigation messaging?

## 9. Requested deliverables

From the design pass:

1. Mocks for scenarios 1, 3, 4, 5 and 6 (light and dark), for directions A and B, plus C's map treatment.
2. The key-management surface (Settings page or Licences window): This computer, Trusted authorities, Permits, including empty, populated and error states.
3. The per-dataset state table (§4 B) turned into concrete visuals: tag text, icon, colour token, tooltip, primary action.
4. Copy for every state, notification and confirmation, especially wrong-key, untrusted-authority and expiry.
5. A short `HANDOFF.md` with the decisions taken on the §8 questions (same shape as the timeline handoff).

Engineering prerequisites to schedule alongside, not blocking the mocks:

- **A protected-fixture generator.** We have no real protected data, so build our own: e.g. an `s100 protect` CLI command or a `tools/` script that takes plain exchange sets/cells, a hardware ID and an output folder, generates (or reuses) a test SA and data-server certificate, encrypts the chosen datasets, signs the catalogue over plaintext, and writes `PERMIT.XML`/`PERMIT.SIGN`. It needs knobs for each §6 scenario: per-cell omit permit, wrong edition, expiry before issue, wrong hardware ID, untrusted SA, separate permit-only output. The recipe in `docs/protected-exchange-sets.md` is the starting point. Checked-in output under `tests/datasets/ExchangeSets/Protected-*` would also give the viewer, MCP and visual-regression tests something stable. The test SA private key and hardware ID used for fixtures are test values and can be committed with clear "TEST ONLY" naming.
- Protection-aware load classification (`LoadFailureClassifier` → `DatasetPermitException`, `DatasetDecryptionException`).
- A credential-store abstraction for the identity.
- A decision on disk caching of protected content (§4 I).
