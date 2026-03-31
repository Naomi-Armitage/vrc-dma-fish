param(
    [string[]]$TargetDir
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$downloadUrl = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17/MemProcFS_files_and_binaries-win_x64-latest.zip"
$zipFile = Join-Path $baseDir "memprocfs.zip"
$tempDir = Join-Path $baseDir "temp_mem"
$requiredFiles = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "FTD601.dll", "info.db")

function Resolve-TargetDirectories {
    param([string[]]$ConfiguredTargets)

    if ($ConfiguredTargets -and $ConfiguredTargets.Count -gt 0) {
        foreach ($dir in $ConfiguredTargets) {
            $resolved = Join-Path $baseDir $dir
            New-Item -ItemType Directory -Force -Path $resolved | Out-Null
            Resolve-Path $resolved | Select-Object -ExpandProperty Path
        }

        return
    }

    $autoTargets = Get-ChildItem -Path (Join-Path $baseDir "bin") -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\net8\.0$" } |
        Select-Object -ExpandProperty FullName -Unique

    if ($autoTargets) {
        $autoTargets
        return
    }

    $defaultTarget = Join-Path $baseDir "bin\Debug\net8.0"
    New-Item -ItemType Directory -Force -Path $defaultTarget | Out-Null
    Resolve-Path $defaultTarget | Select-Object -ExpandProperty Path
}

$targetDirectories = @(Resolve-TargetDirectories -ConfiguredTargets $TargetDir)

Write-Host "--- VrcDmaFish Setup ---" -ForegroundColor Cyan
Write-Host "Downloading MemProcFS native libraries..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipFile -TimeoutSec 120

if (Test-Path $tempDir) {
    Remove-Item -LiteralPath $tempDir -Recurse -Force
}

Write-Host "Extracting files..." -ForegroundColor Yellow
Expand-Archive -Path $zipFile -DestinationPath $tempDir -Force

$allFiles = Get-ChildItem -Path $tempDir -Recurse -File
$missing = @()

foreach ($fileName in $requiredFiles) {
    $found = $allFiles | Where-Object { $_.Name -eq $fileName } | Select-Object -First 1
    if (-not $found) {
        $missing += $fileName
        continue
    }

    foreach ($target in $targetDirectories) {
        Copy-Item -LiteralPath $found.FullName -Destination $target -Force
        Write-Host "[OK] $fileName -> $target" -ForegroundColor Green
    }
}

if ($missing.Count -gt 0) {
    throw "Missing files in archive: $($missing -join ', ')"
}

Remove-Item -LiteralPath $zipFile -Force
Remove-Item -LiteralPath $tempDir -Recurse -Force

Write-Host ""
Write-Host "Setup finished successfully." -ForegroundColor Cyan
