# StoreFetcher

StoreFetcher is an ASP.NET Core service for collecting Swedish grocery-store
data and serving it to other apps from MariaDB.

## First slice

- MariaDB-backed `stores` table via EF Core
- OpenStreetMap Overpass import service
- Hangfire job endpoint and dashboard
- Swagger UI at `/swagger`
- Admin UI at `/Admin/Stores`

## Configuration

Update `ConnectionStrings:StoreFetcher` in `appsettings.json` or user secrets:

```json
{
  "ConnectionStrings": {
    "StoreFetcher": "Server=localhost;Port=3306;Database=storefetcher;User=storefetcher;Password=storefetcher;"
  },
  "Hangfire": {
    "StartServer": true
  }
}
```

`Hangfire:StartServer` is `false` by default so the app can start before a
local MariaDB instance is ready. Set it to `true` when you want jobs to run.

## Commands

```sh
dotnet restore
dotnet ef database update
dotnet run
```

Queue a small OSM scan through Swagger or:

```http
POST /api/scan-jobs/osm-sweden?limit=25
```
