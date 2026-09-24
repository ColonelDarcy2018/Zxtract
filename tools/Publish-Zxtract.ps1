[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishName = "Zxtract-$Version-$Runtime"
$publishDir = Join-Path $repoRoot "artifacts\$publishName"
$zipPath = Join-Path $repoRoot "artifacts\$publishName.zip"
$project = Join-Path $repoRoot "src\ExtractUtil.App\ExtractUtil.App.csproj"

New-Item -ItemType Directory -Path (Join-Path $repoRoot "artifacts") -Force | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    throw "发布目录已存在，请先移动或备份它：$publishDir"
}
if (Test-Path -LiteralPath $zipPath) {
    throw "发布压缩包已存在，请先移动或备份它：$zipPath"
}

dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Published: $zipPath"
