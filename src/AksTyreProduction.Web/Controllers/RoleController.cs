using System.Security.Claims;
using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Controllers;

public class RoleController(AppDbContext db) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string role, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        var username = User.Identity.Name;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Username == username);
        if (user is null || !ApplicationRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
        }

        var allowed = user.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!allowed.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Selected role is not assigned to this user account.";
            return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
        }

        var claims = HttpContext.User.Claims
            .Where(c => c.Type != ClaimTypes.Role && c.Type != "SelectedRole")
            .Select(c => new Claim(c.Type, c.Value, c.ValueType, c.Issuer, c.OriginalIssuer));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim("SelectedRole", role));
        if (role == ApplicationRoles.Worker)
        {
            identity.AddClaim(new Claim(ApplicationPolicies.PermissionClaimType, ApplicationPolicies.JobsReadClaim));
            if (user.CanWriteJobs)
            {
                identity.AddClaim(new Claim(ApplicationPolicies.PermissionClaimType, ApplicationPolicies.JobsWriteClaim));
            }
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    }
}
