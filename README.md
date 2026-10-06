# AKS Tyre Production Tracking System

A working local proof-of-concept for tracking commercial tyre retreading from receiving through dispatch. The application uses ASP.NET Core MVC, Entity Framework Core, SQLite, and local Bootstrap assets. It requires no cloud services, Docker, SQL Server, or paid APIs.

## Requirements

- Windows 10/11
- .NET SDK 10.0.400 or compatible .NET 10 SDK
- VS Code is optional

Verify the SDK with:

```powershell
dotnet --info
```

## Run locally

From the repository root:

```powershell
dotnet restore
dotnet run --project src/AksTyreProduction.Web --urls http://localhost:5080
```

Open <http://localhost:5080>.

The application automatically applies EF Core migrations and seeds realistic dummy data on its first start. A separate `dotnet ef database update` command is not required. To apply migrations manually:

```powershell
dotnet ef database update --project src/AksTyreProduction.Web --startup-project src/AksTyreProduction.Web
```

## Demo access

Development seeds local test accounts; sign in with username `Admin` and password `Admin`. Passwords are stored as ASP.NET Core password hashes. Do not use demo accounts in production. Administrators can create Administrator, Management, and Worker accounts. Management has full application read/write access but can create, update, and reset Worker accounts only. Workers are restricted to job pages; their job-write access is off by default and can be enabled by an Administrator or Management account. Workers cannot perform QC release, reopen jobs, manage inventory or pricing, create retreads, scrap tyres, or dispatch. New passwords must be at least 12 characters and include upper/lowercase letters, a number, and a symbol. The role selector switches the active role, and server-side policies restrict access and writes. Login attempts are rate-limited per client IP.

For a first non-development deployment, provide `BootstrapAdmin__Username` and `BootstrapAdmin__Password` through the host's secret configuration. The password must meet the requirements above. These values are used only to create the first Administrator when the user table is empty; the app does not seed demo users or demo data outside Development. Remove the bootstrap secret from the host after that first startup. Full account recovery, MFA, and centralized identity-provider integration are not included yet.

Seeded operators include Peter Mokoena, John Naidoo, Sarah Dlamini, Mike Jacobs, and Thandi Nkosi. Seeded customers include XYZ Logistics, ABC Transport, and Durban Freight Services.

## Demo walkthrough: GTC260001

1. Open the dashboard and review database-driven KPIs, WIP, costs, recent activity, and station averages.
2. Open **Tyre Search**, enter `GTC260001`, and open its Digital Tyre Passport.
3. Review completed Retread #1 and active Retread #2, including production events, material batches, repairs, labour, and costs.
4. Choose **Open Factory Station**, or open **Buffing** and scan `GTC260001`.
5. Select an operator/machine and start the station transaction.
6. Complete it using a demo duration such as `95` minutes. The captured operator rate is used to calculate immutable historical labour cost.
7. Return to the passport and add material usage. Select a matching batch when the material requires lot tracking.
8. Confirm that the timeline and actual cost breakdown update.
9. Open **Batch Traceability**, search `CG-260912`, and follow an affected tyre back to its passport.
10. Open **Customer Portal** and search `ABC123456`. Confirm that progress is shown without labour rates, materials costs, markup, or internal notes.
11. To demonstrate rework, complete a Final Inspection with `Fail`, then perform another Repair/Curing/Final Inspection sequence. Each attempt remains in history.
12. A passed Final Inspection is required before **QC Release**. Pass QC, then use **Mark Ready for Dispatch** before dispatching.

## Key design rules

- `Tyre` is the permanent physical identity.
- Each return creates another `RetreadJob`; it does not replace the tyre.
- Every station operation creates a `StationTransaction` historical event.
- Material usages snapshot unit price and decrement inventory.
- Station transactions snapshot labour rate and derive labour cost from duration.
- Failed inspections and rework events remain in the production timeline.
- QC Passed and Ready for Dispatch are separate audited states; dispatch is service-layer gated by Ready for Dispatch.
- Permanently scrapped tyres cannot start another retread.
- Customer portal output uses a dedicated DTO with no internal cost properties.

## Tests

Run:

```powershell
dotnet test AksTyreProduction.slnx
```

The tests cover tyre creation, repeat retreads and retained history, labour calculation/rate snapshots, material calculation/price snapshots, derived total cost, retained failed inspections, QC gating, dispatch gating, scrap protection, and customer DTO privacy.

## Data and uploaded files

- SQLite database: `aks-tyres.db` in the directory from which the application is launched (the repository root when using the command above).
- Uploaded images: `src/AksTyreProduction.Web/wwwroot/uploads/`
- EF Core migrations: `src/AksTyreProduction.Web/Data/Migrations/`

Records persist across application restarts. Back up the SQLite file and uploads directory together.

## Reset demo data

Stop the application, move `aks-tyres.db` somewhere safe, and start the application again. It will create and seed a fresh database. Moving the file is recommended over deleting it so the old demo data remains recoverable.

## Known limitations

- The local `Admin` account and role switcher are intended for this offline demo. Production deployment still requires individual accounts, password hashing or an identity provider, account recovery, and granular authorization policies.
- Draft invoice records snapshot the calculated selling price, but PDF invoice generation, tax rules, payment status, and accounting-system posting are not included.
- Printable Code 39 tags use the browser print dialog. Direct industrial label-printer integration is not included.
- Station screens provide typed factory capture fields and preserve them with the transaction. These flexible station details are stored as structured JSON rather than separate relational columns for every measurement.
- Material prices/stock, customers, operators, and machines have practical demo management controls; advanced purchasing, goods receipts, batch creation, deactivate/edit history, and approval workflows remain future ERP work.
- Dashboard reporting is operational and database-driven, but there is no CSV/PDF export or external BI integration.
- Photo files are locally stored and content-type/size checked; production malware scanning and image processing are not included.
- Workflow is defined in code and is ready to be moved to configurable database definitions later.

## Production evolution

The application is intentionally local and compact. Services and relational entities provide seams for later SQL Server, Azure, Microsoft 365 authentication, physical scanner/printer, machine, ERP/accounting, customer-authentication, notification, Power BI, and API integrations.
