using System.Globalization;
using System.Xml;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Writes <c>PERMIT.XML</c> files, and signs them as <c>PERMIT.SIGN</c>: the
/// data-server side of the Part 15 permit exchange.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.4. The inverse of
/// <see cref="PermitFile.Read(Stream)"/>. Build the permit with
/// <see cref="PermitFile.Create"/> and <see cref="DataPermit.Create"/>.
/// </remarks>
public static class PermitFileWriter
{
    /// <summary>The name of the permit file in an exchange set.</summary>
    public const string PermitFileName = "PERMIT.XML";

    /// <summary>The name of the permit's signature file in an exchange set.</summary>
    public const string SignatureFileName = "PERMIT.SIGN";

    /// <summary>Writes a permit file to a stream.</summary>
    /// <param name="permit">The permit file.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <remarks>This method is synchronous.</remarks>
    public static void Write(PermitFile permit, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(destination);

        const string ns = Part15Xml.SecurityNamespace;
        using var writer = Part15Xml.CreateWriter(destination);
        writer.WriteStartDocument();
        writer.WriteStartElement("Permit", ns);
        foreach (var group in permit.Groups)
        {
            var header = group.Header;
            writer.WriteStartElement("header", ns);
            WriteOptional(writer, "issueDate", header.IssueDate is { } issueDate ? Part15Xml.FormatDate(issueDate) : null);
            WriteOptional(writer, "dataServerName", header.DataServerName);
            WriteOptional(writer, "dataServerIdentifier", header.DataServerIdentifier);
            WriteOptional(writer, "version", header.Version);
            WriteOptional(writer, "userpermit", header.UserPermit);
            writer.WriteEndElement();

            writer.WriteStartElement("products", ns);
            foreach (var (productId, permits) in group.Products)
            {
                writer.WriteStartElement("product", ns);
                writer.WriteAttributeString("id", productId);
                foreach (var dataPermit in permits)
                {
                    // Element order follows Part 15 Table 15-7.
                    writer.WriteStartElement("datasetPermit", ns);
                    writer.WriteElementString("filename", ns, dataPermit.FileName);
                    WriteOptional(writer, "editionNumber", dataPermit.EditionNumber?.ToString(CultureInfo.InvariantCulture));
                    WriteOptional(writer, "issueDate", dataPermit.IssueDate is { } datasetIssueDate ? Part15Xml.FormatDate(datasetIssueDate) : null);
                    writer.WriteElementString("expiry", ns, Part15Xml.FormatDate(dataPermit.Expiry));
                    writer.WriteElementString("encryptedKey", ns, Convert.ToHexString(dataPermit.EncryptedKey));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>
    /// Writes a permit file and its standalone signature, signed over the exact
    /// bytes written (§15-7.4.5).
    /// </summary>
    /// <param name="permit">The permit file.</param>
    /// <param name="signer">The data server's signer.</param>
    /// <param name="permitDestination">The stream to write <c>PERMIT.XML</c> to; it is left open.</param>
    /// <param name="signatureDestination">The stream to write <c>PERMIT.SIGN</c> to; it is left open.</param>
    /// <remarks>This method is synchronous.</remarks>
    public static void WriteSigned(
        PermitFile permit,
        Part15Signer signer,
        Stream permitDestination,
        Stream signatureDestination)
    {
        ArgumentNullException.ThrowIfNull(permit);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(permitDestination);
        ArgumentNullException.ThrowIfNull(signatureDestination);

        using var buffer = new MemoryStream();
        Write(permit, buffer);
        var permitBytes = buffer.ToArray();

        var signature = signer.SignStandalone(permitBytes, PermitFileName, "permit-signature");
        permitDestination.Write(permitBytes);
        StandaloneDigitalSignatureWriter.Write(signature, signatureDestination);
    }

    private static void WriteOptional(XmlWriter writer, string localName, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            writer.WriteElementString(localName, Part15Xml.SecurityNamespace, value);
    }
}
