$ProgressPreference = 'SilentlyContinue'
$TargetDir = "bin/Debug/net8.0"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir
}

Write-Host "--- VrcDmaFish Setup: Mirror Mode ---" -ForegroundColor Cyan
$GithubUrl = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17.0/MemProcFS_v5.17.0_win_x64.zip"
$Mirrors = @(
    "https://mirror.ghproxy.com/$GithubUrl",
    "https://ghproxy.net/$GithubUrl",
    $GithubUrl
)

$ZipFile = "memprocfs.zip"
$Success = $false

foreach ($url in $Mirrors) {
    Write-Host "Attempting download from: $url" -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri $url -OutFile $ZipFile -TimeoutSec 60
        if ((Get-Item $ZipFile).Length -gt 1000000) {
            $Success = $true
            Write-Host "Download complete!" -ForegroundColor Green
            break
        }
    } catch {
        Write-Host "Failed to download from this link." -ForegroundColor Gray
    }
}

if (-not $Success) {
    Write-Host "All download links failed. Please check your internet connection." -ForegroundColor Red
    return
}

Write-Host "Deploying driver files..." -ForegroundColor Yellow
if (Test-Path "temp_mem") { Remove-Item -Recurse -Force "temp_mem" }
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$FilesToCopy = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "FTD601.dll", "info.db")
foreach ($file in $FilesToCopy) {
    $subPath = "temp_mem/MemProcFS_v5.17.0_win_x64/$file"
    if (Test-Path $subPath) {
        Copy-Item $subPath -Destination $TargetDir -Force
        Write-Host "[OK] Deployed: $file" -ForegroundColor Green
    } elseif (Test-Path "temp_mem/$file") {
        Copy-Item "temp_mem/$file" -Destination $TargetDir -Force
        Write-Host "[OK] Deployed: $file" -ForegroundColor Green
    }
}

Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host ""
Write-Host "Setup finished successfully!" -ForegroundColor Cyan
Write-Host "You can now run 'dotnet run' to start the bot." -ForegroundColor Cyan
