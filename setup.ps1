# VrcDmaFish 一键环境配置脚本 (∠・ω< )⌒★
$ProgressPreference = 'SilentlyContinue'
$TargetDir = "bin/Debug/net8.0"

if (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir
}

Write-Host "Ciallo~ 正在帮主人准备 DMA 运行环境喵！(๑•̀ㅂ•́)و✧" -ForegroundColor Cyan

# 1. 下载最新的 MemProcFS (这里使用固定版本链接以确保稳定)
$Url = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17.0/MemProcFS_v5.17.0_win_x64.zip"
$ZipFile = "memprocfs.zip"

Write-Host "正在从 GitHub 下载驱动库... 可能会有点慢，请主人稍等喵~" -ForegroundColor Yellow
Invoke-WebRequest -Uri $Url -OutFile $ZipFile

# 2. 解压并移动核心 DLL
Write-Host "正在解压并进行投喂..." -ForegroundColor Yellow
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$FilesToCopy = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "info.db")
foreach ($file in $FilesToCopy) {
    if (Test-Path "temp_mem/$file") {
        Copy-Item "temp_mem/$file" -Destination $TargetDir -Force
        Write-Host "[OK] 已将 $file 投递到 $TargetDir 喵！" -ForegroundColor Green
    }
}

# 3. 清理垃圾
Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host "`n全部搞定啦主人！现在您可以直接运行 dotnet run 开始钓鱼了喵！(≧▽≦)ゞ" -ForegroundColor Cyan
