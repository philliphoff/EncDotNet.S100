namespace EncDotNet.S100.Viewer.Resources;

// RTZ route exchange (#961). Kept in its own part so concurrent Routes work
// doesn't collide.
internal static partial class Strings
{
    public static string Export_RtzDescription => Get(nameof(Export_RtzDescription));
    public static string Export_RtzAudience => Get(nameof(Export_RtzAudience));
    public static string Export_RtzLeavesOutFormat => Get(nameof(Export_RtzLeavesOutFormat));
    public static string Export_RtzFieldSquat => Get(nameof(Export_RtzFieldSquat));
    public static string Export_RtzFieldDescription => Get(nameof(Export_RtzFieldDescription));
    public static string Export_RtzFieldPorts => Get(nameof(Export_RtzFieldPorts));
    public static string Export_RtzFieldVessel => Get(nameof(Export_RtzFieldVessel));
    public static string Export_RtzFieldVesselNumbers => Get(nameof(Export_RtzFieldVesselNumbers));
    public static string Export_RtzFieldChannelLimits => Get(nameof(Export_RtzFieldChannelLimits));
    public static string Export_RtzFieldWaterSpeeds => Get(nameof(Export_RtzFieldWaterSpeeds));
    public static string Export_RtzFieldSafetyMargin => Get(nameof(Export_RtzFieldSafetyMargin));
    public static string Export_RtzFieldFixed => Get(nameof(Export_RtzFieldFixed));
    public static string Export_RtzFieldIdentity => Get(nameof(Export_RtzFieldIdentity));
    public static string Export_RtzNoS421Content => Get(nameof(Export_RtzNoS421Content));
    public static string Export_S421NoRtzContent => Get(nameof(Export_S421NoRtzContent));
    public static string Copy_RtzKeepsInfo => Get(nameof(Copy_RtzKeepsInfo));
    public static string Copy_RtzKeepsLegEnvelope => Get(nameof(Copy_RtzKeepsLegEnvelope));
    public static string Copy_RtzNotKeptTrim => Get(nameof(Copy_RtzNotKeptTrim));
    public static string Copy_RtzNotKeptTrimFormat => Get(nameof(Copy_RtzNotKeptTrimFormat));
    public static string Copy_RtzNotKeptLegDetails => Get(nameof(Copy_RtzNotKeptLegDetails));
    public static string Copy_RtzNotKeptLegDetailsFormat => Get(nameof(Copy_RtzNotKeptLegDetailsFormat));
    public static string Copy_RtzNotKeptScheduleDetails => Get(nameof(Copy_RtzNotKeptScheduleDetails));
    public static string Copy_RtzNotKeptVesselDetails => Get(nameof(Copy_RtzNotKeptVesselDetails));
    public static string Copy_RtzNotKeptExtensionsFormat => Get(nameof(Copy_RtzNotKeptExtensionsFormat));
    public static string CopyDialog_IdentityFromFileFormat => Get(nameof(CopyDialog_IdentityFromFileFormat));
    public static string ExportDialog_IdentityKeptFileFormat => Get(nameof(ExportDialog_IdentityKeptFileFormat));
}
