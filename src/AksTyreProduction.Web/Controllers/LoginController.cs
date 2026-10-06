using System.Security.Claims;
using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace AksTyreProduction.Web.Controllers;

public class LoginController(AppDbContext db, IPasswordHasher<AppUser> passwordHasher) : Controller
{
    [AllowAnonymous, HttpGet("/Login")]
    public IActionResult Index(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous, HttpPost("/Login"), ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Index(string username, string password, string? returnUrl = null)
    {
        var normalizedUsername = username?.Trim() ?? string.Empty;
        var user = await db.Users.FirstOrDefaultAsync(x => x.Username == normalizedUsername || x.Username.ToLower() == normalizedUsername.ToLower());
        var passwordVerification = user is null ? PasswordVerificationResult.Failed : VerifyPassword(user, password);
        var legacyPasswordMatch = user is not null && string.Equals(user.PasswordHash, password, StringComparison.Ordinal);
        if (user is null || (passwordVerification == PasswordVerificationResult.Failed && !legacyPasswordMatch))
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Error = "Incorrect username or password.";
            return View();
        }

        if (legacyPasswordMatch || passwordVerification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }

        var selectedRole = user.SelectedRole;
        var assignedRoles = user.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(role => ApplicationRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!assignedRoles.Contains(selectedRole, StringComparer.OrdinalIgnoreCase))
        {
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Error = "This account has no valid role assigned. Contact an administrator.";
            return View();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("SelectedRole", selectedRole),
                new Claim("AvailableRoles", string.Join(',', assignedRoles)),
                new Claim("CredentialVersion", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))),
                new Claim(ClaimTypes.Role, selectedRole)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (selectedRole == ApplicationRoles.Worker)
        {
            identity.AddClaim(new Claim(ApplicationPolicies.PermissionClaimType, ApplicationPolicies.JobsReadClaim));
            if (user.CanWriteJobs)
            {
                identity.AddClaim(new Claim(ApplicationPolicies.PermissionClaimType, ApplicationPolicies.JobsWriteClaim));
            }
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
        if (selectedRole == ApplicationRoles.Worker &&
            (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/Tyres", StringComparison.OrdinalIgnoreCase) && !returnUrl.StartsWith("/Stations", StringComparison.OrdinalIgnoreCase)))
        {
            return RedirectToAction("Index", "Tyres");
        }
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Index));
    }

    [AllowAnonymous, HttpGet] public IActionResult Denied() => View("Index");

    private PasswordVerificationResult VerifyPassword(AppUser user, string password)
    {
        try
        {
            return passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}
