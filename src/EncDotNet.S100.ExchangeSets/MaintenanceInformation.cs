

namespace EncDotNet.S100.ExchangeSets
{
   
    public enum MaintenanceFrequencyCode
    {
        asNeeded = 1,
        irregular = 2
    };

    public class MaintenanceInformation
    {
        public MaintenanceFrequencyCode? MaintenanceAndUpdateFrequency { get; init; }

        public DateOnly? MaintenanceDate { get; init; }
        public string? UserDefinedMaintenanceFrequency { get; init; }
    }
}
