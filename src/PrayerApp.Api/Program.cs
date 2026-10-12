using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Register services in the DI container (think: wiring up dependencies before app.listen()).
builder.Services.AddOpenApi();

// Locally this comes from user secrets; in a container, from the
// ConnectionStrings__Default environment variable.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' not found. See README: dotnet user-secrets set ...");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline (think: Express middleware, in order).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
