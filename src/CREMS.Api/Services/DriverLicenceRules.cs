namespace CREMS.Api.Services;

public static class DriverLicenceRules
{
    public static bool ValidNumber(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.All(character => character is >= '0' and <= '9');

    public static bool ValidClasses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var classes = value.Split(',', StringSplitOptions.TrimEntries);
        return classes.Length <= 8 && classes.Distinct().Count() == classes.Length && classes.All(item => item.Length == 1 && item[0] is >= '1' and <= '8');
    }

    public static string NormalizeClasses(string value) => string.Join(',', value.Split(',', StringSplitOptions.TrimEntries).Order());
}
