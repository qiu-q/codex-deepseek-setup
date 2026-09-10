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

    $entries = Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | ForEach-Object {
        [PSCustomObject]@{
            Path = [IO.Path]::GetRelativePath($OutputDirectory, $_.FullName).Replace('\', '/')
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
    Write-Host "ZIP：$([math]::Round($archive.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    Write-Host "SHA-256：$hash" -ForegroundColor Cyan
}
