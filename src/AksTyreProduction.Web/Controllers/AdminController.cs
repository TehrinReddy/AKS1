using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Controllers;

[Authorize(Roles = $"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
public class AdminController(AppDbContext db, IPasswordHasher<AppUser> passwordHasher) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Operators = await db.Operators.ToListAsync();
        ViewBag.Machines = await db.Machines.ToListAsync();
        ViewBag.Audits = await db.AuditEvents.OrderByDescending(x => x.OccurredAt).Take(30).ToListAsync();

        var isAdministrator = User.IsInRole(ApplicationRoles.Administrator);
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!isAdministrator)
        {
            query = query.Where(x => x.RolesCsv == ApplicationRoles.Worker);
        }

        var users = await query.OrderBy(x => x.Username).ToListAsync();
        ViewBag.Users = users.Select(user => new UserSummaryVm
        {
            Id = user.Id,
            Username = user.Username,
            Roles = user.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            DefaultRole = user.DefaultRole,
            CanWriteJobs = user.CanWriteJobs
        }).ToList();
        ViewBag.CanManageAllAccounts = isAdministrator;
        ViewBag.AllRoles = isAdministrator ? AccountAccessRules.AccountTypes : [ApplicationRoles.Worker];
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOperator(string employeeId, string name, string role, decimal labourRate)
    {
        db.Operators.Add(new Operator { EmployeeId = employeeId, Name = name, Role = role, LabourRate = labourRate });
        await db.SaveChangesAsync();
        TempData["Success"] = "Operator added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMachine(string machineId, string name, string type)
    {
        db.Machines.Add(new Machine { MachineId = machineId, Name = name, Type = type });
        await db.SaveChangesAsync();
        TempData["Success"] = "Machine added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string username, string password, string role, bool canWriteJobs = false)
    {
        username = username?.Trim() ?? "";
        var actorRole = ActiveRole();
        if (!AccountAccessRules.CanCreateAccount(actorRole, role) || username.Length is 0 or > 50 ||
            !PasswordRequirements.IsStrong(password) || (role != ApplicationRoles.Worker && canWriteJobs))
        {
            TempData["Error"] = "This account type is not permitted, or the username/password/access selection is invalid.";
            return RedirectToAction(nameof(Index));
        }

        if (await db.Users.AnyAsync(user => user.Username.ToLower() == username.ToLower()))
        {
            TempData["Error"] = "That username already exists.";
            return RedirectToAction(nameof(Index));
        }

        var user = new AppUser
        {
            Username = username,
            RolesCsv = role,
            DefaultRole = role,
            CanWriteJobs = role == ApplicationRoles.Worker && canWriteJobs
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        db.AuditEvents.Add(new AuditEvent
        {
            User = User.Identity?.Name ?? "Unknown",
            Action = "User account created",
            NewValue = $"{username} · {role} · Jobs {(user.CanWriteJobs ? "read/write" : "read-only")}",
            OccurredAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "User account created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUserAccess(int id, string role, bool canWriteJobs = false)
    {
        var user = await db.Users.FindAsync(id);
        var actorRole = ActiveRole();
        if (user is null || !AccountAccessRules.CanManageAccount(actorRole, role) ||
            !AccountAccessRules.CanManageAccount(actorRole, user.RolesCsv) ||
            (role != ApplicationRoles.Worker && canWriteJobs))
        {
            TempData["Error"] = "You cannot manage that account or grant the selected access.";
            return RedirectToAction(nameof(Index));
        }

        if (user.Id == CurrentUserId())
        {
            TempData["Error"] = "You cannot change your own account role.";
            return RedirectToAction(nameof(Index));
        }

        if (user.RolesCsv == ApplicationRoles.Administrator && role != ApplicationRoles.Administrator)
        {
            var anotherAdminExists = await db.Users.AnyAsync(other => other.Id != user.Id && other.RolesCsv == ApplicationRoles.Administrator);
            if (!anotherAdminExists)
            {
                TempData["Error"] = "The last Administrator account cannot lose its role.";
                return RedirectToAction(nameof(Index));
            }
        }

        var oldAccess = $"{user.RolesCsv}; jobs write={user.CanWriteJobs}";
        user.RolesCsv = role;
        user.DefaultRole = role;
        user.CanWriteJobs = role == ApplicationRoles.Worker && canWriteJobs;
        db.AuditEvents.Add(new AuditEvent
        {
            User = User.Identity?.Name ?? "Unknown",
            Action = "User access changed",
            OldValue = $"{user.Username}: {oldAccess}",
            NewValue = $"{user.Username}: {role}; jobs write={user.CanWriteJobs}",
            OccurredAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "User access updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUserPassword(int id, string password)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || !AccountAccessRules.CanManageAccount(ActiveRole(), user.RolesCsv) ||
            !PasswordRequirements.IsStrong(password))
        {
            TempData["Error"] = "You cannot reset that account, or the new password does not meet requirements.";
            return RedirectToAction(nameof(Index));
        }

        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.AuditEvents.Add(new AuditEvent
        {
            User = User.Identity?.Name ?? "Unknown",
            Action = "User password reset",
            NewValue = user.Username,
            OccurredAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Password reset.";
        return RedirectToAction(nameof(Index));
    }

    private string ActiveRole() => User.FindFirst("SelectedRole")?.Value ?? "";

    private int CurrentUserId() => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : 0;
}