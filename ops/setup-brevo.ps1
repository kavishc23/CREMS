param(
    [Parameter(Mandatory = $true)]
    [string]$SenderEmail
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '../src/CREMS.Api/CREMS.Api.csproj'
$secureKey = Read-Host 'Paste your Brevo API key (input is hidden)' -AsSecureString
$keyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($keyPointer)
    if ([string]::IsNullOrWhiteSpace($apiKey)) { throw 'No API key entered.' }
    $headers = @{ 'api-key' = $apiKey; Accept = 'application/json' }
    try {
        $account = Invoke-RestMethod -Uri 'https://api.brevo.com/v3/account' -Headers $headers
        $senders = Invoke-RestMethod -Uri 'https://api.brevo.com/v3/senders' -Headers $headers
    } catch {
        throw 'Brevo could not validate the key. Check the key, network connection, and any Brevo IP restrictions, then try again.'
    }
    $sender = $senders.senders | Where-Object { $_.email -eq $SenderEmail -and $_.active -eq $true }
    if (-not $sender) { throw "Verify $SenderEmail as an active sender in Brevo, then run this command again. Nothing was changed." }
    $settings = @{
        'Email:ApiKey' = $apiKey
        'Email:Enabled' = 'true'
        'Email:PreferHttpApi' = 'true'
        'Email:FromAddress' = $SenderEmail
        'Email:FromName' = 'Carpenters Rentals'
        'Email:RedirectAllTo' = $SenderEmail
        'Email:AllowDirectDeliveryOutsideProduction' = 'false'
        'Email:NotificationDirectDelivery' = 'false'
    }
    $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $projectPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not save local development settings.' }
    Write-Host 'Brevo key and sender verified. Settings saved in local .NET user secrets, outside the repository.'
    Write-Host "Test mode: ALL application emails are redirected to $SenderEmail. No email was sent by this setup command."
    Write-Host 'Return to the chat and say setup completed. The API needs a restart to load these settings.'
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($keyPointer)
    $apiKey = $null
    $headers = $null
    $settings = $null
    $secureKey.Dispose()
}

