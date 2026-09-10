param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Publish.Common.ps1")
$payloadSource = Join-Path $repoRoot "payload"
$msix = Join-Path $payloadSource "ChatGPT-x64.msix"
$license = Join-Path $payloadSource "ChatGPT-License.xml"

$output = Join-Path $repoRoot "artifacts\internal\win-x64"
$helperTemp = Join-Path $repoRoot "artifacts\.helper-internal"
$obfuscationOutput = Join-Path $repoRoot "artifacts\.obfuscation-internal"
$assemblyBackup = Join-Path $repoRoot "artifacts\.assembly-backup-internal"
$archive = Join-Path $repoRoot "artifacts\CodexDeepSeekSetup-internal-win-x64.zip"
$appProject = Join-Path $repoRoot "src\CodexDeepSeekSetup.App\CodexDeepSeekSetup.App.csproj"
$appBuildDirectory = Join-Path $repoRoot "src\CodexDeepSeekSetup.App\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64"
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $helperTemp) { Remove-Item -LiteralPath $helperTemp -Recurse -Force }
if (Test-Path -LiteralPath $obfuscationOutput) { Remove-Item -LiteralPath $obfuscationOutput -Recurse -Force }
if (Test-Path -LiteralPath $assemblyBackup) { Remove-Item -LiteralPath $assemblyBackup -Recurse -Force }
New-Item -Path $output -ItemType Directory -Force | Out-Null
New-Item -Path $obfuscationOutput -ItemType Directory -Force | Out-Null
New-Item -Path $assemblyBackup -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src\CodexDeepSeekSetup.Helper\CodexDeepSeekSetup.Helper.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $helperTemp `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Helper 发布失败，退出码：$LASTEXITCODE" }

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Obfuscar 工具还原失败，退出码：$LASTEXITCODE" }

dotnet build $appProject -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false `
    -p:DebugType=None -p:DebugSymbols=false `
    -p:DefineConstants=INTERNAL_BUILD
if ($LASTEXITCODE -ne 0) { throw "内部版预构建失败，退出码：$LASTEXITCODE" }

$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Obfuscar.Internal.xml") -Raw
$inputXmlPath = [Security.SecurityElement]::Escape($appBuildDirectory.Replace('\', '/'))
$outputXmlPath = [Security.SecurityElement]::Escape($obfuscationOutput.Replace('\', '/'))
$obfuscarConfig = Join-Path $obfuscationOutput "obfuscar.generated.xml"
$template.Replace("__INPUT_PATH__", $inputXmlPath).Replace("__OUTPUT_PATH__", $outputXmlPath) |
    Set-Content -LiteralPath $obfuscarConfig -Encoding utf8

dotnet tool run obfuscar.console -- $obfuscarConfig
if ($LASTEXITCODE -ne 0) { throw "内部版混淆失败，退出码：$LASTEXITCODE" }

$protectedAssemblies = @("CodexDeepSeekSetup.Core.dll", "CodexDeepSeekSetup.App.Logic.dll")
foreach ($assemblyName in $protectedAssemblies) {
    $protectedAssembly = Join-Path $obfuscationOutput $assemblyName
    if (-not (Test-Path -LiteralPath $protectedAssembly)) { throw "混淆输出缺失：$assemblyName" }
    Copy-Item -LiteralPath (Join-Path $appBuildDirectory $assemblyName) -Destination $assemblyBackup -Force
}

try {
    foreach ($assemblyName in $protectedAssemblies) {
        Copy-Item -LiteralPath (Join-Path $obfuscationOutput $assemblyName) -Destination (Join-Path $appBuildDirectory $assemblyName) -Force
    }

    dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true -o $output `
        --no-build --no-restore `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -p:DefineConstants=INTERNAL_BUILD
    if ($LASTEXITCODE -ne 0) { throw "主程序发布失败，退出码：$LASTEXITCODE" }
}
finally {
    # Never leave obfuscated assemblies in bin/. Open-source or ordinary builds
    # executed after this script must always start from the original outputs.
    foreach ($assemblyName in $protectedAssemblies) {
        $originalAssembly = Join-Path $assemblyBackup $assemblyName
        if (Test-Path -LiteralPath $originalAssembly) {
            Copy-Item -LiteralPath $originalAssembly -Destination (Join-Path $appBuildDirectory $assemblyName) -Force
        }
    }
    if (Test-Path -LiteralPath $assemblyBackup) { Remove-Item -LiteralPath $assemblyBackup -Recurse -Force }
}

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
Remove-Item -LiteralPath $obfuscarConfig -Force
Complete-ReleasePackage `
    -OutputDirectory $output `
    -ArchivePath $archive `
    -EnforceSizeBudget (-not ((Test-Path -LiteralPath $msix) -and (Test-Path -LiteralPath $license)))

Write-Host "内部开发版（未签名）输出：$output" -ForegroundColor Green
Write-Host "内部压缩包：$archive" -ForegroundColor Green
Write-Host "对外分发前必须使用受信任的 Authenticode 代码签名证书签署两个 EXE。" -ForegroundColor Yellow
