using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Reads Part 15 standalone digital-signature documents.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.11.2.</remarks>
public static class StandaloneDigitalSignatureReader
{
    /// <summary>
    /// Reads a standalone signature from a stream.
    /// </summary>
    /// <param name="stream">The standalone signature XML stream.</param>
    /// <returns>The parsed signature document.</returns>
    public static StandaloneDigitalSignature Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = XDocument.Load(stream);
        return Parse(document.Root ?? throw new XmlException("Missing standalone signature root element."));
    }

    /// <summary>
    /// Reads a standalone signature from a file.
    /// </summary>
    /// <param name="path">The path to the standalone signature file.</param>
    /// <returns>The parsed signature document.</returns>
    public static StandaloneDigitalSignature Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    private static StandaloneDigitalSignature Parse(XElement root)
    {
        if (!string.Equals(root.Name.LocalName, "StandaloneDigitalSignature", StringComparison.Ordinal))
        {
            throw new XmlException(
                $"Unexpected standalone signature root element '{root.Name.LocalName}'.");
        }

        var fileName = RequiredChildValue(root, "filename");
        var certificatesElement = RequiredChild(root, "certificates");
        var signatureElement = RequiredChild(root, "digitalSignature");
        var schemeAdministrator = RequiredChild(certificatesElement, "schemeAdministrator");
        var schemeAdministratorId = RequiredAttribute(schemeAdministrator, "id");

        var certificates = certificatesElement
            .Elements()
            .Where(element => element.Name.LocalName == "certificate")
            .Select(element => new CertificateEntry
            {
                Id = RequiredAttribute(element, "id"),
                Issuer = RequiredAttribute(element, "issuer"),
                Value = ParseBase64(element, "certificate"),
            })
            .ToList();
        if (certificates.Count == 0)
        {
            throw new XmlException("A standalone signature must contain at least one certificate.");
        }

        return new StandaloneDigitalSignature
        {
            FileName = fileName,
            Certificates = new CertificateBlock
            {
                SchemeAdministratorId = schemeAdministratorId,
                Certificates = certificates,
            },
            Signature = new DigitalSignatureValue
            {
                Id = RequiredAttribute(signatureElement, "id"),
                CertificateRef = RequiredAttribute(signatureElement, "certificateRef"),
                Value = ParseBase64(signatureElement, "digitalSignature"),
            },
        };
    }

    private static XElement RequiredChild(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName)
        ?? throw new XmlException($"Missing required '{localName}' element.");

    private static string RequiredChildValue(XElement parent, string localName)
    {
        var value = RequiredChild(parent, localName).Value.Trim();
        return value.Length > 0
            ? value
            : throw new XmlException($"The '{localName}' element is empty.");
    }

    private static string RequiredAttribute(XElement element, string localName)
    {
        var value = (string?)element.Attribute(localName);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new XmlException(
                $"The '{element.Name.LocalName}' element is missing its '{localName}' attribute.");
    }

    private static byte[] ParseBase64(XElement element, string elementName)
    {
        try
        {
            return Convert.FromBase64String(element.Value.Trim());
        }
        catch (FormatException ex)
        {
            throw new XmlException($"The '{elementName}' element is not valid base64.", ex);
        }
    }
}
