namespace Ecliptix.Core.Settings;

public class DefaultSystemSettings
{
    public required string DefaultTheme { get; set; }
    public required string Environment { get; set; }
    public required string DataCenterConnectionString { get; set; }
    public required string CountryCodeApi { get; set; }
    public required string DomainName { get; set; }
    public string? Culture { get; set; }
    public required string PrivacyPolicyUrl { get; set; }
    public required string TermsOfServiceUrl { get; set; }
    public required string SupportUrl { get; set; }
}
