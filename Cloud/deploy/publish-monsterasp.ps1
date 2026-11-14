<#
.SYNOPSIS
    Builds Coduo POS Cloud and uploads it to MonsterASP.NET (Windows / IIS) over FTP.

.DESCRIPTION
    1. dotnet publish (Release, framework-dependent: MonsterASP provides the .NET 9 runtime).
    2. Unless -BuildOnly: puts app_offline.htm on the site, uploads the new files, removes app_offline.htm.

    Server settings: if Cloud\deploy\cloud.settings.json exists on this PC (copy it from cloud.settings.json.example), it is published
    with the site as cloud.settings.json. Visual Studio's publish does the same.
    It NEVER touches the server's data/ folder (control.db, sites/*.db, keys/), and never deletes anything on the server:
    files that no longer exist in a new build are left behind (harmless).

.PARAMETER Server
    FTP host from the MonsterASP panel (Websites > your site > FTP), e.g. site1234.siteasp.net.
.PARAMETER User
    FTP user from the same page.
.PARAMETER RemoteDir
    Folder on the server that is the site root. MonsterASP uses /wwwroot.
.PARAMETER BuildOnly
    Only build, and zip the result. Nothing is sent anywhere.
.PARAMETER PlainFtp
    Use unencrypted FTP. Only if the host refuses FTPS; the password then crosses the network in clear text.

    The password is read from $env:NEXPOS_FTP_PASSWORD, or asked for. It is never written to disk.

.EXAMPLE
    .\Cloud\deploy\publish-monsterasp.ps1 -BuildOnly
.EXAMPLE
    .\Cloud\deploy\publish-monsterasp.ps1 -Server site1234.siteasp.net -User site1234
#>
[CmdletBinding()]
param(
    [string]$Server,
    [string]$User,
    [string]$RemoteDir = '/wwwroot',
    [switch]$BuildOnly,
    [switch]$PlainFtp
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out  = Join-Path $repo 'publish\cloud'
$zip  = Join-Path $repo 'publish\nexpos-cloud.zip'

# ---- 1. Build -------------------------------------------------------------------------------------------------------
Write-Host "Publishing Cloud to $out ..." -ForegroundColor Cyan
if (Test-Path $out) {
    try { Remove-Item $out -Recurse -Force -ErrorAction Stop }
    catch {
        # Something (Explorer, an antivirus scan) holds a file in the old output: build into a fresh folder instead.
        $out = Join-Path $repo ('publish\cloud-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Write-Warning "Old publish folder is locked; using $out instead."
    }
}
dotnet publish (Join-Path $repo 'Cloud\Cloud.csproj') -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Never ship a local database, local secrets or this script's own folder.
Remove-Item (Join-Path $out 'data') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $out 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $out 'deploy') -Recurse -Force -ErrorAction SilentlyContinue

if (-not (Test-Path (Join-Path $out 'Cloud.dll')) -or -not (Test-Path (Join-Path $out 'web.config'))) {
    throw "Publish output is missing Cloud.dll or web.config."
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Built. Zip (for the panel's File Manager upload): $zip" -ForegroundColor Green

if ($BuildOnly) { return }

# ---- 2. Upload ------------------------------------------------------------------------------------------------------
if (-not $Server -or -not $User) { throw "Pass -Server and -User (or use -BuildOnly)." }

$plain = $env:NEXPOS_FTP_PASSWORD
if (-not $plain) {
    $secure = Read-Host "FTP password for $User@$Server" -AsSecureString
    $plain  = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$cred = New-Object System.Net.NetworkCredential($User, $plain)
$base = "ftp://$Server" + ('/' + $RemoteDir.Trim('/')).TrimEnd('/')

function Invoke-Ftp([string]$Url, [string]$Method, [byte[]]$Body = $null) {
    $req = [System.Net.FtpWebRequest]::Create($Url)
    $req.Method      = $Method
    $req.Credentials = $cred
    $req.EnableSsl   = -not $PlainFtp
    $req.UseBinary   = $true
    $req.UsePassive  = $true
    $req.KeepAlive   = $false
    if ($null -ne $Body) {
        $req.ContentLength = $Body.Length
        $s = $req.GetRequestStream(); $s.Write($Body, 0, $Body.Length); $s.Close()
    }
    $res = $req.GetResponse(); $res.Close()
}

function New-RemoteDir([string]$Url) {
    try { Invoke-Ftp $Url ([System.Net.WebRequestMethods+Ftp]::MakeDirectory) }
    catch [System.Net.WebException] { } # already exists
}

$offline = [Text.Encoding]::UTF8.GetBytes('<html><body style="font-family:sans-serif"><h2>Updating, back in a minute.</h2></body></html>')
$uploaded = 0
try {
    Write-Host "Taking the site offline while files are replaced ..." -ForegroundColor Cyan
    Invoke-Ftp "$base/app_offline.htm" ([System.Net.WebRequestMethods+Ftp]::UploadFile) $offline

    Get-ChildItem $out -Recurse -Directory | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($out.Length).TrimStart('\').Replace('\', '/')
        New-RemoteDir "$base/$rel"
    }
    Get-ChildItem $out -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($out.Length).TrimStart('\').Replace('\', '/')
        Invoke-Ftp "$base/$rel" ([System.Net.WebRequestMethods+Ftp]::UploadFile) ([IO.File]::ReadAllBytes($_.FullName))
        $uploaded++
        if ($uploaded % 25 -eq 0) { Write-Host "  $uploaded files ..." }
    }
}
finally {
    # Always bring the site back, even if the upload failed part-way (a half-uploaded site is still better than offline;
    # re-run the script to finish).
    try { Invoke-Ftp "$base/app_offline.htm" ([System.Net.WebRequestMethods+Ftp]::DeleteFile) }
    catch { Write-Warning "Could not remove app_offline.htm. Delete it in the panel's File Manager or the site stays offline." }
}

Write-Host "Uploaded $uploaded files. Check https://<your-domain>/healthz" -ForegroundColor Green
