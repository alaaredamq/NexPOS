# Build script for Electron Desktop App
# This script publishes the .NET app and then builds the Electron package

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NexPos Desktop App Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Publish .NET Application
Write-Host "Step 1: Publishing .NET Application..." -ForegroundColor Yellow
Write-Host ""

$publishPath = Join-Path $PSScriptRoot "publish\app"

if (Test-Path $publishPath) {
    Write-Host "Cleaning existing publish folder..." -ForegroundColor Gray
    Remove-Item -Path $publishPath -Recurse -Force
}

Write-Host "Publishing to: $publishPath" -ForegroundColor Gray
dotnet publish "Web\Web.csproj" -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -p:PublishReadyToRunComposite=false -o $publishPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to publish .NET application!" -ForegroundColor Red
    exit 1
}

Write-Host "✓ .NET application published successfully" -ForegroundColor Green
Write-Host ""

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

$buildType = $args[0]
if (-not $buildType) {
    $buildType = "win"
    Write-Host "No build type specified, using default: $buildType" -ForegroundColor Gray
    Write-Host "Options: win, win-installer, win-portable" -ForegroundColor Gray
    Write-Host ""
}

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

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Your EXE files are in the 'dist' folder:" -ForegroundColor Yellow
Get-ChildItem -Path "dist" -Filter "*.exe" | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}
Write-Host ""
