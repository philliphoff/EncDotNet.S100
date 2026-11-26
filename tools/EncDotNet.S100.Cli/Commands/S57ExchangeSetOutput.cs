using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57;
using EncDotNet.S57.ExchangeSets;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Commands;

/// <summary>
/// Writes a converted S-57 cell as an exchange set, for the commands that
/// write S-57 (<c>s101 convert</c> and <c>nis convert</c>).
/// </summary>
internal static class S57ExchangeSetOutput
{
    private const string Volume = "V01X01";

    /// <summary>
    /// Writes <c>ENC_ROOT/&lt;cell&gt;/&lt;cell&gt;.000</c> and
    /// <c>ENC_ROOT/CATALOG.031</c> under <paramref name="root"/>. The catalogue
    /// lists itself and the cell, with the cell's extent and CRC (S-57 Part 3
    /// §7.8.1; the ENC product specification §5.6).
    /// </summary>
    public static (string CellPath, string CatalogPath) Write(
        string root, string cellName, byte[] cell, S57Document document)
    {
        var encRoot = Path.Combine(root, "ENC_ROOT");
        var cellDir = Path.Combine(encRoot, cellName);
        Directory.CreateDirectory(cellDir);

        var cellFile = cellName + ".000";
        var cellPath = Path.Combine(cellDir, cellFile);
        File.WriteAllBytes(cellPath, cell);

        var extent = S57Dataset.ReadMetadata(document).Extent;
        var catalog = new S57Catalog
        {
            Entries =
            [
                new S57CatalogEntry
                {
                    RecordName = "CD",
                    RecordId = 1,
                    FileName = "CATALOG.031",
                    Volume = Volume,
                    Implementation = "ASC",
                },
                new S57CatalogEntry
                {
                    RecordName = "CD",
                    RecordId = 2,
                    // CATD file names use '\' whatever the host (S-57 Part 3 §7.8.1).
                    FileName = $"{cellName}\\{cellFile}",
                    Volume = Volume,
                    Implementation = "BIN",
                    SouthernmostLatitude = extent?.SouthLatitude,
                    WesternmostLongitude = extent?.WestLongitude,
                    NorthernmostLatitude = extent?.NorthLatitude,
                    EasternmostLongitude = extent?.EastLongitude,
                    CrcChecksum = S57CatalogWriter.ComputeCrc(cell),
                },
            ],
        };

        var catalogPath = Path.Combine(encRoot, "CATALOG.031");
        S57CatalogWriter.WriteToFile(catalogPath, catalog);
        return (cellPath, catalogPath);
    }

    /// <summary>Warns when a cell name isn't an 8-character S-57 one.</summary>
    public static void WarnOnCellName(string cellName)
    {
        if (cellName.Length == 8 && cellName.All(char.IsAsciiLetterOrDigit))
            return;

        AnsiConsole.MarkupLineInterpolated(
            $"[yellow]Warning:[/] '{cellName}' is not an 8-character S-57 cell name (such as XX5CELL1); some S-57 tools may reject it. Pass --cell-name to choose one.");
    }
}
