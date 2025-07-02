namespace Ecliptix.Core.Settings;

public class AppSettings
{
    public string DefaultTheme { get; set; } = "Light";
    public string Environment { get; set; } = "Development";
    public string? DataCenterConnectionString { get; set; }
    public string? DomainName { get; set; }
    
    public LocalizationSettings Localization { get; set; } = new();
}