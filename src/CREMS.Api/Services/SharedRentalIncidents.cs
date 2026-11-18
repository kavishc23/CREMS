using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Corporate;
using CREMS.Api.Domain.Operations;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;

namespace CREMS.Api.Services;

public static class SharedRentalIncidents
{
    // Cases are the operations queue for the same RentalIncident, never a second report.
    public static async Task SyncCase(ApplicationDbContext db, Booking booking, RentalIncident incident, CancellationToken token)
    {
        var subject = $"Customer incident — {incident.IncidentNumber} — {booking.BookingNumber}";
        var legacySubject = $"Customer incident — {booking.BookingNumber}";
        var cases = await db.CustomerCases.Where(x => x.CustomerId == booking.CustomerId &&
            (x.Subject == subject || x.Subject == legacySubject && x.Description.StartsWith(incident.IncidentNumber + "."))).ToListAsync(token);
        if (cases.Count == 0)
        {
            var item = new CustomerCase
            {
                CaseNumber = $"CASE-{Guid.NewGuid():N}"[..25], CustomerId = booking.CustomerId, BranchId = booking.BranchId,
                Type = CaseType.Breakdown, Priority = CasePriority.High,
                Subject = subject,
                Description = $"{incident.IncidentNumber}: {incident.Description}", DueAt = DateTimeOffset.UtcNow.AddMinutes(30)
            };
            db.CustomerCases.Add(item); cases.Add(item);
        }
        foreach (var item in cases)
        {
            item.Status = incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed ? CaseStatus.Resolved : incident.Status == IncidentStatus.Open ? CaseStatus.Open : CaseStatus.InProgress;
            item.Resolution = incident.CustomerResolution;
        }
    }

    // Replacement handovers have their own asset-specific record; the signed
    // original handover and agreement are kept unchanged.
    public static async Task<RentalInspection?> CurrentHandover(ApplicationDbContext db, Booking booking, CancellationToken token)
    {
        var assetId = booking.Items.FirstOrDefault()?.AssetId;
        var replacement = await db.AssetInspections.AsNoTracking().Where(x => x.BookingId == booking.Id &&
            x.AssetId == assetId && x.Stage == Domain.Assets.InspectionStage.PreHire && x.Notes != null && x.Notes.StartsWith("Incident replacement handover:"))
            .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(token);
        return replacement is null ? booking.Inspections.Where(x => x.Type == InspectionType.Handover).OrderByDescending(x => x.CompletedAt).FirstOrDefault()
            : new RentalInspection { BookingId = booking.Id, Type = InspectionType.Handover,
                MeterReading = replacement.MeterReading, FuelLevelPercent = (int?)replacement.FuelPercent,
                ConditionNotes = replacement.Notes, EvidenceJson = replacement.EvidenceJson,
                SignatureName = replacement.CustomerSignatureName, SignatureDataUrl = replacement.CustomerSignatureDataUrl,
                CompletedAt = replacement.CompletedAt, CompletedByName = replacement.CompletedByName };
    }

    public static string[] Evidence(RentalIncident incident)
    {
        try { return JsonSerializer.Deserialize<string[]>(incident.EvidenceJson ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }
}
