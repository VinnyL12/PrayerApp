using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PrayerApp.Client;
using PrayerApp.Client.Auth;
using PrayerApp.Client.Services;
using PrayerApp.Shared.Contracts.Notifications;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// From wwwroot/appsettings.json. The client and API are separate origins in development.
var apiBaseUrl = new Uri(builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl is missing from wwwroot/appsettings.json."));

builder.Services.AddSingleton(TimeProvider.System);

// --- Auth ---------------------------------------------------------------------------
// Singletons on purpose: IHttpClientFactory builds message handlers in their own DI
// scope, so a scoped TokenStore would give BearerTokenHandler a different, empty copy.
// (In WebAssembly a singleton lives as long as the browser tab.)
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ApiAuthenticationStateProvider>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// --- HTTP -----------------------------------------------------------------------------
builder.Services.AddTransient<IncludeCredentialsHandler>();
builder.Services.AddTransient<BearerTokenHandler>();

// /auth/* calls: carry the refresh cookie, never a bearer token.
builder.Services.AddHttpClient(AuthService.HttpClientName, client => client.BaseAddress = apiBaseUrl)
    .AddHttpMessageHandler<IncludeCredentialsHandler>();

// Everything else: bearer token attached automatically.
builder.Services.AddHttpClient<PrayerApiClient>(client => client.BaseAddress = apiBaseUrl)
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped<NotificationState>();

// One SignalR connection per tab, created once and shared (a singleton lives as long as the tab).
builder.Services.AddSingleton(sp => new LiveConnection(
    new Uri(apiBaseUrl, LiveUpdateEvents.HubPath),
    sp.GetRequiredService<AuthService>(),
    sp.GetRequiredService<TokenStore>(),
    sp.GetRequiredService<ILogger<LiveConnection>>()));
builder.Services.AddScoped<ModerationState>();

await builder.Build().RunAsync();
