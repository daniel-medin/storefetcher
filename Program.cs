using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using StoreFetcher.Data;
using StoreFetcher.Dtos;
using StoreFetcher.Models;
using StoreFetcher.Services;

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

    if (builder.Configuration.GetValue("Hangfire:StartServer", false))
    {
        builder.Services.AddHangfireServer();
    }
}
else
{
    builder.Services.AddScoped<IStoreScanQueue, DisabledStoreScanQueue>();
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
    int page = 1,
    int pageSize = 50) =>
{
    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 250);

    var query = db.Stores
        .AsNoTracking()
        .Include(store => store.Correction)
        .OrderBy(store => store.Correction != null && store.Correction.Name != null
            ? store.Correction.Name
            : store.Name)
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

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
