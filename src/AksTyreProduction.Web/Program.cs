using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using AksTyreProduction.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPasswordHasher<AksTyreProduction.Web.Models.AppUser>, PasswordHasher<AksTyreProduction.Web.Models.AppUser>>();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath,"data-protection-keys"))).SetApplicationName("AksTyreProduction");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Login";
    options.AccessDeniedPath = "/Login/Denied";
    options.Cookie.Name = "AksTyreAuth";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId);
        var selectedRole = context.Principal?.FindFirstValue("SelectedRole");
        var credentialVersion = user is null ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)));
        var roles = user?.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(role => AksTyreProduction.Web.Models.ApplicationRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (user is null || selectedRole is null || !roles.Contains(selectedRole, StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(context.Principal?.FindFirstValue("CredentialVersion"), credentialVersion, StringComparison.Ordinal))
        {
            context.RejectPrincipal();
            return;
        }

        var existingRoles = context.Principal!.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
        var existingAvailableRoles = context.Principal.FindFirstValue("AvailableRoles") ?? "";
        var expectedPermissions = selectedRole == ApplicationRoles.Worker
            ? user.CanWriteJobs ? new[] { ApplicationPolicies.JobsReadClaim, ApplicationPolicies.JobsWriteClaim } : [ApplicationPolicies.JobsReadClaim]
            : [];
        var existingPermissions = context.Principal.FindAll(ApplicationPolicies.PermissionClaimType).Select(claim => claim.Value).OrderBy(value => value).ToArray();
        if (existingRoles.Length != 1 || !string.Equals(existingRoles[0], selectedRole, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existingAvailableRoles, string.Join(',', roles), StringComparison.Ordinal) ||
            !existingPermissions.SequenceEqual(expectedPermissions.OrderBy(value => value), StringComparer.Ordinal))
        {
            var claims = context.Principal.Claims
                .Where(claim => claim.Type is not (ClaimTypes.Role or "AvailableRoles" or ApplicationPolicies.PermissionClaimType))
                .Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer))
                .ToList();
            claims.Add(new Claim("AvailableRoles", string.Join(',', roles)));
            claims.Add(new Claim(ClaimTypes.Role, selectedRole));
            claims.AddRange(expectedPermissions.Select(permission => new Claim(ApplicationPolicies.PermissionClaimType, permission)));
            context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name, ClaimTypes.Name, ClaimTypes.Role)));
            context.ShouldRenew = true;
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(ApplicationPolicies.JobsRead, policy => policy.RequireAssertion(context =>
        context.User.IsInRole(ApplicationRoles.Administrator) ||
        context.User.IsInRole(ApplicationRoles.Management) ||
        context.User.HasClaim(ApplicationPolicies.PermissionClaimType, ApplicationPolicies.JobsReadClaim)));
    options.AddPolicy(ApplicationPolicies.JobsWrite, policy => policy.RequireAssertion(context =>
        context.User.IsInRole(ApplicationRoles.Administrator) ||
        context.User.IsInRole(ApplicationRoles.Management) ||
        context.User.HasClaim(ApplicationPolicies.PermissionClaimType, ApplicationPolicies.JobsWriteClaim)));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=aks-tyres.db", sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddScoped<ProductionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    await DemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        app.Environment.IsDevelopment(),
        app.Configuration["BootstrapAdmin:Username"],
        app.Configuration["BootstrapAdmin:Password"]);
}

app.Run();

public partial class Program { }
