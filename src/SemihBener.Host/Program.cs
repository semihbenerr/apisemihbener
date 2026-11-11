using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SemihBener.Abstractions;
using SemihBener.Host.Core;
using SemihBener.Host.Plugins;
using SemihBener.Host.Security;
using SemihBener.Host.Web;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------------------
// 1) Çalışma verisi, loglar, şifreleme anahtarları, ayar deposu
//    Host'un açılışı hiçbir veritabanına bağlı değil.
// ---------------------------------------------------------------------------
var paths = new HostPaths(Path.Combine(builder.Environment.ContentRootPath, config["Host:DataPath"] ?? "App_Data"));

var logStore = new InMemoryLogStore(paths.Logs);
builder.Logging.AddProvider(new PanelLoggerProvider(logStore));

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(paths.Keys))
    .SetApplicationName(HostInfo.AppName);

// Plugin'leri DI kurulmadan önce yüklediğimiz için aynı anahtar klasörünü kullanan erken bir provider.
var earlyProtection = DataProtectionProvider.Create(
    new DirectoryInfo(paths.Keys),
    options => options.SetApplicationName(HostInfo.AppName));
var secrets = new SecretProtector(earlyProtection.CreateProtector(HostInfo.SecretsPurpose));

var store = new HostStateStore(paths);

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(logStore);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(secrets);

// ---------------------------------------------------------------------------
// 2) Kimlik doğrulama: panel çerezi + her plugin için ayrı şema (loader ekler)
// ---------------------------------------------------------------------------
var authentication = builder.Services.AddAuthentication();
authentication.AddCookie(AuthSchemes.Admin, options =>
{
    options.LoginPath = "/admin/account/login";
    options.LogoutPath = "/admin/account/logout";
    options.AccessDeniedPath = "/admin/account/login";
    options.Cookie.Name = "sb_admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// ---------------------------------------------------------------------------
// 3) Plugin'leri yükle (hatalı olan kapanır, host her durumda açılır)
// ---------------------------------------------------------------------------
var catalog = PluginLoader.Load(builder, authentication, store, secrets, paths);
builder.Services.AddSingleton(catalog);

var mvc = builder.Services
    .AddControllersWithViews(options => options.Conventions.Add(new PluginRouteConvention(catalog)))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

foreach (var plugin in catalog.Active)
{
    mvc.AddApplicationPart(plugin.Assembly!);
}

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<ClientIpResolver>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<HostRestarter>();
builder.Services.AddSingleton<PluginInstaller>();
builder.Services.AddSingleton<PluginDatabaseService>();

// CORS: plugin bazında, panelden yönetilen domain listesi
builder.Services.AddCors();
builder.Services.Replace(ServiceDescriptor.Singleton<ICorsPolicyProvider, PluginCorsPolicyProvider>());

// İstek boyutu ve süre sınırları
var maxBodyBytes = config.GetValue("Protection:MaxRequestBodyKb", 512) * 1024L;
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = maxBodyBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxBodyBytes);

builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
{
    Timeout = TimeSpan.FromSeconds(config.GetValue("Protection:RequestTimeoutSeconds", 30)),
    TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable
});

// ---------------------------------------------------------------------------
// 4) Rate limit: genel eşzamanlılık sınırı + her plugin için ayrı havuz
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        return ValueTask.CompletedTask;
    };

    // App pool'u korur: aynı anda işlenen istek sayısı sınırlı, fazlası kuyruğa, kuyruk dolarsa reddedilir.
    var maxConcurrent = config.GetValue("Protection:MaxConcurrentRequests", 64);
    var queueLimit = config.GetValue("Protection:RequestQueueLimit", 32);
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = maxConcurrent,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));

    var strictPerMinute = config.GetValue("Protection:StrictRequestsPerMinute", 5);

    foreach (var plugin in catalog.Active)
    {
        var perMinute = Math.Max(1, plugin.Record.RequestsPerMinute);

        options.AddPolicy(RateLimitPolicies.Standard(plugin.Key), context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.RequestServices.GetRequiredService<ClientIpResolver>().ResolvePartition(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

        options.AddPolicy(RateLimitPolicies.Strict(plugin.Key), context =>
            RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + context.RequestServices.GetRequiredService<ClientIpResolver>().Resolve(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = strictPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    }
});

// ---------------------------------------------------------------------------
// 5) Her plugin için ayrı OpenAPI dokümanı: /openapi/{key}.json
// ---------------------------------------------------------------------------
var openApiEnabled = config.GetValue("OpenApi:Enabled", true);
if (openApiEnabled)
{
    foreach (var plugin in catalog.Active)
    {
        var current = plugin;
        builder.Services.AddOpenApi(current.Key, options =>
            options.AddDocumentTransformer((document, _, _) =>
            {
                PluginOpenApiDocument.Apply(document, current);
                return Task.CompletedTask;
            }));
    }
}

// ---------------------------------------------------------------------------
// 6) Uygulama
// ---------------------------------------------------------------------------
var app = builder.Build();

if (store.LoadWarning is not null)
{
    app.Logger.LogError("{Warning}", store.LoadWarning);
}

foreach (var message in catalog.StartupMessages)
{
    app.Logger.LogWarning("Plugin yükleme: {Message}", message);
}

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (config.GetValue("Host:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<CloudflareOnlyMiddleware>();

// /api/... dışındaki her adres önce panel girişi ister (ana sayfa, panel, Swagger dahil).
app.UseMiddleware<SiteGateMiddleware>();

app.UseRouting();
app.UseRequestTimeouts();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (openApiEnabled && catalog.Active.Any())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "api.semihbener.com";
        foreach (var plugin in catalog.Active)
        {
            options.SwaggerEndpoint($"/openapi/{plugin.Key}.json", $"{plugin.Record.Name} API");
        }
    });
}

app.MapAreaControllerRoute(
    name: "admin",
    areaName: "Admin",
    pattern: "admin/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllers();

foreach (var plugin in catalog.Active)
{
    var current = plugin;

    // Herkese açık health ucu: /api/{key}/health
    app.MapGet($"/api/{current.Key}/health", async (HttpContext context, PluginDatabaseService databases) =>
        {
            var checks = await databases.HealthAsync(current, context.RequestServices, context.RequestAborted);
            var healthy = checks.All(c => c.Ok);
            var body = new
            {
                plugin = current.Key,
                version = current.Record.Version,
                status = healthy ? "ok" : "degraded",
                databases = checks.Select(c => new { context = c.Context, ok = c.Ok }),
                timeUtc = DateTime.UtcNow
            };
            return healthy ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithGroupName(current.Key)
        .WithTags("Health")
        .RequireRateLimiting(RateLimitPolicies.Standard(current.Key));

    // Plugin'in kendi minimal API uçları
    var group = app.MapGroup($"/api/{current.Key}")
        .WithGroupName(current.Key)
        .RequireRateLimiting(RateLimitPolicies.Standard(current.Key));

    if (current.Record.AuthKind != PluginAuthKind.None)
    {
        group.RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.ForPlugin(current.Key) });
    }

    try
    {
        current.Instance!.MapEndpoints(group);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "{Plugin} endpoint'leri eklenemedi.", current.Key);
    }
}

app.MapGet("/", () => Results.Redirect("/admin"))
    .ExcludeFromDescription();

app.Run();
