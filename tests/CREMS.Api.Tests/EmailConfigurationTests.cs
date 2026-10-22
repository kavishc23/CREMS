using CREMS.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class EmailConfigurationTests
{
    [Theory]
    [InlineData(true, true, "key", "", "", "", "sender@example.test", true)]
    [InlineData(true, true, "", "smtp.example.test", "user", "password", "sender@example.test", true)]
    [InlineData(true, false, "key", "smtp.example.test", "user", "password", "sender@example.test", true)]
    [InlineData(true, false, "key", "", "", "", "sender@example.test", false)]
    [InlineData(false, true, "key", "", "", "", "sender@example.test", false)]
    [InlineData(true, true, "key", "", "", "", " ", false)]
    [InlineData(true, true, " ", "", "", "", "sender@example.test", false)]
    [InlineData(true, true, "", "smtp.example.test", "", "password", "sender@example.test", false)]
    [InlineData(true, true, "", "smtp.example.test", "user", " ", "sender@example.test", false)]
    public void Health_checks_the_configured_delivery_transport(bool enabled, bool preferApi, string key,
        string host, string user, string password, string sender, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Enabled"] = enabled.ToString(), ["Email:PreferHttpApi"] = preferApi.ToString(),
            ["Email:ApiKey"] = key, ["Email:Host"] = host, ["Email:Username"] = user,
            ["Email:Password"] = password, ["Email:FromAddress"] = sender,
        }).Build();
        Assert.Equal(expected, config.GetSection(EmailOptions.Section).Get<EmailOptions>()!.IsProviderConfigured);
    }

    [Fact]
    public void Missing_email_configuration_is_not_ready() => Assert.False(new EmailOptions().IsProviderConfigured);
}
