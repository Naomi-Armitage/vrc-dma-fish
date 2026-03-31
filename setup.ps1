$ProgressPreference = 'SilentlyContinue'
$TargetDir = "bin/Debug/net8.0"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir
}

Write-Host "--- VrcDmaFish Setup: Intelligent Mode ---" -ForegroundColor Cyan

# 1. 检测地理位置喵
Write-Host "Detecting location..." -ForegroundColor Yellow
$IsChina = $false
try {
    $ipInfo = Invoke-RestMethod -Uri "http://ip-api.com/json/" -TimeoutSec 5
    if ($ipInfo.countryCode -eq "CN") {
        $IsChina = $true
        Write-Host "Location: Domestic (CN). Using mirror..." -ForegroundColor Green
    } else {
        Write-Host "Location: Overseas ($($ipInfo.countryCode)). Using direct link..." -ForegroundColor Green
    }
} catch {
    Write-Host "Location detection failed, defaulting to direct link..." -ForegroundColor Gray
}

# 2. 准备主人指定的最新链接喵
$BaseUrl = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17/MemProcFS_files_and_binaries-win_x64-latest.zip"
$DownloadUrl = $BaseUrl

if ($IsChina) {
    $DownloadUrl = "https://mirror.ghproxy.com/" + $BaseUrl
}

$ZipFile = "memprocfs.zip"

# 3. 下载喵
Write-Host "Downloading from: $DownloadUrl" -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $ZipFile -TimeoutSec 120
} catch {
    Write-Host "Download failed! Please check your network or URL." -ForegroundColor Red
    return
}

# 4. 部署文件喵
Write-Host "Extracting files..." -ForegroundColor Yellow
if (Test-Path "temp_mem") { Remove-Item -Recurse -Force "temp_mem" }
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$FilesToCopy = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "FTD601.dll", "info.db")
# 递归搜索所有子目录，防止 ZIP 内部结构变化喵
$allFiles = Get-ChildItem -Path "temp_mem" -Recurse

foreach ($file in $FilesToCopy) {
    $found = $allFiles | Where-Object { $_.Name -eq $file } | Select-Object -First 1
    if ($found) {
        Copy-Item $found.FullName -Destination $TargetDir -Force
        Write-Host "[OK] Deployed: $file" -ForegroundColor Green
    }
}

Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host ""
Write-Host "Setup finished successfully! High-speed DMA ready." -ForegroundColor Cyan
