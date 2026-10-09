# EncDotNet.S100.ExchangeSets

`EncDotNet.S100.ExchangeSets` reads S-100 exchange set catalogues
(`CATALOG.XML`), finds the datasets and support files they list, and
implements the S-100 Part 15 data protection scheme: signature and checksum
verification, permit authentication and dataset decryption, and signing and
permit issuing. Reference it when you need the catalogue model, integrity
checks or Part 15 protection directly. To open and render the datasets in an
exchange set, use `S100ExchangeSet` in the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) facade.

## Install

```bash
dotnet add package EncDotNet.S100.ExchangeSets
```

## Example: list the datasets in an exchange set

```csharp
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets;

using var exchangeSet = await ExchangeSet.OpenAsync(
    FileSystemAssetSource.Create("path/to/exchange-set"));

foreach (var dataset in exchangeSet.Catalogue.DatasetDiscoveryMetadata)
    Console.WriteLine($"{dataset.RelativePath}: {dataset.ProductSpecification?.Name}");
```

`ExchangeSet` disposes its asset source when you dispose it.

## Main entry points

- `ExchangeSet` opens an exchange set through an `IAssetSource` and fetches
  its datasets, support files and catalogue files.
- `ExchangeCatalogueReader` parses `CATALOG.XML` into an `ExchangeCatalogue`.
- `DatasetDiscoveryMetadata` describes each dataset: file name, bounding box,
  product specification, edition and update, and signatures.
- `SupportFileDiscoveryMetadata` and `CatalogueDiscoveryMetadata` describe
  support files and embedded catalogues.
- `ExchangeSetVerifier` checks signatures and file integrity; see
  [Verify signatures and checksums](#verify-signatures-and-checksums).
- The `EncDotNet.S100.ExchangeSets.Protection` namespace reads and produces
  protected data; see [Read encrypted datasets](#read-encrypted-datasets) and
  [Sign data and issue permits](#sign-data-and-issue-permits).

## File paths in the catalogue

Producers lay out exchange sets differently, and a catalogue can give a file's
location in several forms. `ExchangeSet` turns each of them into one path
relative to the asset source:

- A `<filePath>` directory element with a bare `<fileName>`. For example, UKHO
  S-101 uses `filePath=101GB00502793` and `fileName=101GB00502793.000`.
- A full path in `<fileName>`, with or without a `file:/` URI prefix, such as
  `file:/S-101/DATASET_FILES/101AU005BTB01.000`.
- Windows-style separators and a leading slash in `<filePath>`, such as NOAA
  S-102's `\S102\PBC_UTM11N_MLLW_LALB`.

To get the path to pass to an `IAssetSource`, use
`DatasetDiscoveryMetadata.RelativePath` (or the same property on the support
file and catalogue metadata types), or call
`ExchangeSet.ResolveRelativePath(filePath, fileName)`.
`ExchangeSet.NormalizeFileName` removes the `file:/` prefix, backslash
separators and leading slashes from a bare file name.

The reader also recognizes discovery records wrapped in product-specific
elements and namespaces, such as `S102_DatasetDiscoveryMetadata` in
`http://www.iho.int/s102/2.0/xc`, as well as the generic
`S100_DatasetDiscoveryMetadata`.

### Catalogues without a discovery wrapper

S-100 Edition 5.x (Part 17) nests discovery records in a wrapper element such
as `<datasetDiscoveryMetadata>`. Some catalogues use the older `S100EC` layout
(namespace `http://www.iho.int/S100EC`) and put `S100_DatasetDiscoveryMetadata`
records directly under the root, with no wrapper. The JCOMM/IHO S-411 sample
sets do this. The reader handles both: it reads the wrapper's children when the
wrapper is present, and scans the root otherwise.

## Verify signatures and checksums

`ExchangeSetVerifier` checks two things for each file in an exchange set:

- **Digital signatures.** An exchange set can carry several signatures per
  file in `CATALOG.XML`, using DSA or ECDSA over SHA-256. They can cover the
  unencrypted, compressed or encrypted data. A distribution signature signs
  another signature's ASN.1 R/S bytes.
- **Integrity.** The verifier computes each file's SHA-256 digest and confirms
  the file is present and readable, so you can check even an unsigned exchange
  set for missing or corrupt files.

```csharp
using EncDotNet.S100.ExchangeSets;

IExchangeSetVerifier verifier = new ExchangeSetVerifier();

var trust = new TrustAnchorOptions
{
    // During development, skip certificate chain validation:
    AllowUntrustedCertificates = true,

    // In production, supply the IHO Scheme Administrator root certificates:
    // TrustedRoots = [saRootCert],
};

ExchangeSetVerificationResult result = await verifier.VerifyAsync(
    assetSource, // IAssetSource: a folder or ZIP
    catalogue,   // ExchangeCatalogue, from ExchangeCatalogueReader
    trust,
    cancellationToken);

if (result.IsUnsigned)
{
    // No file is signed.
}
else if (result.AllValid)
{
    // Every file has a valid signature.
}
else if (result.HasInvalidSignatures)
{
    // At least one file has an invalid signature.
    foreach (var file in result.FileResults)
        Console.WriteLine($"{file.FileName}: {file.Outcome} ({file.Detail})");
}
```

Signature IDs are unique across the catalogue. The verifier resolves
`signatureRef` chains within the same resource entry and allows forward
references. It rejects missing references, duplicates, references to another
resource, and cycles. `FileVerificationResult.Outcome` is the aggregate
result for a file, and `SignatureResults` has the result of each signature.

### Signature model types

| Type | Description | S-100 Part 15 ref |
|---|---|---|
| `DigitalSignatureAlgorithm` | Legacy `DSA` and `ECDSA`, and the Part 15 file-transfer `ECDSA384SHA2` algorithm | §15-8.4, §15-8.7 |
| `DigitalSignatureValue` | A parsed legacy, signature-on-data or signature-on-signature value: id, certificate reference, raw signature bytes and form-specific metadata | §15-8.8, §15-8.11.3–6 |
| `DigitalSignatureKind` / `SignatureDataStatus` | The signature form, and whether a data signature covers the unencrypted, compressed or encrypted representation | §15-8.11.3–6 |
| `SignatureVerificationResult` | The outcome for one signature, with a structured failure reason | §15-8.8 |
| `CertificateBlock` | The catalogue's certificates: the scheme administrator ID and certificate entries | §15-5 |
| `CertificateEntry` | One X.509 certificate: id, issuer and DER-encoded bytes | §15-5.2 |
| `CryptographicHash` | A parsed hash MRN, `urn:mrn:iho:s100:hash:<alg>:<hex>`, used to check a resource's integrity | §15-8.10, Table 15-12 |

`DatasetDiscoveryMetadata`, `SupportFileDiscoveryMetadata` and
`CatalogueDiscoveryMetadata` expose `DigitalSignatureAlgorithm`, the ordered
`DigitalSignatures`, a single `DigitalSignatureValue` kept for compatibility,
and `ExpectedHash`. `ExchangeCatalogue.Certificates` holds the
`CertificateBlock`.

### Verification outcomes

Each `FileVerificationResult` reports two independent results: the signature
result (`Outcome`) and the checksum result (`ChecksumOutcome`). A file can, for
example, have a valid checksum and no signature. `ComputedSha256` is the file's
SHA-256 digest in lowercase hexadecimal, which is useful for unsigned files.
Both results use the `VerificationOutcome` enum:

| `VerificationOutcome` | Applies to | Meaning |
|---|---|---|
| `Ok` | both | The signature is valid and its certificate trusted, or the computed digest matches the declared hash. |
| `NotSigned` | signature | The file has no digital signature. |
| `SignatureInvalid` | signature | The signature doesn't match the file contents. |
| `CertificateUntrusted` | signature | The signature is valid but its certificate isn't trusted. |
| `CertificateExpired` | signature | The certificate has expired. |
| `CertificateNotFound` | signature | The referenced certificate isn't in the catalogue. |
| `FileMissing` | both | The file isn't in the asset source; the exchange set is incomplete. |
| `Error` | both | An unexpected error occurred during verification. |
| `NoChecksum` | checksum | The file is present and readable, but there's no declared hash to compare with. |
| `ChecksumMismatch` | checksum | The computed digest doesn't match the declared hash. |

New `VerificationOutcome` members are only ever added at the end. Names and
numeric values don't change, so other code, including the S-57 exchange-set
bridge, can mirror them.

`ExchangeSetVerificationResult` summarizes all files:

- Signatures: `AllValid`, `HasInvalidSignatures` and `IsUnsigned`.
- Integrity: `HasChecksumMismatches`, `HasMissingFiles` and
  `IntegrityVerified`.

### How a missing checksum is treated

S-100 has no per-resource CRC element like S-57's `CATALOG.031`. The digital
signature "serves the dual purpose of a checksum against the unencrypted data
file" (Part 15 §15-8.9). The only standalone digest is the optional hash MRN
`urn:mrn:iho:s100:hash:<alg>:<hex>` (§15-8.10, Table 15-12). Real catalogues
rarely include it, and the specification gives it no fixed place in the
catalogue.

So the verifier works like this:

- It hashes every file with a streaming SHA-256, so large HDF5 files aren't
  loaded into memory, and checks that each file is present and readable.
- When the catalogue declares a hash MRN for a resource,
  `ExchangeCatalogueReader` finds it on a best-effort basis and exposes it as
  `ExpectedHash`. The verifier compares the computed digest with it and
  reports `Ok` or `ChecksumMismatch`. Otherwise the file reports `NoChecksum`.

`NoChecksum` isn't a failure:

- `AllValid` checks signatures only. It's `true` only when every file's
  signature is `Ok`, so it's `false` for an unsigned exchange set. Pair it with
  `IsUnsigned` to tell "signed and all valid" from "unsigned". It isn't the
  overall integrity result.
- `IntegrityVerified` is the integrity result. It's `true` unless a file is
  missing or a declared checksum doesn't match. `NoChecksum` doesn't make it
  `false`.
- `s100 validate` follows the same rule; see
  [Verify from the command line](#verify-from-the-command-line).

The S-57 verification in the upstream `EncDotNet.S57` library uses the same
rule. Its `AllValid` doesn't fail on a missing CRC, because the `CATALOG.031`
entry for itself has none, and fails only on a mismatch, a missing file, an
error or an invalid signature.

### Trust anchors

`TrustAnchorOptions` controls how certificates are trusted:

- `TrustedRoots` is a list of `X509Certificate2` Scheme Administrator (SA)
  root certificates. The verifier matches a signing certificate's `Issuer`
  against them.
- `AllowUntrustedCertificates`, when `true`, still checks that signatures are
  correct but skips certificate chain validation. Use it during development or
  for exchange sets from unknown sources.

The IHO publishes test SA certificates for interoperability testing. In
production, supply the official IHO SA root certificate.

### Verify from the command line

`s100 validate` verifies an exchange set when you pass a `CATALOG.XML`, a
folder that contains one, or a `.zip` with one at its root:

```bash
s100 validate exchangeset/CATALOG.XML
s100 validate ./exchangeset
s100 validate exchangeset.zip --format json
```

It prints a table of signature and checksum results for each file, or JSON
with `--format json`. It exits with `0` when no file fails, and `6` (the exit
code for findings) when any file fails. A file fails on `ChecksumMismatch`,
`FileMissing`, `Error` or an invalid signature. With `--strict`, `NotSigned`
and `NoChecksum` also fail.

The same command verifies S-57 and S-63 exchange sets: pass a folder that
contains a `CATALOG.031`, or the file itself. It checks each file's CRC-32
through `EncDotNet.S100.Datasets.S57.S57ExchangeSetVerification`, which maps
the `EncDotNet.S57` result onto the same `ExchangeSetVerificationResult` model
and exit codes. `NoChecksum` and `NotSigned` don't fail. See
[exchange-set integrity verification](../EncDotNet.S100.Datasets.S57/README.md#exchange-set-integrity-verification)
in the S-57 README.

```bash
s100 validate s57set/CATALOG.031
s100 validate ./s57set --format json
```

[Command-line rendering](../../docs/cli.md) lists every `s100` command.

## Read encrypted datasets

The `EncDotNet.S100.ExchangeSets.Protection` namespace implements the
confidentiality part of Part 15: reading encrypted datasets.

| Type | Role | S-100 Part 15 ref |
|---|---|---|
| `S100Cipher` | AES-128 primitives: single-block key wrap and unwrap (`EncryptBlock`, `DecryptBlock`) and the dataset modified-CBC mode (`DecryptDataset`, `EncryptDataset`) | §15-6 |
| `HardwareId` | The 16-byte Data Client system id (`HW_ID`) | §15-7.3.1.1 |
| `UserPermit` | The 46-character user permit: parse and validate (CRC-32), `Create`, and `DecryptHardwareId(M_KEY)` | §15-7.3 |
| `DataPermit` | One `datasetPermit` record: `encryptedKey`, required expiry, and edition and issue identity | §15-7.4.4 |
| `PermitFile` / `PermitGroup` / `PermitHeader` | The `PERMIT.XML` parser, accepting the 5.0 and 5.1 namespaces, with `TryGetPermit` lookup | §15-7.4 |
| `StandaloneDigitalSignatureReader` / `PermitSignatureVerifier` | Parse `PERMIT.SIGN`, validate its certificate chain and ECDSA P-384/SHA-384 signature, and expose the permit only after authentication | §15-7.4.5, §15-8.11.2 |
| `IDatasetKeyProvider` / `PermitKeyProvider` | Get a cell key from an authenticated permit, and enforce the catalogue's edition, issue date and expiry | §15-7.4.4 |
| `DecryptingAssetSource` | An `IAssetSource` decorator that decrypts, and optionally decompresses, files that have a key | §15-5, §15-6 |
| `DatasetPermitException` / `DatasetDecryptionException` | A permit refuses a dataset (with a `PermitEvaluationResult`), or a permitted dataset's cell key can't decrypt it | §15-6, §15-7.4.4 |

The cryptography follows the §15 worked examples, which the unit tests check:

- AES-128 with PKCS#7 padding, in the §15-6.2.4 modified CBC mode. A random
  block is added before encryption and dropped after decryption, so no IV is
  sent.
- Cell keys and hardware ids are exactly one AES block, wrapped with
  single-block ECB.
- Compression (§15-5.2) is ZIP/DEFLATE, applied before encryption. With
  `decompress: true`, `DecryptingAssetSource` unzips the single-entry archive.

```csharp
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets.Protection;

// Recover the hardware id from a user permit (needs the OEM M_KEY), or use the one the client holds.
HardwareId hwId = UserPermit.Parse(userPermitText).DecryptHardwareId(manufacturerKey);

// Authenticate the permit file before using any key in it.
await using Stream permitXml = File.OpenRead("PERMIT.XML");
await using Stream permitSign = File.OpenRead("PERMIT.SIGN");
PermitAuthenticationResult authentication =
    await PermitSignatureVerifier.AuthenticateAsync(
        permitXml, permitSign, "PERMIT.XML", trustAnchors);
PermitFile permits = authentication.PermitFile
    ?? throw new InvalidDataException(authentication.Verification.Detail);

// The catalogue's metadata limits which permit edition, issue date and expiry apply.
var keys = new PermitKeyProvider(permits, hwId, catalogue);

// Wrap any IAssetSource so encrypted datasets read as plaintext.
using IAssetSource source = new DecryptingAssetSource(fileSystemOrZipSource, keys, decompress: true);
await using Stream plaintext = await source.OpenAsync("S-101/101GB40079ABCDEF.000");
```

`PermitFile.Read(...)` still reads a permit file for inspecting its metadata,
but it returns an unauthenticated permit, which `PermitKeyProvider` rejects.
To use keys, authenticate the permit with
`PermitSignatureVerifier.AuthenticateAsync(...)`.

The facade's `S100ExchangeSet.WithDecryption` wraps `DecryptingAssetSource`
for you. [Reading protected exchange sets](../../docs/protected-exchange-sets.md)
walks through the whole process, including code that creates a protected test
exchange set.

### Permit and decryption errors

- If a dataset's permit doesn't allow it, the read throws
  `DatasetPermitException` before any decryption. `Evaluation.Outcome` says
  why, for example `EditionMismatch` or `IssuedAfterExpiry`.
- A permit's `encryptedKey` is a bare AES block with no checksum, so a wrong
  hardware id isn't detected when the key is unwrapped. It shows up when that
  key fails to decrypt the dataset, and `DecryptingAssetSource` throws
  `DatasetDecryptionException`.
  - The exception names the dataset (`DatasetPath`) and points to the
    hardware id, the manufacturer key used to recover it, or a permit issued
    for a different Data Client.
  - It derives from `CryptographicException`, and `InnerException` is the
    original failure.
  - Detection relies on the PKCS#7 padding check, so about one wrong key in
    256 decrypts without an error and returns unreadable content.

### Verify signatures on encrypted datasets

For legacy signatures over the unencrypted resource, pass a
`DecryptingAssetSource` to `ExchangeSetVerifier.VerifyAsync(...)`.

For catalogues that use the explicit Part 15 signature forms, pass the raw
asset source to the verifier, and give the authenticated key provider to a
`Part15SignatureContentResolver`. One resource can then carry signatures over
several representations:

```csharp
var contentResolver = new Part15SignatureContentResolver(keys);
var verifier = new ExchangeSetVerifier(contentResolver);
ExchangeSetVerificationResult verification =
    await verifier.VerifyAsync(fileSystemOrZipSource, catalogue, trust);
```

- An `encrypted` signature hashes the stored ciphertext and needs no permit.
- A `compressed` signature decrypts only when it has to.
- An `unencrypted` signature decrypts and decompresses as the discovery
  metadata says.

All representations, and chained distribution signatures, can be on one
resource.

## Sign data and issue permits

The data-server side of Part 15 mirrors the readers above, and its output
verifies with them:

| Type | Role | S-100 Part 15 ref |
|---|---|---|
| `Part15Signer` | Signs with an ECDSA P-384 certificate key (SHA-384, DER): `SignData` and `SignDataAsync` (`S100_SE_SignatureOnData` with a `dataStatus`), `SignSignature` (`S100_SE_SignatureOnSignature`), `SignStandalone`, and `CreateCertificateBlock` for the matching `CertificateBlock` | §15-8.4, §15-8.11 |
| `StandaloneDigitalSignatureWriter` | Writes `PERMIT.SIGN` and `CATALOG.SIGN` documents | §15-8.11.2 |
| `DataPermit.Create` | Issues a `datasetPermit` by wrapping a cell key with the recipient's `HardwareId` | §15-7.4.4 |
| `PermitFile.Create` / `PermitGroup` | Builds a permit file from header and product groups | §15-7.4 |
| `PermitFileWriter` | Writes `PERMIT.XML`; `WriteSigned` also writes `PERMIT.SIGN` over the exact bytes written | §15-7.4, §15-7.4.5 |

```csharp
using EncDotNet.S100.ExchangeSets.Protection;

using var signer = new Part15Signer(dataServerCertificate, "urn:mrn:iho:s62:xx:key1", schemeAdministratorId: "IHO");
var permit = PermitFile.Create(
[
    new PermitGroup(
        new PermitHeader { IssueDate = DateOnly.FromDateTime(DateTime.UtcNow), DataServerName = "Example", DataServerIdentifier = "EX", Version = "1.0.0" },
        new Dictionary<string, IReadOnlyList<DataPermit>>
        {
            ["S-101"] = [DataPermit.Create("101AA00DS0019", cellKey, hardwareId, expiry: new DateOnly(2027, 12, 31), editionNumber: 1)],
        }),
]);

using var permitXml = File.Create("PERMIT.XML");
using var permitSign = File.Create("PERMIT.SIGN");
PermitFileWriter.WriteSigned(permit, signer, permitXml, permitSign);
```

Permit files and standalone signatures are written in the
`http://www.iho.int/s100/se/5.1` namespace, as in the §15-7.4.6 example.

## Limitations

- You can produce signatures, standalone signature files and signed permits.
  Writing a signed `CATALOG.XML` and protecting a whole exchange set aren't
  implemented yet
  ([#843](https://github.com/philliphoff/EncDotNet.S100/issues/843)).
- Decryption, permit authentication and every catalogue-level signature form
  are implemented in the library. SoundCharts and `s100` don't open protected
  datasets yet.
- S-100 requires no per-resource hash, so `NoChecksum` is the usual result for
  unsigned exchange sets, and it doesn't count as a failure. The reader finds
  hash MRNs on a best-effort basis.
