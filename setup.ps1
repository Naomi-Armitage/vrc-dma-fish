# VrcDmaFish Setup Script - High Speed Mirror Version (∠・ω< )⌒★
$ProgressPreference = 'SilentlyContinue'
$TargetDir = "bin/Debug/net8.0"

# 强制使用 TLS 1.2，防止旧版系统下载失败
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir
}

Write-Host "--- VrcDmaFish Environment Setup (Mirror Mode) ---" -ForegroundColor Cyan
Write-Host "Preparing DMA libraries for master..." -ForegroundColor Cyan

$GithubUrl = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17.0/MemProcFS_v5.17.0_win_x64.zip"
# 加速镜像列表喵
$Mirrors = @(
    "https://mirror.ghproxy.com/$GithubUrl",
    "https://ghproxy.net/$GithubUrl",
    $GithubUrl
)

$ZipFile = "memprocfs.zip"
$Success = $false

foreach ($url in $Mirrors) {
    Write-Host "Trying download from: $url" -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri $url -OutFile $ZipFile -TimeoutSec 60
        if ((Get-Item $ZipFile).Length -gt 1MB) {
            $Success = $true
            Write-Host "Download Success! (≧▽≦)ゞ" -ForegroundColor Green
            break
        }
    } catch {
        Write-Host "This link failed, trying next..." -ForegroundColor Gray
    }
}

if (-not $Success) {
    Write-Host "!!! ALL DOWNLOADS FAILED !!!" -ForegroundColor Red
    Write-Host "Please download manually from: $GithubUrl" -ForegroundColor Red
    Write-Host "Then extract vmm.dll/FTD601.dll to $TargetDir" -ForegroundColor Red
    return
}

# 解压并移动核心 DLL
Write-Host "Extracting and deploying files..." -ForegroundColor Yellow
if (Test-Path "temp_mem") { Remove-Item -Recurse -Force "temp_mem" }
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$FilesToCopy = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "FTD601.dll", "info.db")
foreach ($file in $FilesToCopy) {
    if (Test-Path "temp_mem/MemProcFS_v5.17.0_win_x64/$file") {
        Copy-Item "temp_mem/MemProcFS_v5.17.0_win_x64/$file" -Destination $TargetDir -Force
        Write-Host "[OK] Deployed: $file" -ForegroundColor Green
    } elseif (Test-Path "temp_mem/$file") {
        Copy-Item "temp_mem/$file" -Destination $TargetDir -Force
        Write-Host "[OK] Deployed: $file" -ForegroundColor Green
    }
}

# 清理
Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host "`nSetup finished! Your high-end DMA is ready to go喵！" -ForegroundColor Cyan
