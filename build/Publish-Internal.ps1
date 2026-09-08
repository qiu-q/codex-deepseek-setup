param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$payloadSource = Join-Path $repoRoot "payload"
$msix = Join-Path $payloadSource "ChatGPT-x64.msix"
$license = Join-Path $payloadSource "ChatGPT-License.xml"

$output = Join-Path $repoRoot "artifacts\internal\win-x64"
$helperTemp = Join-Path $repoRoot "artifacts\.helper-internal"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $helperTemp) { Remove-Item -LiteralPath $helperTemp -Recurse -Force }
New-Item -Path $output -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src\CodexDeepSeekSetup.Helper\CodexDeepSeekSetup.Helper.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $helperTemp `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Helper 发布失败，退出码：$LASTEXITCODE" }

dotnet publish (Join-Path $repoRoot "src\CodexDeepSeekSetup.App\CodexDeepSeekSetup.App.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $output `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -p:DefineConstants=INTERNAL_BUILD
if ($LASTEXITCODE -ne 0) { throw "主程序发布失败，退出码：$LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $helperTemp "CodexDeepSeekSetup.Helper.exe") -Destination $output -Force
if ((Test-Path -LiteralPath $msix) -and (Test-Path -LiteralPath $license)) {
    $payloadDestination = Join-Path $output "payload"
    New-Item -Path $payloadDestination -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $msix -Destination $payloadDestination -Force
    Copy-Item -LiteralPath $license -Destination $payloadDestination -Force
    Write-Host "已附带本地官方离线文件。" -ForegroundColor Cyan
}
else {
    Write-Host "未发现完整 payload，内部版运行时将从 OpenAI 官方地址下载。" -ForegroundColor Yellow
}
Remove-Item -LiteralPath $helperTemp -Recurse -Force

Write-Host "内部开发版（未签名）输出：$output" -ForegroundColor Green
Write-Host "对外分发前必须使用受信任的 Authenticode 代码签名证书签署两个 EXE。" -ForegroundColor Yellow
