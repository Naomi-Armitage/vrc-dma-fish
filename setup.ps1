$ProgressPreference = 'SilentlyContinue'
$TargetDirs = @("bin/Debug/net8.0", "bin/Release/net8.0", ".")
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "--- VrcDmaFish Setup: Professional ---" -ForegroundColor Cyan

$GithubUrl = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17/MemProcFS_files_and_binaries-win_x64-latest.zip"
$ZipFile = "memprocfs.zip"
$Success = $false

$IsChina = $false
try {
    $ip = Invoke-RestMethod -Uri "http://ip-api.com/json/" -TimeoutSec 3
    if ($ip.countryCode -eq "CN") { $IsChina = $true }
} catch {}

$Urls = if ($IsChina) { @("https://ghp.ci/" + $GithubUrl, $GithubUrl) } else { @($GithubUrl) }

foreach ($url in $Urls) {
    Write-Host "Downloading from: $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $ZipFile -TimeoutSec 120
        if (Test-Path $ZipFile) { $Success = $true; break }
    } catch {
        Write-Host "Link failed, trying next..."
    }
}

if (-not $Success) {
    Write-Host "Download failed! Please check your network." -ForegroundColor Red
    return
}

Write-Host "Deploying driver files..."
if (Test-Path "temp_mem") { Remove-Item -Recurse -Force "temp_mem" }
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$Dlls = Get-ChildItem -Path "temp_mem" -Recurse -Include "vmm.dll","leechcore.dll","FTD3XX.dll","FTD601.dll","info.db"

foreach ($dir in $TargetDirs) {
    if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir }
    foreach ($dll in $Dlls) {
        Copy-Item $dll.FullName -Destination $dir -Force
    }
    Write-Host "Deployed to: $dir"
}

Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host ""
Write-Host "Setup finished successfully!"
Write-Host "You can now run 'dotnet run' to start."
