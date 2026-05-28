using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using StoreFetcher.Data;
using StoreFetcher.Dtos;
using StoreFetcher.Models;
using StoreFetcher.Services;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("StoreFetcher")
    ?? throw new InvalidOperationException("Missing StoreFetcher connection string.");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddRazorPages();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".keys")));
builder.Services.AddDbContext<StoreFetcherDbContext>(options =>
    options.UseMySql(connectionString, new MariaDbServerVersion(new Version(10, 11, 0))));
builder.Services.AddHttpClient<OverpassClient>();
builder.Services.AddScoped<StoreImportService>();
builder.Services.AddScoped<StoreScanJob>();
builder.Services.AddSingleton<IAddressEnrichmentRunner, AddressEnrichmentRunner>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StoreFetcher API",
        Version = "v1",
        Description = DatasetMetadata.AttributionStatement,
    });
});

var hangfireEnabled = builder.Configuration.GetValue("Hangfire:Enabled", false);
if (hangfireEnabled)
{
    builder.Services.AddHangfire(configuration => configuration.UseStorage(
        new MySqlStorage(
            connectionString,
            new MySqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(15),
            })));
    builder.Services.AddScoped<IStoreScanQueue, HangfireStoreScanQueue>();
    builder.Services.AddScoped<IStoreScanMonitor, HangfireStoreScanMonitor>();

    if (builder.Configuration.GetValue("Hangfire:StartServer", false))
    {
        builder.Services.AddHangfireServer();
    }
}
else
{
    builder.Services.AddScoped<IStoreScanQueue, DisabledStoreScanQueue>();
    builder.Services.AddScoped<IStoreScanMonitor, DisabledStoreScanMonitor>();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

if (hangfireEnabled)
{
    app.UseHangfireDashboard("/hangfire");
}

app.MapGet("/api/stores", async (
    StoreFetcherDbContext db,
    string? q,
    string? brand,
    string? sort,
    string? dir,
    int page = 1,
    int pageSize = 50) =>
{
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 250);
    sort = string.IsNullOrWhiteSpace(sort) ? "name" : sort.Trim().ToLowerInvariant();
    var descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

    var query = db.Stores
        .AsNoTracking()
        .Include(store => store.Correction)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(q))
    {
        query = query.Where(store =>
            store.Name.Contains(q) ||
            (store.Correction != null && store.Correction.Name != null && store.Correction.Name.Contains(q)) ||
            (store.City != null && store.City.Contains(q)) ||
            (store.Correction != null && store.Correction.City != null && store.Correction.City.Contains(q)) ||
            (store.Street != null && store.Street.Contains(q)) ||
            (store.Correction != null && store.Correction.Street != null && store.Correction.Street.Contains(q)) ||
            (store.Brand != null && store.Brand.Contains(q)) ||
            (store.Correction != null && store.Correction.Brand != null && store.Correction.Brand.Contains(q)));
    }

    if (!string.IsNullOrWhiteSpace(brand))
    {
        query = query.Where(store =>
            (store.Brand != null && store.Brand.Contains(brand)) ||
            (store.Correction != null && store.Correction.Brand != null && store.Correction.Brand.Contains(brand)));
    }

    query = ApplyStoreSort(query, sort, descending);

    var total = await query.CountAsync();
    var storeRows = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();
    var stores = storeRows.Select(StoreResponse.FromStore).ToList();

    return Results.Ok(new PagedStoreResponse(stores, page, pageSize, total));
})
.WithName("SearchStores")
;

app.MapGet("/api/stores/{id:int}", async (StoreFetcherDbContext db, int id) =>
{
    var store = await db.Stores
        .AsNoTracking()
        .Include(store => store.Correction)
        .FirstOrDefaultAsync(store => store.Id == id);
    return store is null ? Results.NotFound() : Results.Ok(StoreResponse.FromStore(store));
})
.WithName("GetStore")
;

app.MapGet("/api/dataset", async (StoreFetcherDbContext db) =>
{
    var storeCount = await db.Stores.AsNoTracking().CountAsync();
    var generatedAt = await db.Stores
        .AsNoTracking()
        .Select(store => (DateTimeOffset?)store.LastSeenAt)
        .MaxAsync();

    return Results.Ok(new DatasetMetadataResponse(
        DatasetMetadata.Name,
        DatasetMetadata.Source,
        DatasetMetadata.Attribution,
        DatasetMetadata.License,
        DatasetMetadata.LicenseUrl,
        DatasetMetadata.AttributionStatement,
        generatedAt,
        storeCount));
})
.WithName("GetDatasetMetadata")
;

app.MapGet("/api/admin/address-enrichment/status", (
    IAddressEnrichmentRunner runner,
    IWebHostEnvironment environment) =>
{
    var status = runner.GetStatus();
    var outputPath = Path.Combine(environment.ContentRootPath, "data", "address-enriched-import.json");
    var reviewPath = Path.Combine(environment.ContentRootPath, "data", "address-review.json");

    return Results.Ok(new
    {
        status.Running,
        status.ProcessId,
        status.StartedAt,
        status.FinishedAt,
        status.ExitCode,
        status.Message,
        enrichedImport = ReadAddressSummary(outputPath, "stores"),
        review = ReadAddressSummary(reviewPath, "reviews"),
    });
})
.WithName("GetAddressEnrichmentStatus")
;

app.MapPut("/api/stores/{id:int}", async (
    StoreFetcherDbContext db,
    int id,
    UpdateStoreRequest request) =>
{
    var store = await db.Stores
        .Include(store => store.Correction)
        .FirstOrDefaultAsync(store => store.Id == id);
    if (store is null)
    {
        return Results.NotFound();
    }

    store.Correction ??= new StoreCorrection { StoreId = store.Id };
    request.ApplyTo(store.Correction);
    await db.SaveChangesAsync();

    return Results.Ok(StoreResponse.FromStore(store));
})
.WithName("UpdateStore")
;

app.MapPost("/api/admin/import/stores", async (
    HttpRequest request,
    IConfiguration configuration,
    StoreImportService importer,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var configuredKey = configuration["StoreImport:AdminKey"];
    if (string.IsNullOrWhiteSpace(configuredKey))
    {
        return Results.Problem(
            "Store import is disabled. Configure StoreImport:AdminKey before using this endpoint.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var providedKey = request.Headers["X-StoreFetcher-Import-Key"].ToString();
    if (string.IsNullOrWhiteSpace(providedKey) || !SecretEquals(configuredKey, providedKey))
    {
        return Results.Unauthorized();
    }

    var contentType = request.ContentType ?? "";
    if (!contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) &&
        !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            error = "Send multipart/form-data with a file field named 'file', or send raw application/json.",
        });
    }

    try
    {
        PreparedStoreImportResult result;

        if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "Missing import file. Use a file field named 'file'." });
            }

            await using var stream = file.OpenReadStream();
            result = await importer.ImportPreparedJsonAsync(stream, cancellationToken);
        }
        else
        {
            result = await importer.ImportPreparedJsonAsync(request.Body, cancellationToken);
        }

        return Results.Ok(result);
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Prepared store import failed because the JSON was invalid.");
        return Results.BadRequest(new { error = "Import file is not valid JSON." });
    }
    catch (InvalidDataException ex)
    {
        logger.LogWarning(ex, "Prepared store import failed validation.");
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("ImportPreparedStores")
.Accepts<IFormFile>("multipart/form-data")
.Accepts<PreparedStoreDataset>("application/json")
.DisableAntiforgery()
;

app.MapPost("/api/scan-jobs/osm-sweden", (
    IStoreScanQueue queue,
    int limit = 1000) =>
{
    limit = Math.Clamp(limit, 1, 50000);
    var result = queue.EnqueueSwedenOsmScan(limit);
    return result.Enabled
        ? Results.Accepted($"/hangfire/jobs/details/{result.JobId}", result)
        : Results.Problem(result.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("EnqueueSwedenOsmScan")
;

app.MapPost("/api/scan-jobs/osm-place", (
    IStoreScanQueue queue,
    string place,
    int limit = 1000) =>
{
    place = place.Trim();
    if (string.IsNullOrWhiteSpace(place))
    {
        return Results.BadRequest(new { error = "Place is required." });
    }

    if (place.Length > 160)
    {
        return Results.BadRequest(new { error = "Place must be 160 characters or fewer." });
    }

    limit = Math.Clamp(limit, 1, 50000);
    var result = queue.EnqueuePlaceOsmScan(place, limit);
    return result.Enabled
        ? Results.Accepted($"/hangfire/jobs/details/{result.JobId}", result)
        : Results.Problem(result.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("EnqueuePlaceOsmScan")
;

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static bool SecretEquals(string expected, string provided)
{
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var providedBytes = Encoding.UTF8.GetBytes(provided);
    return expectedBytes.Length == providedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}

static object ReadAddressSummary(string path, string arrayName)
{
    if (!File.Exists(path))
    {
        return new
        {
            exists = false,
            count = (int?)null,
            generatedAt = (DateTimeOffset?)null,
            lastWriteTime = (DateTimeOffset?)null,
        };
    }

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var count = root.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("count", out var countElement) &&
            countElement.TryGetInt32(out var metadataCount)
                ? metadataCount
                : CountJsonArray(root, arrayName);
        var generatedAt = root.TryGetProperty("metadata", out metadata) &&
            metadata.TryGetProperty("generated_at", out var generatedAtElement) &&
            generatedAtElement.TryGetDateTimeOffset(out var parsedGeneratedAt)
                ? parsedGeneratedAt
                : (DateTimeOffset?)null;

        return new
        {
            exists = true,
            count,
            generatedAt,
            lastWriteTime = new DateTimeOffset(File.GetLastWriteTime(path)),
        };
    }
    catch (JsonException)
    {
        return new
        {
            exists = true,
            count = (int?)null,
            generatedAt = (DateTimeOffset?)null,
            lastWriteTime = new DateTimeOffset(File.GetLastWriteTime(path)),
        };
    }
}

static int? CountJsonArray(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array
        ? array.GetArrayLength()
        : null;

static IQueryable<Store> ApplyStoreSort(
    IQueryable<Store> query,
    string sort,
    bool descending) =>
    sort switch
    {
        "brand" => descending
            ? query.OrderByDescending(store => store.Correction != null && store.Correction.Brand != null
                    ? store.Correction.Brand
                    : store.Brand)
                .ThenBy(store => store.Correction != null && store.Correction.Name != null
                    ? store.Correction.Name
                    : store.Name)
            : query.OrderBy(store => store.Correction != null && store.Correction.Brand != null
                    ? store.Correction.Brand
                    : store.Brand)
                .ThenBy(store => store.Correction != null && store.Correction.Name != null
                    ? store.Correction.Name
                    : store.Name),
        "address" => descending
            ? query.OrderByDescending(store => store.Correction != null && store.Correction.City != null
                    ? store.Correction.City
                    : store.City)
                .ThenByDescending(store => store.Correction != null && store.Correction.Street != null
                    ? store.Correction.Street
                    : store.Street)
                .ThenBy(store => store.Id)
            : query.OrderBy(store => store.Correction != null && store.Correction.City != null
                    ? store.Correction.City
                    : store.City)
                .ThenBy(store => store.Correction != null && store.Correction.Street != null
                    ? store.Correction.Street
                    : store.Street)
                .ThenBy(store => store.Id),
        "coordinates" => descending
            ? query.OrderByDescending(store => store.Correction != null && store.Correction.Latitude != null
                    ? store.Correction.Latitude
                    : store.Latitude)
                .ThenByDescending(store => store.Correction != null && store.Correction.Longitude != null
                    ? store.Correction.Longitude
                    : store.Longitude)
                .ThenBy(store => store.Id)
            : query.OrderBy(store => store.Correction != null && store.Correction.Latitude != null
                    ? store.Correction.Latitude
                    : store.Latitude)
                .ThenBy(store => store.Correction != null && store.Correction.Longitude != null
                    ? store.Correction.Longitude
                    : store.Longitude)
                .ThenBy(store => store.Id),
        "correction" => descending
            ? query.OrderByDescending(store => store.Correction != null).ThenBy(store => store.Id)
            : query.OrderBy(store => store.Correction != null).ThenBy(store => store.Id),
        "updated" => descending
            ? query.OrderByDescending(store => store.Correction != null
                    ? store.Correction.UpdatedAt
                    : store.UpdatedAt)
                .ThenBy(store => store.Id)
            : query.OrderBy(store => store.Correction != null
                    ? store.Correction.UpdatedAt
                    : store.UpdatedAt)
                .ThenBy(store => store.Id),
        _ => descending
            ? query.OrderByDescending(store => store.Correction != null && store.Correction.Name != null
                    ? store.Correction.Name
                    : store.Name)
                .ThenBy(store => store.Id)
            : query.OrderBy(store => store.Correction != null && store.Correction.Name != null
                    ? store.Correction.Name
                    : store.Name)
                .ThenBy(store => store.Id),
    };
