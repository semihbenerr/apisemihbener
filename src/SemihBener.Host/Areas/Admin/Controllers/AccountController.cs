using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SemihBener.Abstractions;
using SemihBener.Host.Areas.Admin.Models;
using SemihBener.Host.Core;
using SemihBener.Host.Security;

namespace SemihBener.Host.Areas.Admin.Controllers;

public sealed class AccountController(
    HostStateStore store,
    SecretProtector secrets,
    LoginThrottle throttle,
    ClientIpResolver ipResolver,
    IConfiguration configuration,
    ILogger<AccountController> logger) : AdminControllerBase
{
    private const string TotpIssuer = "api.semihbener.com";

    // ---------------- Giriş ----------------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (store.Snapshot().Admin is null)
        {
            return RedirectToAction(nameof(Setup));
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        var ip = ipResolver.Resolve(HttpContext);
        if (throttle.IsLocked(ip, out var remaining))
        {
            model.Error = $"Çok fazla hatalı deneme. {Math.Ceiling(remaining.TotalMinutes)} dakika sonra tekrar deneyin.";
            model.Password = "";
            return View(model);
        }

        var admin = store.Snapshot().Admin;
        if (admin is null)
        {
            return RedirectToAction(nameof(Setup));
        }

        var valid = string.Equals(admin.Username, model.Username?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && PasswordHasher.Verify(model.Password ?? "", admin.PasswordHash);

        if (valid && admin.TotpEnabled)
        {
            var secret = secrets.Unprotect(admin.TotpSecretProtected);
            valid = secret is not null && Totp.Verify(secret, model.TotpCode);
        }

        if (!valid)
        {
            throttle.RegisterFailure(ip);
            logger.LogWarning("Panel girişi başarısız. IP: {Ip}", ip);
            model.Error = "Kullanıcı adı, şifre veya doğrulama kodu hatalı.";
            model.Password = "";
            model.TotpCode = null;
            return View(model);
        }

        throttle.Reset(ip);
        store.Update(state =>
        {
            if (state.Admin is not null)
            {
                state.Admin.LastLoginAtUtc = DateTime.UtcNow;
            }
        });

        await SignInAsync(admin.Username);
        logger.LogWarning("Panel girişi yapıldı. IP: {Ip}", ip);

        return Url.IsLocalUrl(model.ReturnUrl) ? Redirect(model.ReturnUrl) : Redirect("/admin");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.Admin);
        return RedirectToAction(nameof(Login));
    }

    // ---------------- İlk kurulum ----------------

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Setup()
    {
        if (store.Snapshot().Admin is not null)
        {
            return RedirectToAction(nameof(Login));
        }

        return View(new SetupViewModel { RequiresToken = !IsLocalRequest() });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Setup(SetupViewModel model)
    {
        if (store.Snapshot().Admin is not null)
        {
            return RedirectToAction(nameof(Login));
        }

        model.RequiresToken = !IsLocalRequest();
        model.Username = model.Username?.Trim() ?? "";

        if (model.RequiresToken && !SetupTokenMatches(model.SetupToken))
        {
            throttle.RegisterFailure(ipResolver.Resolve(HttpContext));
            model.Error = "Kurulum anahtarı hatalı veya sunucuda tanımlı değil (appsettings.Production.json → Admin:SetupToken).";
            return View(model);
        }

        if (model.Username.Length is < 3 or > 32)
        {
            model.Error = "Kullanıcı adı 3-32 karakter olmalı.";
            return View(model);
        }

        if ((model.Password ?? "").Length < 12)
        {
            model.Error = "Şifre en az 12 karakter olmalı.";
            return View(model);
        }

        if (model.Password != model.ConfirmPassword)
        {
            model.Error = "Şifreler aynı değil.";
            return View(model);
        }

        store.Update(state =>
        {
            state.Admin ??= new AdminAccount
            {
                Username = model.Username,
                PasswordHash = PasswordHasher.Hash(model.Password!),
                CreatedAtUtc = DateTime.UtcNow
            };
        });

        logger.LogWarning("Admin hesabı oluşturuldu: {User}", model.Username);
        await SignInAsync(model.Username);
        Flash("Admin hesabı oluşturuldu. Güvenlik sayfasından 2 adımlı doğrulamayı açmanız önerilir.");
        return Redirect("/admin");
    }

    // ---------------- Güvenlik: şifre ve 2FA ----------------

    [HttpGet]
    public IActionResult Security()
    {
        var admin = store.Snapshot().Admin!;
        var pending = secrets.Unprotect(admin.TotpPendingSecretProtected);

        return View(new SecurityViewModel
        {
            Username = admin.Username,
            TotpEnabled = admin.TotpEnabled,
            PendingSecret = pending,
            PendingUri = pending is null ? null : Totp.BuildUri(pending, admin.Username, TotpIssuer)
        });
    }

    [HttpPost]
    public IActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var admin = store.Snapshot().Admin!;
        if (!PasswordHasher.Verify(currentPassword ?? "", admin.PasswordHash))
        {
            FlashError("Mevcut şifre hatalı.");
        }
        else if ((newPassword ?? "").Length < 12)
        {
            FlashError("Yeni şifre en az 12 karakter olmalı.");
        }
        else if (newPassword != confirmPassword)
        {
            FlashError("Yeni şifreler aynı değil.");
        }
        else
        {
            store.Update(state => state.Admin!.PasswordHash = PasswordHasher.Hash(newPassword!));
            Flash("Şifre değiştirildi.");
        }

        return RedirectToAction(nameof(Security));
    }

    [HttpPost]
    public IActionResult StartTotp()
    {
        var secret = Totp.GenerateSecret();
        store.Update(state => state.Admin!.TotpPendingSecretProtected = secrets.Protect(secret));
        Flash("Kodu doğrulama uygulamanıza ekleyin, sonra uygulamadaki 6 haneli kodu girin.");
        return RedirectToAction(nameof(Security));
    }

    [HttpPost]
    public IActionResult ConfirmTotp(string code)
    {
        var admin = store.Snapshot().Admin!;
        var pending = secrets.Unprotect(admin.TotpPendingSecretProtected);

        if (pending is null || !Totp.Verify(pending, code))
        {
            FlashError("Kod doğrulanamadı. Telefonun saatinin doğru olduğundan emin olun.");
            return RedirectToAction(nameof(Security));
        }

        store.Update(state =>
        {
            state.Admin!.TotpSecretProtected = state.Admin.TotpPendingSecretProtected;
            state.Admin.TotpPendingSecretProtected = null;
            state.Admin.TotpEnabled = true;
        });

        logger.LogWarning("Panel için 2 adımlı doğrulama açıldı.");
        Flash("2 adımlı doğrulama açıldı.");
        return RedirectToAction(nameof(Security));
    }

    [HttpPost]
    public IActionResult DisableTotp(string password)
    {
        var admin = store.Snapshot().Admin!;
        if (!PasswordHasher.Verify(password ?? "", admin.PasswordHash))
        {
            FlashError("Şifre hatalı.");
            return RedirectToAction(nameof(Security));
        }

        store.Update(state =>
        {
            state.Admin!.TotpEnabled = false;
            state.Admin.TotpSecretProtected = null;
            state.Admin.TotpPendingSecretProtected = null;
        });

        logger.LogWarning("Panel için 2 adımlı doğrulama kapatıldı.");
        Flash("2 adımlı doğrulama kapatıldı.");
        return RedirectToAction(nameof(Security));
    }

    // ---------------- Yardımcılar ----------------

    private async Task SignInAsync(string username)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.Role, "admin")],
            AuthSchemes.Admin);

        await HttpContext.SignInAsync(
            AuthSchemes.Admin,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
    }

    private bool IsLocalRequest()
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote);
    }

    private bool SetupTokenMatches(string? provided)
    {
        var expected = configuration["Admin:SetupToken"];
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 16 || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }
}
