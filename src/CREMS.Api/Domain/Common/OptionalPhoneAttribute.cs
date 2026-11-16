using System.ComponentModel.DataAnnotations;

namespace CREMS.Api.Domain.Common;

// Optional contact fields arrive as empty strings from forms. Validate a phone
// only when supplied; required customer contact fields still use [Required, Phone].
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    public OptionalPhoneAttribute() : base("Enter a valid phone number or leave this field blank.") { }
    public override bool IsValid(object? value) => value is null || value is string text &&
        (string.IsNullOrWhiteSpace(text) || new PhoneAttribute().IsValid(text));
}
