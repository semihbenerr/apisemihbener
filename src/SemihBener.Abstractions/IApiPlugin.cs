using Microsoft.AspNetCore.Routing;

namespace SemihBener.Abstractions;

/// <summary>
/// Her plugin bu arayüzü uygulayan tek bir public sınıf içerir.
/// Host plugin'i yüklerken bu sınıfı bulur, parametresiz constructor ile oluşturur ve çağırır.
/// </summary>
public interface IApiPlugin
{
    /// <summary>
    /// Plugin'in servislerini kaydeder. Veritabanı için <see cref="PluginServiceContext.AddDbContext{TContext}"/>,
    /// JWT üretimi için <see cref="PluginServiceContext.AddJwtIssuer{TMarker}"/> kullanılır.
    /// </summary>
    void ConfigureServices(PluginServiceContext context);

    /// <summary>
    /// İsteğe bağlı minimal API endpoint'leri. Gelen builder zaten /api/{key} grubudur;
    /// kimlik doğrulama ve rate limit host tarafından uygulanmıştır.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
