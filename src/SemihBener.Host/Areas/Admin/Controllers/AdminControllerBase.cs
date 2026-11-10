using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SemihBener.Host.Core;

namespace SemihBener.Host.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(AuthenticationSchemes = AuthSchemes.Admin)]
[AutoValidateAntiforgeryToken]
[ApiExplorerSettings(IgnoreApi = true)]
public abstract class AdminControllerBase : Controller
{
    protected void Flash(string message) => TempData["Flash"] = message;

    protected void FlashError(string message) => TempData["FlashError"] = message;
}
