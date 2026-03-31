# VrcDmaFish 一键环境配置脚本 - FT601 兼容版 (∠・ω< )⌒★
$ProgressPreference = 'SilentlyContinue'
$TargetDir = "bin/Debug/net8.0"

if (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir
}

Write-Host "Ciallo~ 检测到主人拥有 FT601 高端设备，正在准备专项环境喵！(๑•̀ㅂ•́)و✧" -ForegroundColor Cyan

# 下载最新的 MemProcFS
$Url = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17.0/MemProcFS_v5.17.0_win_x64.zip"
$ZipFile = "memprocfs.zip"

Write-Host "正在从 GitHub 获取最新的驱动套件..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $Url -OutFile $ZipFile

# 解压并移动核心 DLL (包含 FT3XX 和 FT601 兼容项)
Write-Host "正在进行精准投喂..." -ForegroundColor Yellow
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$FilesToCopy = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "FTD601.dll", "info.db")
foreach ($file in $FilesToCopy) {
    if (Test-Path "temp_mem/$file") {
        Copy-Item "temp_mem/$file" -Destination $TargetDir -Force
        Write-Host "[OK] 已部署: $file" -ForegroundColor Green
    }
}

Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host "`n配置完成！FT601 驱动已就绪，请主人尽情享受极速 DMA 喵！(≧▽≦)ゞ" -ForegroundColor Cyan
