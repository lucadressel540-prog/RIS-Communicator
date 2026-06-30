param(
    [string]$OutputPath = ".\dist\mobile-app",
    [switch]$PreserveVersion
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$target = Join-Path $root $OutputPath
$existingVersion = $null
$existingVersionPath = Join-Path $target "version.json"

if ($PreserveVersion -and (Test-Path $existingVersionPath)) {
    $existingVersion = Get-Content -LiteralPath $existingVersionPath -Raw
}

if (Test-Path $target) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
            break
        } catch {
            if ($attempt -eq 3 -and (Test-Path $target)) {
                throw
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$files = @(
    "index.html",
    "styles.css",
    "app.js",
    "manifest.webmanifest",
    "service-worker.js",
    "_worker.js"
)

foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $target $file) -Force
}

Copy-Item -LiteralPath (Join-Path $root "assets") -Destination (Join-Path $target "assets") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "tft") -Destination (Join-Path $target "tft") -Recurse -Force
if (Test-Path (Join-Path $root "functions")) {
    Copy-Item -LiteralPath (Join-Path $root "functions") -Destination (Join-Path $target "functions") -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $target "data") | Out-Null
Copy-Item -Path (Join-Path $root "data\*.json") -Destination (Join-Path $target "data") -Force

if ($existingVersion) {
    Set-Content -LiteralPath (Join-Path $target "version.json") -Value $existingVersion -Encoding UTF8
} else {
    $version = Get-Date -Format "yyyyMMddHHmmss"
    @{
        version = $version
        builtAt = (Get-Date).ToString("o")
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target "version.json") -Encoding UTF8
}

@"
/*
  Assets can be cached; app shell and runtime files must stay refreshable.
*/
/index.html
  Cache-Control: no-cache

/styles.css
  Cache-Control: no-cache

/app.js
  Cache-Control: no-cache

/manifest.webmanifest
  Cache-Control: no-cache

/assets/*
  Cache-Control: public, max-age=31536000, immutable

/data/trains.json
  Cache-Control: no-cache

/data/stations.json
  Cache-Control: no-cache

/data/data-version.json
  Cache-Control: no-cache

/version.json
  Cache-Control: no-cache

/service-worker.js
  Cache-Control: no-cache

/*
  X-Content-Type-Options: nosniff
"@ | Set-Content -LiteralPath (Join-Path $target "_headers") -Encoding UTF8

$zipPath = Join-Path $root "dist\mobile-app.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Push-Location $target
try {
    Compress-Archive -Path * -DestinationPath $zipPath
} finally {
    Pop-Location
}

Write-Host "Mobile Hosting Paket erstellt:" -ForegroundColor Green
Write-Host $target
Write-Host "ZIP erstellt:" -ForegroundColor Green
Write-Host $zipPath
