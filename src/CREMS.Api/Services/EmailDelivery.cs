using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using System.Text.Json;
using CREMS.Api.Data;
using CREMS.Api.Domain.Identity;
using CREMS.Api.Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CREMS.Api.Services;

public sealed class EmailOptions
{
    public const string Section = "Email";
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Carpenters Rentals";
    public string? RedirectAllTo { get; set; }
    public bool AllowDirectDeliveryOutsideProduction { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public bool PreferHttpApi { get; set; } = true;
}

public interface IEmailQueue
{
    OutboundEmail Queue(ApplicationDbContext db, string recipient, string subject, string htmlBody, string? textBody = null, string? category = null);
}

public sealed class EmailQueue : IEmailQueue
{
    public OutboundEmail Queue(ApplicationDbContext db, string recipient, string subject, string htmlBody, string? textBody = null, string? category = null)
    {
        var email = new OutboundEmail { Recipient = recipient.Trim(), Subject = subject.Trim(), HtmlBody = htmlBody, TextBody = textBody, Category = category };
        db.OutboundEmails.Add(email);
        return email;
    }
}

public static class EmailTemplate
{
    public static string Branded(string heading, string content, string brandName = "Carpenters Rentals") => $"""
        <!doctype html><html><body style="margin:0;background:#f5f5f2;font-family:Arial,sans-serif;color:#171717">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr><td align="center" style="padding:32px 12px">
        <table role="presentation" width="600" style="max-width:100%;background:#fff;border:1px solid #deded8;border-radius:10px;overflow:hidden">
        <tr><td style="background:#ffed00;padding:22px 28px"><strong style="font-size:22px">{HtmlEncoder.Default.Encode(brandName)}</strong><br><span>Carpenters Group · CREMS</span></td></tr>
        <tr><td style="padding:30px"><h1 style="font-size:24px;margin:0 0 18px">{HtmlEncoder.Default.Encode(heading)}</h1>{content}</td></tr>
        <tr><td style="padding:18px 30px;background:#111;color:#fff;font-size:12px">Carpenters Fiji · This is an automated operational message.</td></tr>
        </table></td></tr></table></body></html>
        """;
}

public sealed class EmailDeliveryWorker(IServiceScopeFactory scopeFactory, IOptions<EmailOptions> options, IHostEnvironment environment, ILogger<EmailDeliveryWorker> logger, IHttpClientFactory httpClients) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (options.Value.Enabled) await Process(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Email delivery cycle failed."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task Process(CancellationToken token)
    {
        ValidateConfiguration();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var messages = await db.OutboundEmails.Where(x => (x.Status == EmailDeliveryStatus.Queued || x.Status == EmailDeliveryStatus.Failed) && x.Attempts < 5 && x.NextAttemptAt <= now).OrderBy(x => x.CreatedAt).Take(10).ToListAsync(token);
        foreach (var message in messages)
        {
            message.Status = EmailDeliveryStatus.Sending; message.Attempts++; await db.SaveChangesAsync(token);
            try { message.ProviderMessageId=await Send(message.Recipient, message.Subject, message.HtmlBody, message.TextBody, token); message.Status = EmailDeliveryStatus.Sent; message.SentAt = DateTimeOffset.UtcNow; message.FailureReason = null; }
            catch (Exception ex) { message.Status = EmailDeliveryStatus.Failed; message.FailureReason = Failure(ex); message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, message.Attempts)); logger.LogError(ex,"SMTP delivery failed for message {MessageId}, category {Category}, attempt {Attempt}.",message.Id,message.Category,message.Attempts); }
            await db.SaveChangesAsync(token);
        }
        var rentals = await db.RentalNotifications.Include(x => x.Booking).Where(x => x.Channel == NotificationChannel.Email && (x.Status == NotificationStatus.Queued || x.Status == NotificationStatus.Failed) && x.Attempts < 5 && x.NextAttemptAt <= now).OrderBy(x => x.CreatedAt).Take(10).ToListAsync(token);
        foreach (var message in rentals)
        {
            message.Attempts++;
            try { var html = EmailTemplate.Branded(message.Subject, $"<p style=\"line-height:1.6\">{HtmlEncoder.Default.Encode(message.Message)}</p>"); await Send(message.Recipient, message.Subject, html, message.Message, token); message.Status = NotificationStatus.Sent; message.SentAt = DateTimeOffset.UtcNow; message.FailureReason = null; }
            catch (Exception ex) { message.Status = NotificationStatus.Failed; message.FailureReason = Failure(ex); message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, message.Attempts)); logger.LogError(ex,"SMTP rental notification delivery failed on attempt {Attempt}.",message.Attempts); }
            await db.SaveChangesAsync(token);
        }
    }

    private async Task<string?> Send(string recipient, string subject, string html, string? text, CancellationToken token)
    {
        var settings = options.Value; var actualRecipient = string.IsNullOrWhiteSpace(settings.RedirectAllTo) ? recipient : settings.RedirectAllTo;
        var actualSubject = environment.IsProduction() ? subject : $"[{environment.EnvironmentName.ToUpperInvariant()}] {subject}";
        if(settings.PreferHttpApi&&!string.IsNullOrWhiteSpace(settings.ApiKey))return await SendWithBrevoApi(actualRecipient!,actualSubject,html,text,token);
        using var message = new MailMessage { From = new MailAddress(settings.FromAddress, settings.FromName), Subject = actualSubject, Body = html, IsBodyHtml = true };
        message.To.Add(actualRecipient!); if (!string.IsNullOrWhiteSpace(text)) message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(text, null, "text/plain"));
        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.UseSsl, UseDefaultCredentials = false, Credentials = new NetworkCredential(settings.Username, settings.Password), DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 20_000 };
        await client.SendMailAsync(message, token);
        return null;
    }

    private async Task<string?> SendWithBrevoApi(string recipient,string subject,string html,string? plainText,CancellationToken token)
    {
        var settings=options.Value;var client=httpClients.CreateClient("Brevo");using var request=new HttpRequestMessage(HttpMethod.Post,"v3/smtp/email");request.Headers.Add("api-key",settings.ApiKey);request.Content=JsonContent.Create(new{sender=new{name=settings.FromName,email=settings.FromAddress},to=new[]{new{email=recipient}},subject,htmlContent=html,textContent=plainText,tags=new[]{"crems"}});using var response=await client.SendAsync(request,token);var body=await response.Content.ReadAsStringAsync(token);if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"Brevo API returned {(int)response.StatusCode}: {body[..Math.Min(body.Length,500)]}");using var json=JsonDocument.Parse(body);return json.RootElement.TryGetProperty("messageId",out var id)?id.GetString():null;
    }

    private void ValidateConfiguration()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.FromAddress)) throw new InvalidOperationException("Email is enabled but the sender address is missing.");
        if(string.IsNullOrWhiteSpace(settings.ApiKey)&&(string.IsNullOrWhiteSpace(settings.Host)||string.IsNullOrWhiteSpace(settings.Username)||string.IsNullOrWhiteSpace(settings.Password)))throw new InvalidOperationException("Configure either Email:ApiKey for Brevo HTTPS delivery or all SMTP credentials.");
        if (!environment.IsProduction() && string.IsNullOrWhiteSpace(settings.RedirectAllTo) && !settings.AllowDirectDeliveryOutsideProduction) throw new InvalidOperationException("Set Email:RedirectAllTo or explicitly enable Email:AllowDirectDeliveryOutsideProduction.");
    }
    private static string Failure(Exception ex)
    {
        var parts=new List<string>();for(var current=ex;current is not null;current=current.InnerException)parts.Add(current is SmtpException smtp?$"{current.GetType().Name} ({smtp.StatusCode}): {current.Message}":$"{current.GetType().Name}: {current.Message}");
        var text=string.Join(" -> ",parts);return text[..Math.Min(text.Length,1000)];
    }
}
