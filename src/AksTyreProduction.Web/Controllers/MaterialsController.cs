using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using AksTyreProduction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Controllers;
public class MaterialsController(AppDbContext db,ProductionService service) : Controller
{
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
    public async Task<IActionResult> Index()=>View(await db.Materials.Include(x=>x.Batches).OrderBy(x=>x.Name).ToListAsync());
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
    public async Task<IActionResult> Traceability(string? lot){ViewBag.Lot=lot;return View(string.IsNullOrWhiteSpace(lot)?[]:await db.MaterialUsages.Include(x=>x.Material).Include(x=>x.MaterialBatch).Include(x=>x.RetreadJob).ThenInclude(x=>x.Tyre).ThenInclude(x=>x.Customer).Where(x=>x.MaterialBatch!.LotNumber.Contains(lot)).ToListAsync());}
    [Authorize(Policy=ApplicationPolicies.JobsWrite)]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Use(int jobId,int materialId,decimal quantity,int? batchId,int? transactionId,int tyreId){try{await service.AddMaterialAsync(jobId,materialId,quantity,batchId,transactionId);TempData["Success"]="Material usage and historical price recorded.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction("Details","Tyres",new{id=tyreId});}
    [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Update(int id,decimal currentUnitCost,decimal quantityOnHand,decimal reorderLevel){var material=await db.Materials.FindAsync(id);if(material!=null){var oldValue=$"Cost {material.CurrentUnitCost}; stock {material.QuantityOnHand}; reorder {material.ReorderLevel}";material.CurrentUnitCost=currentUnitCost;material.QuantityOnHand=quantityOnHand;material.ReorderLevel=reorderLevel;db.AuditEvents.Add(new AksTyreProduction.Web.Models.AuditEvent{User=User.Identity?.Name??"Unknown",Action="Material master updated",OldValue=$"{material.Name}: {oldValue}",NewValue=$"{material.Name}: Cost {currentUnitCost}; stock {quantityOnHand}; reorder {reorderLevel}",OccurredAt=DateTime.Now});await db.SaveChangesAsync();TempData["Success"]="Material master updated. Historical usage costs were not changed.";}return RedirectToAction(nameof(Index));}
}
