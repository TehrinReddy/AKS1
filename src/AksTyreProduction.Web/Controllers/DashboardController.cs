using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Controllers;
[Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
public class DashboardController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var jobs=await db.RetreadJobs.Include(x=>x.MaterialUsages).Include(x=>x.StationTransactions).ThenInclude(x=>x.Operator).Include(x=>x.StationTransactions).ThenInclude(x=>x.Machine).Include(x=>x.Tyre).ThenInclude(x=>x.Customer).ToListAsync();
        var tx=jobs.SelectMany(x=>x.StationTransactions).ToList(); var today=DateTime.Today;
        var vm=new DashboardVm {
            ReceivedToday=jobs.Count(x=>x.ReceivedAt.Date==today), InProduction=jobs.Count(x=>x.Status==JobStatus.InProduction), Completed=jobs.Count(x=>x.Status==JobStatus.Dispatched),
            Rejected=jobs.Count(x=>x.Status==JobStatus.Rejected), AwaitingRepair=jobs.Count(x=>x.CurrentStage=="Casing Repair"), AwaitingQc=jobs.Count(x=>x.CurrentStage=="QC Release"), ReadyForDispatch=jobs.Count(x=>x.Status==JobStatus.ReadyForDispatch),
            MaterialCost=jobs.Sum(x=>x.MaterialCost), LabourCost=jobs.Sum(x=>x.LabourCost), TotalCost=jobs.Sum(x=>x.TotalCost),
            MaterialCostsByCategory=await db.MaterialUsages.Include(x=>x.Material).GroupBy(x=>x.Material.Category).ToDictionaryAsync(x=>x.Key,x=>x.Sum(u=>u.TotalCost)),
            Wip=jobs.Where(x=>x.Status==JobStatus.InProduction).GroupBy(x=>x.CurrentStage).ToDictionary(x=>x.Key,x=>x.Count()),
            AverageMinutes=tx.Where(x=>x.EndedAt!=null).GroupBy(x=>x.Station).ToDictionary(x=>x.Key,x=>Math.Round(x.Average(t=>t.DurationMinutes),1)),
            Recent=tx.OrderByDescending(x=>x.StartedAt).Take(8).ToList(), Attention=jobs.Where(x=>x.Status==JobStatus.Rejected||x.CurrentStage=="Casing Repair").Take(8).ToList(),
            OperatorProductivity=tx.Where(x=>x.EndedAt!=null).GroupBy(x=>x.Operator?.Name??"Unknown").ToDictionary(x=>x.Key,x=>x.Count()),
            MachineUtilisationMinutes=tx.Where(x=>x.EndedAt!=null&&x.Machine!=null).GroupBy(x=>x.Machine!.Name).ToDictionary(x=>x.Key,x=>(double)x.Sum(t=>t.DurationMinutes)),
            Bottleneck=jobs.Where(x=>x.Status==JobStatus.InProduction).GroupBy(x=>x.CurrentStage).OrderByDescending(x=>x.Count()).Select(x=>$"{x.Key} ({x.Count()} tyres)").FirstOrDefault()??"None"
        }; return View(vm);
    }

    public async Task<IActionResult> Statistics()
    {
        var jobs = await db.RetreadJobs
            .Include(x => x.Tyre)
            .Include(x => x.StationTransactions).ThenInclude(x => x.Operator)
            .Include(x => x.StationTransactions).ThenInclude(x => x.Machine)
            .Include(x => x.MaterialUsages).ThenInclude(x => x.Material)
            .ToListAsync();

        var totalRetreads = jobs.Count;
        var totalTyres = await db.Tyres.CountAsync();
        var activeJobs = jobs.Count(x => x.Status is JobStatus.InProduction or JobStatus.QcPassed or JobStatus.ReadyForDispatch);
        var readyForDispatchJobs = jobs.Count(x => x.Status == JobStatus.ReadyForDispatch);
        var rejectedJobs = jobs.Count(x => x.Status == JobStatus.Rejected);
        var dispatchedJobs = jobs.Count(x => x.Status == JobStatus.Dispatched);

        var averageCycleHours = jobs
            .Where(x => x.ReceivedAt != default && x.CompletedAt.HasValue)
            .Select(x => (x.CompletedAt!.Value - x.ReceivedAt).TotalHours)
            .DefaultIfEmpty(0)
            .Average();

        var qcPassed = jobs.Count(x => x.QcPassedAt.HasValue);
        var qcPassRate = totalRetreads == 0 ? 0 : (double)qcPassed / totalRetreads * 100d;
        var scrapRate = totalTyres == 0 ? 0 : (double)db.Tyres.Count(x => x.IsScrapped) / totalTyres * 100d;

        var stageBreakdown = jobs
            .GroupBy(x => x.CurrentStage)
            .OrderByDescending(x => x.Count())
            .Take(10)
            .ToDictionary(x => x.Key, x => x.Count());

        var stationAverageMinutes = jobs
            .SelectMany(x => x.StationTransactions)
            .Where(x => x.EndedAt.HasValue)
            .GroupBy(x => x.Station)
            .ToDictionary(x => x.Key, x => Math.Round(x.Average(t => t.DurationMinutes), 1));

        var operatorProductivity = jobs
            .SelectMany(x => x.StationTransactions)
            .Where(x => x.Operator != null)
            .GroupBy(x => x.Operator!.Name)
            .ToDictionary(x => x.Key, x => x.Count());

        var totalMaterialSpend = jobs.Sum(x => x.MaterialCost);
        var totalLabourSpend = jobs.Sum(x => x.LabourCost);
        var totalProductionSpend = jobs.Sum(x => x.TotalCost);

        var alerts = new List<string>();
        if (rejectedJobs > 0) alerts.Add($"{rejectedJobs} rejected tyre(s) need root-cause review.");
        if (readyForDispatchJobs > 0) alerts.Add($"{readyForDispatchJobs} tyre(s) ready for dispatch approval.");
        if (jobs.Any(x => x.Status == JobStatus.InProduction && x.CurrentStage == "Buffing")) alerts.Add("Buffing is the current live bottleneck.");

        var vm = new StatisticsVm
        {
            TotalTyres = totalTyres,
            TotalRetreads = totalRetreads,
            ActiveJobs = activeJobs,
            ReadyForDispatchJobs = readyForDispatchJobs,
            RejectedJobs = rejectedJobs,
            DispatchedJobs = dispatchedJobs,
            TotalMaterialSpend = totalMaterialSpend,
            TotalLabourSpend = totalLabourSpend,
            TotalProductionSpend = totalProductionSpend,
            AverageCycleHours = averageCycleHours,
            QcPassRate = qcPassRate,
            ScrapRate = scrapRate,
            StageBreakdown = stageBreakdown,
            StationAverageMinutes = stationAverageMinutes,
            OperatorProductivity = operatorProductivity,
            PriorityAlerts = alerts
        };

        return View(vm);
    }
}
