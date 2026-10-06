using AksTyreProduction.Web.Data;using AksTyreProduction.Web.Models;using AksTyreProduction.Web.Services;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace AksTyreProduction.Web.Controllers;
[Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management}")]
public class InvoicesController(AppDbContext db,ProductionService service):Controller
{
    public async Task<IActionResult> Index()=>View(await db.Invoices.Include(x=>x.RetreadJob).ThenInclude(x=>x.Tyre).ThenInclude(x=>x.Customer).OrderByDescending(x=>x.CreatedAt).ToListAsync());
    [HttpPost,ValidateAntiForgeryToken]public async Task<IActionResult> Create(int jobId,int tyreId,string invoiceNumber){try{await service.CreateInvoiceAsync(jobId,invoiceNumber);TempData["Success"]="Draft invoice created from the calculated selling price.";}catch(Exception ex){TempData["Error"]=ex.Message;}return RedirectToAction("Details","Tyres",new{id=tyreId});}
}
