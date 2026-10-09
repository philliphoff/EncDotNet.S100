using System.Globalization;
using System.Text;
using System.Xml;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>Shared settings for the Part 15 documents this library writes.</summary>
internal static class Part15Xml
{
    /// <summary>
    /// The S-100 security (S100SE) namespace for permit and standalone signature
    /// documents, as used by the Part 15 §15-7.4.6 example.
    /// </summary>
    public const string SecurityNamespace = "http://www.iho.int/s100/se/5.1";

    public static XmlWriter CreateWriter(Stream destination) =>
        XmlWriter.Create(destination, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            CloseOutput = false,
        });

    public static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
