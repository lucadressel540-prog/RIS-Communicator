param(
    [string]$ConfigPath = ".\cloudflare.publish.local.json"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$resolvedConfigPath = Join-Path $root $ConfigPath

if (-not (Test-Path $resolvedConfigPath)) {
    throw "Cloudflare-Konfiguration fehlt: $resolvedConfigPath"
}

$config = Get-Content -LiteralPath $resolvedConfigPath -Raw | ConvertFrom-Json

$apiToken = ""
if ($config.PSObject.Properties.Name -contains "ApiToken") {
    $apiToken = [string]$config.ApiToken
}
if ([string]::IsNullOrWhiteSpace($apiToken) -and -not [string]::IsNullOrWhiteSpace($env:CLOUDFLARE_API_TOKEN)) {
    $apiToken = $env:CLOUDFLARE_API_TOKEN
}
if ([string]::IsNullOrWhiteSpace($apiToken)) {
    throw "Cloudflare-Upload nicht moeglich: ApiToken fehlt in cloudflare.publish.local.json oder als Windows-Umgebungsvariable CLOUDFLARE_API_TOKEN."
}

$accountId = ""
if ($config.PSObject.Properties.Name -contains "AccountId") {
    $accountId = [string]$config.AccountId
}
if ([string]::IsNullOrWhiteSpace($accountId) -and -not [string]::IsNullOrWhiteSpace($env:CLOUDFLARE_ACCOUNT_ID)) {
    $accountId = $env:CLOUDFLARE_ACCOUNT_ID
}

$namespaceId = ""
if ($config.PSObject.Properties.Name -contains "KvNamespaceId") {
    $namespaceId = [string]$config.KvNamespaceId
}
if ([string]::IsNullOrWhiteSpace($namespaceId) -and -not [string]::IsNullOrWhiteSpace($env:CLOUDFLARE_KV_NAMESPACE_ID)) {
    $namespaceId = $env:CLOUDFLARE_KV_NAMESPACE_ID
}

$namespaceName = "ris-Kommunikator-Zustand"
if ($config.PSObject.Properties.Name -contains "KvNamespaceName" -and -not [string]::IsNullOrWhiteSpace([string]$config.KvNamespaceName)) {
    $namespaceName = [string]$config.KvNamespaceName
}

$headers = @{
    Authorization = "Bearer $apiToken"
}

function Invoke-CloudflareJson {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $parameters = @{
        Uri     = $Uri
        Method  = $Method
        Headers = $headers
    }

    if ($null -ne $Body) {
        $parameters.ContentType = "application/json"
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        return Invoke-RestMethod @parameters
    } catch {
        $details = ""
        if ($_.ErrorDetails.Message) {
            $details = " Details: $($_.ErrorDetails.Message)"
        }
        throw "Cloudflare API-Aufruf fehlgeschlagen: $Uri.$details"
    }
}

if ([string]::IsNullOrWhiteSpace($accountId)) {
    $accounts = Invoke-CloudflareJson -Uri "https://api.cloudflare.com/client/v4/accounts"
    if (-not $accounts.success) {
        throw "Cloudflare Konto konnte nicht gelesen werden."
    }
    if ($accounts.result.Count -ne 1) {
        throw "AccountId fehlt in cloudflare.publish.local.json. Es wurden $($accounts.result.Count) Cloudflare-Konten gefunden."
    }
    $accountId = [string]$accounts.result[0].id
}

if ([string]::IsNullOrWhiteSpace($namespaceId)) {
    $namespaces = Invoke-CloudflareJson -Uri "https://api.cloudflare.com/client/v4/accounts/$accountId/storage/kv/namespaces"
    if (-not $namespaces.success) {
        throw "Cloudflare KV-Namensraeume konnten nicht gelesen werden."
    }

    $matches = @($namespaces.result | Where-Object {
        $_.title -ieq $namespaceName -or $_.title -ieq "RIS_STATE"
    })

    if ($matches.Count -eq 1) {
        $namespaceId = [string]$matches[0].id
    } elseif ($namespaces.result.Count -eq 1) {
        $namespaceId = [string]$namespaces.result[0].id
    } else {
        $knownNames = ($namespaces.result | ForEach-Object { $_.title }) -join ", "
        throw "KvNamespaceId fehlt in cloudflare.publish.local.json. Gefundene Namensraeume: $knownNames"
    }
}

& (Join-Path $PSScriptRoot "build-mobile-hosting.ps1") -PreserveVersion | Out-Null

$trainsPath = Join-Path $root "data\trains.json"
$versionPath = Join-Path $root "data\data-version.json"

if (-not (Test-Path $trainsPath)) {
    throw "data/trains.json fehlt."
}

if (-not (Test-Path $versionPath)) {
    @{
        version   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        updatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    } | ConvertTo-Json | Set-Content -LiteralPath $versionPath -Encoding UTF8
}

$kvBase = "https://api.cloudflare.com/client/v4/accounts/$accountId/storage/kv/namespaces/$namespaceId/values"

try {
    Invoke-WebRequest `
        -Uri "$kvBase/trains" `
        -Method Put `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -InFile $trainsPath `
        -UseBasicParsing | Out-Null

    Invoke-WebRequest `
        -Uri "$kvBase/data-version" `
        -Method Put `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -InFile $versionPath `
        -UseBasicParsing | Out-Null
} catch {
    $details = ""
    if ($_.ErrorDetails.Message) {
        $details = " Details: $($_.ErrorDetails.Message)"
    }
    throw "Cloudflare KV-Upload fehlgeschlagen.$details"
}

Write-Host "Cloudflare KV-Daten sind aktualisiert." -ForegroundColor Green
