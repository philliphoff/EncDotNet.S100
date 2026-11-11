namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>The JPEG 2000 settings of data representation template 5.40 (octets 22 and 23).</summary>
/// <param name="CompressionType">Code table 5.40: <c>0</c> lossless, <c>1</c> lossy.</param>
/// <param name="TargetCompressionRatio">The ratio the encoder aimed for, M in M:1; <c>255</c> when missing.</param>
internal sealed record Jpeg2000PackingParameters(byte CompressionType, byte TargetCompressionRatio);
