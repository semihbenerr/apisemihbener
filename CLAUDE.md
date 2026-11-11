# CLAUDE.md — api.semihbener.com

Bu dosya Claude Code oturumları için proje bağlamıdır. Kullanıcıyla Türkçe konuş.

## Amaç

Natro paylaşımlı hosting (Windows/IIS, Plesk, **tek app pool**) üzerinde tek bir ASP.NET Core MVC uygulaması,
tek domain (`api.semihbener.com`) üzerinden birden fazla mobil uygulamaya bağımsız API sunar.
Host bir kez yayınlanır; her uygulamanın API'si ayrı bir **plugin** (class library) olarak
admin panelinden zip ile yüklenir ve `/api/{key}/...` altında yayına girer.

## Durum (11 Kasım 2026) — buradan devam et

- Lokal klasör: `C:\Github\Ana Projeler\apisemihbener` (GitHub: `semihbenerr/apisemihbener`, dal `main`).
- Mimari plan dokümanı: https://claude.ai/artifact/L4wMqKB6sRtoR9hcdyoNDt

### Alınan kararlar (11 Kasım 2026)

1. **Tüm uygulamalar MSSQL + EF Core 10.** Pomelo/MySQL/docker-compose kaldırıldı; tüm plugin'ler MSSQL. Lokal geliştirme `.\SQLEXPRESS` üzerinde. Not: 10 MSSQL sınırı dolarsa sonraki uygulamalar için yeniden MySQL/Pomelo (EF 9) değerlendirilir.
2. **EhliKurban plugin host'ta kalıyor.** Mevcut MSSQL veritabanını kullanır, migration kapalı. (QurbanPro.WebAPI ayrı backend'i bağımsız durumda; bu plugin ondan bağımsız çalışır.)

### Sıradaki işler

1. ~~MSSQL geçişi~~ (yapıldı). Kalan: `dotnet build SemihBener.sln` → derleme hatalarını düzelt.
2. Migration'ları üret (`tools/add-migration.ps1`), lokal test: `/admin` kurulum → bağlantılar → yeniden başlat → "Tabloları oluştur" → Swagger.
3. Natro'da kontrol edilmemiş olanlar: sunucuda **.NET 10 runtime** var mı (Plesk → semihbener.com → .NET ayarları; yoksa self-contained publish).
4. Natro'ya yayın (README bölüm 3).

## Natro hosting bilgileri

- Paket: **Sınırsız Pro Hosting** (Windows, Plesk), 2048 MB RAM, 2 çekirdek, **10 MSSQL**, sınırsız MySQL, ücretsiz SSL, **tek app pool**.
- Plesk'teki domainler: `semihbener.com` (.NET açık, ana site), `semihbener.xyz` (.NET), `ehlikurban.com`. Ayrıca `ehlikurban.xyz` kayıtlı.
- `api.semihbener.com` henüz açılmadı: Plesk → Websites & Domains → **Add Subdomain**.

## Diğer projelerden öğrenilenler

- Kullanıcının lokal SQL Server'ı: `.\SQLEXPRESS`, Windows kimlik doğrulaması (QurbanPro DB: `Data Source=.\SQLEXPRESS;Initial Catalog=QurbanPro;Integrated Security=true;TrustServerCertificate=True;`).
- **Güvenlik:** `EhliKurban/QurbanPro-Backend/QurbanPro.WebAPI/appsettings.json` içinde SMTP şifresi açık yazılı ve git'te takip ediliyor. Kullanıcıya söylendi; şifre değiştirilip ortam değişkenine taşınmalı. Değeri hiçbir yere kopyalama.

## Yapı

| Yol | İçerik |
| --- | --- |
| `src/SemihBener.Abstractions` | Host–plugin sözleşmesi: `IApiPlugin`, `PluginServiceContext` (`AddDbContext<T>()`, `AddJwtIssuer<T>()`), `PluginControllerBase`, `[StrictRateLimit]`, `JwtTokenIssuer<T>`, `PasswordHasher`, `PluginDesignTimeFactory<T>` |
| `src/SemihBener.Host` | MVC panel (`Areas/Admin`), plugin yükleyici (`Plugins/`), güvenlik (`Security/`), CORS/OpenAPI/log (`Web/`), `Program.cs` |
| `plugins/SemihBener.Plugin.*` | QuizTactics (MSSQL, JWT), BidClash (MSSQL, JWT), Football (MSSQL, API key), EhliKurban (MSSQL mevcut DB, API key, migration kapalı) |
| `tools/` | `pack-plugin.ps1` (panel için zip), `add-migration.ps1` |

## Bağlayıcı kararlar (değiştirmeden önce kullanıcıya sor)

- **.NET 10, EF Core 10.0.x.** Tek EF provider: `Microsoft.EntityFrameworkCore.SqlServer`. Sürümler `Directory.Build.props` içinde.
- **Veritabanı: tümü MSSQL.** Her plugin ayrı DB + ayrı kullanıcı. Lokal: `.\SQLEXPRESS`, Windows kimlik doğrulaması. EhliKurban mevcut DB'yi kullanır (migration kapalı).
- **Host ayarları `App_Data/host-state.json`** (JSON, veritabanı değil): host açılışı hiçbir DB'ye bağlı olmasın. Sırlar Data Protection ile şifreli, anahtarlar `App_Data/keys`.
- **Plugin yükleme:** `AssemblyLoadContext`, bellekten (stream) yükleme; host'ta bulunan assembly'ler her zaman host'tan gelir (TPA öncelikli). Bir plugin'in hatası sadece onu kapatır; yükleme sırasında süreç çökerse `loading-{key}.marker` sayesinde bir sonraki açılışta otomatik kapanır. Güvenli mod: `App_Data/SAFE_MODE`.
- **Route/auth/rate limit host tarafından zorlanır** (`PluginRouteConvention`): plugin controller'ları `PluginControllerBase`'den türer ve sadece göreli `[Route("x")]` yazar. Her plugin'in kendi auth şeması (`plugin:{key}`) ve rate limit politikası var; bir plugin'in token'ı/anahtarı diğerinde geçmez.
- **Site kapısı (`SiteGateMiddleware`):** `/api/...` dışındaki her adres (kök, panel, Swagger, OpenAPI) admin girişi ister.
- **Koruma:** genel eşzamanlılık sınırı (64 + 32 kuyruk), plugin bazlı rate limit (kullanıcı veya IP), `[StrictRateLimit]` uçlarında IP başına 5/dk, gövde 512 KB, istek 30 sn. Cloudflare desteği (`Network:*`).
- **Panel:** tek admin, kurulumda lokal değilse `Admin:SetupToken` gerekir, 5 hatalı girişte 15 dk kilit, TOTP 2FA.
- Değişiklik gerektiren ayarlar yeniden başlatmayla devreye girer (`HostRestarter` → `StopApplication`, IIS ilk istekte açar).

## Plugin yazma kuralları

- `plugin.json` zorunlu: `key` (küçük harf, URL öneki), `name`, `version`, `entryAssembly`, `database` (`mssql`/`none`), `auth` (`jwt`/`apikey`/`none`), `migrations`, `minHostVersion`.
- Controller'da mutlaka sınıf seviyesinde `[Route("...")]` olmalı (`[ApiController]` attribute routing ister).
- Login/register uçlarında `[AllowAnonymous]` + `[StrictRateLimit]`.
- Plugin kendi thread'ini/Timer'ını açmaz (tek app pool: yakalanmayan hata tüm API'leri düşürür).
- Eşzamanlılık için elle artan `Version` sütunu + `IsConcurrencyToken()` (BidClash örneği). MSSQL'de `rowversion` da kullanılabilir; mevcut kod int `Version` ile kalıyor.
- Listeler en fazla 100 kayıt döner (`Math.Clamp`).

## Komutlar

```powershell
dotnet tool install --global dotnet-ef --version 10.*
.\tools\add-migration.ps1 -Plugin SemihBener.Plugin.QuizTactics
dotnet build SemihBener.sln
dotnet run --project src/SemihBener.Host        # http://localhost:5080/admin
.\tools\pack-plugin.ps1 -Plugin SemihBener.Plugin.Football
```

## Kullanıcı tercihleri

- Kopyala-yapıştır çalışan, eksiksiz kod; yer tutucu talimat yok.
- Planlama istendiğinde kod yazma; önce plan.
