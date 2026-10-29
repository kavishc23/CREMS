param([switch]$Setup)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Run-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE). See the output above." }
}
function Read-Secret([string]$Prompt) {
    $secret = Read-Host $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$api = $null
try {
    foreach ($command in @('dotnet', 'node', 'npm.cmd')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "Missing $command. Install prerequisites from docs/CLIENT_HANDOVER.md, then reopen PowerShell."
        }
    }
    Run-Checked 'dotnet' @('--version')
    if ([int]((& node --version).TrimStart('v').Split('.')[0]) -lt 22) { throw 'Node.js 22 or newer is required.' }

    $settingsFile = Join-Path (Get-Location) '.env.windows.json'
    if ($Setup -or -not (Test-Path $settingsFile)) {
        if (Test-Path $settingsFile) { throw 'Settings already exist. Edit .env.windows.json to change them; setup will not overwrite credentials.' }
        Write-Host 'First-time setup. Settings stay on this laptop; never share .env.windows.json.'
        $mode = Read-Host 'Database: enter docker (Docker Desktop) or existing (installed SQL Server)'
        if ($mode -notin @('docker', 'existing')) { throw 'Choose docker or existing.' }
        $settings = [ordered]@{ DatabaseMode = $mode; AdminEmail = (Read-Host 'Initial administrator email'); AdminPassword = (Read-Secret 'Initial administrator password (10+ characters, uppercase, lowercase, digit, symbol)'); AdminName = 'System Administrator' }
        if ($mode -eq 'docker') {
            $settings.DatabasePassword = Read-Secret 'New SQL Server password (12+ characters, uppercase, lowercase, digit, symbol)'
            if ($settings.DatabasePassword.Length -lt 12 -or $settings.DatabasePassword -notmatch '[A-Z]' -or $settings.DatabasePassword -notmatch '[a-z]' -or $settings.DatabasePassword -notmatch '[0-9]' -or $settings.DatabasePassword -notmatch '[^a-zA-Z0-9]') { throw 'SQL Server password does not meet the stated requirements.' }
        } else {
            $settings.ConnectionString = Read-Secret 'SQL Server connection string (account needs database creation/migration permissions)'
        }
        if ($settings.AdminEmail -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$' -or $settings.AdminPassword.Length -lt 10 -or $settings.AdminPassword -notmatch '[A-Z]' -or $settings.AdminPassword -notmatch '[a-z]' -or $settings.AdminPassword -notmatch '[0-9]' -or $settings.AdminPassword -notmatch '[^a-zA-Z0-9]') { throw 'Enter a valid email and a password meeting the stated requirements.' }
        $settings | ConvertTo-Json | Set-Content -Encoding UTF8 $settingsFile
    }
    $settings = Get-Content -Raw $settingsFile | ConvertFrom-Json
    if ($settings.DatabaseMode -eq 'docker') {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Install and open Docker Desktop, or configure an existing SQL Server.' }
        $env:CREMS_DB_PASSWORD = $settings.DatabasePassword
        Run-Checked 'docker' @('info', '--format', '{{.OSType}}')
        Run-Checked 'docker' @('compose', 'up', '-d', 'sqlserver')
        $container = & docker compose ps -q sqlserver
        if (-not $container) { throw 'SQL Server container did not start.' }
        $healthy = $false
        for ($attempt = 0; $attempt -lt 90; $attempt++) {
            $health = & docker inspect --format '{{.State.Health.Status}}' $container
            if ($health -eq 'healthy') { $healthy = $true; break }
            Start-Sleep -Seconds 2
        }
        if (-not $healthy) { throw 'SQL Server did not become healthy. Run docker compose logs sqlserver. Existing database volumes retain their original password.' }
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
        $builder['Data Source'] = 'localhost,1433'
        $builder['Initial Catalog'] = 'Crems'
        $builder['User ID'] = 'sa'
        $builder['Password'] = $settings.DatabasePassword
        $builder['TrustServerCertificate'] = $true
        $env:ConnectionStrings__DefaultConnection = $builder.ConnectionString
    } elseif ($settings.DatabaseMode -eq 'existing' -and $settings.ConnectionString) {
        $env:ConnectionStrings__DefaultConnection = $settings.ConnectionString
    } else { throw 'Invalid database settings in .env.windows.json.' }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:BootstrapAdmin__Email = $settings.AdminEmail
    $env:BootstrapAdmin__Password = $settings.AdminPassword
    $env:BootstrapAdmin__FullName = $settings.AdminName
    $env:Email__Enabled = 'false'
    $env:CREMS_API_URL = 'http://localhost:5080'
    $env:CREMS_FRONTEND_URL = 'http://localhost:5173'
    $env:FrontendUrl = $env:CREMS_FRONTEND_URL
    foreach ($port in @(5080, 5173)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { throw "Port $port is occupied. Stop the existing application first." }
    }
    Run-Checked 'dotnet' @('restore', 'src/CREMS.Api/CREMS.Api.csproj')
    $lockHash = (Get-FileHash 'src/crems-web/package-lock.json' -Algorithm SHA256).Hash
    $dependencyStamp = 'src/crems-web/node_modules/.crems-lock-hash'
    if (-not (Test-Path $dependencyStamp) -or (Get-Content -Raw $dependencyStamp).Trim() -ne $lockHash) {
        Run-Checked 'npm.cmd' @('--prefix', 'src/crems-web', 'ci', '--no-audit', '--no-fund')
        Set-Content -Path $dependencyStamp -Value $lockHash
    }
    Run-Checked 'dotnet' @('build', 'src/CREMS.Api/CREMS.Api.csproj', '--no-restore')
    $api = Start-Process dotnet -ArgumentList @('run', '--project', 'src/CREMS.Api', '--no-build', '--no-launch-profile', '--urls', $env:CREMS_API_URL) -PassThru -NoNewWindow
    $ready = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if ($api.HasExited) { throw 'API stopped. Review its error above.' }
        try {
            $response = Invoke-WebRequest "$env:CREMS_API_URL/api/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'API did not become ready. Review the database connection and API output.' }
    Write-Host 'CREMS is ready at http://localhost:5173. Sign in with your administrator email. Press Ctrl+C to stop.'
    Run-Checked 'npm.cmd' @('--prefix', 'src/crems-web', 'run', 'dev')
} catch {
    Write-Host "CREMS: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -ErrorAction SilentlyContinue }
    foreach ($name in @('ConnectionStrings__DefaultConnection', 'BootstrapAdmin__Password', 'CREMS_DB_PASSWORD')) {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
}
