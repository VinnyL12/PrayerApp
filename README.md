# Prayer Hub

A cross-faith prayer and support hub. Users join topical communities, post prayer
requests, and others respond with a small set of preset acknowledgments.

**Stack:** ASP.NET Core Web API · Blazor WebAssembly · EF Core · SQL Server · ASP.NET Core Identity + JWT

> Work in progress. Full architecture write-up to come.

## Running locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[Docker Desktop](https://www.docker.com/products/docker-desktop/)
(on Apple Silicon, enable *Use Rosetta for x86/amd64 emulation*).

1. **Start SQL Server**
   ```bash
   cp .env.example .env        # then set a strong SA_PASSWORD
   docker compose up -d
   ```

2. **Give the API its secrets** (stored outside the repo via user secrets)
   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" \
     "Server=localhost,1433;Database=PrayerApp;User Id=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True;Encrypt=True" \
     --project src/PrayerApp.Api
   dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 64)" --project src/PrayerApp.Api
   ```

3. **Create the database**
   ```bash
   dotnet tool restore
   dotnet ef database update --project src/PrayerApp.Api
   ```

4. **Run**
   ```bash
   dotnet run --project src/PrayerApp.Api --launch-profile http      # http://localhost:5160
   dotnet run --project src/PrayerApp.Client --launch-profile http   # http://localhost:5137
   ```

## Tests

```bash
dotnet test
```

## Project layout

| Project | Purpose |
|---|---|
| `src/PrayerApp.Domain` | Entities and domain logic (no EF Core) |
| `src/PrayerApp.Shared` | Enums and request/response types shared by API and client |
| `src/PrayerApp.Api` | Web API, EF Core `AppDbContext`, migrations |
| `src/PrayerApp.Client` | Blazor WebAssembly frontend |
| `tests/PrayerApp.Tests` | xUnit tests |
