# EncDotNet.S100.ExchangeSets

Reader for S-100 Exchange Set catalogues, dataset/support file discovery, and digital signature verification.

## Overview

This library parses S-100 Exchange Set `CATALOG.XML` files and provides access to the datasets and support files within an exchange set. Key types include:

- **`ExchangeSet`** — opens and navigates an exchange set through an `IAssetSource`.
- **`ExchangeCatalogue`** — the parsed catalogue metadata.
- **`ExchangeCatalogueReader`** — XML parser for the exchange catalogue.
- **`DatasetDiscoveryMetadata`** — metadata for each dataset in the exchange set (file name, bounding box, product specification).
- **`SupportFileDiscoveryMetadata`** — metadata for support files.
- **`CatalogueDiscoveryMetadata`** — metadata for embedded catalogues.

## File path resolution

Producers lay out exchange sets in different ways, and the catalogue can
describe a file's location in several forms. `ExchangeSet` normalizes all
of them into a single source-relative path:

- A separate `<filePath>` directory element combined with a bare
  `<fileName>` (e.g. UKHO S-101: `filePath=101GB00502793`,
  `fileName=101GB00502793.000`).
- A full path folded into `<fileName>`, optionally with a `file:/` URI
  prefix (e.g. `file:/S-101/DATASET_FILES/101AU005BTB01.000`).
- Windows-style separators and a leading slash in `<filePath>`
  (e.g. NOAA S-102: `\S102\PBC_UTM11N_MLLW_LALB`).

Use **`DatasetDiscoveryMetadata.RelativePath`** (and the equivalent on the
support/catalogue metadata types) — or the static
`ExchangeSet.ResolveRelativePath(filePath, fileName)` — to obtain the path
to pass to an `IAssetSource`. `ExchangeSet.NormalizeFileName` handles the
`file:/` prefix, backslash separators, and leading slashes on a bare file
name.

The reader also recognizes dataset/support/catalogue discovery items that
are wrapped in product-specific elements and namespaces
(e.g. `S102_DatasetDiscoveryMetadata` in `http://www.iho.int/s102/2.0/xc`),
not just the generic `S100_DatasetDiscoveryMetadata`.

## Digital Signature Verification

The library implements the S-100 Part 15 Data Protection Scheme for **signature verification** and a complementary **checksum/integrity** dimension. Exchange sets may include per-file digital signatures (DSA or ECDSA P-256 over SHA-256) embedded in `CATALOG.XML`, along with a certificate block referencing the signing data provider. Independently of any signature, each file's SHA-256 digest is computed and its presence/readability confirmed, so even an **unsigned** exchange set can be checked for missing or corrupt files.

### Model types

| Type | Description | S-100 Part 15 ref |
|---|---|---|
| `DigitalSignatureAlgorithm` | Enum: `DSA`, `ECDSA` | §15-4.1 |
| `DigitalSignatureValue` | Parsed `S100_SE_DigitalSignature` (id, certificateRef, raw signature bytes) | §15-4.2 |
| `CertificateBlock` | Certificate collection from the catalogue (scheme administrator ID + certificate entries) | §15-5 |
| `CertificateEntry` | Individual X.509 certificate (id, issuer, DER-encoded bytes) | §15-5.2 |
| `CryptographicHash` | Parsed hash MRN `urn:mrn:iho:s100:hash:<alg>:<hex>` used to integrity-check a resource | §15-8.10, Table 15-12 |

These are surfaced as properties on `DatasetDiscoveryMetadata`, `SupportFileDiscoveryMetadata`, `CatalogueDiscoveryMetadata` (via `DigitalSignatureAlgorithm`, `DigitalSignatureValue?`, and `ExpectedHash?`), and on `ExchangeCatalogue` (via `CertificateBlock?`).

### Verification API

```csharp
// Create a verifier
IExchangeSetVerifier verifier = new ExchangeSetVerifier();

// Configure trust anchors (optional — pass trusted SA root certificates)
var trust = new TrustAnchorOptions
{
    // For development/testing, skip certificate chain validation:
    AllowUntrustedCertificates = true,

    // For production, supply IHO SA root certificates:
    // TrustedRoots = [saRootCert],
};

// Verify an exchange set
ExchangeSetVerificationResult result = await verifier.VerifyAsync(
    assetSource,    // IAssetSource (filesystem or ZIP)
    catalogue,      // ExchangeCatalogue (from ExchangeCatalogueReader)
    trust,
    cancellationToken);

// Inspect results
if (result.IsUnsigned)
{
    // No signatures present — exchange set is unsigned
}
else if (result.AllValid)
{
    // All files have valid signatures
}
else if (result.HasInvalidSignatures)
{
    // At least one file has an invalid or untrusted signature
    foreach (var file in result.FileResults)
    {
        Console.WriteLine($"{file.FileName}: {file.Outcome} — {file.Detail}");
    }
}
```

### Verification outcomes

Each `FileVerificationResult` reports **two independent dimensions**: the digital-signature outcome (`Outcome`) and the checksum/integrity outcome (`ChecksumOutcome`). A file may, for example, report a valid checksum while being unsigned. `ComputedSha256` carries the file's SHA-256 digest (lower-case hex) — useful for the unsigned case. Both dimensions use the same `VerificationOutcome` enum:

| `VerificationOutcome` | Dimension | Meaning |
|---|---|---|
| `Ok` | both | Signature valid (and certificate trusted), or computed digest matched the declared hash |
| `NotSigned` | signature | No digital signature present for this file |
| `SignatureInvalid` | signature | Signature does not match the file contents |
| `CertificateUntrusted` | signature | Signature is valid but the certificate is not trusted |
| `CertificateExpired` | signature | Certificate has expired |
| `CertificateNotFound` | signature | Referenced certificate not found in the catalogue |
| `FileMissing` | both | Referenced file not found in the asset source (incomplete set) |
| `Error` | both | Unexpected error during verification |
| `NoChecksum` | checksum | File present and readable, but no declared hash to compare against |
| `ChecksumMismatch` | checksum | Computed digest does not match the declared cryptographic hash |

> The `VerificationOutcome` members are append-only: their names and ordinals are stable so downstream consumers (including the S-57 exchange-set bridge) can mirror them.

`ExchangeSetVerificationResult` exposes aggregate helpers across both dimensions: `AllValid`, `HasInvalidSignatures`, `IsUnsigned` (signature side) and `HasChecksumMismatches`, `HasMissingFiles`, `IntegrityVerified` (checksum side).

#### How a missing checksum is treated

S-100 integrity is delivered by Part 15 signatures, and the specification mandates **no** per-resource checksum element, so a "no checksum present" case (`NoChecksum`) must **not** count as a failure. This is a deliberate, documented decision:

- `AllValid` is a strict **signature-only** predicate — it requires every file's signature to be `Ok`, ignores the checksum dimension, and is therefore `false` for an unsigned set. Callers pair it with `IsUnsigned` to tell "signed and all valid" apart from "unsigned". It is **not** the overall integrity verdict.
- `IntegrityVerified` is the integrity verdict: `true` unless a file is missing or a *declared* checksum mismatched. `NoChecksum` does **not** fail it.
- The `s100 validate` exit code follows the same rule — a file fails only on `ChecksumMismatch` / `FileMissing` / `Error` / invalid signature (and, under `--strict`, also `NotSigned` / `NoChecksum`).

This mirrors the sibling S-57 implementation (`EncDotNet` #6), whose `AllValid` likewise treats a missing CRC as non-failing (the CATALOG.031 self-reference legitimately has none) and fails only on mismatch, missing file, error, or invalid signature — keeping the two repos' semantics consistent for any future shared/bridge abstraction.

### Checksum / integrity verification

S-100 has **no per-resource CRC element** like S-57's CATALOG.031; the digital signature itself "serves the dual purpose of a checksum against the unencrypted data file" (Part 15 §15-8.9). The only standalone digest construct is the optional cryptographic hash MRN `urn:mrn:iho:s100:hash:<alg>:<hex>` (§15-8.10, Table 15-12), which real catalogues rarely carry and for which the specification defines no fixed catalogue slot. Accordingly:

- Every file is hashed (streaming SHA-256) and checked for presence/readability, so an unsigned set can still be checked for **missing or corrupt** files.
- When the catalogue declares a hash MRN for a resource (discovered best-effort by `ExchangeCatalogueReader` and surfaced as `ExpectedHash`), the computed digest is compared against it (`Ok` / `ChecksumMismatch`); otherwise the file reports `NoChecksum`.

### Part 15 decryption seam

Encryption/decryption is **not** implemented. The seam for it is `ExchangeSetVerifier.OpenContentForHashingAsync(...)`, a `protected virtual` hook that today returns the raw bytes from the asset source. A future override (or injected decrypted-content provider) would return the decrypted, decompressed bytes for a `dataStatus="encrypted"` signature (§15-8.8, Table 15-10) so the computed digest matches the signature, which is produced over the unencrypted resource. The newer `S100_SE_SignatureOnData` / `S100_SE_SignatureOnSignature` forms and the `dataStatus` attribute are not yet parsed.

### CLI

The `s100 validate` command verifies an exchange set when given a `CATALOG.XML`, a directory containing one, or a `.zip` whose root holds one:

```sh
s100 validate exchangeset/CATALOG.XML
s100 validate ./exchangeset            # folder
s100 validate exchangeset.zip --format json
```

It prints a per-file signature/checksum table (or JSON), exits `0` when no file fails, and `6` (the shared findings exit code) on any failure. `--strict` additionally fails unsigned files and files with no declared checksum.

The same command also verifies **S-57 / S-63 exchange sets** (a folder containing a `CATALOG.031`, or the `CATALOG.031` file itself) by routing through `EncDotNet.S100.Datasets.S57.S57ExchangeSetVerification`, which checks each file's CRC-32 and maps the upstream `EncDotNet.S57` result onto this same `ExchangeSetVerificationResult` model and exit-code semantics (`NoChecksum` / `NotSigned` non-failing). See the [S-57 bridge README](../EncDotNet.S100.Datasets.S57/README.md#exchange-set-integrity-verification).

```sh
s100 validate s57set/CATALOG.031
s100 validate ./s57set --format json   # folder containing CATALOG.031
```

### Trust anchor model

`TrustAnchorOptions` controls how certificate trust is evaluated:

- **`TrustedRoots`** — a list of `X509Certificate2` instances representing trusted Scheme Administrator (SA) root certificates. A signing certificate's `Issuer` field is matched against these roots.
- **`AllowUntrustedCertificates`** — when `true`, signatures are verified for correctness but certificate chain validation is skipped. This is useful during development or when loading exchange sets from unknown sources.

The IHO publishes test SA certificates for interoperability testing. For production use, supply the official IHO SA root certificate.

### Scope and limitations

- **Verification only** — signing/authoring of exchange sets is not yet implemented.
- **Encryption is out of scope** — Part 15 §3 Confidentiality (ENC permits, cell-level decryption) is not supported; see the decryption seam above.
- **Checksum reference is opportunistic** — S-100 mandates no per-resource hash, so `NoChecksum` is the common (and non-failing) result for unsigned sets; hash-MRN placement is discovered best-effort.
- File hashing uses streaming SHA-256 to avoid loading large HDF5 files into memory.

## Installation

```sh
dotnet add package EncDotNet.S100.ExchangeSets
```
