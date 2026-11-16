using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using CREMS.Api.Data;
using CREMS.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CREMS.Api.Tests;

public sealed class AssessmentEmailDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Local_SMTP_acceptance_or_rejection_persists_delivery_status_and_retry_schedule(bool reject)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<string>();
        var receiver = Task.Run(async () => {
            using var client = await listener.AcceptTcpClientAsync(token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 localhost CREMS assessment receiver");
            while (await reader.ReadLineAsync(token) is { } command) {
                received.Add(command);
                if (command.StartsWith("EHLO") || command.StartsWith("HELO")) await writer.WriteLineAsync("250 localhost");
                else if (command == "DATA") {
                    await writer.WriteLineAsync("354 End data with a dot");
                    while (await reader.ReadLineAsync(token) is { } line && line != ".") received.Add(line);
                    await writer.WriteLineAsync(reject ? "550 Controlled assessment rejection" : "250 Controlled assessment accepted");
                    if (reject) break;
                } else if (command == "QUIT") { await writer.WriteLineAsync("221 Goodbye"); break; }
                else await writer.WriteLineAsync("250 OK");
            }
        }, token);
        var services = new ServiceCollection();
        var databaseName = $"assessment-mail-{Guid.NewGuid()}";
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        Guid id;
        using (var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var mail = new EmailQueue().Queue(db, "fictional-customer@example.test", "Assessment notification", "<p>Controlled message</p>", "Controlled message", "ManualNotification");
            id = mail.Id; using (db.SuppressNotifications()) await db.SaveChangesAsync(token);
        }
        var settings = Options.Create(new EmailOptions { Enabled = true, Host = "127.0.0.1", Port = port, UseSsl = false,
            Username = "local-test", Password = "local-test", PreferHttpApi = false, FromAddress = "crems@example.test", RedirectAllTo = "assessor@example.test" });
        using var worker = new EmailDeliveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), settings, new TestEnvironment(), NullLogger<EmailDeliveryWorker>.Instance, new NoExternalHttp());
        try {
            var process = typeof(EmailDeliveryWorker).GetMethod("Process", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)process.Invoke(worker, [token])!;
            await receiver.WaitAsync(token);
            using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var email = await db.OutboundEmails.SingleAsync(x => x.Id == id, token);
            Assert.Equal(reject ? EmailDeliveryStatus.Failed : EmailDeliveryStatus.Sent, email.Status);
            Assert.Equal(1, email.Attempts);
            Assert.Contains(received, line => line.Contains("RCPT TO:<assessor@example.test>", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(received, line => line.Contains("RCPT TO:<fictional-customer@example.test>", StringComparison.OrdinalIgnoreCase));
            if (reject) { Assert.Contains("Controlled assessment rejection", email.FailureReason); Assert.Null(email.SentAt); Assert.True(email.NextAttemptAt > DateTimeOffset.UtcNow); }
            else { Assert.NotNull(email.SentAt); Assert.Null(email.FailureReason); }
            var admin = new AdministrationController(db, new CurrentStaffScope(db), new ConfigurationBuilder().Build(), null!) {
                ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
            if (reject) {
                Assert.IsType<NoContentResult>(await admin.RetryEmail(id, token));
                Assert.Equal(EmailDeliveryStatus.Queued, email.Status); Assert.Equal(0, email.Attempts); Assert.Null(email.FailureReason);
            } else Assert.IsType<BadRequestObjectResult>(await admin.RetryEmail(id, token));
        } finally { listener.Stop(); }
    }

    private sealed class NoExternalHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => throw new InvalidOperationException("External email is prohibited in this test."); }
    private sealed class TestEnvironment : IHostEnvironment {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "CREMS.Assessment";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
