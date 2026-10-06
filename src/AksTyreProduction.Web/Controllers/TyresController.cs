using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using AksTyreProduction.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Controllers;
public class TyresController(AppDbContext db,ProductionService service,IWebHostEnvironment env) : Controller
{
    [Authorize(Policy=ApplicationPolicies.JobsRead)]
    public async Task<IActionResult> Index(string? q)
    {
        var query=db.Tyres.Include(x=>x.Customer).Include(x=>x.RetreadJobs).AsQueryable();
        if(!string.IsNullOrWhiteSpace(q)) query=query.Where(x=>x.AksTyreId.Contains(q)||x.SerialNumber.Contains(q)||x.Customer.Name.Contains(q)||x.Customer.AccountNumber.Contains(q)||x.RetreadJobs.Any(j=>j.JobNumber.Contains(q)));
        ViewBag.Query=q; return View(await query.OrderBy(x=>x.AksTyreId).ToListAsync());
    }
    [Authorize(Policy=ApplicationPolicies.JobsRead)]
    public async Task<IActionResult> Details(int id, int? retreadId)
    {
        var tyre=await db.Tyres.Include(x=>x.Customer).Include(x=>x.RetreadJobs).ThenInclude(x=>x.StationTransactions).ThenInclude(x=>x.Operator).Include(x=>x.RetreadJobs).ThenInclude(x=>x.MaterialUsages).ThenInclude(x=>x.Material).Include(x=>x.RetreadJobs).ThenInclude(x=>x.MaterialUsages).ThenInclude(x=>x.MaterialBatch).Include(x=>x.RetreadJobs).ThenInclude(x=>x.Repairs).Include(x=>x.RetreadJobs).ThenInclude(x=>x.Photos).Include(x=>x.RetreadJobs).ThenInclude(x=>x.Invoice).SingleOrDefaultAsync(x=>x.Id==id);
        if (tyre == null) return NotFound();
        ViewBag.SelectedRetreadId = TyreHistory.SelectRetread(tyre, retreadId)?.Id ?? 0;
        return View(tyre);
    }
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.Receiving}")]
    [HttpGet] public async Task<IActionResult> Receive(){ViewBag.Customers=await db.Customers.OrderBy(x=>x.Name).ToListAsync();return View(new ReceiveVm());}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.Receiving}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Receive(ReceiveVm vm)
    {
        if(!ModelState.IsValid){ViewBag.Customers=await db.Customers.ToListAsync();return View(vm);} var tyre=await service.CreateTyreAsync(vm.Brand,vm.Size,vm.SerialNumber,vm.CustomerId,vm.JobNumber,vm.OriginalTreadPattern,vm.CasingCondition,vm.Application,vm.EstimatedCompletion); TempData["Success"]=$"{tyre.AksTyreId} created with Retread #1.";return RedirectToAction(nameof(Details),new{id=tyre.Id});
    }
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.Receiving}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> NewRetread(int id,string jobNumber){try{await service.StartRetreadAsync(id,jobNumber);TempData["Success"]="New retread started.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Details),new{id});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Scrap(int id,string reason){try{await service.ScrapAsync(id,reason);TempData["Success"]="Tyre permanently scrapped.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Details),new{id});}
    [Authorize(Policy=ApplicationPolicies.JobsWrite)]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Upload(int id,int jobId,int? stationTransactionId,IFormFile photo)
    {
        if(photo.Length>0 && photo.Length<=5_000_000 && photo.ContentType.StartsWith("image/")){var ext=Path.GetExtension(photo.FileName);var safe=$"{Guid.NewGuid():N}{ext}";var folder=Path.Combine(env.WebRootPath,"uploads");Directory.CreateDirectory(folder);await using var stream=System.IO.File.Create(Path.Combine(folder,safe));await photo.CopyToAsync(stream);db.PhotoAttachments.Add(new PhotoAttachment{RetreadJobId=jobId,StationTransactionId=stationTransactionId,FileName=safe,OriginalFileName=Path.GetFileName(photo.FileName),UploadedAt=DateTime.Now});await db.SaveChangesAsync();TempData["Success"]="Photo attached to the production history.";}else TempData["Error"]="Select an image up to 5 MB.";return RedirectToAction(nameof(Details),new{id});
    }
    [Authorize(Policy=ApplicationPolicies.JobsRead)]
    public async Task<IActionResult> Tag(int id){var tyre=await db.Tyres.Include(x=>x.Customer).Include(x=>x.RetreadJobs).SingleOrDefaultAsync(x=>x.Id==id);return tyre==null?NotFound():View(tyre);}
    [Authorize(Policy=ApplicationPolicies.JobsRead)]
    public async Task<IActionResult> Barcode(int id){var code=await db.Tyres.Where(x=>x.Id==id).Select(x=>x.AksTyreId).SingleOrDefaultAsync();return code==null?NotFound():Content(Code39Barcode.RenderSvg(code),"image/svg+xml");}
    [Authorize(Policy=ApplicationPolicies.JobsWrite)]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> AddRepair(int id,int jobId,string type,string description,string location,string patchSize,decimal quantity){try{await service.AddRepairAsync(jobId,type,description,location,patchSize,quantity);TempData["Success"]="Repair record added. Add the actual material below to include its cost.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Details),new{id});}
}
