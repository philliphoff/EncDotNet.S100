namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Writes Part 15 standalone digital-signature documents, the form of
/// <c>PERMIT.SIGN</c> and <c>CATALOG.SIGN</c>.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-8.11.2. The inverse of
/// <see cref="StandaloneDigitalSignatureReader"/>; create the document with
/// <see cref="Part15Signer.SignStandalone"/>.
/// </remarks>
public static class StandaloneDigitalSignatureWriter
{
    /// <summary>Writes a standalone signature document to a stream.</summary>
    /// <param name="signature">The signature document.</param>
    /// <param name="destination">The stream to write to; it is left open.</param>
    /// <exception cref="ArgumentException">
    /// The document names no Scheme Administrator or carries no certificate.
    /// </exception>
    /// <remarks>This method is synchronous.</remarks>
    public static void Write(StandaloneDigitalSignature signature, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(destination);
        var schemeAdministratorId = signature.Certificates.SchemeAdministratorId;
        if (string.IsNullOrWhiteSpace(schemeAdministratorId))
        {
            throw new ArgumentException(
                "A standalone signature must identify its Scheme Administrator (§15-8.11.1).",
                nameof(signature));
        }

        if (signature.Certificates.Certificates.Count == 0)
        {
            throw new ArgumentException(
                "A standalone signature must contain at least one certificate.", nameof(signature));
        }

        const string ns = Part15Xml.SecurityNamespace;
        using var writer = Part15Xml.CreateWriter(destination);
        writer.WriteStartDocument();
        writer.WriteStartElement("StandaloneDigitalSignature", ns);
        writer.WriteElementString("filename", ns, signature.FileName);

        writer.WriteStartElement("certificates", ns);
        writer.WriteStartElement("schemeAdministrator", ns);
        writer.WriteAttributeString("id", schemeAdministratorId);
        writer.WriteEndElement();
        foreach (var certificate in signature.Certificates.Certificates)
        {
            writer.WriteStartElement("certificate", ns);
            writer.WriteAttributeString("id", certificate.Id);
            writer.WriteAttributeString("issuer", certificate.Issuer ?? schemeAdministratorId);
            writer.WriteString(Convert.ToBase64String(certificate.Value));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        writer.WriteStartElement("digitalSignature", ns);
        writer.WriteAttributeString("id", signature.Signature.Id);
        writer.WriteAttributeString("certificateRef", signature.Signature.CertificateRef);
        writer.WriteString(Convert.ToBase64String(signature.Signature.Value));
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }
}
