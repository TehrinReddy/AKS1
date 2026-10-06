using AksTyreProduction.Web.Data;
using AksTyreProduction.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AksTyreProduction.Web.Services;

public class ProductionService(AppDbContext db, IHttpContextAccessor? httpContextAccessor = null)
{
    public async Task<Tyre> CreateTyreAsync(string brand, string size, string serial, int customerId, string jobNumber, string treadPattern="", string casingCondition="Good", string application="Commercial", DateTime? estimatedCompletion=null)
    {
        var next = (await db.Tyres.Select(x => x.AksTyreId).ToListAsync()).Select(x => int.TryParse(x.Replace("GTC26", ""), out var n) ? n : 0).DefaultIfEmpty().Max() + 1;
        var tyre = new Tyre { AksTyreId = $"GTC26{next:0000}", Brand = brand, Size = size, SerialNumber = serial, CustomerId = customerId, OriginalTreadPattern=treadPattern, CasingCondition=casingCondition, Application=application };
        db.Tyres.Add(tyre);
        await db.SaveChangesAsync();
        var job=await StartRetreadAsync(tyre.Id, jobNumber); job.EstimatedCompletion=estimatedCompletion; await db.SaveChangesAsync();
        return tyre;
    }

    public async Task<RetreadJob> StartRetreadAsync(int tyreId, string jobNumber)
    {
        var tyre = await db.Tyres.Include(x => x.RetreadJobs).SingleAsync(x => x.Id == tyreId);
        if (tyre.IsScrapped) throw new InvalidOperationException("A permanently scrapped tyre cannot start another retread.");
        if (tyre.RetreadJobs.Any(x => x.Status is JobStatus.InProduction or JobStatus.QcPassed or JobStatus.ReadyForDispatch)) throw new InvalidOperationException("This tyre already has an active retread.");
        var job = new RetreadJob { TyreId = tyreId, RetreadNumber = tyre.RetreadJobs.Count + 1, JobNumber = jobNumber, ReceivedAt = DateTime.Now, CurrentStage = "Initial Inspection" };
        db.RetreadJobs.Add(job);
        Audit(tyreId, null, "Retread started", null, $"Retread #{job.RetreadNumber}");
        await db.SaveChangesAsync();
        return job;
    }

    public async Task<StationTransaction> StartStationAsync(int jobId, string station, int operatorId, int? machineId)
    {
        var job = await db.RetreadJobs.SingleAsync(x => x.Id == jobId);
        if (job.Status != JobStatus.InProduction) throw new InvalidOperationException("Only jobs in production can start station work.");
        if (!Workflow.Stages.Contains(station, StringComparer.Ordinal) || station is "Receiving" or "QC Release" or "Dispatch") throw new InvalidOperationException("This is not a production station.");
        if (!string.Equals(job.CurrentStage, station, StringComparison.Ordinal)) throw new InvalidOperationException($"The next required station is {job.CurrentStage}; {station} cannot be started yet.");
        if (await db.StationTransactions.AnyAsync(x => x.RetreadJobId == jobId && x.EndedAt == null)) throw new InvalidOperationException("Complete the active station transaction first.");
        var op = await db.Operators.SingleAsync(x => x.Id == operatorId && x.Active);
        var tx = new StationTransaction { RetreadJobId = jobId, Station = station, OperatorId = operatorId, MachineId = machineId, StartedAt = DateTime.Now, LabourRateSnapshot = op.LabourRate };
        db.StationTransactions.Add(tx);
        job.CurrentStage = station;
        Audit(job.TyreId, job.Id, $"{station} started", null, op.Name);
        await db.SaveChangesAsync();
        return tx;
    }

    public async Task CompleteStationAsync(int transactionId, TransactionResult result, string notes, string? failureReason, DateTime completedAt, string? detailsJson=null)
    {
        var tx = await db.StationTransactions.Include(x => x.RetreadJob).SingleAsync(x => x.Id == transactionId);
        if (tx.EndedAt != null) throw new InvalidOperationException("This transaction is already complete.");
        if (completedAt < tx.StartedAt) throw new InvalidOperationException("Completion time cannot be earlier than the station start time.");
        var durationMinutes = Math.Max(1, (int)Math.Round((completedAt - tx.StartedAt).TotalMinutes, MidpointRounding.AwayFromZero));
        tx.DurationMinutes = durationMinutes;
        tx.EndedAt = completedAt;
        tx.Result = result;
        tx.Notes = notes ?? "";
        tx.FailureReason = failureReason;
        tx.DetailsJson = detailsJson;
        tx.LabourCost = decimal.Round(tx.LabourRateSnapshot * durationMinutes / 60m, 2);
        var job = tx.RetreadJob;
        if (result == TransactionResult.Fail && tx.Station is "Initial Inspection" or "Shearography / NDT")
        {
            job.Status = JobStatus.Rejected;
            job.RejectionReason = failureReason;
            job.CustomerVisibleRejectionNote = RetreadJob.DefaultCustomerVisibleRejectionMessage;
        }
        else if (result == TransactionResult.Fail && tx.Station == "Final Inspection") job.CurrentStage = "Casing Repair";
        else { var index = Array.IndexOf(Workflow.Stages, tx.Station); job.CurrentStage = index >= 0 && index < Workflow.Stages.Length - 1 ? Workflow.Stages[index + 1] : tx.Station; }
        Audit(job.TyreId, job.Id, $"{tx.Station} completed", "Active", $"{result} ({durationMinutes} min)");
        await db.SaveChangesAsync();
    }

    public async Task<MaterialUsage> AddMaterialAsync(int jobId, int materialId, decimal quantity, int? batchId, int? transactionId)
    {
        if (quantity <= 0) throw new InvalidOperationException("Quantity must be greater than zero.");
        var material = await db.Materials.SingleAsync(x => x.Id == materialId && x.Active);
        if (material.QuantityOnHand < quantity) throw new InvalidOperationException("Insufficient inventory.");
        MaterialBatch? batch = null;
        if (batchId.HasValue)
        {
            batch = await db.MaterialBatches.SingleAsync(x => x.Id == batchId && x.MaterialId == materialId);
            if (batch.QuantityRemaining < quantity) throw new InvalidOperationException("Insufficient batch inventory.");
            batch.QuantityRemaining -= quantity;
        }
        else if (material.BatchTrackingRequired) throw new InvalidOperationException("A batch is required for this material.");
        var usage = new MaterialUsage { RetreadJobId = jobId, MaterialId = materialId, MaterialBatchId = batchId, StationTransactionId = transactionId, Quantity = quantity, UnitCostSnapshot = material.CurrentUnitCost, TotalCost = decimal.Round(quantity * material.CurrentUnitCost, 2), UsedAt = DateTime.Now };
        material.QuantityOnHand -= quantity;
        db.MaterialUsages.Add(usage);
        var job = await db.RetreadJobs.SingleAsync(x => x.Id == jobId);
        Audit(job.TyreId, job.Id, "Material consumed", null, $"{material.Name} {quantity} {material.UnitOfMeasure}");
        await db.SaveChangesAsync();
        return usage;
    }

    public async Task ReopenJobAsync(int jobId, int operatorId, string? targetStage = null)
    {
        var job = await db.RetreadJobs.SingleAsync(x => x.Id == jobId);
        if (job.Status == JobStatus.Dispatched || job.Status == JobStatus.Scrapped)
            throw new InvalidOperationException("Dispatched or scrapped jobs cannot be reopened.");

        var previousStage = job.CurrentStage;
        var stage = string.IsNullOrWhiteSpace(targetStage) ? job.CurrentStage : targetStage;
        if (!Workflow.Stages.Contains(stage, StringComparer.Ordinal) || stage is "Receiving" or "QC Release" or "Dispatch")
        {
            if (string.Equals(job.CurrentStage, "QC Release", StringComparison.Ordinal))
            {
                stage = "Final Inspection";
            }
            else if (string.Equals(job.CurrentStage, "Ready for Dispatch", StringComparison.Ordinal))
            {
                stage = "QC Release";
            }
            else
            {
                throw new InvalidOperationException($"The job cannot be reopened to '{stage}'.");
            }
        }

        var operatorEntity = await db.Operators.SingleOrDefaultAsync(x => x.Id == operatorId && x.Active)
            ?? throw new InvalidOperationException("Select an active operator to reopen the job.");

        job.Status = JobStatus.InProduction;
        job.CurrentStage = stage;
        job.QcPassedAt = null;
        job.QcOperatorId = null;
        job.QcOperatorNameSnapshot = null;
        job.ReadyForDispatchAt = null;
        job.CompletedAt = null;

        Audit(job.TyreId, job.Id, "Job reopened", previousStage, $"{stage} ({operatorEntity.Name})");
        await db.SaveChangesAsync();
    }

    public async Task ReopenFinalInspectionAsync(int jobId, int operatorId)
    {
        await ReopenJobAsync(jobId, operatorId, "Final Inspection");
    }

    public async Task ReleaseQcAsync(int jobId, int operatorId)
    {
        var job = await db.RetreadJobs.SingleAsync(x => x.Id == jobId);
        var qcOperator = await db.Operators.SingleOrDefaultAsync(x => x.Id == operatorId && x.Active)
            ?? throw new InvalidOperationException("Select an active QC operator.");
        var latestInspection = await db.StationTransactions
            .Where(x => x.RetreadJobId == jobId && x.Station == "Final Inspection")
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();
        if (latestInspection is null || latestInspection.EndedAt is null || latestInspection.Result != TransactionResult.Pass) throw new InvalidOperationException("The latest final inspection must be completed and passed before QC release.");
        job.Status = JobStatus.QcPassed; job.CurrentStage = "QC Passed"; job.QcPassedAt=DateTime.Now;
        job.QcOperatorId = qcOperator.Id;
        job.QcOperatorNameSnapshot = qcOperator.Name;
        Audit(job.TyreId, job.Id, "QC passed", "In Production", $"QC Passed ({qcOperator.Name})");
        await db.SaveChangesAsync();
    }

    public async Task MarkReadyForDispatchAsync(int jobId)
    {
        var job=await db.RetreadJobs.SingleAsync(x=>x.Id==jobId);
        if(job.Status!=JobStatus.QcPassed) throw new InvalidOperationException("QC must pass before the tyre can be released for dispatch.");
        job.Status=JobStatus.ReadyForDispatch;job.CurrentStage="Ready for Dispatch";job.ReadyForDispatchAt=DateTime.Now;
        Audit(job.TyreId,job.Id,"Released for dispatch","QC Passed","Ready for Dispatch");await db.SaveChangesAsync();
    }

    public async Task<Repair> AddRepairAsync(int jobId,string type,string description,string location,string patchSize,decimal quantity)
    {
        var job=await db.RetreadJobs.SingleAsync(x=>x.Id==jobId);var repair=new Repair{RetreadJobId=jobId,Type=type,Description=description,Location=location,PatchSize=patchSize,Quantity=quantity,CreatedAt=DateTime.Now};db.Repairs.Add(repair);Audit(job.TyreId,job.Id,"Repair recorded",null,$"{type} · {patchSize} × {quantity}");await db.SaveChangesAsync();return repair;
    }

    public async Task<Invoice> CreateInvoiceAsync(int jobId,string invoiceNumber)
    {
        var job=await db.RetreadJobs.Include(x=>x.MaterialUsages).Include(x=>x.StationTransactions).Include(x=>x.Invoice).SingleAsync(x=>x.Id==jobId);
        if(job.Status is not (JobStatus.QcPassed or JobStatus.ReadyForDispatch or JobStatus.Dispatched)) throw new InvalidOperationException("QC release is required before invoice preparation.");
        if(job.Invoice!=null) return job.Invoice;
        var invoice=new Invoice{RetreadJobId=jobId,InvoiceNumber=invoiceNumber,CreatedAt=DateTime.Now,ProductionCostSnapshot=job.TotalCost,MarkupPercentSnapshot=job.MarkupPercent,SellingPriceSnapshot=decimal.Round(job.SellingPrice,2)};db.Invoices.Add(invoice);Audit(job.TyreId,job.Id,"Draft invoice created",null,invoiceNumber);await db.SaveChangesAsync();return invoice;
    }

    public async Task DispatchAsync(int jobId, string reference, string method, string notes)
    {
        var job = await db.RetreadJobs.SingleAsync(x => x.Id == jobId);
        if (job.Status != JobStatus.ReadyForDispatch) throw new InvalidOperationException("The tyre must be marked Ready for Dispatch before dispatch.");
        db.Dispatches.Add(new Dispatch { RetreadJobId = jobId, DispatchDate = DateTime.Now, Reference = reference, Method = method, Notes = notes });
        job.Status = JobStatus.Dispatched; job.CurrentStage = "Dispatched"; job.CompletedAt = DateTime.Now;
        Audit(job.TyreId, job.Id, "Tyre dispatched", "Ready for Dispatch", reference);
        await db.SaveChangesAsync();
    }

    public async Task ScrapAsync(int tyreId, string reason)
    {
        var tyre = await db.Tyres.FindAsync(tyreId) ?? throw new InvalidOperationException("Tyre not found.");
        tyre.IsScrapped = true; tyre.ScrapReason = reason; tyre.ScrappedAt = DateTime.Now;
        Audit(tyre.Id, null, "Tyre scrapped", "Active", reason);
        await db.SaveChangesAsync();
    }

    private void Audit(int? tyreId, int? jobId, string action, string? oldValue, string? newValue, string? user = null) => db.AuditEvents.Add(new AuditEvent { TyreId = tyreId, RetreadJobId = jobId, User = user ?? httpContextAccessor?.HttpContext?.User.Identity?.Name ?? "System", Action = action, OldValue = oldValue, NewValue = newValue, OccurredAt = DateTime.Now });
}
