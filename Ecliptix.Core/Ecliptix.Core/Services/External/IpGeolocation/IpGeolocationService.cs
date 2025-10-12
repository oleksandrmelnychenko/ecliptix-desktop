using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Core.Services.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.External.IpGeolocation;

public sealed class IpGeolocationService(HttpClient http) : IIpGeolocationService
{
    private const string BaseUrl = "https://api.country.is";

    public async Task<Result<IpCountry, InternalServiceApiFailure>> GetIpCountryAsync(
        CancellationToken ct = default)
    {

        using HttpRequestMessage req = new(HttpMethod.Get, BaseUrl);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using HttpResponseMessage res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                string error = await SafeReadAsStringAsync(res.Content, ct).ConfigureAwait(false);
                return Result<IpCountry, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.ApiRequestFailed(
                        $"Geo API failed with {(int)res.StatusCode} {res.ReasonPhrase}: {Trim(error, 1000)}"));
            }

            await using Stream stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            JsonElement root = doc.RootElement;

            string ipParsed = TryGetAnyCaseInsensitive(root, "ip", "ipAddress", "query") ?? "unknown";
            string? country =
                TryGetAnyCaseInsensitive(root, "country", "country_name", "countryName") ??
                TryGetAnyCaseInsensitive(root, "countryCode", "country_code");

            if (string.IsNullOrWhiteSpace(country))
            {
                return Result<IpCountry, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.ApiRequestFailed("Geo API returned no country."));
            }

            return Result<IpCountry, InternalServiceApiFailure>.Ok(new IpCountry(ipParsed, country));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<IpCountry, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Geo API request timed out."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result<IpCountry, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Geo API request canceled by caller."));
        }
        catch (HttpRequestException ex)
        {
            return Result<IpCountry, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Network error calling Geo API.", ex));
        }
        catch (JsonException ex)
        {
            return Result<IpCountry, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.ApiRequestFailed("Invalid JSON from Geo API.", ex));
        }
    }

    private static string? TryGetAnyCaseInsensitive(JsonElement root, params string[] names)
    {
        foreach (string n in names)
        {
            if (root.TryGetProperty(n, out JsonElement el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }

        return (from p in root.EnumerateObject()
                from n in names
                where p.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                select p.Value.GetString()).FirstOrDefault();
    }

    private static async Task<string> SafeReadAsStringAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            return await content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}