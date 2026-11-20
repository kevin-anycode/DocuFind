<#
.SYNOPSIS
    下载 DocuFind 的向量模型 bge-small-zh-v1.5（INT8 ONNX，约 25 MB）。

.DESCRIPTION
    默认下载到仓库的 models\ 目录，构建时会自动复制到输出目录。
    加 -Install 则直接装到 %LOCALAPPDATA%\DocuFind\models（用户级安装）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\fetch-model.ps1
    powershell -ExecutionPolicy Bypass -File tools\fetch-model.ps1 -Install
#>
[CmdletBinding()]
param(
    [string]$Destination,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

$repo = 'Xenova/bge-small-zh-v1.5'
$files = @(
    @{ Remote = 'onnx/model_int8.onnx'; Local = 'bge-small-zh-v1.5-int8.onnx'; MinSize = 10MB },
    @{ Remote = 'vocab.txt';            Local = 'vocab.txt';                    MinSize = 100KB }
)

if ($Install) {
    $Destination = Join-Path $env:LOCALAPPDATA 'DocuFind\models'
}
elseif (-not $Destination) {
    $Destination = Join-Path $PSScriptRoot '..\models'
}

$Destination = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$mirrors = @('https://hf-mirror.com', 'https://huggingface.co')

Write-Host "目标目录: $Destination" -ForegroundColor Cyan

foreach ($file in $files) {
    $target = Join-Path $Destination $file.Local

    if ((Test-Path $target) -and (Get-Item $target).Length -ge $file.MinSize) {
        Write-Host "已存在，跳过: $($file.Local)" -ForegroundColor DarkGray
        continue
    }

    $downloaded = $false
    foreach ($mirror in $mirrors) {
        $url = "$mirror/$repo/resolve/main/$($file.Remote)"
        try {
            Write-Host "下载 $($file.Remote) <- $mirror" -ForegroundColor Yellow
            Invoke-WebRequest -Uri $url -OutFile "$target.part" -UseBasicParsing -TimeoutSec 900 `
                -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) DocuFind/1.0'
            $size = (Get-Item "$target.part").Length
            if ($size -lt $file.MinSize) { throw "文件太小（$size 字节），可能不是模型文件" }
            Move-Item -Force "$target.part" $target
            Write-Host ("  完成: {0:N1} MB" -f ($size / 1MB)) -ForegroundColor Green
            $downloaded = $true
            break
        }
        catch {
            Write-Host "  失败: $($_.Exception.Message)" -ForegroundColor Red
            Remove-Item -ErrorAction SilentlyContinue "$target.part"
        }
    }

    if (-not $downloaded) { throw "无法下载 $($file.Remote)，请检查网络或手动下载后放到 $Destination" }
}

Write-Host '模型就绪。' -ForegroundColor Green
