using Microsoft.AspNetCore.Components.Authorization;
using turning.Web.Auth;
using turning.Web.Components;
using turning.Web.ExperimentSessions;
using turning.Web.Surveys;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAuthorizationCore();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "BlazorServer";
    options.DefaultAuthenticateScheme = "BlazorServer";
    options.DefaultChallengeScheme = "BlazorServer";
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BlazorServerAuthHandler>("BlazorServer", _ => { });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<BrowserTokenStore>();
builder.Services.AddScoped<TokenAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(static serviceProvider => serviceProvider.GetRequiredService<TokenAuthenticationStateProvider>());
builder.Services.AddHttpClient<AuthApiClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var configuredBaseUrl = EnsureTrailingSlash(configuration["Api:BaseUrl"] ?? "https://localhost:5001");
    client.BaseAddress = new Uri(configuredBaseUrl, UriKind.Absolute);
});
builder.Services.AddHttpClient<ExperimentSessionApiClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var configuredBaseUrl = EnsureTrailingSlash(configuration["Api:BaseUrl"] ?? "https://localhost:5001");
    client.BaseAddress = new Uri(configuredBaseUrl, UriKind.Absolute);
});
builder.Services.AddHttpClient<SurveyApiClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var configuredBaseUrl = EnsureTrailingSlash(configuration["Api:BaseUrl"] ?? "https://localhost:5001");
    client.BaseAddress = new Uri(configuredBaseUrl, UriKind.Absolute);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
// El shell debe cargar para que el circuito reconstruya el token del navegador.
// AuthorizeRouteView controla las paginas y la API valida sus propios permisos.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();

static string EnsureTrailingSlash(string value)
{
    return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
}
internal sealed class BlazorServerAuthHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
{
    public BlazorServerAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    // No autentica nada aquí: Blazor lo hace en el circuito
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());

    // Una recarga llega antes de que el circuito pueda leer el token del navegador.
    protected override Task HandleChallengeAsync(Microsoft.AspNetCore.Authentication.AuthenticationProperties properties)
    {
        var returnUrl = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
        Response.Redirect($"{Request.PathBase}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return Task.CompletedTask;
    }

    // No prohíbe: Blazor maneja el 403
    protected override Task HandleForbiddenAsync(Microsoft.AspNetCore.Authentication.AuthenticationProperties properties)
        => Task.CompletedTask;
}
