param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Publish.Common.ps1")
$output = Join-Path $repoRoot "artifacts\open-source\win-x64"
$helperTemp = Join-Path $repoRoot "artifacts\.helper-open-source"
$networkHelperTemp = Join-Path $repoRoot "artifacts\.network-helper-open-source"
$archive = Join-Path $repoRoot "artifacts\CodexDeepSeekSetup-open-source-win-x64.zip"

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $helperTemp) { Remove-Item -LiteralPath $helperTemp -Recurse -Force }
if (Test-Path -LiteralPath $networkHelperTemp) { Remove-Item -LiteralPath $networkHelperTemp -Recurse -Force }
New-Item -Path $output -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src\CodexDeepSeekSetup.Helper\CodexDeepSeekSetup.Helper.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $helperTemp `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Helper 发布失败，退出码：$LASTEXITCODE" }

dotnet publish (Join-Path $repoRoot "src\CodexDeepSeekSetup.NetworkHelper\CodexDeepSeekSetup.NetworkHelper.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $networkHelperTemp `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Network Helper 发布失败，退出码：$LASTEXITCODE" }

dotnet publish (Join-Path $repoRoot "src\CodexDeepSeekSetup.App\CodexDeepSeekSetup.App.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $output `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "主程序发布失败，退出码：$LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $helperTemp "CodexDeepSeekSetup.Helper.exe") -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $networkHelperTemp "CodexDeepSeekSetup.NetworkHelper.exe") -Destination $output -Force
Remove-Item -LiteralPath $helperTemp -Recurse -Force
Remove-Item -LiteralPath $networkHelperTemp -Recurse -Force
Complete-ReleasePackage -OutputDirectory $output -ArchivePath $archive

Write-Host "开源开发版（未签名）输出：$output" -ForegroundColor Green
Write-Host "开源压缩包：$archive" -ForegroundColor Green
Write-Host "对外分发前必须使用受信任的 Authenticode 代码签名证书签署三个 EXE。" -ForegroundColor Yellow
