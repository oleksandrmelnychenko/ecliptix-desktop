namespace Ecliptix.Core.Settings;

public class DefaultAppSettings
{
    public string DefaultTheme { get; set; }
    public string Environment { get; set; } 
    public string? DataCenterConnectionString { get; set; }
    
    public string? DomainName { get; set; }
    
    public string Culture { get; set; } 
}