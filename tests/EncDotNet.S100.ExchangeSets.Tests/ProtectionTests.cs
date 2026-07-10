using System.IO.Compression;
using System.Text;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Tests for the S-100 Part 15 confidentiality support. Where possible these
/// use the known-answer vectors published in S-100 Edition 5.2.1 Part 15 so the
/// implementation is pinned to the specification.
/// </summary>
public class ProtectionTests
{
    // S-100 Part 15 §15-7.3.1.1 / Table 15-4 worked example.
    private const string ExampleHardwareId = "40384B45B54596201114FE9904220101";
    private const string ExampleManufacturerKey = "4D5A79677065774A7343705272664F72";
    private const string ExampleEncryptedHardwareId = "AD1DAD797C966EC9F6A55B66ED982815";
    private const string ExampleManufacturerId = "859868";
    private const string ExampleUserPermit = "AD1DAD797C966EC9F6A55B66ED98281599B3C7B1859868";

    private static byte[] Hex(string s) => Convert.FromHexString(s);

    // --- S100Cipher: block wrapping -----------------------------------------

    [Fact]
    public void EncryptBlock_MatchesPart15HardwareIdExample()
    {
        byte[] encrypted = S100Cipher.EncryptBlock(Hex(ExampleHardwareId), Hex(ExampleManufacturerKey));

        Assert.Equal(ExampleEncryptedHardwareId, Convert.ToHexString(encrypted));
    }

    [Fact]
    public void DecryptBlock_IsInverseOfEncryptBlock()
    {
        byte[] recovered = S100Cipher.DecryptBlock(
            Hex(ExampleEncryptedHardwareId), Hex(ExampleManufacturerKey));

        Assert.Equal(ExampleHardwareId, Convert.ToHexString(recovered));
    }

    [Fact]
    public void EncryptBlock_MatchesFipsAes128KnownAnswer()
    {
        // S-100 Part 15 §15-6.2.5 (FIPS single-block example).
        byte[] cipher = S100Cipher.EncryptBlock(
            Hex("00112233445566778899AABBCCDDEEFF"),
            Hex("000102030405060708090A0B0C0D0E0F"));

        Assert.Equal("69C4E0D86A7B0430D8CDB78070B4C55A", Convert.ToHexString(cipher));
    }

    // --- S100Cipher: dataset (modified CBC) ---------------------------------

    [Fact]
    public void DecryptDataset_MatchesPart15ModifiedCbcExample()
    {
        // S-100 Part 15 §15-6.2.5 modified CBC example.
        byte[] ciphertext = Hex("ba45ee0602a629357ae3902c224dd9d5dd3b073b847f4d432871194397d9a603");
        byte[] cellKey = Hex("123456789ABCDEF0123456789ABCDEF0");

        byte[] plaintext = S100Cipher.DecryptDataset(ciphertext, cellKey);

        Assert.Equal("FEDCBA9876543210", Convert.ToHexString(plaintext));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1000)]
    public void EncryptDataset_RoundTrips(int length)
    {
        byte[] cellKey = Hex(ExampleManufacturerKey);
        byte[] payload = new byte[length];
        new Random(length).NextBytes(payload);

        byte[] ciphertext = S100Cipher.EncryptDataset(payload, cellKey);
        byte[] recovered = S100Cipher.DecryptDataset(ciphertext, cellKey);

        Assert.Equal(payload, recovered);
    }

    [Fact]
    public void EncryptDataset_ProducesDifferentCiphertextEachTime()
    {
        byte[] cellKey = Hex(ExampleManufacturerKey);
        byte[] payload = Encoding.ASCII.GetBytes("S-100 Part 15");

        byte[] a = S100Cipher.EncryptDataset(payload, cellKey);
        byte[] b = S100Cipher.EncryptDataset(payload, cellKey);

        Assert.NotEqual(a, b);
        Assert.Equal(payload, S100Cipher.DecryptDataset(a, cellKey));
        Assert.Equal(payload, S100Cipher.DecryptDataset(b, cellKey));
    }

    [Fact]
    public void DecryptDataset_RejectsNonBlockAlignedInput()
    {
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => S100Cipher.DecryptDataset(new byte[20], Hex(ExampleManufacturerKey)));
    }

    [Fact]
    public void EncryptBlock_RejectsWrongSizedKey()
    {
        Assert.Throws<ArgumentException>(() => S100Cipher.EncryptBlock(new byte[16], new byte[8]));
    }

    // --- UserPermit ----------------------------------------------------------

    [Fact]
    public void UserPermit_Parse_ReadsExampleAndValidatesChecksum()
    {
        UserPermit permit = UserPermit.Parse(ExampleUserPermit);

        Assert.Equal(ExampleManufacturerId, permit.ManufacturerId);
        Assert.Equal(0x99B3C7B1u, permit.Checksum);
        Assert.Equal(ExampleEncryptedHardwareId, Convert.ToHexString(permit.EncryptedHardwareId));
    }

    [Fact]
    public void UserPermit_DecryptHardwareId_RecoversHardwareId()
    {
        UserPermit permit = UserPermit.Parse(ExampleUserPermit);

        HardwareId hwId = permit.DecryptHardwareId(Hex(ExampleManufacturerKey));

        Assert.Equal(ExampleHardwareId, hwId.ToString());
    }

    [Fact]
    public void UserPermit_Create_ProducesExamplePermit()
    {
        UserPermit permit = UserPermit.Create(
            HardwareId.Parse(ExampleHardwareId),
            Hex(ExampleManufacturerKey),
            ExampleManufacturerId);

        Assert.Equal(ExampleUserPermit, permit.ToString());
    }

    [Fact]
    public void UserPermit_Parse_RejectsBadChecksum()
    {
        string tampered = "AD1DAD797C966EC9F6A55B66ED98281500000000859868";

        Assert.Throws<FormatException>(() => UserPermit.Parse(tampered));
    }

    [Fact]
    public void UserPermit_Parse_RejectsWrongLength()
    {
        Assert.Throws<FormatException>(() => UserPermit.Parse("TOOSHORT"));
    }

    // --- PermitFile ----------------------------------------------------------

    private const string ExamplePermitXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Permit xmlns="http://www.iho.int/s100/se/5.1">
            <header>
                <issueDate>2018-03-20Z</issueDate>
                <dataServerName>Primar</dataServerName>
                <dataServerIdentifier>PR</dataServerIdentifier>
                <version>1.0.0</version>
                <userpermit>267C3AD506E69B1ED18AA5ECC7FFDE6E7C330CE8859868</userpermit>
            </header>
            <products>
                <product id="S-101">
                    <datasetPermit>
                        <filename>101GB40079ABCDEF</filename>
                        <editionNumber>10</editionNumber>
                        <expiry>2022-12-31</expiry>
                        <encryptedKey>2E16E07E451FF1854156634DA3DD3FB8</encryptedKey>
                    </datasetPermit>
                    <datasetPermit>
                        <filename>101NO32802411223</filename>
                        <editionNumber>5</editionNumber>
                        <expiry>2022-06-10</expiry>
                        <encryptedKey>C714B5C0FBDF14BFE4B1F12E62CE5FF6</encryptedKey>
                    </datasetPermit>
                </product>
                <product id="S-102">
                    <datasetPermit>
                        <filename>102NO329048208.h5</filename>
                        <editionNumber>1</editionNumber>
                        <expiry>2022-12-31</expiry>
                        <encryptedKey>50BBC28B6793E1C3966B45FB2932E1BE</encryptedKey>
                    </datasetPermit>
                </product>
            </products>
        </Permit>
        """;

    private static PermitFile ReadExamplePermit()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ExamplePermitXml));
        return PermitFile.Read(stream);
    }

    [Fact]
    public void PermitFile_Read_ParsesHeaderAndProducts()
    {
        PermitFile permit = ReadExamplePermit();

        PermitGroup group = Assert.Single(permit.Groups);
        Assert.Equal("Primar", group.Header.DataServerName);
        Assert.Equal("PR", group.Header.DataServerIdentifier);
        Assert.Equal(new DateOnly(2018, 3, 20), group.Header.IssueDate);
        Assert.True(group.Products.ContainsKey("S-101"));
        Assert.True(group.Products.ContainsKey("S-102"));
        Assert.Equal(2, group.Products["S-101"].Count);
    }

    [Fact]
    public void PermitFile_TryGetPermit_MatchesByBaseNameIgnoringExtension()
    {
        PermitFile permit = ReadExamplePermit();

        Assert.True(permit.TryGetPermit("101GB40079ABCDEF.000", out DataPermit? found));
        Assert.NotNull(found);
        Assert.Equal(10, found!.EditionNumber);
        Assert.Equal(new DateOnly(2022, 12, 31), found.Expiry);
        Assert.Equal("2E16E07E451FF1854156634DA3DD3FB8", Convert.ToHexString(found.EncryptedKey));
    }

    [Fact]
    public void PermitFile_TryGetPermit_MatchesExplicitExtension()
    {
        PermitFile permit = ReadExamplePermit();

        Assert.True(permit.TryGetPermit("102NO329048208.h5", out DataPermit? found));
        Assert.NotNull(found);
        Assert.Equal("50BBC28B6793E1C3966B45FB2932E1BE", Convert.ToHexString(found!.EncryptedKey));
    }

    [Fact]
    public void PermitFile_TryGetPermit_ProductIdRestrictsSearch()
    {
        PermitFile permit = ReadExamplePermit();

        Assert.False(permit.TryGetPermit("102NO329048208.h5", out _, productId: "S-101"));
        Assert.True(permit.TryGetPermit("102NO329048208.h5", out _, productId: "S-102"));
    }

    [Fact]
    public void DataPermit_AppliesTo_ExtensionRules()
    {
        var noExt = new DataPermit("ABC123", new byte[16]);
        Assert.True(noExt.AppliesTo("ABC123"));
        Assert.True(noExt.AppliesTo("ABC123.000"));
        Assert.True(noExt.AppliesTo("abc123.001"));
        Assert.False(noExt.AppliesTo("ABC124.000"));

        var withExt = new DataPermit("ABC123.h5", new byte[16]);
        Assert.True(withExt.AppliesTo("ABC123.h5"));
        Assert.False(withExt.AppliesTo("ABC123.000"));
    }

    // --- End to end: PermitKeyProvider + DecryptingAssetSource ---------------

    [Fact]
    public async Task DecryptingAssetSource_DecryptsKeyedFile_AndPassesThroughOthers()
    {
        byte[] hardwareId = Hex(ExampleHardwareId);
        byte[] cellKey = Hex("000102030405060708090A0B0C0D0E0F");
        byte[] payload = Encoding.ASCII.GetBytes("This is the decrypted S-101 dataset content.");

        // A permit whose encryptedKey unwraps (with the HW_ID) to our cell key.
        byte[] encryptedKey = S100Cipher.EncryptBlock(cellKey, hardwareId);
        string permitXml = BuildPermitXml("101AA00000000001", Convert.ToHexString(encryptedKey));
        PermitFile permitFile;
        using (var ps = new MemoryStream(Encoding.UTF8.GetBytes(permitXml)))
        {
            permitFile = PermitFile.Read(ps);
        }

        var keyProvider = new PermitKeyProvider(permitFile, HardwareId.FromBytes(hardwareId));

        var inner = new InMemoryAssetSource();
        inner.AddFile("S-101/101AA00000000001.000", S100Cipher.EncryptDataset(payload, cellKey));
        inner.AddFile("CATALOG.XML", Encoding.UTF8.GetBytes("<catalogue/>"));

        using var decrypting = new DecryptingAssetSource(inner, keyProvider);

        Assert.Equal(payload, await ReadAll(decrypting, "S-101/101AA00000000001.000"));
        Assert.Equal("<catalogue/>", Encoding.UTF8.GetString(await ReadAll(decrypting, "CATALOG.XML")));
    }

    [Fact]
    public async Task DecryptingAssetSource_DecompressesSingleEntryZip()
    {
        byte[] cellKey = Hex("000102030405060708090A0B0C0D0E0F");
        byte[] hardwareId = Hex(ExampleHardwareId);
        byte[] payload = Encoding.ASCII.GetBytes(new string('x', 500));

        byte[] zipped = ZipSingleEntry("101AA00000000002.000", payload);
        byte[] encrypted = S100Cipher.EncryptDataset(zipped, cellKey);

        byte[] encryptedKey = S100Cipher.EncryptBlock(cellKey, hardwareId);
        string permitXml = BuildPermitXml("101AA00000000002", Convert.ToHexString(encryptedKey));
        PermitFile permitFile;
        using (var ps = new MemoryStream(Encoding.UTF8.GetBytes(permitXml)))
        {
            permitFile = PermitFile.Read(ps);
        }

        var keyProvider = new PermitKeyProvider(permitFile, HardwareId.FromBytes(hardwareId));
        var inner = new InMemoryAssetSource();
        inner.AddFile("101AA00000000002.000", encrypted);

        using var decrypting = new DecryptingAssetSource(inner, keyProvider, decompress: true);

        Assert.Equal(payload, await ReadAll(decrypting, "101AA00000000002.000"));
    }

    private static string BuildPermitXml(string fileName, string encryptedKeyHex) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Permit xmlns="http://www.iho.int/s100/se/5.1">
            <header>
                <dataServerName>Test</dataServerName>
            </header>
            <products>
                <product id="S-101">
                    <datasetPermit>
                        <filename>{fileName}</filename>
                        <expiry>2099-12-31</expiry>
                        <encryptedKey>{encryptedKeyHex}</encryptedKey>
                    </datasetPermit>
                </product>
            </products>
        </Permit>
        """;

    private static byte[] ZipSingleEntry(string entryName, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            entryStream.Write(content);
        }

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAll(DecryptingAssetSource source, string path)
    {
        await using Stream stream = await source.OpenAsync(path);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    // --- DecryptingAssetSource: disposal ownership --------------------------

    [Fact]
    public async Task DisposeAsync_DisposesInnerWhenOwned()
    {
        var inner = new TrackingAssetSource();
        var source = new DecryptingAssetSource(inner, new NoKeyProvider(), ownsInner: true);

        await source.DisposeAsync();

        Assert.True(inner.DisposedAsync);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotDisposeInnerWhenNotOwned()
    {
        var inner = new TrackingAssetSource();
        var source = new DecryptingAssetSource(inner, new NoKeyProvider(), ownsInner: false);

        await source.DisposeAsync();

        Assert.False(inner.Disposed);
        Assert.False(inner.DisposedAsync);
    }

    [Fact]
    public void Dispose_DoesNotDisposeInnerWhenNotOwned()
    {
        var inner = new TrackingAssetSource();
        var source = new DecryptingAssetSource(inner, new NoKeyProvider(), ownsInner: false);

        source.Dispose();

        Assert.False(inner.Disposed);
    }

    private sealed class NoKeyProvider : IDatasetKeyProvider
    {
        public bool TryGetCellKey(string datasetFileName, out byte[]? cellKey)
        {
            cellKey = null;
            return false;
        }
    }

    private sealed class TrackingAssetSource : EncDotNet.S100.Core.IAssetSource
    {
        public bool Disposed { get; private set; }

        public bool DisposedAsync { get; private set; }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            DisposedAsync = true;
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
