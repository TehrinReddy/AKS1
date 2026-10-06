using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using AksTyreProduction.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AksTyreProduction.Web.Controllers;
public class StationsController(AppDbContext db,ProductionService service) : Controller
{
    [Authorize(Policy=ApplicationPolicies.JobsRead)]
    public async Task<IActionResult> Index(string station="Buffing",string? tyreCode=null)
    {
        if (User.IsInRole(ApplicationRoles.Worker) && station is "QC Release" or "Dispatch") return Forbid();
        var vm=new StationVm { Station=station,TyreCode=tyreCode,Operators=await db.Operators.Where(x=>x.Active).ToListAsync(),Machines=await db.Machines.Where(x=>x.Active).ToListAsync() };
        if(!string.IsNullOrWhiteSpace(tyreCode)){var tyre=await db.Tyres.Include(x=>x.Customer).Include(x=>x.RetreadJobs).ThenInclude(x=>x.StationTransactions).SingleOrDefaultAsync(x=>x.AksTyreId==tyreCode);vm.Job=tyre?.RetreadJobs.OrderByDescending(x=>x.RetreadNumber).FirstOrDefault();vm.Active=vm.Job?.StationTransactions.FirstOrDefault(x=>x.EndedAt==null);if(tyre==null)TempData["Error"]="Tyre not found.";}
        return View(vm);
    }
    [Authorize(Policy=ApplicationPolicies.JobsWrite)]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Start(int jobId,string station,int operatorId,int? machineId,string tyreCode){try{await service.StartStationAsync(jobId,station,operatorId,machineId);TempData["Success"]=$"{station} started.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station,tyreCode});}
    [Authorize(Policy=ApplicationPolicies.JobsWrite)]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Complete(int transactionId,TransactionResult result,string? notes,string? failureReason,string station,string tyreCode)
    {try{if(result==TransactionResult.Fail&&string.IsNullOrWhiteSpace(failureReason))throw new InvalidOperationException("A failure reason is required.");var details=Request.Form.Where(x=>x.Key.StartsWith("detail_")).ToDictionary(x=>x.Key[7..].Replace('_',' '),x=>x.Value.ToString());await service.CompleteStationAsync(transactionId,result,notes??"",failureReason,DateTime.Now,JsonSerializer.Serialize(details));TempData["Success"]="Transaction completed and actual completion time recorded.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station,tyreCode});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.QC}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> ReopenJob(int jobId,int operatorId,string tyreCode,string? stage){try{await service.ReopenJobAsync(jobId,operatorId,stage);TempData["Success"]="Job reopened. Continue from the selected stage.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station=string.IsNullOrWhiteSpace(stage)?"Final Inspection":stage,tyreCode});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.QC}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> ReopenFinalInspection(int jobId,int operatorId,string tyreCode){try{await service.ReopenFinalInspectionAsync(jobId,operatorId);TempData["Success"]="Final inspection reopened. Record a fresh pass or fail before QC release.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station="Final Inspection",tyreCode});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.QC}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> ReleaseQc(int jobId,int operatorId,string tyreCode){try{await service.ReleaseQcAsync(jobId,operatorId);TempData["Success"]="QC passed. Tyre is ready for dispatch.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station="QC Release",tyreCode});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.QC},{ApplicationRoles.Dispatch}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> MarkReady(int jobId,string tyreCode){try{await service.MarkReadyForDispatchAsync(jobId);TempData["Success"]="Tyre released and ready for dispatch.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station="QC Release",tyreCode});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.Dispatch}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Dispatch(int jobId,string reference,string method,string? notes,string tyreCode){try{await service.DispatchAsync(jobId,reference,method,notes??"");TempData["Success"]="Tyre dispatched and retread completed.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index),new{station="Dispatch",tyreCode});}
}
