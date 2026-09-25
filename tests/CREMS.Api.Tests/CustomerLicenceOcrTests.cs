using CREMS.Api.Services;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class CustomerLicenceOcrTests
{
    [Fact]
    public void Parses_only_supported_licence_fields_and_classes()
    {
        var result = CustomerLicenceService.Parse("Name: RAKESH KUMAR\nLicence No: DL-448821\nClasses: 1, 2, 9\nDOB: 01/01/1990");
        Assert.Equal("RAKESH KUMAR", result.FullName);
        Assert.Equal("DL-448821", result.LicenceNumber);
        Assert.Equal([1, 2, 9], result.Classes);
    }

    [Fact]
    public void Parses_fiji_licence_layout_without_a_name_label()
    {
        var result = CustomerLicenceService.Parse("REPUBLIC OF FIJI DRIVER LICENCE\n(PRODUCE ON DEMAND)\nMR. SUDHANSU JAYSHIL KISUN\nTUATUA, LABASA\nLicence Number\n1078896\nFull Licence Classes\n2\nDate of Birth\n05/08/2004");
        Assert.Equal("SUDHANSU JAYSHIL KISUN", result.FullName);
        Assert.Equal("1078896", result.LicenceNumber);
        Assert.Equal([2], result.Classes);
    }

    [Theory]
    [InlineData("MRS. YANI UBIUBI LEDUA", "YANI UBIUBI LEDUA")]
    [InlineData("Miss ANNIE ELIZABETH PYNE", "ANNIE ELIZABETH PYNE")]
    [InlineData("MR SUDHANSU JAYSHIL KISUN", "SUDHANSU JAYSHIL KISUN")]
    public void Parses_supported_titles_case_insensitively(string nameLine, string expectedName)
    {
        var result = CustomerLicenceService.Parse($"{nameLine}\nLicence Number\n1028978\nFull Licence Classes\n2");

        Assert.Equal(expectedName, result.FullName);
        Assert.Equal("1028978", result.LicenceNumber);
        Assert.Equal([2], result.Classes);
    }

    [Fact]
    public void Trims_adjacent_labels_from_a_fiji_ocr_name_line()
    {
        var result = CustomerLicenceService.Parse("MR. SUDHANSU JAYSHILKISUN Licence\nLicence Number\n1078896\nFull Licence Classes\n2");
        Assert.Equal("SUDHANSU JAYSHILKISUN", result.FullName);
    }

    [Theory]
    [InlineData("Rakesh Kumar", "KUMAR, RAKESH")]
    [InlineData("Mereani O'Connor", "MEREANI OCONNOR")]
    public void Compares_names_without_order_or_punctuation(string stored, string scanned) =>
        Assert.True(CustomerLicenceService.NamesMatch(stored, scanned));

    [Fact]
    public void Chooses_consistent_fields_across_noisy_ocr_passes()
    {
        var result = CustomerLicenceService.Parse("""
            MRS. YANI UBIUBI LEDUA
            Licence Number 102B978
            Full Licence Classes 2
            MRS. YANI UBIUBI LEDUA
            Licence Number 1028978
            Full Licence Classes 2
            MRS: YANI UBIUBI LEDUA
            Licence Number 1028978
            Full Licence Classes 2
            """);

        Assert.Equal("YANI UBIUBI LEDUA", result.FullName);
        Assert.Equal("1028978", result.LicenceNumber);
        Assert.Equal([2], result.Classes);
    }

    [Fact]
    public void Recovers_the_canonical_customer_name_from_blurry_ocr_tokens()
    {
        var result = CustomerLicenceService.Parse("MRS, YaNt ue UBI LEDU,\nLicence Number\n1028978", allowMissingClasses: true, expectedName: "Yani Ubiubi Ledua");

        Assert.Equal("Yani Ubiubi Ledua", result.FullName);
        Assert.Equal("1028978", result.LicenceNumber);
        Assert.Empty(result.Classes);
    }

    [Theory]
    [InlineData("2", "7", new[] { 2 })]
    [InlineData("2,3", "7", new[] { 2, 3 })]
    public void Reads_fiji_classes_only_below_the_class_label(string licenceClasses, string conditions, int[] expected)
    {
        var result = CustomerLicenceService.Parse($"""
            MR. SAMPLE CUSTOMER
            Licence Number
            1078038
            Full Licence Classes
            {licenceClasses}
            Chief Executive Officer
            Land Transport Authority
            Expiry Date       Conditions
            12/07/2029        {conditions}
            """);

        Assert.Equal(expected, result.Classes);
    }

    [Fact]
    public void Does_not_treat_a_conditions_number_as_a_licence_class_when_the_class_is_unreadable()
    {
        var result = CustomerLicenceService.Parse("""
            MR. SAMPLE CUSTOMER
            Licence Number
            1074426
            Full Licence Classes
            Chief Executive Officer
            Land Transport Authority
            Expiry Date       Conditions
            09/01/2029        7
            """, allowMissingClasses: true);

        Assert.Empty(result.Classes);
    }

    [Fact]
    public void Does_not_pair_a_class_label_with_a_conditions_digit_from_another_ocr_pass()
    {
        var result = CustomerLicenceService.Parse("""
            MR. SAMPLE CUSTOMER
            Licence Number 1074426
            Full Licence Classes
            [[CREMS_PASS_BREAK]]
            Conditions
            7
            [[CREMS_CENTRE_BEGIN]]
            Full Licence Classes
            2
            [[CREMS_CENTRE_END]]
            """);

        Assert.Equal([2], result.Classes);
    }

    [Fact]
    public void Accepts_a_standalone_class_only_inside_the_trusted_centre_crop()
    {
        var result = CustomerLicenceService.Parse("""
            MR. SAMPLE CUSTOMER
            Licence Number 1074426
            Conditions
            7
            [[CREMS_CENTRE_BEGIN]]
            2
            [[CREMS_CENTRE_END]]
            """);

        Assert.Equal([2], result.Classes);
    }

    [Fact]
    public void Prefers_the_labelled_centre_class_over_unrelated_standalone_digits()
    {
        var result = CustomerLicenceService.Parse("""
            MR. SAMPLE CUSTOMER
            Licence Number 1074426
            [[CREMS_CENTRE_BEGIN]]
            SAWANI 1
            1
            Full Licence Classes
            2
            [[CREMS_CENTRE_END]]
            """);

        Assert.Equal([2], result.Classes);
    }

    [Theory]
    [InlineData("Rakesh Kumar", "")]
    [InlineData("Rakesh Kumar", "---")]
    [InlineData("Rakesh Kumar", "Kumar")]
    [InlineData("Rakesh Kumar", "Rakesh Kumar Singh")]
    public void Partial_or_empty_names_do_not_verify_another_identity(string stored, string scanned) =>
        Assert.False(CustomerLicenceService.NamesMatch(stored, scanned));}
