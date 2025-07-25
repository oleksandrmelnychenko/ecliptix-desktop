namespace Ecliptix.Core.Settings;

public class DefaultSystemSettings
{
    public string DefaultTheme { get; set; }
    public string Environment { get; set; } 
    public required string DataCenterConnectionString { get; set; }
    public required string CountryCodeApi { get; set; }
    
    public required string DomainName { get; set; }
    
    public string Culture { get; set; } 
}