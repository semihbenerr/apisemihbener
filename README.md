# api.semihbener.com

Tek hosting, tek domain, birden fazla bağımsız API. Host bir kez yayınlanır; her mobil uygulamanın API'si ayrı bir **plugin** olarak panelden yüklenir ve `/api/{key}/...` altında yayına girer. Her plugin'in kendi veritabanı, kendi kimlik doğrulaması ve kendi rate limit'i vardır.

```
api.semihbener.com
├── /                      → giriş ekranı (/api dışındaki her adres önce şifre ister)
├── /admin                 → yönetim paneli (MVC)
├── /swagger               → her plugin'in ayrı API dokümanı
├── /api/quiztactics/...   → QuizTactics plugin'i  ── MySQL  DB_QuizTactics  (JWT)
├── /api/bidclash/...      → BidClash plugin'i     ── MySQL  DB_BidClash     (JWT)
├── /api/football/...      → Football plugin'i     ── MySQL  DB_Football     (X-Api-Key)
└── /api/ehlikurban/...    → EhliKurban plugin'i   ── MSSQL  mevcut DB       (X-Api-Key)
```

## Klasörler

| Klasör | İçerik |
| --- | --- |
| `src/SemihBener.Host` | MVC panel + API host. Natro'ya bir kez publish edilir. |
| `src/SemihBener.Abstractions` | Host ile plugin'ler arasındaki sözleşme (`IApiPlugin`, `PluginControllerBase`, JWT, şifre hash'i). |
| `plugins/*` | Her uygulamanın API'si: `Controllers/`, `Data/` (DbContext + Migrations), `Contracts/`, `plugin.json`. |
| `tools/` | `pack-plugin.ps1` (zip üretir), `add-migration.ps1` (migration üretir). |
| `docker/`, `docker-compose.yml` | Lokal MySQL 8.4 (Natro gibi her uygulamaya ayrı DB ve kullanıcı). |

Host'un ayarları (plugin listesi, şifreli bağlantılar, admin hesabı) `App_Data/host-state.json` dosyasındadır. Host'un açılışı hiçbir veritabanına bağlı değildir: MySQL kapalı olsa bile panel açılır.

---

## 1. Lokal kurulum (ilk kez)

**Gerekenler:** .NET 10 SDK, Docker Desktop, Visual Studio 2022 17.14+ veya 2026.

```powershell
# 1) MySQL'i başlat (DB_QuizTactics, DB_BidClash, DB_Football ve kullanıcıları otomatik oluşur)
docker compose up -d

# 2) EF Core aracını kur (bir kez)
dotnet tool install --global dotnet-ef --version 9.*

# 3) MySQL plugin'leri için ilk migration'ları üret
.\tools\add-migration.ps1 -Plugin SemihBener.Plugin.QuizTactics
.\tools\add-migration.ps1 -Plugin SemihBener.Plugin.BidClash
.\tools\add-migration.ps1 -Plugin SemihBener.Plugin.Football

# 4) Tüm solution'ı derle (plugin'ler de derlenmeli)
dotnet build SemihBener.sln
```

Sonra Visual Studio'da `SemihBener.Host`'u başlat (veya `dotnet run --project src/SemihBener.Host`). Tarayıcı `http://localhost:5080/admin` açılır.

1. **İlk kurulum:** admin kullanıcı adı ve şifre (en az 12 karakter). Lokalde kurulum anahtarı istenmez.
2. Geliştirme modunda 4 plugin, derleme klasörlerinden otomatik bulunur. Her birine girip bağlantıyı yaz, **Bağlantıyı test et**, **Kaydet**:

   | Plugin | Lokal bağlantı |
   | --- | --- |
   | quiztactics | `Server=localhost;Port=3306;Database=DB_QuizTactics;User=quiztactics;Password=dev_quiztactics;` |
   | bidclash | `Server=localhost;Port=3306;Database=DB_BidClash;User=bidclash;Password=dev_bidclash;` |
   | football | `Server=localhost;Port=3306;Database=DB_Football;User=football;Password=dev_football;` |
   | ehlikurban | `Server=localhost;Database=EhliKurban;Trusted_Connection=True;TrustServerCertificate=True;` |

3. **Yeniden başlat** (geliştirme ortamında süreç kapanır, Visual Studio'dan tekrar başlat).
4. MySQL plugin'lerinde **Tabloları oluştur / güncelle**.
5. `/swagger` üzerinden test et. JWT'li uygulamalarda önce `auth/register` → dönen token'ı **Authorize**'a yapıştır. API key'li uygulamalarda anahtar plugin sayfasında yazar.

### EhliKurban: mevcut veritabanından model üretme

`plugins/SemihBener.Plugin.EhliKurban/Data` içindeki model **örnektir**. Gerçek tabloları üretmek için:

```powershell
cd plugins\SemihBener.Plugin.EhliKurban
dotnet ef dbcontext scaffold "Server=localhost;Database=EhliKurban;Trusted_Connection=True;TrustServerCertificate=True;" Microsoft.EntityFrameworkCore.SqlServer --context EhliKurbanDbContext --output-dir Data --force
```

Scaffold sonrası controller'lar gerçek tablolara göre uyarlanır. EhliKurban'da migration kapalıdır (`plugin.json: "migrations": false`); API mevcut tabloları değiştirmez.

---

## 2. Yeni bir API eklemek

1. `plugins/SemihBener.Plugin.Football` klasörünü kopyala, adlarını değiştir (`SemihBener.Plugin.YeniApp`), solution'a ekle.
2. `plugin.json`: `key` (URL öneki, küçük harf), `name`, `version`, `entryAssembly`, `database` (`mysql` / `mssql` / `none`), `auth` (`jwt` / `apikey` / `none`), `migrations`.
3. Controller'lar `PluginControllerBase`'den türer ve **göreli** route kullanır: `[Route("matches")]` → `/api/yeniapp/matches`. Önek, kimlik doğrulama ve rate limit host tarafından eklenir.
4. Login/register gibi uçlara `[AllowAnonymous]` ve `[StrictRateLimit]` eklenir.
5. Migration: `.\tools\add-migration.ps1 -Plugin SemihBener.Plugin.YeniApp`
6. Zip: `.\tools\pack-plugin.ps1 -Plugin SemihBener.Plugin.YeniApp` → `artifacts\SemihBener.Plugin.YeniApp-1.0.0.zip`
7. Natro'da boş MySQL DB + kullanıcı aç → panelde **Plugin yükle** → bağlantıyı gir, test et → **Yeniden başlat** → **Tabloları oluştur**.

**Güncelleme:** `plugin.json`'daki `version`'ı artır, yeniden paketle, yükle. Önceki sürüm saklanır; plugin sayfasından geri dönülebilir.

**Kurallar:** Plugin kendi thread'ini/Timer'ını açmaz (tek app pool'da yakalanmayan hata tüm API'leri düşürür). Plugin'e özel NuGet paketleri zip'e otomatik girer; host'ta olan paketler (EF Core, Pomelo, ASP.NET) her zaman host'tan kullanılır.

---

## 3. Natro'ya yayın

### İlk kurulum

1. Natro panelinde `api.semihbener.com` subdomain'ini aç, **ASP.NET Core** desteğini ve **SSL**'i (Let's Encrypt) etkinleştir.
2. Host'u yayınla:
   ```powershell
   dotnet publish src\SemihBener.Host -c Release -o publish
   # Sunucuda .NET 10 runtime yoksa:
   dotnet publish src\SemihBener.Host -c Release -r win-x64 --self-contained -o publish
   ```
3. `publish` klasörüne `appsettings.Production.json` oluştur (`appsettings.Production.example.json`'dan kopyala) ve `Admin:SetupToken` için en az 16 karakterlik rastgele bir değer yaz.
4. `publish` içeriğini FTP ile subdomain'in köküne yükle.
5. `https://api.semihbener.com/admin` → kurulum anahtarı ile admin hesabını oluştur → **Güvenlik** sayfasından 2FA'yı aç.
6. Natro'da her MySQL plugin'i için boş veritabanı ve kullanıcı aç. Panelde MySQL sürümü (örn. `8.0.36-mysql` veya `5.7.44-mysql`) plugin sayfasına girilir.
7. Her plugin için zip'i yükle → bağlantıyı gir → **Yeniden başlat** → **Tabloları oluştur**.
8. Kontrol: `https://api.semihbener.com/api/{key}/health`

### Host güncelleme

1. Sunucuya `app_offline.htm` yükle (IIS uygulamayı durdurur, dosyalar serbest kalır).
2. Yeni `publish` dosyalarını yükle. **`App_Data` klasörüne ve `appsettings.Production.json`'a dokunma.**
3. `app_offline.htm`'yi sil.

### Yedekleme

`App_Data/host-state.json`, `App_Data/keys/` ve `App_Data/plugins/` düzenli yedeklenmeli. `keys` kaybolursa kayıtlı bağlantılar ve anahtarlar çözülemez, panelden yeniden girilmesi gerekir.

---

## 4. Koruma (DDoS, brute-force, app pool)

| Katman | Ne yapar | Ayar |
| --- | --- | --- |
| Cloudflare (önerilir) | DDoS'u Natro'ya ulaşmadan durdurur, bot koruması, login için rate limit kuralı | Cloudflare DNS'te proxy açık; sonra `Network:TrustCloudflare = true` |
| Genel eşzamanlılık sınırı | Aynı anda en fazla 64 istek işlenir, 32'si kuyrukta bekler; fazlası reddedilir. App pool boğulmaz. | `Protection:MaxConcurrentRequests`, `RequestQueueLimit` |
| Plugin rate limit | Plugin başına, kullanıcı (JWT) veya IP başına dakikalık limit | Panel → plugin → Ayarlar |
| Sıkı limit | `[StrictRateLimit]` uçlarında IP başına dakikada 5 istek | `Protection:StrictRequestsPerMinute` |
| İstek sınırları | Gövde en fazla 512 KB, istek süresi en fazla 30 sn | `Protection:MaxRequestBodyKb`, `RequestTimeoutSeconds` |
| Site kapısı | `/api/...` dışındaki her adres (ana sayfa, panel, Swagger) önce giriş ister. Mobil uygulamalar `/api` altında kendi token/API key'leriyle çalışır. | — |
| Panel | 5 hatalı girişte 15 dk kilit, 2FA | — |
| Çökme koruması | Yüklenirken süreci çökerten plugin bir sonraki açılışta otomatik kapanır (IIS çökme döngüsüne girmez) | — |
| Güvenli mod | Hiçbir plugin yüklenmez, sadece panel açılır | Panel → Pano, veya `App_Data/SAFE_MODE` dosyası, veya `Host:SafeMode = true` |

`Network:CloudflareOnly = true` yapılırsa Cloudflare'i atlayıp sunucuya doğrudan gelen istekler reddedilir.

---

## 5. Sorun giderme

| Belirti | Sebep / çözüm |
| --- | --- |
| Panel açılıyor ama ayar kaydedilmiyor / plugin yüklenmiyor | Uygulama `App_Data` klasörüne yazamıyor → Plesk Dosya Yöneticisi'nden site klasörüne uygulama havuzu kullanıcısı için yazma izni ver. |
| 500.30 / 500.31 | Runtime eksik → self-contained publish. Ayrıntı için `web.config`'te `stdoutLogEnabled="true"` yapıp `logs/` klasörüne bak. |
| 500.19 | `web.config`'teki WebDAV `remove` satırları hosting'de kilitli → o iki satırı sil. |
| PUT/DELETE 405 | WebDAV açık → yukarıdaki satırlar silinmemeli; Natro'dan WebDAV'ı kapatmasını iste. |
| 413 zip yüklerken | `web.config`'teki `maxAllowedContentLength` (50 MB) hosting'de geçersiz olabilir. |
| Plugin "Error" durumunda | Plugin sayfasında hata mesajı ve Loglar sayfası. Bağlantı yoksa önce bağlantıyı girip yeniden başlat. |
| Kurulum sayfası anahtar istiyor ama yok | `appsettings.Production.json` → `Admin:SetupToken` (veya `web.config` → ortam değişkeni `Admin__SetupToken`). |
| Panelde kayıtlı şifreler kayboldu | `App_Data/keys` silinmiş veya değişmiş; bağlantıları ve anahtarları panelden yeniden gir. |

## Sürümler

.NET 10 üzerinde EF Core **9.0.x** kullanılır: MySQL provider'ı Pomelo henüz EF Core 10'u desteklemiyor. Tüm paket sürümleri `Directory.Build.props` dosyasındadır.
