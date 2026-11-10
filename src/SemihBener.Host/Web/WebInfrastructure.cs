using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.OpenApi;
using SemihBener.Abstractions;
using SemihBener.Host.Core;
using SemihBener.Host.Plugins;
using SemihBener.Host.Security;

namespace SemihBener.Host.Web;

/// <summary>
/// CORS izinleri plugin bazındadır: /api/{key}/... isteğine sadece o plugin için panelden
/// eklenen domainler erişebilir. Değişiklikler yeniden başlatma gerektirmeden uygulanır.
/// Mobil uygulamalar CORS'tan etkilenmez.
/// </summary>
public sealed class PluginCorsPolicyProvider(HostStateStore store) : ICorsPolicyProvider
{
    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var key = ExtractPluginKey(context.Request.Path);
        if (key is null)
        {
            return Task.FromResult<CorsPolicy?>(null);
        }

        var origins = store.GetCorsOrigins(key);
        if (origins.Length == 0)
        {
            return Task.FromResult<CorsPolicy?>(null);
        }

        var policy = new CorsPolicyBuilder()
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromHours(1))
            .Build();

        return Task.FromResult<CorsPolicy?>(policy);
    }

    public static string? ExtractPluginKey(PathString path)
    {
        if (!path.StartsWithSegments("/api", out var rest) || !rest.HasValue)
        {
            return null;
        }

        var value = rest.Value!.TrimStart('/');
        var slash = value.IndexOf('/');
        var key = slash < 0 ? value : value[..slash];
        return key.Length == 0 ? null : key.ToLowerInvariant();
    }
}

/// <summary>Her plugin'in OpenAPI dokümanına başlık ve doğru güvenlik şemasını ekler.</summary>
public static class PluginOpenApiDocument
{
    private const string SchemeId = "auth";

    public static void Apply(OpenApiDocument document, LoadedPlugin plugin)
    {
        document.Info = new OpenApiInfo
        {
            Title = $"{plugin.Record.Name} API",
            Version = plugin.Record.Version,
            Description = $"/api/{plugin.Key}"
        };

        IOpenApiSecurityScheme? scheme = plugin.Record.AuthKind switch
        {
            PluginAuthKind.Jwt => new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = $"{plugin.Record.Name} giriş uç noktasından alınan token."
            },
            PluginAuthKind.ApiKey => new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyAuthenticationOptions.HeaderName,
                Description = $"{plugin.Record.Name} API anahtarı (panelde görünür)."
            },
            _ => null
        };

        if (scheme is null)
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = scheme;

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = []
        });
    }
}
