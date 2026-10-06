using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Controllers;

[Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
public class FormsController(AppDbContext db) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public async Task<IActionResult> Preview(string id, int? tyreId, int? retreadId)
    {
        var key = NormalizeFormKey(id);
        var model = await BuildPreviewVmAsync(key, tyreId, retreadId);
        ViewBag.FormKey = model.FormKey;
        ViewBag.FormTitle = model.Title;
        return View(model);
    }

    private static string NormalizeFormKey(string? id)
    {
        return (id ?? "tyre-change-slip") switch
        {
            "tyre-change-slip" => "tyre-change-slip",
            "vehicle-survey" => "vehicle-survey",
            "job-card" => "job-card",
            "mini-risk-assessment" => "mini-risk-assessment",
            "tyre-action" => "tyre-action",
            "retread-inspection" => "retread-inspection",
            _ => "tyre-change-slip"
        };
    }

    private async Task<FormPreviewVm> BuildPreviewVmAsync(string key, int? tyreId, int? retreadId)
    {
        var model = new FormPreviewVm
        {
            FormKey = key,
            Title = key switch
            {
                "tyre-change-slip" => "Tyre Change Slip",
                "vehicle-survey" => "Vehicle Survey",
                "job-card" => "Job Card",
                "mini-risk-assessment" => "Mini Risk Assessment",
                "tyre-action" => "Tyre Action",
                "retread-inspection" => "Retread Inspection",
                _ => "AKS Form"
            }
        };

        if (!tyreId.HasValue)
        {
            return model;
        }

        var tyre = await db.Tyres
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.RetreadJobs)
            .ThenInclude(x => x.StationTransactions)
            .Include(x => x.RetreadJobs)
            .ThenInclude(x => x.MaterialUsages)
            .ThenInclude(x => x.Material)
            .SingleOrDefaultAsync(x => x.Id == tyreId.Value);

        if (tyre == null)
        {
            return model;
        }

        var selectedRetread = tyre.RetreadJobs
            .OrderByDescending(x => x.RetreadNumber)
            .FirstOrDefault();

        if (retreadId.HasValue)
        {
            selectedRetread = tyre.RetreadJobs.FirstOrDefault(x => x.Id == retreadId.Value) ?? selectedRetread;
        }

        if (selectedRetread == null)
        {
            model.TyreId = tyre.Id;
            model.CustomerName = tyre.Customer.Name;
            model.Brand = tyre.Brand;
            model.Size = tyre.Size;
            model.SerialNumber = tyre.SerialNumber;
            model.AksTyreId = tyre.AksTyreId;
            return model;
        }

        model.TyreId = tyre.Id;
        model.RetreadId = selectedRetread.Id;
        model.CustomerName = tyre.Customer.Name;
        model.Brand = tyre.Brand;
        model.Size = tyre.Size;
        model.SerialNumber = tyre.SerialNumber;
        model.AksTyreId = tyre.AksTyreId;
        model.JobNumber = selectedRetread.JobNumber;
        model.RetreadNumber = selectedRetread.RetreadNumber;
        model.CurrentStage = selectedRetread.CurrentStage;
        model.Status = selectedRetread.Status.ToString();
        model.ReceivedAt = selectedRetread.ReceivedAt;
        model.EstimatedCompletion = selectedRetread.EstimatedCompletion;
        model.QcPassedAt = selectedRetread.QcPassedAt;
        model.QcOperatorName = selectedRetread.QcOperatorNameSnapshot ?? "Unassigned";
        model.MaterialCost = selectedRetread.MaterialUsages.Sum(x => x.TotalCost);
        model.LabourCost = selectedRetread.StationTransactions.Sum(x => x.LabourCost);
        model.TotalCost = selectedRetread.TotalCost;
        return model;
    }
}
