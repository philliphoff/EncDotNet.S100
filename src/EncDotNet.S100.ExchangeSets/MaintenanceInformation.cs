
namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Enum for Maintenance Frequency Code, representing the frequency of maintenance and updates for a dataset.
/// </summary>
public enum MaintenanceFrequencyCode
{
    AsNeeded = 1,
    Irregular = 2
}

/// <summary>
/// Represents maintenance information for a dataset, including maintenance frequency and dates.
/// </summary>
public sealed class MaintenanceInformation
{
    public MaintenanceFrequencyCode? MaintenanceAndUpdateFrequency { get; init; }

    public DateOnly? MaintenanceDate { get; init; }

    public string? UserDefinedMaintenanceFrequency { get; init; }
}
