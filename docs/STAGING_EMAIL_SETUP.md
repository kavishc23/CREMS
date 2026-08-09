# CREMS staging email setup

CREMS uses SMTP and works with Postmark, SendGrid, Mailtrap, Amazon SES, Microsoft 365, and other transactional providers.

## Safety rules

- Use a separate staging SMTP credential.
- Never commit the SMTP password.
- `Email__RedirectAllTo` is mandatory outside Production when email is enabled.
- Set the redirect to a mailbox controlled by the development team.
- Staging subjects are automatically prefixed with `[STAGING]`.

For deliberate end-to-end testing with approved real recipients, set `Email__AllowDirectDeliveryOutsideProduction=true` instead of a redirect. Check the queued recipient list first and turn this option off after the test.

## Required staging environment variables

```text
ASPNETCORE_ENVIRONMENT=Staging
Email__Enabled=true
Email__Host=smtp.provider.example
Email__Port=587
Email__UseSsl=true
Email__Username=staging-smtp-username
Email__Password=staging-smtp-password
Email__FromAddress=crems-staging@your-verified-domain.example
Email__FromName=Carpenters Rentals - Staging
Email__RedirectAllTo=your-team-test-inbox@example.com
FrontendUrl=https://your-staging-web-address.example
```

For local testing, store values with .NET user secrets:

```bash
dotnet user-secrets set "Email:Enabled" "true" --project src/CREMS.Api
dotnet user-secrets set "Email:Host" "smtp.provider.example" --project src/CREMS.Api
dotnet user-secrets set "Email:Port" "587" --project src/CREMS.Api
dotnet user-secrets set "Email:UseSsl" "true" --project src/CREMS.Api
dotnet user-secrets set "Email:Username" "staging-smtp-username" --project src/CREMS.Api
dotnet user-secrets set "Email:Password" "staging-smtp-password" --project src/CREMS.Api
dotnet user-secrets set "Email:FromAddress" "crems-staging@your-domain.example" --project src/CREMS.Api
dotnet user-secrets set "Email:RedirectAllTo" "your-team-test-inbox@example.com" --project src/CREMS.Api
```

Restart the API after changing configuration. Database migrations apply automatically when the API starts.

## Test procedure

1. Start the API in Staging.
2. On the staff login screen, select **Forgot password?**
3. Enter an active staff email address.
4. Confirm delivery goes to `Email__RedirectAllTo`, not the staff address.
5. Enter the six-digit code and a new password.
6. Confirm the code cannot be reused and expires after ten minutes.
7. Complete a rental return with a customer email and confirm the invoice message arrives in the staging inbox.
8. Check queued and failed message counts under Administration → Security & system.

## Common SMTP endpoints

| Provider | SMTP host | Typical port |
|---|---|---:|
| Postmark | `smtp.postmarkapp.com` | 587 |
| SendGrid | `smtp.sendgrid.net` | 587 |
| Mailtrap Email Sandbox | Shown in its integration screen | 2525 or 587 |
| Amazon SES | Region-specific SMTP endpoint | 587 |

Use the current settings shown by your selected provider.
