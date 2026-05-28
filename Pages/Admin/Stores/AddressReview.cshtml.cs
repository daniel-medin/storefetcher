using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoreFetcher.Data;
using StoreFetcher.Models;

namespace StoreFetcher.Pages.Admin.Stores;

public sealed class AddressReviewModel(
    IWebHostEnvironment environment,
    StoreFetcherDbContext db) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const double DefaultAutoAcceptDistanceMeters = 15;

    public IReadOnlyList<AddressReviewItem> Reviews { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, "data", "address-review.json");
        if (!System.IO.File.Exists(path))
        {
            return;
        }

        await using var stream = System.IO.File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<AddressReviewFile>(
            stream,
            JsonOptions,
            cancellationToken);
        Reviews = file?.Reviews ?? [];
    }

    public async Task<IActionResult> OnPostAcceptAsync(int storeId, CancellationToken cancellationToken)
    {
        var file = await ReadReviewFileAsync(cancellationToken);
        var item = file.Reviews.FirstOrDefault(review => review.Store.Id == storeId);
        if (item?.SuggestedMatch?.Address is null)
        {
            TempData["StatusMessage"] = "No suggested address exists for that review item.";
            return RedirectToPage();
        }

        var store = await db.Stores
            .Include(candidate => candidate.Correction)
            .FirstOrDefaultAsync(candidate => candidate.Id == storeId, cancellationToken);
        if (store is null)
        {
            TempData["StatusMessage"] = "Store no longer exists.";
            return RedirectToPage();
        }

        ApplySuggestedAddress(store, item.SuggestedMatch.Address);

        await db.SaveChangesAsync(cancellationToken);
        await RemoveReviewItemAsync(file, storeId, cancellationToken);
        TempData["StatusMessage"] = $"Accepted address for {store.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int storeId, CancellationToken cancellationToken)
    {
        var file = await ReadReviewFileAsync(cancellationToken);
        await RemoveReviewItemAsync(file, storeId, cancellationToken);
        TempData["StatusMessage"] = "Rejected review item.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAutoAcceptAsync(
        double? maxDistanceMeters,
        CancellationToken cancellationToken)
    {
        var limit = maxDistanceMeters is > 0
            ? maxDistanceMeters.Value
            : DefaultAutoAcceptDistanceMeters;
        var file = await ReadReviewFileAsync(cancellationToken);
        var acceptedStoreIds = new List<int>();

        foreach (var item in file.Reviews)
        {
            if (item.SuggestedMatch?.Address is null ||
                item.SuggestedMatch.DistanceMeters > limit)
            {
                continue;
            }

            var store = await db.Stores
                .Include(candidate => candidate.Correction)
                .FirstOrDefaultAsync(candidate => candidate.Id == item.Store.Id, cancellationToken);
            if (store is null)
            {
                continue;
            }

            ApplySuggestedAddress(store, item.SuggestedMatch.Address);
            acceptedStoreIds.Add(store.Id);
        }

        if (acceptedStoreIds.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await RemoveReviewItemsAsync(file, acceptedStoreIds, cancellationToken);
        }

        TempData["StatusMessage"] =
            $"Auto-accepted {acceptedStoreIds.Count} suggested address(es) within {limit:F1} m.";
        return RedirectToPage();
    }

    public string FormatReason(string reason) =>
        reason switch
        {
            "ambiguous_nearby_addresses" => "Ambiguous nearby addresses",
            "no_candidates" => "No nearby candidates",
            "no_complete_address" => "No complete address",
            "too_far" => "Nearest candidate is too far away",
            "has_manual_correction_api_source" => "Manual correction exists",
            _ => reason.Replace('_', ' '),
        };

    public string FormatAddress(AddressCandidateAddress? address)
    {
        if (address is null)
        {
            return "Missing address";
        }

        var street = string.Join(
            " ",
            new[] { address.Street, address.HouseNumber }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new[] { street, address.Postcode, address.City }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var formatted = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(formatted) ? "Missing address" : formatted;
    }

    private async Task<AddressReviewFile> ReadReviewFileAsync(CancellationToken cancellationToken)
    {
        var path = ReviewPath;
        if (!System.IO.File.Exists(path))
        {
            return new AddressReviewFile(new AddressReviewMetadata(null, 0), []);
        }

        await using var stream = System.IO.File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AddressReviewFile>(
            stream,
            JsonOptions,
            cancellationToken) ?? new AddressReviewFile(new AddressReviewMetadata(null, 0), []);
    }

    private async Task RemoveReviewItemAsync(
        AddressReviewFile file,
        int storeId,
        CancellationToken cancellationToken)
    {
        await RemoveReviewItemsAsync(file, [storeId], cancellationToken);
    }

    private async Task RemoveReviewItemsAsync(
        AddressReviewFile file,
        IReadOnlyCollection<int> storeIds,
        CancellationToken cancellationToken)
    {
        var remaining = file.Reviews
            .Where(review => !storeIds.Contains(review.Store.Id))
            .ToList();
        var updated = file with
        {
            Metadata = (file.Metadata ?? new AddressReviewMetadata(null, 0)) with
            {
                Count = remaining.Count,
            },
            Reviews = remaining,
        };

        await using var stream = System.IO.File.Create(ReviewPath);
        await JsonSerializer.SerializeAsync(stream, updated, JsonOptions, cancellationToken);
    }

    private string ReviewPath =>
        Path.Combine(environment.ContentRootPath, "data", "address-review.json");

    private static void ApplySuggestedAddress(Store store, AddressCandidateAddress address)
    {
        store.Correction ??= new StoreCorrection { StoreId = store.Id };
        store.Correction.Name ??= store.Name;
        store.Correction.Street = Clean(address.Street);
        store.Correction.HouseNumber = Clean(address.HouseNumber);
        store.Correction.Postcode = Clean(address.Postcode);
        store.Correction.City = Clean(address.City);
        store.Correction.Country = Clean(address.Country) ?? "SE";
        store.Correction.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AddressReviewFile(
    [property: JsonPropertyName("metadata")] AddressReviewMetadata? Metadata,
    [property: JsonPropertyName("reviews")] IReadOnlyList<AddressReviewItem> Reviews);

public sealed record AddressReviewMetadata(
    [property: JsonPropertyName("generated_at")] DateTimeOffset? GeneratedAt,
    [property: JsonPropertyName("count")] int Count);

public sealed record AddressReviewItem(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("store")] AddressReviewStore Store,
    [property: JsonPropertyName("suggested_match")] AddressReviewCandidate? SuggestedMatch,
    [property: JsonPropertyName("candidates")] IReadOnlyList<AddressReviewCandidate> Candidates);

public sealed record AddressReviewStore(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("lat")] double Latitude,
    [property: JsonPropertyName("lon")] double Longitude,
    [property: JsonPropertyName("osm")] AddressReviewOsm? Osm,
    [property: JsonPropertyName("has_correction")] bool HasCorrection,
    [property: JsonPropertyName("current_address")] AddressCandidateAddress? CurrentAddress);

public sealed record AddressReviewOsm(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("url")] string? Url);

public sealed record AddressReviewCandidate(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("distance_meters")] double DistanceMeters,
    [property: JsonPropertyName("address")] AddressCandidateAddress? Address,
    [property: JsonPropertyName("lat")] double Latitude,
    [property: JsonPropertyName("lon")] double Longitude,
    [property: JsonPropertyName("raw")] AddressCandidateRaw? Raw);

public sealed record AddressCandidateAddress(
    [property: JsonPropertyName("street")] string? Street,
    [property: JsonPropertyName("house_number")] string? HouseNumber,
    [property: JsonPropertyName("postcode")] string? Postcode,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("country")] string? Country);

public sealed record AddressCandidateRaw(
    [property: JsonPropertyName("osm_type")] string? OsmType,
    [property: JsonPropertyName("osm_id")] long? OsmId,
    [property: JsonPropertyName("osm_url")] string? OsmUrl);
