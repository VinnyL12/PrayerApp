using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PrayerApp.Api.Acknowledgments;
using PrayerApp.Api.Admin;
using PrayerApp.Api.Auth;
using PrayerApp.Api.Communities;
using PrayerApp.Api.Data;
using PrayerApp.Api.LiveUpdates;
using PrayerApp.Api.Moderation;
using PrayerApp.Api.Notifications;
using PrayerApp.Api.Posts;
using PrayerApp.Api.Profile;
using PrayerApp.Domain.Entities;
using PrayerApp.Domain.Ranking;
using PrayerApp.Shared.Contracts.Notifications;

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

builder.Services.AddOptions<RankingOptions>().BindConfiguration("Ranking");
builder.Services.AddOptions<ModerationOptions>().BindConfiguration("Moderation");

// Notification pipeline: one shared queue, drained by a hosted background service that
// starts and stops with the app.
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddHostedService<NotificationProcessor>();

// Live updates: a SignalR hub that pushes "refetch" hints to signed-in browsers, replacing
// the bell's polling. In-memory, so this works for one API instance; several instances
// would need a backplane (Redis or Azure SignalR) to reach users connected elsewhere.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();
builder.Services.AddSingleton<LiveUpdatePublisher>();

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

        // Browsers can't set an Authorization header on a WebSocket, so the SignalR client
        // sends the token as ?access_token=. Accept it there and nowhere else: a token in a
        // URL can end up in logs and history, so ordinary endpoints keep requiring the header.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].ToString();
                if (token.Length > 0 && context.Request.Path.StartsWithSegments(LiveUpdateEvents.HubPath))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });

// Secure by default: every endpoint requires a signed-in user unless it explicitly opts
// out with AllowAnonymous(). Forgetting an attribute fails closed, not open.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    // Admins only, checked against the database on each request (see AdminAuthorizationHandler).
    .AddPolicy(Policies.Admin, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));

builder.Services.AddOptions<AdminOptions>().BindConfiguration(AdminOptions.SectionName);
builder.Services.AddScoped<AdminAccess>();
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
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

// Local development conveniences: apply pending migrations on startup, and optionally
// load demo data so a fresh database opens onto a populated app.
if (app.Environment.IsDevelopment())
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    if (app.Configuration.GetValue<bool>("Demo:SeedOnStartup"))
    {
        await DemoDataSeeder.SeedAsync(app.Services,
            app.Configuration["Demo:Password"] ?? throw new InvalidOperationException("Demo:Password is not set."),
            // Your own registered accounts, from user secrets so real emails stay out of the repo.
            app.Configuration.GetSection("Demo:PersonalAccounts").Get<string[]>());
    }
}

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
app.MapPostImageEndpoints();
app.MapPostUpdateEndpoints();
app.MapAcknowledgmentEndpoints();
app.MapNotificationEndpoints();
app.MapModerationEndpoints();
app.MapAdminEndpoints();
app.MapProfileEndpoints();

// Behind the fallback policy like everything else. A JWT is only checked when a connection
// opens, so without CloseOnAuthenticationExpiration a socket would outlive its 15-minute
// token indefinitely; with it, the server drops the connection at expiry and the client
// reconnects with a fresh token (re-checking admin status on the way in).
app.MapHub<LiveUpdatesHub>(LiveUpdateEvents.HubPath, options => options.CloseOnAuthenticationExpiration = true);

app.Run();
