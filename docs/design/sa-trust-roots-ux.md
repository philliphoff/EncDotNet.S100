# Scheme Administrator trust roots — UX decision note

> Status: **Built** (2026-11-24).
> Mock: `sa-trust-roots-mock.html` (PNG renders
> alongside it). Builds on Settings → Keys & certificates (#845) and the
> signature badge outcomes in `ViewModels/SignatureStatus.cs`.

Today nothing the viewer opens can be **Verified**: the library only trusts
the Scheme Administrator (SA) roots it's given, and the viewer gives it none.
This note decides where SA roots live, how they're managed, and what the
Datasets badge says once they exist.

## 1. Decisions

| # | Decision |
|---|---|
| D1 | SA roots get their **own tab**, *Scheme administrators*, on Keys & certificates, between *Trusted authorities* and *System IDs* (the two Part 15 tabs sit together). It is not a section of *Trusted authorities*: those roots vouch for SECOM servers, these vouch for chart data, and one list would let an MCP root make charts "verified". |
| D2 | Rows reuse the B4 row anatomy (`KeysRowTemplate`): icon tile, title, badges, status, mono fingerprint, sub line, action chips. Icon is `Certificate` (SECOM authorities keep `Shield`). |
| D3 | Two built-in rows: **IHO scheme administrator** (on by default) and **IHO test scheme administrator** (off by default, `TEST` badge in warning tone, an extra line saying it's for testing only). Built-ins can be turned off and on, never removed. |
| D4 | If the build doesn't bundle the IHO production certificate, its row is replaced by a **dashed placeholder** (B6 style) at the top of the list that says so and offers *Add from file…*. Nothing else changes; the test row still shows. |
| D5 | Users add roots with *Add from file…* (`.cer`, `.crt`, `.pem`, `.der`). A PEM bundle may hold several roots; the dialog lists each and adds them all as separate rows. User-added rows have *View* and *Remove*. |
| D6 | The SA tab shows fingerprints in the library's format (`SchemeAdministratorRoots.Sha256Fingerprint`: upper-case hex pairs joined by colons), because the point is to compare against what the SA publishes. The row shows a short form; *View* and the add dialog show the full value with a **Copy** button. |
| D7 | **Remove** asks for confirmation (it needs the file to undo). **Turn off / Turn on** doesn't (it's one click to undo). |
| D8 | A data set verified **only** through the test root gets its own outcome, **Verified (test)**: outline `ShieldCheckmark` in warning amber, tooltip says test data. It never looks like a production Verified (filled green). If one file chains to production and another only to test, the set is *Verified (test)* — the weakest anchor wins. |
| D9 | Verified tooltips name the root: "Signatures verified against {root name}". Needs the library to report which trusted root anchored each signature (it doesn't today; see §7). |
| D10 | Tooltips can't hold links, so the **Unverified** tooltip says where roots are managed, and the exchange-set **inspector** gets a *Scheme administrators…* chip under the signature line (Unverified, Verified (test)) that opens Settings on this tab (the existing `KeysTab` deep-link pattern, like `Keys_Fix_TrustedAuthorities`). |
| D11 | Changing roots (add, remove, on, off) **re-verifies every open exchange set** automatically, in the background, without a toast. Badges go to *Checking* and then settle. Only the trust decision needs redoing; hashes and signature maths can be cached so it's cheap. |
| D12 | The tab count is the number of rows (as on *Trusted authorities*); it becomes a red `!` when a root that's on has expired. |

## 2. Layout of the tab

```
Scheme administrators  2
─────────────────────────────────────────────────────────────────────
Intro text (max 560)                                  [Add from file…]

[banner: expired root / nothing trusted]         (only when it applies)
[dashed placeholder: IHO certificate not included] (only when absent)

┌ list ─────────────────────────────────────────────────────────────┐
│ [cert] IHO scheme administrator  BUILT IN                          │
│        SHA-256 3F:A2:9C:01 … 7B:E4                                 │
│        Self-signed · valid 1 Jan 2025 to 31 Dec 2044 · built in…   │
│        [View] [Turn off]                                           │
├────────────────────────────────────────────────────────────────────┤
│ [cert] IHO test scheme administrator  BUILT IN  TEST          Off  │  (dimmed)
│        SHA-256 …                                                   │
│        Self-signed · valid … · built in, can be turned off …       │
│        For conformance testing. Data it verifies isn't for nav…    │
│        [View] [Turn on]                                            │
├────────────────────────────────────────────────────────────────────┤
│ [cert] Example HO root  ADDED                                      │
│        … added from file 23 Nov 2026                               │
│        [View] [Remove]                                             │
└────────────────────────────────────────────────────────────────────┘
Note text
```

Row status (right of the title) follows the existing rules: `Off` (muted),
`Expired` (destructive), `Not valid yet` (warning, with the clock icon).
A row that's off is dimmed.

## 3. States

| State | What shows |
|---|---|
| Default | IHO row on; test row off and dimmed. Count `2`. |
| IHO certificate not bundled | Dashed placeholder at the top (copy in §5.3). Test row below. It stays even after the user adds their own copy of the IHO root (it describes the build, and the user's row is just a user-added row). |
| User-added | Extra rows, sub line ends "added from file {date}", actions *View*, *Remove*. No `BUILT IN` badge. |
| Test root on | Test row undimmed; `TEST` badge stays. No banner: the Datasets badge (D8) carries the warning where it matters. |
| Nothing on | Warning banner "No scheme administrator is on" above the list (amber variant of B5). Arises when the IHO row is off or absent and no user root is on. |
| Expired root that's on | Existing destructive banner pattern with SA wording; row status `Expired`; tab count `!`. Action *Replace…* opens *Add from file…*. |
| Not yet valid | Row status `Not valid yet` (warning, clock). No banner. |
| Import error | Shown in the add dialog's error line; *Add* disabled. §5.4. |

## 4. Interactions

- **Add from file…** opens a file picker filtered to `*.cer;*.crt;*.pem;*.der`
  (plus All files), then the *Add scheme administrator* dialog, width 480,
  same anatomy as *Add trusted authority*: title, intro, file name, a field
  card per certificate, a *Name* box (single-certificate files only;
  bundles use each certificate's CN), the error line, Cancel / Add.
- Field card: *Subject*, *Issued by* (`Self-signed` when subject = issuer),
  *Valid*, *SHA-256* (full, mono, wraps at colons, **Copy** chip), *Key*.
- **Copy** puts the full colon-separated fingerprint on the clipboard; the
  chip reads *Copied* for 1.5 s. Same chip in the *View* dialog.
- **Remove** opens the existing confirm dialog (`IKeysDialogs.Confirm`).
- **Turn off / Turn on**: immediate, then re-verify (D11).
- **Re-verify** (D11): debounced ~300 ms so toggling twice verifies once.
  Exchange sets still loading are verified with the new roots.
- **Inspector chip** *Scheme administrators…* opens Settings → Keys &
  certificates on this tab.

## 5. Copy

All new strings follow the existing `Keys_` naming. Sentence case;
"scheme administrator" is lower case in running text (the existing
Unverified tooltip's capitals are changed to match).

### 5.1 Tab, intro, note

| Key | Text |
|---|---|
| `Keys_Tab_SchemeAdmins` | Scheme administrators |
| `Keys_SchemeAdmins_Intro` | Who SoundCharts trusts to vouch for S-100 chart data. An exchange set is verified only if its signer chains to one of these. Kept apart from SECOM authorities. |
| `Keys_AddSchemeAdmin` | Add from file… |
| `Keys_AddSchemeAdmin_Help` | Trust a scheme administrator's root certificate from a .cer, .crt, .pem or .der file |
| `Keys_SchemeAdmins_Note` | Exchange sets whose signer doesn't chain to a root here still open. Their badge reads Unverified. Open exchange sets are checked again when you change this list. |
| `Keys_Authorities_Note` (revised) | Self-signed sample data still opens. Its badge reads "Signed · untrusted source". Roots for S-100 chart data are on the Scheme administrators tab. |

### 5.2 Rows

| Key | Text |
|---|---|
| `Keys_SchemeAdmin_IhoName` | IHO scheme administrator |
| `Keys_SchemeAdmin_IhoTestName` | IHO test scheme administrator |
| `Keys_SchemeAdmin_TestLine` | For conformance testing and producer rehearsal. Data it verifies is test data, not for navigation. |
| `Keys_SchemeAdmin_SubFormat` | {0} · valid {1} to {2} · {3} *(0 = issuer)* |
| `Keys_SchemeAdmin_SelfSigned` | Self-signed |
| `Keys_SchemeAdmin_IssuedByFormat` | Issued by {0} |
| `Keys_Fingerprint_ColonShortFormat` | SHA-256 {0} … {1} *(first 4 pairs, last 2)* |
| `Keys_Badge_Added` | ADDED |
| reused | `Keys_Badge_BuiltIn` BUILT IN, `Keys_Kind_Test` TEST, `Keys_Status_Off`, `Keys_Status_Expired`, `Keys_Status_NotYetValid`, `Keys_Authority_BuiltInOrigin`, `Keys_Authority_AddedOriginFormat`, `Keys_Action_View`, `Keys_Action_TurnOff`, `Keys_Action_TurnOn`, `Keys_Action_Remove` |

### 5.3 Placeholder and banners

| Key | Text |
|---|---|
| `Keys_SchemeAdmin_IhoMissingTitle` | IHO certificate not included |
| `Keys_SchemeAdmin_IhoMissingBody` | This build doesn't include the IHO scheme administrator's certificate. Get it from the IHO, check its SHA-256 against the one the IHO publishes, then add it from the file. |
| `Keys_SchemeAdmin_NoneOnTitle` | No scheme administrator is on |
| `Keys_SchemeAdmin_NoneOnBody` | No exchange set can be verified. Signed sets show as Unverified. |
| `Keys_SchemeAdmin_NoneOnAction` | Turn on IHO *(only when the IHO row exists; otherwise* Add from file… *)* |
| `Keys_SchemeAdmin_Expired_Title` | A scheme administrator certificate has expired |
| `Keys_SchemeAdmin_Expired_BodyFormat` | {0} expired {1}. Exchange sets signed under it now show as Unverified. |
| reused | `Keys_ExpiredAuthority_Action` Replace… |

### 5.4 Add dialog and errors

| Key | Text |
|---|---|
| `Keys_AddSchemeAdmin_Title` | Add scheme administrator |
| `Keys_AddSchemeAdmin_Intro` | Exchange sets signed under this root will show as verified. Before you add it, check its SHA-256 against the one the scheme administrator publishes. |
| `Keys_AddSchemeAdmin_BundleFormat` | This file holds {0} roots. Each is added separately. |
| `Keys_AddSchemeAdmin_Name_Help` | How this scheme administrator is named in badges, e.g. IHO |
| `Keys_AddSchemeAdmin_Add` | Add scheme administrator *(bundle:* Add {0} roots*)* |
| `Keys_Copy` / `Keys_Copied` | Copy / Copied |
| `Keys_AddSchemeAdmin_CannotRead` | SoundCharts can't open this file. Check it's still there and you can read it. |
| `Keys_AddSchemeAdmin_NotCertificate` | This file isn't a certificate. Use a .cer, .crt, .pem or .der file. |
| `Keys_AddSchemeAdmin_NoCertificate` | This file holds no certificate. |
| `Keys_AddSchemeAdmin_NotCaFormat` | {0} isn't a certificate authority, so it can't be a scheme administrator root. It may be a signer's certificate: add the root it chains to. |
| `Keys_AddSchemeAdmin_DuplicateFormat` | {0} is already in the list. |
| `Keys_AddSchemeAdmin_SecomDuplicateFormat` | {0} is a SECOM trusted authority. It can be a scheme administrator too, but check that's what you mean. *(warning, Add stays enabled)* |
| `Keys_AddSchemeAdmin_Expired` | This certificate has expired, so nothing signed under it can be verified. *(blocks Add)* |
| `Keys_AddSchemeAdmin_NotYetValidFormat` | This certificate isn't valid until {0}. It's added now and used from then. *(warning, Add stays enabled)* |

Mapping from the library: an `IOException` wrapped in
`SchemeAdministratorRootException` → *CannotRead*; "isn't a certificate"
→ *NotCertificate*; "holds no certificate" → *NoCertificate*;
"isn't a certificate authority" → *NotCaFormat* with the certificate's CN
(not the full DN, and not the file name, which is already on screen). The
viewer matches on cause, not on the library's message text: the library
should expose a reason enum on the exception (§7). Duplicate, expired and
not-yet-valid are viewer checks. In a bundle the dialog names the
certificate at fault and adds none.

### 5.5 Remove confirm

| Key | Text |
|---|---|
| `Keys_RemoveSchemeAdmin_TitleFormat` | Remove {0}? |
| `Keys_RemoveSchemeAdmin_Body` | Exchange sets signed under it will show as Unverified. To trust it again you'll need the certificate file. |
| `Keys_RemoveSchemeAdmin_Confirm` | Remove |

### 5.6 Badge tooltips (Datasets panel and inspector)

| Outcome | Icon | Tooltip |
|---|---|---|
| Verified | `ShieldCheckmark` filled, green | Signatures verified against {0} *(root name, e.g. "IHO scheme administrator")* |
| **Verified (test)** — new | `ShieldCheckmark` regular, amber | Verified against the IHO test scheme administrator. Test data, not for navigation. *(user-named test roots aren't a thing; only the built-in test root triggers this)* |
| Unverified | `ShieldQuestion` filled, amber | Signatures match the data, but the signer isn't from a trusted scheme administrator. Manage them in Settings, Keys and certificates. |
| Unverified, nothing on | same | Signatures match the data, but no scheme administrator is trusted yet. Add one in Settings, Keys and certificates. |
| Not checked, Invalid, Mixed, Unsigned, Checking, Error | unchanged | unchanged |

Inspector chip: `Keys_Fix_SchemeAdmins` = *Scheme administrators…*,
help = *Opens Settings, Keys and certificates, where chart data roots are
managed.*

## 6. Critique notes (what was weighed)

- *Section inside Trusted authorities?* Rejected (D1): the SECOM intro
  talks about MCP; mixing would blur which root makes a chart "verified".
- *Confirm before turning on the test root?* Rejected: the warning belongs
  where the consequence shows (the badge), not in a dialog people click
  through once and forget.
- *Session-only test root* (reuse `THIS RUN ONLY`) was considered; it
  annoys producers who rehearse daily. Left as an open question.
- Amber now means two things on the badge (Unverified, Verified (test)).
  The glyphs differ (question mark vs tick, filled vs outline) and the
  tooltip spells it out; colour is never the only cue.

## 7. Library dependencies

1. Per-signature result needs the **anchoring root** (or its fingerprint)
   for D8/D9. `SignatureVerificationResult` has no such field today.
2. `SchemeAdministratorRootException` should carry a **reason**
   (Unreadable, NotCertificate, Empty, NotCertificateAuthority) and the
   offending certificate, so the viewer doesn't parse messages.
3. Re-verify (D11) is cheapest if trust can be re-decided from cached
   signer certificates without re-hashing files.

## 8. Open questions for the product owner

1. Should turning on the test root last **for this run only**?
2. Should an expired SA root still verify data **signed while it was
   valid**? (Today the chain is checked against now.)
3. Accept a CA that **isn't self-signed** as a root (intermediate anchor)?
   The library allows it; this design does and shows "Issued by …".
4. Fingerprint format: SA tab uses colon pairs (library, IHO style) while
   SECOM rows use 4-char groups. Align SECOM to colons too?
5. Is the IHO production certificate available to bundle, and from where
   do we tell users to get it if not (exact URL for the placeholder)?
