[CmdletBinding()]
param(
    [string]$Version = "1.0.1",
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

$requiredFiles = @(
    "Zxtract.exe",
    "7z.exe",
    "7z.dll",
    "LICENSE.txt",
    "THIRD-PARTY-NOTICES.md",
    "licenses\7-Zip-License.txt"
)
foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $publishDir $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "发布包缺少必要文件：$requiredFile"
    }
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Published: $zipPath"
