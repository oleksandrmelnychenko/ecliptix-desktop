namespace Ecliptix.Core.Services.External.IpGeolocation;

public sealed class CountryApiOptions
{
    public string BaseAddress { get; set; } = null!;
    public string PathTemplate { get; set; } = "/geo?ip={ip}";
    public string? ApiKeyHeaderName { get; set; }
    public string? ApiKey { get; set; }
}