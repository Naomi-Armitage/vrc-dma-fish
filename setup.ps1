$ProgressPreference = 'SilentlyContinue'
$TargetDirs = @("bin/Debug/net8.0", "bin/Release/net8.0", ".")
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "--- VrcDmaFish Setup: Pro Version ---" -ForegroundColor Cyan

$GithubUrl = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17/MemProcFS_files_and_binaries-win_x64-latest.zip"
$ZipFile = "memprocfs.zip"
$Success = $false

# 智能地域检测 (由于主人反馈镜像坏了，咱们改为先尝试直连喵)
$IsChina = $false
try {
    $ip = Invoke-RestMethod -Uri "http://ip-api.com/json/" -TimeoutSec 3
    if ($ip.countryCode -eq "CN") { $IsChina = $true }
} catch {}

$Urls = if ($IsChina) { @("https://ghp.ci/" + $GithubUrl, $GithubUrl) } else { @($GithubUrl) }

foreach ($url in $Urls) {
    Write-Host "Downloading from: $url" -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri $url -OutFile $ZipFile -TimeoutSec 120
        if (Test-Path $ZipFile) { $Success = $true; break }
    } catch { Write-Host "Link failed, retrying..." -ForegroundColor Gray }
}

if (-not $Success) { Write-Host "Download failed!" -ForegroundColor Red; return }

Write-Host "Deploying to all output directories..." -ForegroundColor Yellow
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force
$Dlls = Get-ChildItem -Path "temp_mem" -Recurse -Include "vmm.dll","leechcore.dll","FTD3XX.dll","FTD601.dll","info.db"

foreach ($dir in $TargetDirs) {
    if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir }
    foreach ($dll in $Dlls) {
        Copy-Item $dll.FullName -Destination $dir -Force
    }
    Write-Host "[OK] Deployed to: $dir" -ForegroundColor Green
}

Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"
Write-Host "Done! (≧▽≦)ゞ" -ForegroundColor Cyan
