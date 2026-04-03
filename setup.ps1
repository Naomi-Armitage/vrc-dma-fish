$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$TargetDirs = @(
    (Join-Path $ScriptRoot 'bin\Debug\net8.0'),
    (Join-Path $ScriptRoot 'bin\Release\net8.0'),
    $ScriptRoot
)

$GithubUrl = 'https://github.com/ufrisk/MemProcFS/releases/download/v5.17/MemProcFS_files_and_binaries-win_x64-latest.zip'
$ZipFile = Join-Path $ScriptRoot 'memprocfs.zip'
$ExtractDir = Join-Path $ScriptRoot 'temp_mem'
$RequiredFiles = @('vmm.dll', 'leechcore.dll', 'FTD3XX.dll', 'FTD601.dll', 'info.db')

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-DownloadUrls {
    $isChina = $false

    try {
        $handler = [System.Net.Http.HttpClientHandler]::new()
        $client = [System.Net.Http.HttpClient]::new($handler)
        $client.Timeout = [TimeSpan]::FromSeconds(3)

        try {
            $response = $client.GetStringAsync('http://ip-api.com/json/').GetAwaiter().GetResult()
            $payload = $response | ConvertFrom-Json
            if ($payload.countryCode -eq 'CN') {
                $isChina = $true
            }
        }
        finally {
            $client.Dispose()
            $handler.Dispose()
        }
    }
    catch {
    }

    if ($isChina) {
        return @("https://ghp.ci/$GithubUrl", $GithubUrl)
    }

    return @($GithubUrl, "https://ghp.ci/$GithubUrl")
}

function Invoke-FileDownload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(120)

    try {
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode()

        $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $destination = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)

        try {
            $source.CopyTo($destination)
        }
        finally {
            $destination.Dispose()
            $source.Dispose()
        }
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

function Expand-ZipArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ZipPath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (Test-Path $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Recurse -Force
    }

    [System.IO.Directory]::CreateDirectory($DestinationPath) | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $DestinationPath)
}

Write-Host '--- VrcDmaFish 安装向导 ---' -ForegroundColor Cyan

if (Test-Path $ZipFile) {
    Remove-Item -LiteralPath $ZipFile -Force
}

$downloaded = $false
foreach ($url in Get-DownloadUrls) {
    Write-Host ('正在下载：{0}' -f $url)
    try {
        Invoke-FileDownload -Url $url -DestinationPath $ZipFile
        if (Test-Path $ZipFile) {
            $downloaded = $true
            break
        }
    }
    catch {
        Write-Host ('当前链接失败，尝试下一个：{0}' -f $_.Exception.Message) -ForegroundColor Yellow
    }
}

if (-not $downloaded) {
    Write-Host '下载失败，请检查网络连接。' -ForegroundColor Red
    exit 1
}

Write-Host '正在部署驱动文件...'
Expand-ZipArchive -ZipPath $ZipFile -DestinationPath $ExtractDir

$foundFiles = Get-ChildItem -Path $ExtractDir -File -Recurse | Where-Object { $RequiredFiles -contains $_.Name }
$missingFiles = $RequiredFiles | Where-Object { $_ -notin $foundFiles.Name }

if ($missingFiles.Count -gt 0) {
    Write-Host ('压缩包缺少必要文件：{0}' -f ($missingFiles -join ', ')) -ForegroundColor Red
    exit 1
}

foreach ($dir in $TargetDirs) {
    if (-not (Test-Path $dir)) {
        [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    }

    foreach ($file in $foundFiles) {
        [System.IO.File]::Copy($file.FullName, (Join-Path $dir $file.Name), $true)
    }

    Write-Host ('已部署到：{0}' -f $dir)
}

if (Test-Path $ZipFile) {
    Remove-Item -LiteralPath $ZipFile -Force
}

if (Test-Path $ExtractDir) {
    Remove-Item -LiteralPath $ExtractDir -Recurse -Force
}

Write-Host ''
Write-Host '安装完成。'
Write-Host "现在可以运行 'dotnet run' 启动程序。"
