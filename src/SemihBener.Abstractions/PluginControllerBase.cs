using Microsoft.AspNetCore.Mvc;

namespace SemihBener.Abstractions;

/// <summary>
/// Plugin controller'larının tabanı. Route önekini (/api/{key}), kimlik doğrulamayı,
/// rate limit'i ve Swagger grubunu host otomatik ekler. Controller'a sadece
/// <c>[Route("auth")]</c> gibi göreli bir route yazılır.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class PluginControllerBase : ControllerBase
{
    /// <summary>JWT'deki "sub" claim'i. Sadece JWT kullanan plugin'lerde, giriş yapılmış isteklerde kullanılır.</summary>
    protected int CurrentUserId => User.GetUserId();
}

/// <summary>
/// Login, register gibi brute-force'a açık uçlar için sıkı rate limit uygular
/// (varsayılan: IP başına dakikada 5 istek).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class StrictRateLimitAttribute : Attribute
{
}
