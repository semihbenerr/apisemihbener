using System.Net;
using Microsoft.AspNetCore.Authentication;
using SemihBener.Host.Core;

namespace SemihBener.Host.Security;

/// <summary>
/// İsteği atanın gerçek IP'sini bulur. Cloudflare arkasında RemoteIpAddress Cloudflare'in IP'sidir;
/// gerçek IP CF-Connecting-IP başlığındadır. Bu başlığa sadece istek gerçekten Cloudflare'den
/// geldiyse güvenilir, yoksa herkes sahte IP gönderebilir.
/// </summary>
public sealed class ClientIpResolver
{
    private readonly bool _trustCloudflare;
    private readonly System.Net.IPNetwork[] _cloudflareNetworks;

    public ClientIpResolver(IConfiguration configuration)
    {
        _trustCloudflare = configuration.GetValue("Network:TrustCloudflare", false);
        _cloudflareNetworks = (configuration.GetSection("Network:CloudflareRanges").Get<string[]>() ?? [])
            .Select(range => System.Net.IPNetwork.Parse(range))
            .ToArray();
    }

    public bool IsFromCloudflare(HttpContext context)
    {
        var remote = Normalize(context.Connection.RemoteIpAddress);
        return remote is not null && _cloudflareNetworks.Any(network => network.Contains(remote));
    }

    public string Resolve(HttpContext context)
    {
        if (_trustCloudflare
            && IsFromCloudflare(context)
            && context.Request.Headers.TryGetValue("CF-Connecting-IP", out var header)
            && IPAddress.TryParse(header.ToString(), out var forwarded))
        {
            return forwarded.ToString();
        }

        return Normalize(context.Connection.RemoteIpAddress)?.ToString() ?? "unknown";
    }

    /// <summary>Giriş yapmış isteklerde kullanıcı, diğerlerinde IP bazlı rate limit anahtarı.</summary>
    public string ResolvePartition(HttpContext context)
    {
        var sub = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst("sub")?.Value : null;
        return sub is not null ? $"user:{sub}" : $"ip:{Resolve(context)}";
    }

    private static IPAddress? Normalize(IPAddress? address) =>
        address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
}

/// <summary>
/// Network:CloudflareOnly açıkken Cloudflare'i atlayıp sunucuya doğrudan gelen istekleri reddeder.
/// Lokal istekler (geliştirme) her zaman serbesttir.
/// </summary>
public sealed class CloudflareOnlyMiddleware(RequestDelegate next, ClientIpResolver resolver, IConfiguration configuration)
{
    private readonly bool _enabled = configuration.GetValue("Network:CloudflareOnly", false);

    public Task InvokeAsync(HttpContext context)
    {
        if (_enabled
            && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None)
            && !resolver.IsFromCloudflare(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        return next(context);
    }
}

/// <summary>
/// Site kapısı: /api/... dışındaki her adres (ana sayfa, panel, Swagger, OpenAPI, bilinmeyen yollar)
/// önce panel girişi ister. Mobil uygulamalar ve web siteleri /api/... altında kendi
/// token'ları veya API anahtarlarıyla çalışmaya devam eder.
/// </summary>
public sealed class SiteGateMiddleware(RequestDelegate next)
{
    private static readonly PathString[] PublicPaths =
    [
        new("/admin/account/login"),
        new("/admin/account/setup")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/api")
            || PublicPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var result = await context.AuthenticateAsync(AuthSchemes.Admin);
        if (!result.Succeeded)
        {
            var isRoot = path.Value is null or "/";
            var returnUrl = isRoot ? "" : "?returnUrl=" + Uri.EscapeDataString(path + context.Request.QueryString);
            context.Response.Redirect("/admin/account/login" + returnUrl);
            return;
        }

        await next(context);
    }
}
