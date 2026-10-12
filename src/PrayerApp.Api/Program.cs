using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PrayerApp.Api.Acknowledgments;
using PrayerApp.Api.Auth;
using PrayerApp.Api.Communities;
using PrayerApp.Api.Data;
using PrayerApp.Api.Posts;
using PrayerApp.Domain.Entities;

var builder = WebApplication.CreateBuilder(args);

// Register services in the DI container (think: wiring up dependencies before app.listen()).
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);

// Enums travel as names ("PrayingForYou") instead of numbers. The database still stores
// ints; this only affects JSON. Integer values are rejected so clients can't send 42.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

// Locally this comes from user secrets; in a container, from the
// ConnectionStrings__Default environment variable.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' not found. See README: dotnet user-secrets set ...");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// --- Auth ---------------------------------------------------------------------------

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// AddIdentityCore (not AddIdentity) gives us UserManager, password hashing and lockout
// without Identity's cookie sign-in, since this API authenticates with JWTs.
builder.Services.AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

// Configure JWT validation from JwtOptions once DI can resolve them.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;

        // Keep claim names as they appear in the token ("sub", "name") instead of
        // remapping them to long legacy URIs.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.GetSecurityKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = "name",
            // Default tolerance is 5 minutes, which would stretch a 15-minute token to 20.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Secure by default: every endpoint requires a signed-in user unless it explicitly opts
// out with AllowAnonymous(). Forgetting an attribute fails closed, not open.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();

// The Blazor client runs on a different origin in development. AllowCredentials lets
// the browser send the refresh cookie, and requires listing exact origins.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Configure the HTTP request pipeline (think: Express middleware, in order).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapCommunityEndpoints();
app.MapPostEndpoints();
app.MapAcknowledgmentEndpoints();

app.Run();
