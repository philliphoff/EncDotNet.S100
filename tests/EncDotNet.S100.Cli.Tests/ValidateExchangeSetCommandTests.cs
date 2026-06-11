using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Cli.Infrastructure;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end tests for <c>s100 validate &lt;exchange-set&gt;</c>, which
/// integrity-verifies an exchange set (S-100 Edition 5.2.1 Part 15) rather than
/// running a single-dataset rule pack. Synthetic exchange sets are built in a
/// temp directory — no real ENC data is committed.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ValidateExchangeSetCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("s100-xs-validate-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Validate_unsigned_intact_exchange_set_returns_success()
    {
        WriteFile("data.000", "hello"u8.ToArray());
        WriteCatalogue(DatasetEntry("data.000"));

        int exit = CliApp.Build().Run(["validate", _root]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Validate_exchange_set_via_catalogue_path_returns_success()
    {
        WriteFile("data.000", "hello"u8.ToArray());
        WriteCatalogue(DatasetEntry("data.000"));

        int exit = CliApp.Build().Run(["validate", Path.Combine(_root, "CATALOG.XML")]);

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Validate_missing_file_fails_with_findings_exit_code()
    {
        // Catalogue references a file that is not present on disk.
        WriteCatalogue(DatasetEntry("missing.000"));

        var (exit, stdout) = RunCapturingStdout(["validate", _root, "--format", "json"]);

        Assert.Equal(6, exit);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("exchange-set", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.True(root.GetProperty("hasMissingFiles").GetBoolean());
        var file = root.GetProperty("files")[0];
        Assert.Equal("FileMissing", file.GetProperty("checksumOutcome").GetString());
    }

    [Fact]
    public void Validate_checksum_mismatch_fails_with_findings_exit_code()
    {
        WriteFile("data.000", "actual content"u8.ToArray());
        var wrongHash = "urn:mrn:iho:s100:hash:sha256:" + Sha256Hex("different content"u8.ToArray());
        WriteCatalogue(DatasetEntry("data.000", wrongHash));

        var (exit, stdout) = RunCapturingStdout(["validate", _root, "--format", "json"]);

        Assert.Equal(6, exit);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("hasChecksumMismatches").GetBoolean());
        var file = root.GetProperty("files")[0];
        Assert.Equal("ChecksumMismatch", file.GetProperty("checksumOutcome").GetString());
        Assert.Equal(Sha256Hex("actual content"u8.ToArray()), file.GetProperty("computedSha256").GetString());
    }

    [Fact]
    public void Validate_matching_checksum_returns_success()
    {
        var content = "verified content"u8.ToArray();
        WriteFile("data.000", content);
        WriteCatalogue(DatasetEntry("data.000", "urn:mrn:iho:s100:hash:sha256:" + Sha256Hex(content)));

        var (exit, stdout) = RunCapturingStdout(["validate", _root, "--format", "json"]);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("integrityVerified").GetBoolean());
        Assert.Equal("Ok", doc.RootElement.GetProperty("files")[0].GetProperty("checksumOutcome").GetString());
    }

    [Fact]
    public void Validate_unsigned_set_in_strict_mode_fails()
    {
        WriteFile("data.000", "hello"u8.ToArray());
        WriteCatalogue(DatasetEntry("data.000"));

        // Strict treats unsigned / no-checksum files as failures.
        int exit = CliApp.Build().Run(["validate", _root, "--strict"]);

        Assert.Equal(6, exit);
    }

    [Fact]
    public void ExchangeSetInput_does_not_flag_a_plain_dataset_file()
    {
        var dataset = Path.Combine(_root, "data.000");
        File.WriteAllText(dataset, "not a catalogue");

        Assert.False(ExchangeSetInput.LooksLikeExchangeSet(dataset));
    }

    private static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private void WriteFile(string name, byte[] content) =>
        File.WriteAllBytes(Path.Combine(_root, name), content);

    private static string DatasetEntry(string fileName, string? hashMrn = null)
    {
        var hashElement = hashMrn is null
            ? string.Empty
            : $"\n            <S100XC:resourceHash>{hashMrn}</S100XC:resourceHash>";

        return $"""
                    <S100XC:S100_DatasetDiscoveryMetadata>
                        <S100XC:fileName>{fileName}</S100XC:fileName>{hashElement}
                    </S100XC:S100_DatasetDiscoveryMetadata>
            """;
    }

    private void WriteCatalogue(string datasetEntries)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <S100XC:S100_ExchangeCatalogue xmlns:S100XC="http://www.iho.int/s100/xc/5.0">
                <S100XC:identifier>
                    <S100XC:identifier>TEST</S100XC:identifier>
                    <S100XC:dateTime>2024-01-01</S100XC:dateTime>
                </S100XC:identifier>
                <S100XC:datasetDiscoveryMetadata>
            {datasetEntries}
                </S100XC:datasetDiscoveryMetadata>
            </S100XC:S100_ExchangeCatalogue>
            """;

        File.WriteAllText(Path.Combine(_root, "CATALOG.XML"), xml, Encoding.UTF8);
    }

    private static (int Exit, string Stdout) RunCapturingStdout(string[] args)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            int exit = CliApp.Build().Run(args);
            return (exit, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
