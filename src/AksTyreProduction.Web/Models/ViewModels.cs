using System.ComponentModel.DataAnnotations;

namespace AksTyreProduction.Web.Models;

public class DashboardVm
{
    public int ReceivedToday { get; set; } public int InProduction { get; set; } public int Completed { get; set; }
    public int Rejected { get; set; } public int AwaitingRepair { get; set; } public int AwaitingQc { get; set; } public int ReadyForDispatch { get; set; }
    public decimal MaterialCost { get; set; } public decimal LabourCost { get; set; } public decimal TotalCost { get; set; }
    public Dictionary<string,decimal> MaterialCostsByCategory { get; set; }=[];
    public Dictionary<string,int> Wip { get; set; }=[]; public Dictionary<string,double> AverageMinutes { get; set; }=[];
    public List<StationTransaction> Recent { get; set; }=[]; public List<RetreadJob> Attention { get; set; }=[];
    public Dictionary<string,int> OperatorProductivity { get; set; }=[]; public Dictionary<string,double> MachineUtilisationMinutes { get; set; }=[]; public string Bottleneck { get; set; }="None";
}

public class StatisticsVm
{
    public int TotalTyres { get; set; }
    public int TotalRetreads { get; set; }
    public int ActiveJobs { get; set; }
    public int ReadyForDispatchJobs { get; set; }
    public int RejectedJobs { get; set; }
    public int DispatchedJobs { get; set; }
    public decimal TotalMaterialSpend { get; set; }
    public decimal TotalLabourSpend { get; set; }
    public decimal TotalProductionSpend { get; set; }
    public double AverageCycleHours { get; set; }
    public double QcPassRate { get; set; }
    public double ScrapRate { get; set; }
    public Dictionary<string, int> StageBreakdown { get; set; } = [];
    public Dictionary<string, double> StationAverageMinutes { get; set; } = [];
    public Dictionary<string, int> OperatorProductivity { get; set; } = [];
    public List<string> PriorityAlerts { get; set; } = [];
}
public class ReceiveVm
{
    [Required] public string Brand { get; set; }="Michelin"; [Required] public string Size { get; set; }="11R22.5";
    [Required] public string SerialNumber { get; set; }=""; [Required] public string JobNumber { get; set; }=""; [Required] public int CustomerId { get; set; }
    public string OriginalTreadPattern { get; set; }=""; public string CasingCondition { get; set; }="Good"; public string Application { get; set; }="Commercial"; public DateTime? EstimatedCompletion { get; set; }
}
public class StationVm
{
    public string Station { get; set; }="Buffing"; public string? TyreCode { get; set; } public RetreadJob? Job { get; set; }
    public List<Operator> Operators { get; set; }=[]; public List<Machine> Machines { get; set; }=[]; public StationTransaction? Active { get; set; }
}
public class UserSummaryVm
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public string DefaultRole { get; set; } = "";
    public bool CanWriteJobs { get; set; }
}
public class FormPreviewVm
{
    public string FormKey { get; set; } = "tyre-change-slip";
    public string Title { get; set; } = "Tyre Change Slip";
    public int? TyreId { get; set; }
    public int? RetreadId { get; set; }
    public string AksTyreId { get; set; } = "GTC260001";
    public string CustomerName { get; set; } = "AKS Customer";
    public string Brand { get; set; } = "Michelin";
    public string Size { get; set; } = "11R22.5";
    public string SerialNumber { get; set; } = "SER-0001";
    public string JobNumber { get; set; } = "JOB-0001";
    public int RetreadNumber { get; set; } = 1;
    public string CurrentStage { get; set; } = "Initial Inspection";
    public string Status { get; set; } = "In Production";
    public DateTime? ReceivedAt { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
    public DateTime? QcPassedAt { get; set; }
    public string QcOperatorName { get; set; } = "Unassigned";
    public string VehicleRegistration { get; set; } = "Vehicle registration";
    public string SiteName { get; set; } = "AKS Depot";
    public string Application { get; set; } = "Commercial";
    public decimal MaterialCost { get; set; }
    public decimal LabourCost { get; set; }
    public decimal TotalCost { get; set; }
}
public record CustomerProgressDto(string AksTyreId,string Customer,string Brand,string Size,string SerialNumber,int RetreadNumber,string CurrentStage,DateTime? EstimatedCompletion,IReadOnlyList<string> CompletedStages);
