param([switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$releaseDir = Join-Path $root 'release'
$stage = Join-Path $root ('artifacts\package-0.9.0-' + [guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $stage 'App-Proxy-Bridge-0.9.0-win-x64'
$zip = Join-Path $releaseDir 'App-Proxy-Bridge-0.9.0-win-x64.zip'
$checksums = Join-Path $releaseDir 'SHA256SUMS.txt'

New-Item -ItemType Directory -Path $publishDir, $releaseDir -Force | Out-Null
try {
    $publishArgs = @('publish', (Join-Path $root 'src\AppProxyBridge.App\AppProxyBridge.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publishDir)
    if ($NoRestore) { $publishArgs += '--no-restore' }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败。' }
    Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -File | Remove-Item -Force
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $publishDir
    @'
App Proxy Bridge 0.9.0 开发预览

运行 App Proxy Bridge.exe。程序通过软件支持的环境变量或启动参数使用本地代理，并非全流量透明代理。
真实目标软件兼容性、干净系统和安装升级尚未完成验收。此预览未签名。
'@ | Set-Content -LiteralPath (Join-Path $publishDir '开发预览说明.txt') -Encoding UTF8
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksums, "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
    Write-Host "预览包：$zip"
    Write-Host "校验文件：$checksums"
} finally {
    if (Test-Path -LiteralPath $stage) {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        $resolvedArtifacts = (Resolve-Path -LiteralPath (Join-Path $root 'artifacts')).Path.TrimEnd('\') + '\'
        if (-not $resolvedStage.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw '拒绝清理项目目录之外的构建文件。' }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}

