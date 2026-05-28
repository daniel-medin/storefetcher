# StoreFetcher

StoreFetcher is an ASP.NET Core service for collecting Swedish grocery-store
data, storing it in MariaDB, and serving it to other apps through a small HTTP
API.

The intended production model is:

1. Fetch broad OpenStreetMap data locally.
2. Prepare and upload/import that dataset into production.
3. Use production for API reads, manual corrections, and small maintenance
   scans.

## Capabilities

- MariaDB-backed `Stores` table via EF Core.
- `StoreCorrections` table for manual overrides.
- OpenStreetMap Overpass import service.
- Hangfire job endpoint and dashboard for optional background scans.
- Targeted city or municipality scan mode.
- Swagger UI at `/swagger`.
- Admin UI at `/Admin/Stores`.
- Dataset/license metadata endpoint at `/api/dataset`.
- Admin-only prepared JSON dataset import endpoint.

## Data Model

`Store` contains imported OpenStreetMap facts:

- name, address, country
- latitude and longitude
- shop type and brand
- website, phone, opening hours
- OSM type, OSM id, and OSM URL
- created, updated, and last-seen timestamps

`StoreCorrection` contains manual overrides. API responses prefer correction
values when present, but keep the original imported facts in `Store`.

Stores are unique by `OsmType` and `OsmId`, which allows repeated imports to
update existing rows instead of creating duplicates.

## API Overview

Swagger/OpenAPI is available at:

```http
GET /swagger
```

### Search Stores

```http
GET /api/stores?q=ica&brand=ICA&page=1&pageSize=50
```

Query parameters:

- `q` searches name, city, street, and brand.
- `brand` filters by brand.
- `page` defaults to `1`.
- `pageSize` defaults to `50` and is clamped between `1` and `250`.

Response shape:

```json
{
  "stores": [],
  "page": 1,
  "pageSize": 50,
  "total": 0
}
```

### Get Store

```http
GET /api/stores/{id}
```

Returns one store with correction values applied when present.

### Update Store Correction

```http
PUT /api/stores/{id}
```

Creates or updates a `StoreCorrection` for the store. This does not overwrite
the imported OSM facts.

Request body:

```json
{
  "name": "ICA Example",
  "street": "Examplegatan",
  "houseNumber": "1",
  "postcode": "12345",
  "city": "Goteborg",
  "country": "SE",
  "latitude": 57.7,
  "longitude": 11.9,
  "shop": "supermarket",
  "brand": "ICA",
  "website": "https://example.test",
  "phone": null,
  "openingHours": null,
  "notes": "Manual correction",
  "isActive": true
}
```

### Dataset Metadata

```http
GET /api/dataset
```

Returns dataset name, source, attribution, license, generation timestamp, and
store count.

### Import Prepared Stores

```http
POST /api/admin/import/stores
X-StoreFetcher-Import-Key: local-dev-import-key
Content-Type: multipart/form-data
```

Imports a prepared JSON dataset and upserts stores by `OsmType` and `OsmId`.
Manual corrections in `StoreCorrections` are preserved.

Accepted request formats:

- `multipart/form-data` with a file field named `file`.
- raw `application/json` using the same prepared dataset shape.

Example multipart upload:

```sh
curl -X POST http://localhost:5000/api/admin/import/stores \
  -H "X-StoreFetcher-Import-Key: local-dev-import-key" \
  -F "file=@../data/grocery-stores-sample.json"
```

Example response:

```json
{
  "total": 25,
  "created": 20,
  "updated": 5,
  "skipped": 0
}
```

Production import is disabled until `StoreImport:AdminKey` is configured.

### Queue OSM Scan

```http
POST /api/scan-jobs/osm-sweden?limit=25
```

Queues a Sweden-wide Overpass scan when Hangfire is enabled. The `limit` value
is clamped between `1` and `50000`.

This endpoint is useful locally and for small maintenance jobs. Avoid using it
for large production scans; prefer importing a prepared dataset instead.

### Queue Place Scan

```http
POST /api/scan-jobs/osm-place?place=Stockholm&limit=250
```

Queues a targeted Overpass scan for one Swedish administrative place, such as a
city or municipality. The `place` value should match the OpenStreetMap
administrative area name. The `limit` value is clamped between `1` and `50000`.

The admin stores page also exposes this scan mode with a place input.

## Configuration

Update `ConnectionStrings:StoreFetcher` in `appsettings.json` or user secrets:

```json
{
  "ConnectionStrings": {
    "StoreFetcher": "Server=localhost;Port=3306;Database=storefetcher;User=storefetcher;Password=storefetcher;Allow User Variables=true;"
  },
  "Hangfire": {
    "Enabled": true,
    "StartServer": true
  },
  "StoreImport": {
    "AdminKey": "replace-with-a-secret-import-key"
  }
}
```

`appsettings.Development.json` is configured for the local Docker database on
`localhost:13306` with Hangfire enabled. Production/default settings keep
Hangfire disabled until database credentials are configured.

## Commands

```sh
docker compose up -d
dotnet restore
dotnet ef database update
dotnet run
```

Queue a small OSM scan through Swagger or:

```http
POST /api/scan-jobs/osm-sweden?limit=25
POST /api/scan-jobs/osm-place?place=Stockholm&limit=250
```

## Local-to-Production Workflow

The desired workflow is to keep expensive data collection outside production:

1. Run large Overpass fetches locally.
2. Enrich missing store addresses locally with `npm run enrich:addresses`.
3. Review `data/address-review.json` for uncertain address matches.
4. Import/upload the prepared dataset into production with
   `POST /api/admin/import/stores`.
5. Use `PUT /api/stores/{id}` or the admin UI for manual corrections.
6. Run only targeted production scans for ongoing maintenance.

The import endpoint upserts by `OsmType` and `OsmId` and preserves
`StoreCorrection` rows.

Address enrichment can read either a prepared JSON file or the running local
API:

```sh
npm run enrich:addresses --input=../data/place-grocery-stores.json
npm run enrich:addresses --api=http://localhost:5112
```

The script writes `data/address-enriched-import.json` for high-confidence
address updates and `data/address-review.json` for missing or ambiguous
matches. Pass a local Lantmateriet `Belagenhetsadress` GeoJSON or CSV export
with `--lantmateriet=data/lantmateriet-addresses.geojson` for better Swedish
address matching.

## OpenStreetMap License

StoreFetcher imports OpenStreetMap data. Any exported or published OSM-derived
dataset needs clear attribution and ODbL handling.

Use this attribution in API docs and consuming apps:

```text
Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL). (c) OpenStreetMap contributors.
```

The API exposes the current dataset metadata at:

```http
GET /api/dataset
```
