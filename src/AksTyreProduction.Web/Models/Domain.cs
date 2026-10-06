using System.ComponentModel.DataAnnotations;

namespace AksTyreProduction.Web.Models;

public static class ApplicationRoles
{
    public const string Administrator = "Administrator";
    public const string Management = "Management";
    public const string QC = "QC";
    public const string Receiving = "Receiving";
    public const string Inspector = "Inspector";
    public const string ProductionOperator = "Production Operator";
    public const string Dispatch = "Dispatch";
    public const string Worker = "Worker";

    public static readonly string[] All =
    [
        Administrator,
        Management,
        QC,
        Receiving,
        Inspector,
        ProductionOperator,
        Dispatch,
        Worker
    ];
}

public static class ApplicationPolicies
{
    public const string JobsRead = "Jobs.Read";
    public const string JobsWrite = "Jobs.Write";
    public const string PermissionClaimType = "app:permission";
    public const string JobsReadClaim = "jobs.read";
    public const string JobsWriteClaim = "jobs.write";
}

public static class AccountAccessRules
{
    public static readonly string[] AccountTypes =
    [
        ApplicationRoles.Administrator,
        ApplicationRoles.Management,
        ApplicationRoles.Worker
    ];

    public static bool CanCreateAccount(string actorRole, string targetRole) =>
        string.Equals(actorRole, ApplicationRoles.Administrator, StringComparison.OrdinalIgnoreCase)
            ? AccountTypes.Contains(targetRole, StringComparer.OrdinalIgnoreCase)
            : string.Equals(actorRole, ApplicationRoles.Management, StringComparison.OrdinalIgnoreCase) &&
              string.Equals(targetRole, ApplicationRoles.Worker, StringComparison.OrdinalIgnoreCase);
    public static bool CanManageAccount(string actorRole, string targetRole) =>
        string.Equals(actorRole, ApplicationRoles.Administrator, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(actorRole, ApplicationRoles.Management, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(targetRole, ApplicationRoles.Worker, StringComparison.OrdinalIgnoreCase));
}

public static class PasswordRequirements
{
    public static bool IsStrong(string? password) =>
        password is { Length: >= 12 } &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsLower) &&
        password.Any(char.IsDigit) &&
        password.Any(c => !char.IsLetterOrDigit(c));
}

public class AppUser
{
    public int Id { get; set; }
    [Required] public string Username { get; set; } = "";
    [Required] public string PasswordHash { get; set; } = "";
    [Required] public string RolesCsv { get; set; } = ApplicationRoles.Administrator;
    public string DefaultRole { get; set; } = ApplicationRoles.Administrator;
    public bool CanWriteJobs { get; set; }

    public string SelectedRole
    {
        get
        {
            var roles = RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Length == 0)
            {
                return ApplicationRoles.Administrator;
            }

            if (!string.IsNullOrWhiteSpace(DefaultRole) && roles.Contains(DefaultRole, StringComparer.OrdinalIgnoreCase))
            {
                return DefaultRole;
            }

            var firstKnownRole = roles.FirstOrDefault(role => ApplicationRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(firstKnownRole) ? roles[0] : firstKnownRole;
        }
    }
}

public enum JobStatus { InProduction, Rejected, OnHold, QcPassed, ReadyForDispatch, Dispatched, Scrapped }
public enum TransactionResult { Pending, Pass, Fail, Completed }

public static class Workflow
{
    public static readonly string[] Stages = ["Receiving", "Initial Inspection", "Shearography / NDT", "Buffing", "Casing Repair", "Cushion Gum / Bonding", "Tread Application", "Building / Enveloping", "Curing", "Final Inspection", "QC Release", "Dispatch"];
}

public class Customer
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    [Required] public string AccountNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string Site { get; set; } = "";
    public List<Tyre> Tyres { get; set; } = [];
}

public class Tyre
{
    public int Id { get; set; }
    [Required] public string AksTyreId { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    [Required] public string Brand { get; set; } = "";
    [Required] public string Size { get; set; } = "";
    [Required] public string SerialNumber { get; set; } = "";
    public string OriginalTreadPattern { get; set; } = "";
    public string CasingCondition { get; set; } = "Good";
    public string Application { get; set; } = "Commercial";
    public bool IsScrapped { get; set; }
    public string? ScrapReason { get; set; }
    public DateTime? ScrappedAt { get; set; }
    public List<RetreadJob> RetreadJobs { get; set; } = [];
}

public static class TyreHistory
{
    public static RetreadJob? SelectRetread(Tyre tyre, int? retreadId)
    {
        ArgumentNullException.ThrowIfNull(tyre);

        var ordered = tyre.RetreadJobs.OrderByDescending(x => x.RetreadNumber).ToList();
        if (retreadId.HasValue)
        {
            return ordered.FirstOrDefault(x => x.Id == retreadId.Value) ?? ordered.FirstOrDefault();
        }

        return ordered.FirstOrDefault();
    }
}

public class RetreadJob
{
    public const string DefaultCustomerVisibleRejectionMessage = "This tyre has been rejected and requires a manual review before it can proceed.";

    public int Id { get; set; }
    public int TyreId { get; set; }
    public Tyre Tyre { get; set; } = null!;
    public int RetreadNumber { get; set; }
    [Required] public string JobNumber { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public JobStatus Status { get; set; } = JobStatus.InProduction;
    public string CurrentStage { get; set; } = "Receiving";
    public decimal BaseCost { get; set; }
    public decimal MarkupPercent { get; set; } = 25m;
    public DateTime? EstimatedCompletion { get; set; }
    public DateTime? QcPassedAt { get; set; }
    public int? QcOperatorId { get; set; }
    public Operator? QcOperator { get; set; }
    public string? QcOperatorNameSnapshot { get; set; }
    public DateTime? ReadyForDispatchAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? CustomerVisibleRejectionNote { get; set; }
    public string CustomerFacingStatusMessage => Status == JobStatus.Rejected ? (!string.IsNullOrWhiteSpace(CustomerVisibleRejectionNote) ? CustomerVisibleRejectionNote : DefaultCustomerVisibleRejectionMessage) : "";
    public List<StationTransaction> StationTransactions { get; set; } = [];
    public List<MaterialUsage> MaterialUsages { get; set; } = [];
    public List<Repair> Repairs { get; set; } = [];
    public List<PhotoAttachment> Photos { get; set; } = [];
    public Dispatch? Dispatch { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal MaterialCost => MaterialUsages.Sum(x => x.TotalCost);
    public decimal LabourCost => StationTransactions.Sum(x => x.LabourCost);
    public decimal TotalCost => BaseCost + MaterialCost + LabourCost;
    public decimal SellingPrice => TotalCost * (1 + MarkupPercent / 100m);
}

public class Operator
{
    public int Id { get; set; }
    [Required] public string EmployeeId { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public decimal LabourRate { get; set; }
    public bool Active { get; set; } = true;
}

public class Machine
{
    public int Id { get; set; }
    [Required] public string MachineId { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "Available";
    public bool Active { get; set; } = true;
}

public class StationTransaction
{
    public int Id { get; set; }
    public int RetreadJobId { get; set; }
    public RetreadJob RetreadJob { get; set; } = null!;
    [Required] public string Station { get; set; } = "";
    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;
    public int? MachineId { get; set; }
    public Machine? Machine { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int DurationMinutes { get; set; }
    public decimal LabourRateSnapshot { get; set; }
    public decimal LabourCost { get; set; }
    public TransactionResult Result { get; set; } = TransactionResult.Pending;
    public string Notes { get; set; } = "";
    public string? FailureReason { get; set; }
    public string? DetailsJson { get; set; }
    public List<MaterialUsage> MaterialUsages { get; set; } = [];
}

public class Material
{
    public int Id { get; set; }
    [Required] public string MaterialId { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "kg";
    public decimal CurrentUnitCost { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool Active { get; set; } = true;
    public bool BatchTrackingRequired { get; set; }
    public List<MaterialBatch> Batches { get; set; } = [];
}

public class MaterialBatch
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    [Required] public string LotNumber { get; set; } = "";
    public decimal QuantityRemaining { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class MaterialUsage
{
    public int Id { get; set; }
    public int RetreadJobId { get; set; }
    public RetreadJob RetreadJob { get; set; } = null!;
    public int? StationTransactionId { get; set; }
    public StationTransaction? StationTransaction { get; set; }
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public int? MaterialBatchId { get; set; }
    public MaterialBatch? MaterialBatch { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCostSnapshot { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime UsedAt { get; set; }
}

public class Repair
{
    public int Id { get; set; }
    public int RetreadJobId { get; set; }
    public RetreadJob RetreadJob { get; set; } = null!;
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string Location { get; set; } = "";
    public string PatchSize { get; set; } = "";
    public decimal Quantity { get; set; } = 1m;
    public DateTime CreatedAt { get; set; }
}

public class PhotoAttachment
{
    public int Id { get; set; }
    public int RetreadJobId { get; set; }
    public RetreadJob RetreadJob { get; set; } = null!;
    public int? StationTransactionId { get; set; }
    public string FileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public DateTime UploadedAt { get; set; }
}

public class Dispatch
{
    public int Id { get; set; }
    public int RetreadJobId { get; set; }
    public RetreadJob RetreadJob { get; set; } = null!;
    public DateTime DispatchDate { get; set; }
    public string Method { get; set; } = "Delivery";
    public string Reference { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class Invoice
{
    public int Id { get; set; }
    public int RetreadJobId { get; set; }
    public RetreadJob RetreadJob { get; set; } = null!;
    [Required] public string InvoiceNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public decimal ProductionCostSnapshot { get; set; }
    public decimal MarkupPercentSnapshot { get; set; }
    public decimal SellingPriceSnapshot { get; set; }
    public string Status { get; set; } = "Draft";
}

public class AuditEvent
{
    public int Id { get; set; }
    public int? TyreId { get; set; }
    public int? RetreadJobId { get; set; }
    public string User { get; set; } = "System";
    public string Action { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime OccurredAt { get; set; }
}
