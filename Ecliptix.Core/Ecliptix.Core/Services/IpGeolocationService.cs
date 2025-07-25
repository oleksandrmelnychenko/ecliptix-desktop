using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services;

public record IpCountry(
    string IpAddress,
    string Country);

public static class IpGeolocationService
{
    private const string HttpClientName = "CountryApi";

    public static async Task<Option<IpCountry>> GetIpCountryAsync(IHttpClientFactory httpClientFactory, string apiUrl)
    {
        Result<string, InternalServiceApiFailure> apiResult = await FetchApiResponseAsync(httpClientFactory, apiUrl);
        if (apiResult.IsErr)
        {
            return Option<IpCountry>.None;
        }

        string json = apiResult.Unwrap();
        return ParseJson(json);
    }

    private static async Task<Result<string, InternalServiceApiFailure>> FetchApiResponseAsync(
        IHttpClientFactory httpClientFactory, string apiUrl)
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage responseMessage = await client.GetAsync(apiUrl);

            if (!responseMessage.IsSuccessStatusCode)
            {
                string errorContent = await responseMessage.Content.ReadAsStringAsync();
                return Result<string, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.ApiRequestFailed(
                        $"API request failed with status {responseMessage.StatusCode}: {errorContent}"));
            }

            string content = await responseMessage.Content.ReadAsStringAsync();
            return Result<string, InternalServiceApiFailure>.Ok(content);
        }
        catch (HttpRequestException ex)
        {
            return Result<string, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Network error occurred while fetching IP geolocation", ex));
        }
        catch (TaskCanceledException ex)
        {
            return Result<string, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Request timed out while fetching IP geolocation", ex));
        }
        catch (Exception ex)
        {
            return Result<string, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Unexpected error occurred while fetching IP geolocation",
                    ex));
        }
    }

    private static Option<IpCountry> ParseJson(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string ip = root.GetProperty("ip").GetString() ?? string.Empty;
            string country = root.GetProperty("country").GetString() ?? string.Empty;

            if (string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(country))
            {
                return Option<IpCountry>.None;
            }

            return Option<IpCountry>.Some(new IpCountry(ip, country));
        }
        catch (JsonException)
        {
            return Option<IpCountry>.None;
        }
    }
}