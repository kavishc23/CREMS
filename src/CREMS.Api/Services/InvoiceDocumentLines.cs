using System.Text.Json;
using CREMS.Api.Domain.Rentals;

namespace CREMS.Api.Services;

public static class InvoiceDocumentLines
{
    public static IReadOnlyList<FinancialLine> Read(RentalInvoice invoice)
    {
        if (invoice.Lines.Count > 0) return invoice.Lines.Select(x => new FinancialLine(x.Description, x.Quantity, x.UnitPrice, "Unit", x.IsTaxable)).ToArray();
        // Older invoices keep their itemisation in JSON rather than InvoiceLines.
        try
        {
            using var json = JsonDocument.Parse(invoice.LineItemsJson);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return [];
            return json.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => {
                JsonElement Value(string key) => x.EnumerateObject().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
                decimal Number(string key, decimal fallback = 0) => Value(key).ValueKind == JsonValueKind.Number && Value(key).TryGetDecimal(out var value) ? value : fallback;
                var description = Value("description").ValueKind == JsonValueKind.String ? Value("description").GetString()! : "Recorded invoice line";
                var rate = Number("unitPrice", Number("rate"));
                return new FinancialLine(description, Number("quantity", 1), rate, "Unit", Value("isTaxable").ValueKind != JsonValueKind.False);
            }).ToArray();
        }
        catch (JsonException) { return []; }
    }
}
