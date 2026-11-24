# Build script for Electron Desktop App
# This script publishes the .NET app and then builds the Electron package
#
#   .\build-electron.ps1 [win|win-installer|win-portable] [-Test]
#
# A normal build connects shops to the live Coduo POS Cloud (https://coduos.com).
# -Test builds one that connects to the test site (https://nexpos.tryasp.net) instead; never give a -Test build to a customer.

$liveServer = "https://coduos.com"
$testServer = "https://nexpos.tryasp.net"
$isTest = [bool]($args | Where-Object { $_ -eq '-Test' })
$server = if ($isTest) { $testServer } else { $liveServer }
$requestedType = $args | Where-Object { $_ -notlike '-*' } | Select-Object -First 1
if ($requestedType -and $requestedType -notin @('win', 'win-installer', 'win-portable')) {
    Write-Host "Invalid build type: $requestedType (valid: win, win-installer, win-portable)" -ForegroundColor Red
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Coduo POS Desktop App Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 0: Build the React client (client/ -> Web/wwwroot/app) so the publish below ships it
$clientPath = Join-Path $PSScriptRoot "client"
$hasClient = Test-Path (Join-Path $clientPath "package.json")

if ($hasClient) {
    Write-Host "Step 0: Building React client..." -ForegroundColor Yellow
    Write-Host ""

    Push-Location $clientPath
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to install client dependencies!" -ForegroundColor Red
            exit 1
        }
        # Also runs the bundle-budget check, so an over-budget bundle stops the installer build.
        npm run build
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to build the React client!" -ForegroundColor Red
            exit 1
        }
    } finally {
        Pop-Location
    }

    Write-Host "✓ React client built" -ForegroundColor Green
    Write-Host ""
}

# Step 1: Publish .NET Application
Write-Host "Step 1: Publishing .NET Application..." -ForegroundColor Yellow
Write-Host ""

$publishPath = Join-Path $PSScriptRoot "publish\app"

if (Test-Path $publishPath) {
    Write-Host "Cleaning existing publish folder..." -ForegroundColor Gray
    Remove-Item -Path $publishPath -Recurse -Force
}

Write-Host "Publishing to: $publishPath" -ForegroundColor Gray
dotnet publish "Web\Web.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:PublishReadyToRunComposite=false -p:SkipClientBuild=true -o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to publish .NET application!" -ForegroundColor Red
    exit 1
}

Write-Host "✓ .NET application published successfully" -ForegroundColor Green
Write-Host ""

# Which Coduo POS Cloud this build connects to (Online:Server in the shipped appsettings.json).
$appSettings = Join-Path $publishPath "appsettings.json"
$json = [System.IO.File]::ReadAllText($appSettings)
$pattern = '("Server"\s*:\s*")[^"]*(")'
if ($json -notmatch $pattern) {
    Write-Host "ERROR: Online:Server is missing from $appSettings" -ForegroundColor Red
    exit 1
}
$json = ([regex]$pattern).Replace($json, '${1}' + $server + '${2}', 1)
[System.IO.File]::WriteAllText($appSettings, $json, (New-Object System.Text.UTF8Encoding($false)))
if ($isTest) {
    Write-Host "✓ TEST build: connects to $server" -ForegroundColor Magenta
} else {
    Write-Host "✓ Live build: connects to $server" -ForegroundColor Green
}
Write-Host ""

# The installer is built from publish\app, so fail here (not on a customer's PC) if the SPA is missing from it.
if ($hasClient) {
    $spaIndex = Join-Path $publishPath "wwwroot\app\index.html"
    if (-not (Test-Path $spaIndex)) {
        Write-Host "ERROR: $spaIndex is missing - the React client was not included in the publish output!" -ForegroundColor Red
        exit 1
    }
    Write-Host "✓ React client present in publish output" -ForegroundColor Green
    Write-Host ""
}

# Step 2: Install npm dependencies (if needed)
Write-Host "Step 2: Checking npm dependencies..." -ForegroundColor Yellow
Write-Host ""

if (-not (Test-Path "node_modules")) {
    Write-Host "Installing npm dependencies..." -ForegroundColor Gray
    npm install
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to install npm dependencies!" -ForegroundColor Red
        exit 1
    }
    Write-Host "✓ npm dependencies installed" -ForegroundColor Green
} else {
    Write-Host "✓ npm dependencies already installed" -ForegroundColor Green
}
Write-Host ""

# Step 3: Build Electron package
Write-Host "Step 3: Building Electron package..." -ForegroundColor Yellow
Write-Host ""

$buildType = $args | Where-Object { $_ -notlike '-*' } | Select-Object -First 1
if (-not $buildType) {
    $buildType = "win"
    Write-Host "No build type specified, using default: $buildType" -ForegroundColor Gray
    Write-Host "Options: win, win-installer, win-portable" -ForegroundColor Gray
    Write-Host ""
}

$buildStart = Get-Date
switch ($buildType) {
    "win" { npm run build:win }
    "win-installer" { npm run build:win-installer }
    "win-portable" { npm run build:win-portable }
    default { 
        Write-Host "Invalid build type: $buildType" -ForegroundColor Red
        Write-Host "Valid options: win, win-installer, win-portable" -ForegroundColor Yellow
        exit 1
    }
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build Electron package!" -ForegroundColor Red
    exit 1
}

# A test build says so in its file name ("Coduo POS Setup 2.6.7 TEST.exe"), so it cannot be mistaken for a customer installer.
$built = @(Get-ChildItem -Path "dist" -Filter "*.exe" -File | Where-Object { $_.LastWriteTime -ge $buildStart })
if ($isTest) {
    $built = @($built | ForEach-Object {
        if ($_.BaseName -like '* TEST') { return $_ }
        $target = Join-Path $_.DirectoryName ($_.BaseName + ' TEST' + $_.Extension)
        Remove-Item $target -Force -ErrorAction SilentlyContinue
        Rename-Item -Path $_.FullName -NewName (Split-Path $target -Leaf)
        Get-Item $target
    })
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Built in the 'dist' folder ($server):" -ForegroundColor Yellow
$built | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host ""
