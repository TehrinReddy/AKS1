using AksTyreProduction.Web.Controllers;
using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Tests;

public class AuthModelTests
{
    [Fact]
    public void AllowedRolesIncludeManagementAndQc()
    {
        Assert.Contains("Administrator", ApplicationRoles.All);
        Assert.Contains("Management", ApplicationRoles.All);
        Assert.Contains("Worker", ApplicationRoles.All);
        Assert.Contains("QC", ApplicationRoles.All);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, ApplicationRoles.Management, true)]
    [InlineData(ApplicationRoles.Administrator, ApplicationRoles.Worker, true)]
    [InlineData(ApplicationRoles.Management, ApplicationRoles.Worker, true)]
    [InlineData(ApplicationRoles.Management, ApplicationRoles.Management, false)]
    [InlineData(ApplicationRoles.Worker, ApplicationRoles.Worker, false)]
    public void AccountCreationFollowsTheAdminManagementWorkerHierarchy(string actorRole, string targetRole, bool expected)
    {
        Assert.Equal(expected, AccountAccessRules.CanCreateAccount(actorRole, targetRole));
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, ApplicationRoles.Management, true)]
    [InlineData(ApplicationRoles.Management, ApplicationRoles.Worker, true)]
    [InlineData(ApplicationRoles.Management, ApplicationRoles.Management, false)]
    [InlineData(ApplicationRoles.Worker, ApplicationRoles.Worker, false)]
    public void AccountManagementFollowsTheAdminManagementWorkerHierarchy(string actorRole, string targetRole, bool expected)
    {
        Assert.Equal(expected, AccountAccessRules.CanManageAccount(actorRole, targetRole));
    }

    [Fact]
    public void WorkerJobWriteAccessIsExplicitAndDisabledByDefault()
    {
        var worker = new AppUser { RolesCsv = ApplicationRoles.Worker };
        Assert.False(worker.CanWriteJobs);
        worker.CanWriteJobs = true;
        Assert.True(worker.CanWriteJobs);
    }

    [Fact]
    public void SelectedRoleFallsBackToDefaultRole()
    {
        var user = new AppUser { RolesCsv = "Administrator,Management", DefaultRole = "Management" };
        Assert.Equal("Management", user.SelectedRole);
    }

    [Fact]
    public async Task DemoSeederCreatesHashedDevelopmentAdminCredentials()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await DemoSeeder.SeedAsync(db);
        }

        await using (var db = new AppDbContext(options))
        {
            var admin = await db.Users.SingleAsync(x => x.Username == "Admin");
            Assert.NotEqual("Admin", admin.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<AppUser>().VerifyHashedPassword(admin, admin.PasswordHash, "Admin"));
            Assert.Contains("Administrator", admin.RolesCsv);
        }
    }

    [Fact]
    public async Task DevelopmentSeederDoesNotResetExistingUserPasswords()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await DemoSeeder.SeedAsync(db);
        var admin = await db.Users.SingleAsync(x => x.Username == "Admin");
        var hasher = new PasswordHasher<AppUser>();
        admin.PasswordHash = hasher.HashPassword(admin, "ChangedAdminPassword123!");
        await db.SaveChangesAsync();

        await DemoSeeder.SeedAsync(db);

        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(admin, admin.PasswordHash, "ChangedAdminPassword123!"));
    }

    [Fact]
    public async Task ProductionSeederRequiresAndHashesBootstrapAdminCredentials()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoSeeder.SeedAsync(db, seedDemoData: false));
        await DemoSeeder.SeedAsync(db, seedDemoData: false, "FirstAdmin", "StrongPassword123!");

        var admin = await db.Users.SingleAsync();
        Assert.Equal("FirstAdmin", admin.Username);
        Assert.NotEqual("StrongPassword123!", admin.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<AppUser>().VerifyHashedPassword(admin, admin.PasswordHash, "StrongPassword123!"));
    }

    [Fact]
    public void SensitiveControllersRequireRolePolicies()
    {
        var adminPolicy = typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var dispatchPolicy = typeof(StationsController).GetMethod(nameof(StationsController.Dispatch))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var qcPolicy = typeof(StationsController).GetMethod(nameof(StationsController.ReleaseQc))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var invoicePolicy = typeof(InvoicesController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var jobReadPolicy = typeof(TyresController).GetMethod(nameof(TyresController.Index))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var jobWritePolicy = typeof(StationsController).GetMethod(nameof(StationsController.Start))!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var formPolicy = typeof(FormsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();

        Assert.Contains(ApplicationRoles.Administrator, adminPolicy.Roles);
        Assert.Contains(ApplicationRoles.Management, adminPolicy.Roles);
        Assert.Contains(ApplicationRoles.Management, dispatchPolicy.Roles);
        Assert.Contains(ApplicationRoles.Dispatch, dispatchPolicy.Roles);
        Assert.Contains(ApplicationRoles.Management, qcPolicy.Roles);
        Assert.Contains(ApplicationRoles.QC, qcPolicy.Roles);
        Assert.Contains(ApplicationRoles.Management, invoicePolicy.Roles);
        Assert.Equal(ApplicationPolicies.JobsRead, jobReadPolicy.Policy);
        Assert.Equal(ApplicationPolicies.JobsWrite, jobWritePolicy.Policy);
        Assert.Contains(ApplicationRoles.Management, formPolicy.Roles);
        Assert.DoesNotContain(ApplicationRoles.Worker, formPolicy.Roles);
    }

    [Fact]
    public async Task DashboardStatisticsTracksProductionCounts()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var db = new AppDbContext(options))
        {
            var customer = new Customer { Name = "Stats Test Co", AccountNumber = "ST-1", Address = "Test ST", Site = "Site 1" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            db.Tyres.AddRange(
                new Tyre { AksTyreId = "STAT-001", CustomerId = customer.Id, Brand = "Michelin", Size = "11R22.5", SerialNumber = "ABC001", RetreadJobs = [ new RetreadJob { JobNumber = "J-1", Status = JobStatus.InProduction, CurrentStage = "Buffing", ReceivedAt = DateTime.Today.AddDays(-2), RetreadNumber = 1, StationTransactions = [ new StationTransaction { Station = "Buffing", Operator = new Operator { EmployeeId = "O-1", Name = "Tester", LabourRate = 100m }, StartedAt = DateTime.Today.AddHours(8), EndedAt = DateTime.Today.AddHours(9), DurationMinutes = 60, LabourRateSnapshot = 100m, LabourCost = 100m } ] } ] },
                new Tyre { AksTyreId = "STAT-002", CustomerId = customer.Id, Brand = "Bridgestone", Size = "12R22.5", SerialNumber = "ABC002", RetreadJobs = [ new RetreadJob { JobNumber = "J-2", Status = JobStatus.ReadyForDispatch, CurrentStage = "QC Release", ReceivedAt = DateTime.Today.AddDays(-5), RetreadNumber = 1, ReadyForDispatchAt = DateTime.Today } ] },
                new Tyre { AksTyreId = "STAT-003", CustomerId = customer.Id, Brand = "Continental", Size = "10R20", SerialNumber = "ABC003", RetreadJobs = [ new RetreadJob { JobNumber = "J-3", Status = JobStatus.Rejected, CurrentStage = "Final Inspection", ReceivedAt = DateTime.Today.AddDays(-1), RetreadNumber = 1, RejectionReason = "Sidewall damage" } ] }
            );

            await db.SaveChangesAsync();

            var controller = new DashboardController(db);
            var result = await controller.Statistics();
            var view = Assert.IsType<ViewResult>(result);
            var vm = Assert.IsType<StatisticsVm>(view.Model);

            Assert.Equal(3, vm.TotalRetreads);
            Assert.Equal(2, vm.ActiveJobs);
            Assert.Equal(1, vm.ReadyForDispatchJobs);
            Assert.Equal(1, vm.RejectedJobs);
            Assert.Equal(3, vm.TotalTyres);
        }
    }
}
