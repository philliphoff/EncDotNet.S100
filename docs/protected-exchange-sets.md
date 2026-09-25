# Reading protected exchange sets

## Why it matters

Commercial ENC and other S-100 data is usually **protected** under the S-100
Part 15 data protection scheme. Each dataset is encrypted, and the keys to read
it come in a permit that only one system can use. This guide shows how to read a
protected exchange set with `EncDotNet.S100`: authenticate the permit, decrypt
the datasets, and verify their signatures.

It builds on [Loading datasets](loading-datasets.md), which covers
`S100ExchangeSet`.

## Quick win

You need three things from outside the exchange set:

- the exchange set itself, with its `PERMIT.XML` and `PERMIT.SIGN` files;
- your system's **hardware ID**, which the data server wrapped the dataset keys
  with;
- the **Scheme Administrator certificate** you trust to vouch for the data
  server.

```csharp
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.ExchangeSets.Protection;

HardwareId hardwareId = HardwareId.Parse(File.ReadAllText("hardware-id.txt").Trim());
using var schemeAdministrator = X509CertificateLoader.LoadCertificateFromFile("scheme-administrator.crt");
var trust = new TrustAnchorOptions { TrustedRoots = [schemeAdministrator] };

// 1. Authenticate the permit with its signature.
await using var permitXml = File.OpenRead("protected-set/PERMIT.XML");
await using var permitSign = File.OpenRead("protected-set/PERMIT.SIGN");
var authentication = await PermitSignatureVerifier.AuthenticateAsync(permitXml, permitSign, "PERMIT.XML", trust);
PermitFile permits = authentication.PermitFile
    ?? throw new InvalidOperationException($"Permit rejected: {authentication.Verification.Detail}");

// 2. Open the exchange set and build the key provider from its catalogue.
await using var exchangeSet = await S100ExchangeSet.OpenAsync("protected-set");
var keys = new PermitKeyProvider(permits, hardwareId, exchangeSet.Catalogue);

// 3. Read the datasets through a decrypting view of the exchange set.
await using var decrypted = exchangeSet.WithDecryption(keys);
foreach (var entry in decrypted.Datasets)
{
    using var dataset = await entry.OpenAsync();
    Console.WriteLine($"{entry.Metadata.FileName}: {dataset.Spec}");
}
```

No protected data to hand? [Create a test exchange set](#create-a-test-exchange-set)
generates everything this example reads.

On .NET 8, load the certificate with `new X509Certificate2("scheme-administrator.crt")`
instead; `X509CertificateLoader` is .NET 9 and later.

## Deep dive

### How the pieces fit

| Piece | What it is |
|---|---|
| **Cell key** | A random AES-128 key per dataset. The data server encrypts the dataset with it. |
| **Hardware ID** (`HW_ID`) | A 16-byte identifier of the client system. Permits wrap each cell key with it, so only that system can unwrap the key. |
| **User permit** | The hardware ID encrypted with the system manufacturer's key (`M_KEY`), plus the manufacturer ID. The client sends it to the data server to get permits. |
| **`PERMIT.XML`** | One `datasetPermit` per dataset: the wrapped cell key, an expiry date, and the edition or issue date it's valid for. |
| **`PERMIT.SIGN`** | The data server's signature over `PERMIT.XML`, with the data server's certificate. |
| **Scheme Administrator (SA) certificate** | The root of trust. It issues data-server certificates; you configure it as a trusted root. |
| **Catalogue signatures** | Signatures in `CATALOG.XML` over each dataset, which prove it hasn't been altered. |

The flow is: authenticate `PERMIT.XML` against the SA certificate, unwrap each
cell key with the hardware ID, decrypt the datasets, and (optionally) verify
their signatures.

### Getting the hardware ID

If your system stores its hardware ID, parse its 32-character hexadecimal form
with `HardwareId.Parse`. If you have the system's **user permit** and the
manufacturer key instead, recover the hardware ID from them:

```csharp
HardwareId hardwareId = UserPermit.Parse(userPermitText).DecryptHardwareId(manufacturerKey);
```

`UserPermit.Parse` validates the permit's 46-character form and its checksum,
and throws `FormatException` if either is wrong. `UserPermit.Create` goes the
other way, for a system that needs to issue its own user permit.

### Authenticating the permit

`PermitSignatureVerifier.AuthenticateAsync` checks `PERMIT.SIGN`'s ECDSA P-384
signature over the exact bytes of `PERMIT.XML`. It also checks that the data
server's certificate chains to one of your `TrustedRoots` and is within its
validity dates. It returns the parsed permit only if all of that succeeds:

```csharp
if (!authentication.IsAuthenticated)
{
    // e.g. CertificateUntrusted, SignatureInvalid, NotSigned, CertificateExpired
    Console.WriteLine($"{authentication.Verification.Outcome}: {authentication.Verification.Detail}");
}
```

Permit authentication always requires a trusted root: it rejects
`AllowUntrustedCertificates = true`, even though exchange-set signature
verification accepts it for development. `PermitFile.Read` can parse a permit
for inspection, but `PermitKeyProvider` refuses a permit that hasn't been
authenticated.

### Decrypting datasets

`PermitKeyProvider` resolves each dataset's cell key. It needs the exchange
set's catalogue because a permit only applies to the dataset edition, or issue
date, it was issued for. `WithDecryption(keys)` then returns an exchange set that
decrypts as it reads; files without a permit (the catalogue, unencrypted support
files) are read unchanged. Keep the original exchange set alive while you use the
decrypting one.

To check whether a dataset can be opened before opening it, evaluate its permit:

```csharp
foreach (var entry in exchangeSet.Datasets)
{
    PermitEvaluationResult evaluation = keys.Evaluate(entry.Metadata.FileName);
    Console.WriteLine($"{entry.Metadata.FileName}: {evaluation.Outcome} {evaluation.Detail}");
}
```

`Outcome` is `Allowed`, `NotProtected`, or the reason the permit is refused:

| Outcome | Meaning |
|---|---|
| `PermitNotFound` | The dataset is protected, but the permit file has no permit for it. |
| `EditionMismatch` / `EditionNumberMissing` | The permit is for a different edition, or the catalogue doesn't state one. |
| `IssueDateMismatch` / `IssueDateMissing` | The permit is tied to an issue date that doesn't match, or the catalogue doesn't state one. |
| `IssuedAfterExpiry` | The dataset was issued after the permit expired. |
| `BaseDatasetMissing` | A protected update's base dataset isn't in the exchange set. |

Expiry is checked against the dataset's **issue date**, not today's date: a
permit keeps working for the datasets it was issued for.

Opening a dataset whose permit is refused throws `DatasetPermitException`. Its
`Evaluation` property carries the result above.

### Verifying signatures

`ExchangeSetVerifier` checks the catalogue's signatures over each dataset.
Datasets are usually signed in their **unencrypted** form, so give the verifier
a `Part15SignatureContentResolver` with your key provider, and it decrypts where
it needs to. The verifier reads from an asset source, so open the exchange set
from one you keep:

```csharp
using EncDotNet.S100.Core;

using var source = FileSystemAssetSource.Create("protected-set");
await using var exchangeSet = await S100ExchangeSet.OpenAsync(source);
var keys = new PermitKeyProvider(permits, hardwareId, exchangeSet.Catalogue);

var verifier = new ExchangeSetVerifier(new Part15SignatureContentResolver(keys));
ExchangeSetVerificationResult verification = await verifier.VerifyAsync(source, exchangeSet.Catalogue, trust);

foreach (var file in verification.FileResults)
    Console.WriteLine($"{file.FileName}: signature {file.Outcome}, checksum {file.ChecksumOutcome}");
Console.WriteLine(verification.AllValid ? "All signatures valid." : "Signature problems found.");
```

`AllValid` is true only when every file has a valid signature from a trusted
certificate. The
[`EncDotNet.S100.ExchangeSets` README](../src/EncDotNet.S100.ExchangeSets/README.md#verification-outcomes)
explains each outcome, and how missing checksums are treated.

### Trust anchors in development and production

In **production**, `TrustedRoots` holds the IHO Scheme Administrator's
certificate, from the IHO or your data distributor. Treat it as configuration:
load it from a file or certificate store, never from the exchange set you're
checking.

In **development**, use a test Scheme Administrator that you control, such as
the one [Create a test exchange set](#create-a-test-exchange-set) generates.
The IHO also publishes test SA certificates for interoperability testing.

### Create a test exchange set

> [!CAUTION]
> This code stands in for a **data server**, to produce test data. It isn't part
> of the library API, and a real client never has the data server's private
> keys or the cell keys.

The method below creates a protected exchange set from any S-101 cell, such as
`tests/datasets/S101/S-101/DATASET_FILES/101AA00DS0019.000` in this repository.
It generates a Scheme Administrator certificate and a data-server certificate,
encrypts the cell, signs it in `CATALOG.XML`, and writes a permit for the given
hardware ID:

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.ExchangeSets.Protection;

static X509Certificate2 CreateProtectedExchangeSet(string folder, string cellPath, HardwareId hardwareId)
{
    string cellName = Path.GetFileNameWithoutExtension(cellPath);
    const string issueDate = "2026-03-01";

    // Certificates: a Scheme Administrator root, and a data-server certificate it
    // issues. Part 15 signatures are ECDSA P-384 with SHA-384.
    using var saKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    var saRequest = new CertificateRequest("CN=Test Scheme Administrator", saKey, HashAlgorithmName.SHA384);
    saRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    saRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.DigitalSignature, true));
    var saRoot = saRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

    using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);
    var serverRequest = new CertificateRequest("CN=Test Data Server", serverKey, HashAlgorithmName.SHA384);
    serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    using var serverCert = serverRequest.Create(
        saRoot, saRoot.NotBefore.AddMinutes(1), saRoot.NotAfter.AddMinutes(-1), RandomNumberGenerator.GetBytes(16));
    string serverCertBase64 = Convert.ToBase64String(serverCert.RawData);

    // Encrypt the cell with a random cell key, and sign its unencrypted bytes.
    byte[] plaintext = File.ReadAllBytes(cellPath);
    byte[] cellKey = RandomNumberGenerator.GetBytes(16);
    Directory.CreateDirectory(Path.Combine(folder, "S-101"));
    File.WriteAllBytes(Path.Combine(folder, "S-101", cellName + ".000"), S100Cipher.EncryptDataset(plaintext, cellKey));
    string cellSignature = Convert.ToBase64String(
        serverKey.SignHash(SHA384.HashData(plaintext), DSASignatureFormat.Rfc3279DerSequence));

    // CATALOG.XML: the protected cell, its signature, and the data-server certificate.
    File.WriteAllText(Path.Combine(folder, "CATALOG.XML"), $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0"
                                       xmlns:S100SE="http://www.iho.int/s100/se/5.2">
          <S100XC:identifier>
            <S100XC:identifier>TEST_PROTECTED</S100XC:identifier>
            <S100XC:dateTime>2026-03-01T00:00:00Z</S100XC:dateTime>
          </S100XC:identifier>
          <S100XC:certificates>
            <S100SE:schemeAdministrator id="SA" />
            <S100SE:certificate id="data-server" issuer="SA">{serverCertBase64}</S100SE:certificate>
          </S100XC:certificates>
          <S100XC:datasetDiscoveryMetadata>
            <S100XC:S100_DatasetDiscoveryMetadata>
              <S100XC:fileName>S-101/{cellName}.000</S100XC:fileName>
              <S100XC:dataProtection>true</S100XC:dataProtection>
              <S100XC:digitalSignatureReference>ECDSA-384-SHA2</S100XC:digitalSignatureReference>
              <S100XC:digitalSignatureValue>
                <S100SE:S100_SE_SignatureOnData id="cell-signature" certificateRef="data-server"
                                                dataStatus="unencrypted">{cellSignature}</S100SE:S100_SE_SignatureOnData>
              </S100XC:digitalSignatureValue>
              <S100XC:editionNumber>1</S100XC:editionNumber>
              <S100XC:updateNumber>0</S100XC:updateNumber>
              <S100XC:issueDate>{issueDate}</S100XC:issueDate>
              <S100XC:productSpecification>
                <S100XC:productIdentifier>S-101</S100XC:productIdentifier>
              </S100XC:productSpecification>
            </S100XC:S100_DatasetDiscoveryMetadata>
          </S100XC:datasetDiscoveryMetadata>
        </S100XC:S100_ExchangeCatalogue>
        """);

    // PERMIT.XML: the cell key wrapped with the client's hardware ID.
    string encryptedKey = Convert.ToHexString(S100Cipher.EncryptBlock(cellKey, hardwareId.Value));
    byte[] permitXml = Encoding.UTF8.GetBytes($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Permit xmlns="http://www.iho.int/s100/se/5.1">
          <header>
            <issueDate>{issueDate}</issueDate>
            <dataServerName>Test Data Server</dataServerName>
            <dataServerIdentifier>TS</dataServerIdentifier>
            <version>1.0.0</version>
          </header>
          <products>
            <product id="S-101">
              <datasetPermit>
                <filename>{cellName}</filename>
                <editionNumber>1</editionNumber>
                <expiry>2099-12-31</expiry>
                <encryptedKey>{encryptedKey}</encryptedKey>
              </datasetPermit>
            </product>
          </products>
        </Permit>
        """);
    File.WriteAllBytes(Path.Combine(folder, "PERMIT.XML"), permitXml);

    // PERMIT.SIGN: the data server's signature over the exact PERMIT.XML bytes.
    string permitSignature = Convert.ToBase64String(
        serverKey.SignHash(SHA384.HashData(permitXml), DSASignatureFormat.Rfc3279DerSequence));
    File.WriteAllText(Path.Combine(folder, "PERMIT.SIGN"), $"""
        <StandaloneDigitalSignature xmlns="http://www.iho.int/s100/se/5.1">
          <filename>PERMIT.XML</filename>
          <certificates>
            <schemeAdministrator id="SA" />
            <certificate id="data-server" issuer="SA">{serverCertBase64}</certificate>
          </certificates>
          <digitalSignature id="permit-signature" certificateRef="data-server">{permitSignature}</digitalSignature>
        </StandaloneDigitalSignature>
        """);

    return saRoot;
}
```

Generate a hardware ID, create the exchange set, and save the two files the
[Quick win](#quick-win) reads:

```csharp
var hardwareId = HardwareId.FromBytes(RandomNumberGenerator.GetBytes(16));
File.WriteAllText("hardware-id.txt", hardwareId.ToString());

using var saRoot = CreateProtectedExchangeSet("protected-set", "101AA00DS0019.000", hardwareId);
File.WriteAllBytes("scheme-administrator.crt", saRoot.Export(X509ContentType.Cert));
```

Change the permit's `editionNumber` or `expiry`, or trust a different root, to
see each failure described below.

## Troubleshooting

> [!IMPORTANT]
> A **wrong hardware ID** isn't detected when the permit is checked: the cell
> key unwraps to the wrong value, and reading the dataset fails with
> `CryptographicException` ("Padding is invalid and cannot be removed"). If
> every protected dataset fails that way, check the hardware ID, or the
> manufacturer key you recovered it with.

> [!NOTE]
> `CertificateUntrusted` from `AuthenticateAsync` means the data server's
> certificate doesn't chain to any of your `TrustedRoots`: you're trusting the
> wrong Scheme Administrator, or the permit came from a different scheme.
> `CertificateExpired` means a certificate is outside its validity dates.

> [!TIP]
> `DatasetPermitException` names the dataset and the refusal reason in its
> message, and in `Evaluation.Outcome`. `EditionMismatch` or
> `IssuedAfterExpiry` usually means the exchange set carries a newer edition
> than your permits cover: request updated permits from your data distributor.

## Next step

- [Loading datasets](loading-datasets.md) — exchange sets, updates and asset
  sources.
- [`EncDotNet.S100.ExchangeSets` README](../src/EncDotNet.S100.ExchangeSets/README.md)
  — the Part 15 types and verification outcomes in detail.
