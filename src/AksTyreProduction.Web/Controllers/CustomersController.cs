using AksTyreProduction.Web.Data;using AksTyreProduction.Web.Models;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;using Microsoft.EntityFrameworkCore;
namespace AksTyreProduction.Web.Controllers;
[Authorize]
public class CustomersController(AppDbContext db):Controller
{
 [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.Receiving}")]
 public async Task<IActionResult> Index()=>View(await db.Customers.Include(x=>x.Tyres).OrderBy(x=>x.Name).ToListAsync());
 [Authorize(Roles=$"{ApplicationRoles.Administrator},{ApplicationRoles.Management},{ApplicationRoles.Receiving}")]
 [HttpPost,ValidateAntiForgeryToken]public async Task<IActionResult> Save(int id,string name,string accountNumber,string address,string site){Customer c;if(id==0){c=new Customer();db.Customers.Add(c);}else c=await db.Customers.FindAsync(id)??throw new InvalidOperationException("Customer not found");c.Name=name;c.AccountNumber=accountNumber;c.Address=address;c.Site=site;await db.SaveChangesAsync();TempData["Success"]="Customer saved.";return RedirectToAction(nameof(Index));}
}
