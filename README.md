# StoreFetcher

StoreFetcher is an ASP.NET Core service for collecting Swedish grocery-store
data and serving it to other apps from MariaDB.

## First slice

- MariaDB-backed `stores` table via EF Core
- OpenStreetMap Overpass import service
- Hangfire job endpoint and dashboard
- Swagger UI at `/swagger`
- Admin UI at `/Admin/Stores`
- Dataset/license metadata endpoint at `/api/dataset`
- Manual corrections are stored separately from imported OSM facts

## Configuration

Update `ConnectionStrings:StoreFetcher` in `appsettings.json` or user secrets:

```json
{
  "ConnectionStrings": {
    "StoreFetcher": "Server=localhost;Port=3306;Database=storefetcher;User=storefetcher;Password=storefetcher;"
  },
  "Hangfire": {
    "Enabled": true,
    "StartServer": true
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
```

## OpenStreetMap License

StoreFetcher imports OpenStreetMap data. Any exported or published OSM-derived
dataset needs clear attribution and ODbL handling.

Use this attribution in API docs and consuming apps:

```text
Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL). © OpenStreetMap contributors.
```

The API exposes the current dataset metadata at:

```http
GET /api/dataset
```
