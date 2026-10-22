using System.Text.Json;
using CREMS.Api.Domain.Common;

namespace CREMS.Api.Domain.Rentals;

/// <summary>Division-owned rules for new online requests. Existing bookings retain their recorded terms.</summary>
public sealed record DivisionBookingFlow(
    PersonnelRequirement PersonnelRequirement = PersonnelRequirement.None,
    string QuoteMode = "Automatic",
    bool OnlineRequestsEnabled = true,
    bool BusinessCustomersRequireQuote = true,
    bool UnpricedPersonnelRequiresQuote = true,
    bool UnpricedDeliveryRequiresQuote = true,
    bool RequireVerifiedLicenceForVehicles = true,
    int MinimumHireDays = 1,
    int MaximumHireDays = 366,
    int QuoteValidityDays = 14,
    IReadOnlyList<CustomerQuestion>? CustomerFields = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static DivisionBookingFlow For(Division? division) => Parse(division?.CustomerBookingConfigurationJson);

    public static DivisionBookingFlow Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<DivisionBookingFlow>(json, JsonOptions) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public bool IsValid => Enum.IsDefined(PersonnelRequirement) &&
        QuoteMode is "Automatic" or "Always" or "DirectWhenPriced" &&
        MinimumHireDays is >= 1 and <= 366 && MaximumHireDays is >= 1 and <= 366 &&
        MaximumHireDays >= MinimumHireDays && QuoteValidityDays is >= 1 and <= 90 &&
        OrganizationConfiguration.ValidateQuestions(CustomerFields) is null;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public bool RequiresQuote(bool customerRequested, bool businessCustomer, PersonnelRequirement personnelPolicy,
        bool personnelRequested, bool pricedPersonnelAvailable, bool deliveryRequested, bool pricedTransportAvailable)
    {
        if (customerRequested || QuoteMode == "Always") return true;
        if (QuoteMode == "DirectWhenPriced")
            return personnelRequested && !pricedPersonnelAvailable ||
                deliveryRequested && !pricedTransportAvailable;
        return BusinessCustomersRequireQuote && businessCustomer ||
            personnelPolicy == PersonnelRequirement.Required ||
            UnpricedPersonnelRequiresQuote && personnelRequested && !pricedPersonnelAvailable ||
            UnpricedDeliveryRequiresQuote && deliveryRequested && !pricedTransportAvailable;
    }
}
