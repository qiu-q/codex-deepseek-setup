function Complete-ReleasePackage {
    param(
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [Parameter(Mandatory)] [string] $ArchivePath,
        [bool] $EnforceSizeBudget = $true
    )

    $mainExe = Join-Path $OutputDirectory "CodexDeepSeekSetup.exe"
    $helperExe = Join-Path $OutputDirectory "CodexDeepSeekSetup.Helper.exe"
    foreach ($file in @($mainExe, $helperExe)) {
        if (-not (Test-Path -LiteralPath $file)) { throw "发布文件缺失：$file" }
    }

    $helperBytes = (Get-Item -LiteralPath $helperExe).Length
    if ($helperBytes -gt 25MB) {
        throw "辅助程序超过 25 MB：$([math]::Round($helperBytes / 1MB, 2)) MB"
    }
    $networkHelperExe = Join-Path $OutputDirectory "CodexDeepSeekSetup.NetworkHelper.exe"
    if (Test-Path -LiteralPath $networkHelperExe) {
        $networkHelperBytes = (Get-Item -LiteralPath $networkHelperExe).Length
        if ($networkHelperBytes -gt 25MB) {
            throw "网络辅助程序超过 25 MB：$([math]::Round($networkHelperBytes / 1MB, 2)) MB"
        }
    }

    # Windows PowerShell 5.1 does not provide Path.GetRelativePath. The publish
    # directory is an already-resolved parent of every entry, so a guarded
    # substring keeps the release script compatible with the Windows 10 inbox
    # PowerShell while still emitting portable '/' separators.
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([char[]]@('\', '/'))
    $entries = Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | ForEach-Object {
        $entryPath = [IO.Path]::GetFullPath($_.FullName)
        if (-not $entryPath.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "发布文件不在输出目录内：$entryPath"
        }
        [PSCustomObject]@{
            Path = $entryPath.Substring($outputRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            SizeBytes = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $reportPath = Join-Path $OutputDirectory "release-report.json"
    [PSCustomObject]@{
        CreatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        Runtime = "win-x64"
        SelfContained = $true
        Files = @($entries)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding utf8

    if (Test-Path -LiteralPath $ArchivePath) { Remove-Item -LiteralPath $ArchivePath -Force }
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $ArchivePath -CompressionLevel Optimal
    $archive = Get-Item -LiteralPath $ArchivePath
    if ($EnforceSizeBudget -and $archive.Length -gt 85MB) {
        throw "发布 ZIP 超过 85 MB：$([math]::Round($archive.Length / 1MB, 2)) MB。请查看 release-report.json。"
    }

    $hash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($ArchivePath + ".sha256.txt") -Value "$hash  $($archive.Name)" -Encoding ascii

    Write-Host "主程序：$([math]::Round((Get-Item $mainExe).Length / 1MB, 2)) MB" -ForegroundColor Cyan
    Write-Host "辅助程序：$([math]::Round($helperBytes / 1MB, 2)) MB" -ForegroundColor Cyan
    if (Test-Path -LiteralPath $networkHelperExe) {
        Write-Host "网络辅助：$([math]::Round((Get-Item $networkHelperExe).Length / 1MB, 2)) MB" -ForegroundColor Cyan
    }
    Write-Host "ZIP：$([math]::Round($archive.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    Write-Host "SHA-256：$hash" -ForegroundColor Cyan
}
