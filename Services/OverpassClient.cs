using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoreFetcher.Services;

public sealed class OverpassClient(HttpClient httpClient, ILogger<OverpassClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<OverpassElement>> FetchSwedenGroceryStoresAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = BuildSwedenQuery(limit);
        return await FetchAsync(query, cancellationToken);
    }

    public async Task<IReadOnlyList<OverpassElement>> FetchPlaceGroceryStoresAsync(
        string place,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = BuildPlaceQuery(place, limit);
        return await FetchAsync(query, cancellationToken);
    }

    private async Task<IReadOnlyList<OverpassElement>> FetchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["data"] = query,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://overpass-api.de/api/interpreter")
        {
            Content = body,
        };
        request.Headers.UserAgent.ParseAdd("StoreFetcher/0.1");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Overpass returned {StatusCode}: {Body}", response.StatusCode, content);
            response.EnsureSuccessStatusCode();
        }

        var result = JsonSerializer.Deserialize<OverpassResponse>(content, JsonOptions);
        return result?.Elements ?? [];
    }

    private static string BuildPlaceQuery(string place, int limit)
    {
        var escapedPlace = EscapeOverpassRegex(place);

        return $"""
        [out:json][timeout:180];
        area["ISO3166-1"="SE"][admin_level=2]->.sweden;
        area(area.sweden)["boundary"="administrative"]["name"~"^{escapedPlace}$",i]->.place;
        (
          nwr["shop"~"^(supermarket|grocery|convenience)$"]["name"](area.place);
        );
        out tags center {limit};
        """;
    }

    private static string BuildSwedenQuery(int limit) =>
        $"""
        [out:json][timeout:180];
        area["ISO3166-1"="SE"][admin_level=2]->.sweden;
        (
          nwr["shop"~"^(supermarket|grocery|convenience)$"]["name"](area.sweden);
        );
        out tags center {limit};
        """;

    private static string EscapeOverpassRegex(string value)
    {
        var escaped = value
            .Replace(@"\", @"\\")
            .Replace("^", @"\^")
            .Replace("$", @"\$")
            .Replace(".", @"\.")
            .Replace("*", @"\*")
            .Replace("+", @"\+")
            .Replace("?", @"\?")
            .Replace("(", @"\(")
            .Replace(")", @"\)")
            .Replace("[", @"\[")
            .Replace("]", @"\]")
            .Replace("{", @"\{")
            .Replace("}", @"\}")
            .Replace("|", @"\|");

        return escaped.Replace("\"", "\\\"");
    }
}

public sealed record OverpassResponse(
    [property: JsonPropertyName("elements")] IReadOnlyList<OverpassElement> Elements);

public sealed record OverpassElement(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("lat")] double? Latitude,
    [property: JsonPropertyName("lon")] double? Longitude,
    [property: JsonPropertyName("center")] OverpassCenter? Center,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags);

public sealed record OverpassCenter(
    [property: JsonPropertyName("lat")] double Latitude,
    [property: JsonPropertyName("lon")] double Longitude);
