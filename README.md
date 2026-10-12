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

## Design notes

**Feed ranking** (`src/PrayerApp.Domain/Ranking/FeedRanker.cs`). Each active post is scored

```
score = (VelocityWeight × (acks in last 24h + 1) + UrgencyWeight × urgent) / (age hours + 2) ^ Gravity
```

so posts rise with recent support, urgency gives a head start that decays like everything
else, and age pulls everything down. The top page is selected with a bounded min-heap,
O(N log k), instead of sorting all N candidates. Weights are configurable under `Ranking`
in `appsettings.json`.

**Stable pagination over changing scores.** Every page of one scroll session is ranked
*as of* the instant the first page was requested (carried in the cursor), counting only
posts and acknowledgments that existed then. That ranking is reproducible from the
database, so page N+1 is "everything ranked after the last post shown": no duplicates or
gaps when scores shift mid-scroll, and no server-side session state.

**Consistency.** An acknowledgment row and the post's `AcknowledgmentCount` increment
commit in one transaction; the increment runs in SQL (`count = count + 1`) to avoid lost
updates. A unique index on (PostId, UserId) is the final guard against concurrent duplicates.

**Notification pipeline.** After an acknowledgment commits, the API enqueues an event on a
bounded in-process `Channel<T>`. A hosted `BackgroundService` drains it in batches and writes
notification rows, retrying transient failures with exponential backoff. If the queue is
full, events are dropped and logged (load shedding) rather than slowing requests down.

### Known limitations
- Queued notifications are lost if the API restarts. A durable broker or an outbox table
  would fix this; it's out of scope for a single-instance portfolio app.
- A notification batch that keeps failing is dropped after 3 attempts (logged, no dead-letter store).
- The ranked feed considers the newest 500 active posts from the last 30 days per community.
- Login responds slightly faster for unknown emails than wrong passwords (timing-based
  account enumeration); registration reveals whether an email is taken.

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
