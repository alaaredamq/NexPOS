<#
  Builds NexPOS, seeds a fresh demo database, records the client demo video and converts it to MP4.
  Nothing here touches your real NexPOS database or printers:
    - the database lives in out\demo.db (NEXPOS_DB_PATH)
    - NEXPOS_DEMO=1 swaps the receipt printer for a no-op
  Usage:  powershell -File tools\demo-video\record.ps1            (add -SkipBuild to reuse the last build)
  Output: tools\demo-video\out\NexPOS-Demo.mp4
#>
param([switch]$SkipBuild, [int]$Port = 5055)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$out  = Join-Path $here 'out'
$web  = Join-Path $repo 'Web'

if (-not (Test-Path (Join-Path $here 'node_modules'))) { Push-Location $here; npm install --no-audit --no-fund; Pop-Location }

if (-not $SkipBuild) { dotnet build (Join-Path $web 'Web.csproj') -v q --nologo; if ($LASTEXITCODE) { throw 'build failed' } }

New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item (Join-Path $out 'demo.db*') -Force -ErrorAction SilentlyContinue

$env:NEXPOS_DB_PATH = Join-Path $out 'demo.db'
$env:NEXPOS_DEMO = '1'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:Urls = "http://localhost:$Port"
$env:Jwt__SecretKey = 'demo-only-secret-key-not-used-anywhere-else-1234567890'
# The default limit (100 requests/min/IP) is easy to hit while a scripted browser loads pages.
$env:IpRateLimiting__GeneralRules__0__Limit = '100000'
$env:IpRateLimiting__GeneralRules__1__Limit = '1000000'

$server = Start-Process dotnet -ArgumentList @((Join-Path $web 'bin\Debug\net9.0\Web.dll'), '--seed-demo') `
          -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
          -RedirectStandardOutput (Join-Path $out 'server.log') -RedirectStandardError (Join-Path $out 'server.err')
try {
  $ready = $false
  1..90 | ForEach-Object { if (-not $ready) { Start-Sleep 2; try { Invoke-WebRequest "http://localhost:$Port/en/account/login" -UseBasicParsing | Out-Null; $ready = $true } catch {} } }
  if (-not $ready) { throw 'server did not start - see out\server.log' }

  $env:BASE_URL = "http://localhost:$Port"
  $env:OUT_DIR = $out
  Push-Location $here; node record-demo.js; $rc = $LASTEXITCODE; Pop-Location
  if ($rc) { throw 'recording failed' }
}
finally { if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force } }

$ffmpeg = node -e "console.log(require('$($here.Replace('\','/'))/node_modules/ffmpeg-static'))"
& $ffmpeg -y -hide_banner -loglevel error -ss 0.4 -i (Join-Path $out 'raw.webm') -c:v libx264 -preset slow -crf 19 -pix_fmt yuv420p -r 25 -movflags +faststart (Join-Path $out 'NexPOS-Demo.mp4')
Write-Host "Done: $(Join-Path $out 'NexPOS-Demo.mp4')"
