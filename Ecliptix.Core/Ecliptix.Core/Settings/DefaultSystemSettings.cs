namespace Ecliptix.Core.Settings;

public class DefaultSystemSettings
{
    public required string DEFAULT_THEME { get; set; }
    public required string ENVIRONMENT { get; set; }
    public required string DATA_CENTER_CONNECTION_STRING { get; set; }
    public required string COUNTRY_CODE_API { get; set; }

    public required string DOMAIN_NAME { get; set; }

    public string? CULTURE { get; set; }

    public required string PrivacyPolicyUrl { get; set; }
    public required string TermsOfServiceUrl { get; set; }
    public required string SupportUrl { get; set; }
}
