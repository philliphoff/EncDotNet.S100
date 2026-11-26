---
name: s100-data-protection
description: |
  Expert knowledge of IHO S-100 Part 15 data protection (Edition 5.2.1)
  as implemented in this repo: digital signatures on CATALOG.XML entries
  and CATALOG.SIGN, PERMIT.XML / PERMIT.SIGN, user permits and hardware
  ids, AES dataset encryption, Scheme Administrator (SA) trust roots,
  and the producer side (signing, permit issuing). USE FOR: changes to
  ExchangeSetVerifier, CatalogueSignatureVerifier, TrustAnchorOptions,
  SchemeAdministratorRoots, the `Protection` namespace in
  `src/EncDotNet.S100.ExchangeSets/`, `S100ExchangeSet.WithDecryption`,
  `s100 validate --trust-root`, the viewer's Scheme administrators tab
  and signature badges, protected test exchange sets and
  `ProtectedScenarioGenerator`. DO NOT USE FOR: general CATALOG.XML
  layout or discovery metadata (use s100-framework), S-128 catalogues of
  nautical products (use s128-catalogue), SECOM TLS / trusted
  authorities in `EncDotNet.S100.Collections/Secom` (not chart trust).
---

# S-100 Part 15 data protection expert

## When engaged
- `src/EncDotNet.S100.ExchangeSets/**` signature, trust or
  `Protection/**` code; `SchemeAdministrators/*.pem` embedded roots
- `src/EncDotNet.S100/S100ExchangeSetProtectionExtensions.cs` (facade)
- `tools/EncDotNet.S100.Cli/Commands/ValidateCommand.cs` (`--trust-root`)
- Viewer: `Services/SchemeAdministratorStore.cs`,
  `Services/ExchangeSetService.cs` (`TrustAnchors()`, badge mapping),
  `ViewModels/Keys/*SchemeAdministrator*`, `ViewModels/SignatureStatus.cs`
- `tests/EncDotNet.S100.Testing.Scenarios/Protection/**`,
  `tests/datasets/ExchangeSets/Protected-*`

## Spec anchors
- S-100 Ed 5.2.1 Part 15 (`docs/specs/`): §15-5 certificates/compression,
  §15-6 AES-128 modified CBC, §15-7.3 user permit and `HW_ID`, §15-7.4
  `PERMIT.XML` (§15-7.4.4 `datasetPermit`, §15-7.4.5 `PERMIT.SIGN`),
  §15-8.4/8.7 algorithms, §15-8.9–8.10 signature-as-checksum and hash MRN,
  §15-8.11 signature forms (on data, on signature, standalone `.SIGN`).
- Repo docs: `src/EncDotNet.S100.ExchangeSets/README.md` (type → section
  tables), `docs/protected-exchange-sets.md`, `docs/design/sa-trust-roots-ux.md`
  (viewer decisions D1–D12), `docs/scenario-generator.md` (protected sets).

## Architecture in this repo
- **Verify:** `ExchangeSetVerifier` (per-file signatures + streaming
  SHA-256 integrity) and `CatalogueSignatureVerifier` (`CATALOG.SIGN`).
  Results: `FileVerificationResult` (`Outcome` and `ChecksumOutcome` are
  independent), `ExchangeSetVerificationResult` (`AllValid`,
  `AllSignaturesIntact`, `HasUnverifiedSigners`, `IsUnsigned`,
  `IntegrityVerified`, `TrustedRootsUsed`). `VerificationOutcome` is
  append-only (S-57 bridge `S57ExchangeSetVerification` mirrors it).
- **Trust:** `TrustAnchorOptions { TrustedRoots, AllowUntrustedCertificates }`.
  `SchemeAdministratorRoots`: `LoadIhoRoot()` (Ed 5.2, ECDSA P-384) and
  `LoadIhoTestingRoot()` (Ed 4.0 DSA, test only) load embedded PEMs and
  throw if the pinned `Iho*Fingerprint` constant doesn't match;
  `LoadFile`/`Load` reject non-CA certificates; `Sha256Fingerprint`.
- **Decrypt:** `Protection/` — `S100Cipher`, `HardwareId`, `UserPermit`,
  `PermitFile`, `PermitSignatureVerifier.AuthenticateAsync`,
  `PermitKeyProvider` (refuses unauthenticated permits; enforces edition,
  issue date, expiry → `DatasetPermitException`), `DecryptingAssetSource`
  (→ `DatasetDecryptionException`), `Part15SignatureContentResolver`
  (signatures over encrypted/compressed/unencrypted representations).
- **Produce:** `Part15Signer` (ECDSA P-384/SHA-384),
  `StandaloneDigitalSignatureWriter`, `DataPermit.Create`, `PermitFile.Create`,
  `PermitFileWriter.WriteSigned`. Signed `CATALOG.XML` writing and whole-set
  protection are not implemented (#843); no producer CLI command exists.
- **Consumers:** CLI `s100 validate` trusts only `--trust-root` files (no
  IHO default) with `AllowUntrustedCertificates = true`, so untrusted shows
  as `SignatureValidUntrusted`, failing only under `--strict`. Viewer
  `SchemeAdministratorStore` = built-in IHO root (on), IHO test root (off),
  plus user PEMs in settings; changes re-verify open sets. Test-only
  anchoring → `SignatureStatus.VerifiedTest`. SoundCharts and `s100` don't
  open encrypted datasets yet (library + facade only).

## Review checklist
1. A signature is `Ok` only if it chains to `TrustedRoots`. Never consult the
   OS certificate store, and never take a root from the exchange set being
   verified (catalogue `CertificateBlock` certs are intermediates only).
2. `AllowUntrustedCertificates` may downgrade to `SignatureValidUntrusted`,
   never to `Ok`/"Verified". UI and CLI text must not call that verified.
3. Keys come only from an authenticated permit: use
   `PermitSignatureVerifier.AuthenticateAsync`, not `PermitFile.Read`, before
   `PermitKeyProvider`. Check the permit before any decryption.
4. Keep SA roots (chart data) separate from SECOM trusted authorities
   (`SchemeAdministratorStore` doc, D1): a SECOM/MCP root must never verify
   charts.
5. Test roots must stay distinguishable: the IHO test root is off by
   default and yields "Verified (test)"; weakest anchor wins (D8).
6. Adding/replacing an embedded root: update the `.pem`, the csproj
   `LogicalName`, and the pinned fingerprint constant together, and check the
   fingerprint against the IHO's published value.
7. `NoChecksum`/`NotSigned` are not failures by default; a missing file,
   checksum mismatch or invalid signature always is.
8. Producer output must round-trip through the consumer verifiers (see
   `Part15ProducerTests`, `PermitSignatureTests`).

## Known pitfalls / security invariants
- A wrong hardware id isn't detected at key unwrap (bare AES block); it
  surfaces as a PKCS#7 padding failure, and ~1/256 wrong keys decrypt to
  garbage silently. Don't treat "no exception" as proof of the right key.
- Signature ids are catalogue-unique; `signatureRef` resolution rejects
  missing, duplicate, cross-resource and cyclic references. Keep it so.
- Test crypto material: tests create ephemeral certs (`CertificateRequest`)
  or use `SyntheticScheme` (deterministic, seed-derived, "TEST ONLY - NOT
  REAL", SA id `ZZ00-TEST-SA`). Checked-in sets under
  `tests/datasets/ExchangeSets/Protected-*/TEST-ONLY/` hold only test SA
  certs, hardware ids, user permits and the synthetic `M_KEY`. Never commit
  real permits, user permits, M_KEYs, private keys or `.pfx` files, and
  never embed the synthetic SA as a default trust root.
- Don't log or surface cell keys, M_KEY or decrypted hardware ids in
  exceptions, telemetry (`Diagnostics/Telemetry.cs`) or MCP output.
- `PERMIT.XML` reading accepts the 5.0 and 5.1 `se` namespaces; writers emit
  `http://www.iho.int/s100/se/5.1`.
