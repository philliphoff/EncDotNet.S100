using System.Globalization;
using System.Text;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.ExchangeSets.Protection;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Protection;

/// <summary>A scenario written as a Part 15 protected exchange set, with its TEST ONLY scheme.</summary>
/// <param name="Scenario">The exchange set; <see cref="GeneratedScenario.Datasets"/> name the files as written (encrypted where protected).</param>
/// <param name="Protection">The protection applied.</param>
/// <param name="Scheme">The scheme; dispose it after use.</param>
/// <param name="PermitDirectory">The folder holding <c>PERMIT.XML</c> and <c>PERMIT.SIGN</c>.</param>
public sealed record ProtectedScenario(GeneratedScenario Scenario, ProtectionSpec Protection, SyntheticScheme Scheme, string PermitDirectory) : IDisposable
{
    /// <summary>The <c>PERMIT.XML</c> path.</summary>
    public string PermitPath => Path.Combine(PermitDirectory, PermitFileWriter.PermitFileName);

    /// <summary>The <c>PERMIT.SIGN</c> path; the file is absent when the permit is unsigned.</summary>
    public string PermitSignaturePath => Path.Combine(PermitDirectory, PermitFileWriter.SignatureFileName);

    /// <inheritdoc />
    public void Dispose() => Scheme.Dispose();
}

/// <summary>
/// Writes a scenario as an S-100 Part 15 protected exchange set: the data
/// server's side of the scheme, deterministic for tests.
/// </summary>
/// <remarks>
/// <para>
/// The generator writes the plain scenario with <see cref="ScenarioGenerator"/>,
/// then, for each dataset in the <see cref="ProtectionSpec"/>, signs its
/// plain bytes (<c>dataStatus="unencrypted"</c>) with
/// <see cref="Part15Signer"/>, encrypts it with <see cref="S100Cipher"/>,
/// issues a permit with <see cref="DataPermit.Create"/>, and rewrites
/// <c>CATALOG.XML</c> with the certificate block, signatures and
/// <c>dataProtection</c>. <see cref="PermitFileWriter"/> writes
/// <c>PERMIT.XML</c> and <c>PERMIT.SIGN</c>.
/// </para>
/// <para>
/// Deterministic: keys, certificates and signatures come from
/// <see cref="SyntheticScheme"/>; each dataset's cell key, prefix block and
/// IV are derived from the seed and the dataset's name. Part 15 requires
/// the prefix block and IV to be random; fixing them is for test data only.
/// </para>
/// <para>
/// Beside the data, <c>TEST-ONLY/</c> holds what a test system needs:
/// the Scheme Administrator certificate (PEM), the hardware ID, the user
/// permit, the test manufacturer's ID and key, and the expected outcome for
/// each dataset. None of it is real.
/// </para>
/// </remarks>
public static class ProtectedScenarioGenerator
{
    /// <summary>The folder, under the exchange set root, holding the TEST ONLY scheme files.</summary>
    public const string TestOnlyFolder = "TEST-ONLY";

    /// <summary>Writes the protected scenario into <paramref name="directory"/>.</summary>
    /// <param name="definition">The scenario.</param>
    /// <param name="protection">How to protect it.</param>
    /// <param name="directory">The exchange set's root folder.</param>
    /// <returns>What was written; dispose it to release the scheme's keys.</returns>
    public static ProtectedScenario Generate(ScenarioDefinition definition, ProtectionSpec protection, string directory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(protection);

        var plain = ScenarioGenerator.Generate(definition with { Name = protection.Name }, directory);
        var scheme = new SyntheticScheme(definition.World.Seed);
        try
        {
            return Protect(plain, protection, scheme, directory);
        }
        catch
        {
            scheme.Dispose();
            throw;
        }
    }

    private static ProtectedScenario Protect(GeneratedScenario plain, ProtectionSpec protection, SyntheticScheme scheme, string directory)
    {
        var bySpec = protection.Datasets.ToDictionary(d => d.DatasetName, StringComparer.Ordinal);
        foreach (var name in bySpec.Keys.Where(n => !plain.Datasets.Any(d => DatasetName(d) == n)))
        {
            throw new ArgumentException($"The scenario has no dataset '{name}'.", nameof(protection));
        }

        var definition = plain.Definition;
        var issueDate = DateOnly.FromDateTime(definition.Issued);
        var datasets = new List<GeneratedDataset>();
        var entries = new List<CatalogueDataset>();
        var permits = new Dictionary<string, List<DataPermit>>(StringComparer.Ordinal);
        var outcomes = new StringBuilder();

        using var signer = scheme.CreateSigner(scheme.DataServer);
        for (var i = 0; i < plain.Datasets.Count; i++)
        {
            var dataset = plain.Datasets[i];
            var entry = plain.CatalogueEntries[i];
            var name = DatasetName(dataset);
            if (!bySpec.TryGetValue(name, out var spec))
            {
                datasets.Add(dataset);
                entries.Add(entry);
                outcomes.Append(CultureInfo.InvariantCulture, $"{entry.FileName}\tNotProtected (unsigned)\n");
                continue;
            }

            var plaintext = File.ReadAllBytes(dataset.FullPath);
            var signature = signer.SignData(plaintext, $"sig-{name}");
            if (spec.OmitSignatureDataStatus)
            {
                signature = new DigitalSignatureValue
                {
                    Kind = signature.Kind,
                    Id = signature.Id,
                    CertificateRef = signature.CertificateRef,
                    Value = signature.Value,
                    DataStatus = null,
                };
            }

            var written = dataset;
            var updateNumber = 0;
            if (spec.AsUpdateWithoutBase)
            {
                if (dataset.ProductIdentifier != "S-101")
                {
                    throw new ArgumentException($"Only S-101 datasets can be written as updates; '{name}' is {dataset.ProductIdentifier}.", nameof(protection));
                }

                var relative = Path.ChangeExtension(dataset.RelativePath, ".001");
                var full = Path.ChangeExtension(dataset.FullPath, ".001");
                File.Move(dataset.FullPath, full);
                written = dataset with { RelativePath = relative, FullPath = full };
                updateNumber = 1;
            }

            if (spec.Encrypt)
            {
                var cellKey = scheme.Derive16("cell-key:" + name);
                File.WriteAllBytes(
                    written.FullPath,
                    S100Cipher.EncryptDataset(plaintext, cellKey, scheme.Derive16("prefix:" + name), scheme.Derive16("iv:" + name)));
                if (Permit(spec.Permit, name, cellKey, scheme, issueDate) is { } permit)
                {
                    if (!permits.TryGetValue(dataset.ProductIdentifier, out var list))
                    {
                        permits[dataset.ProductIdentifier] = list = [];
                    }

                    list.Add(permit);
                }
            }

            datasets.Add(written);
            entries.Add(entry with
            {
                FileName = written.RelativePath,
                UpdateNumber = updateNumber,
                DataProtection = spec.Encrypt,
                Signatures = [signature],
            });
            outcomes.Append(CultureInfo.InvariantCulture, $"{written.RelativePath}\t{Expected(spec)}\n");
        }

        var seed = definition.World.Seed.ToString(CultureInfo.InvariantCulture);
        File.WriteAllBytes(
            Path.Combine(directory, "CATALOG.XML"),
            ExchangeCatalogueBuilder.Build(
                $"SYNTHETIC {protection.Name} seed {seed}",
                definition.Issued,
                $"SYNTHETIC PROTECTED test data: scenario '{protection.Name}', seed {seed}, in {definition.World.Name}, a fictitious area. "
                    + "Keys and certificates are TEST ONLY and not real. Not for navigation.",
                entries,
                signer.CreateCertificateBlock()));

        var testOnly = Path.Combine(directory, TestOnlyFolder);
        var permitDirectory = protection.PermitOnly ? Path.Combine(testOnly, "separate-permit") : directory;
        Directory.CreateDirectory(permitDirectory);
        WritePermit(protection.PermitSigning, scheme, permitDirectory, issueDate, permits);
        WriteTestOnlyFiles(testOnly, scheme, protection, outcomes.ToString());

        var scenario = new GeneratedScenario(definition with { Name = protection.Name }, directory, datasets) { CatalogueEntries = entries };
        return new ProtectedScenario(scenario, protection, scheme, permitDirectory);
    }

    private static DataPermit? Permit(PermitKind kind, string name, byte[] cellKey, SyntheticScheme scheme, DateOnly issueDate)
    {
        var farFuture = new DateOnly(2099, 12, 31);
        return kind switch
        {
            PermitKind.Valid => DataPermit.Create(name, cellKey, scheme.HardwareId, farFuture, editionNumber: 1),
            PermitKind.Omitted => null,
            PermitKind.WrongEdition => DataPermit.Create(name, cellKey, scheme.HardwareId, farFuture, editionNumber: 2),
            PermitKind.WrongIssueDate => DataPermit.Create(name, cellKey, scheme.HardwareId, farFuture, issueDate: issueDate.AddDays(1)),
            PermitKind.ExpiredBeforeIssue => DataPermit.Create(name, cellKey, scheme.HardwareId, issueDate.AddDays(-1), editionNumber: 1),
            PermitKind.ExpiringSoon => DataPermit.Create(name, cellKey, scheme.HardwareId, issueDate.AddDays(30), editionNumber: 1),
            PermitKind.WrongHardwareId => DataPermit.Create(name, cellKey, scheme.OtherHardwareId, farFuture, editionNumber: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static void WritePermit(PermitSigning signing, SyntheticScheme scheme, string directory, DateOnly issueDate, Dictionary<string, List<DataPermit>> permits)
    {
        var permitFile = PermitFile.Create(
        [
            new PermitGroup(
                new PermitHeader
                {
                    IssueDate = issueDate,
                    DataServerName = "ZZ00 TEST ONLY data server (fictitious)",
                    DataServerIdentifier = "ZZ",
                    Version = "1.0.0",
                    UserPermit = scheme.UserPermit.ToString(),
                },
                permits
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => (IReadOnlyList<DataPermit>)p.Value, StringComparer.Ordinal)),
        ]);

        var permitPath = Path.Combine(directory, PermitFileWriter.PermitFileName);
        var signaturePath = Path.Combine(directory, PermitFileWriter.SignatureFileName);
        switch (signing)
        {
            case PermitSigning.Unsigned:
                using (var xml = File.Create(permitPath))
                {
                    PermitFileWriter.Write(permitFile, xml);
                }

                break;

            case PermitSigning.Tampered:
                using (var xml = File.Create(permitPath))
                {
                    PermitFileWriter.Write(permitFile, xml);
                }

                // A valid signature by the trusted data server, over other bytes.
                using (var signer = scheme.CreateSigner(scheme.DataServer))
                using (var sign = File.Create(signaturePath))
                {
                    var tampered = File.ReadAllBytes(permitPath).Append((byte)'\n').ToArray();
                    StandaloneDigitalSignatureWriter.Write(signer.SignStandalone(tampered, PermitFileWriter.PermitFileName), sign);
                }

                break;

            default:
                var certificate = signing switch
                {
                    PermitSigning.UntrustedSchemeAdministrator => scheme.DataServerFromOtherScheme,
                    PermitSigning.ExpiredCertificate => scheme.ExpiredDataServer,
                    _ => scheme.DataServer,
                };
                using (var signer = scheme.CreateSigner(certificate))
                using (var xml = File.Create(permitPath))
                using (var sign = File.Create(signaturePath))
                {
                    PermitFileWriter.WriteSigned(permitFile, signer, xml, sign);
                }

                break;
        }
    }

    private static void WriteTestOnlyFiles(string folder, SyntheticScheme scheme, ProtectionSpec protection, string outcomes)
    {
        Directory.CreateDirectory(folder);
        const string warning = "TEST ONLY - NOT REAL. Synthetic Part 15 scheme for generated test data; the keys are derived from public inputs.\n";
        Write(folder, "scheme-administrator.crt", scheme.SchemeAdministrator.ExportCertificatePem() + "\n");
        Write(folder, "hardware-id.txt", scheme.HardwareId + "\n");
        Write(folder, "user-permit.txt", scheme.UserPermit + "\n");
        Write(folder, "manufacturer.txt", $"{warning}M_ID {SyntheticScheme.ManufacturerId}\nM_KEY {Convert.ToHexString(scheme.ManufacturerKey)}\n");
        Write(folder, "expected-outcomes.txt", $"{warning}Scenario {protection.Name}; PERMIT.SIGN: {Expected(protection.PermitSigning)}\n{outcomes}");
    }

    private static void Write(string folder, string name, string text) =>
        File.WriteAllBytes(Path.Combine(folder, name), new UTF8Encoding(false).GetBytes(text.ReplaceLineEndings("\n")));

    private static string Expected(DatasetProtection spec) =>
        spec.AsUpdateWithoutBase ? "BaseDatasetMissing"
        : !spec.Encrypt ? "NotProtected (signed)"
        : spec.Permit switch
        {
            PermitKind.Valid => "Allowed",
            PermitKind.Omitted => "PermitNotFound",
            PermitKind.WrongEdition => "EditionMismatch",
            PermitKind.WrongIssueDate => "IssueDateMismatch",
            PermitKind.ExpiredBeforeIssue => "IssuedAfterExpiry",
            PermitKind.ExpiringSoon => "Allowed (expires 30 days after issue)",
            PermitKind.WrongHardwareId => "Allowed, then DatasetDecryptionException (key wrapped for another hardware ID)",
            _ => spec.Permit.ToString(),
        };

    private static string Expected(PermitSigning signing) => signing switch
    {
        PermitSigning.Trusted => "Ok",
        PermitSigning.UntrustedSchemeAdministrator => "CertificateUntrusted",
        PermitSigning.ExpiredCertificate => "CertificateExpired",
        PermitSigning.Unsigned => "NotSigned",
        PermitSigning.Tampered => "SignatureInvalid",
        _ => signing.ToString(),
    };

    private static string DatasetName(GeneratedDataset dataset) => Path.GetFileNameWithoutExtension(dataset.RelativePath);
}
