<#
.SYNOPSIS
    一键发布桌面版，并生成在线升级所需的 latest.json。

.DESCRIPTION
    流程：清理 → dotnet publish（用 -p:Version 注入版本号）→ 精简 libvlc 多余架构
          → 校验 zip 结构 → 打包 → 计算 SHA256 → 生成 latest.json。

    产物都在 artifacts/ 目录（已被 .gitignore 忽略）：
      artifacts/Music-<版本>.zip    安装包，解压后覆盖安装目录
      artifacts/latest.json         版本清单，上传后把直链填到应用「设置 → 在线升级」

.EXAMPLE
    # 精简包（依赖用户已安装 .NET 10 桌面运行时），清单里写相对文件名
    ./scripts/publish-release.ps1 -Version 1.1.0 -Notes "新增歌词广播"

.EXAMPLE
    # 自包含包，并把清单里的 url 写成完整地址
    ./scripts/publish-release.ps1 -Version 1.1.0 -SelfContained `
        -FeedBaseUrl https://gitee.com/your-name/music/raw/master -Notes "修复若干问题"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+(\.\d+){1,3}$')]
    [string]$Version,

    [string]$Notes = '',

    # 清单里 zip 的地址前缀。留空则写相对文件名（应用会按清单所在目录解析）。
    [string]$FeedBaseUrl = '',

    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-x86')]
    [string]$Runtime = 'win-x64',

    # 默认精简包（需要目标机已安装 .NET 10 桌面运行时）；加上则发布自包含包，体积更大。
    [switch]$SelfContained,

    # 保留 pdb 符号文件。默认剔除：仅 libSkiaSharp.pdb 就有 80MB，会让安装包体积翻倍。
    [switch]$KeepSymbols
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Music.Desktop\Music.Desktop.csproj'
$framework = 'net10.0-windows10.0.19041.0'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$stageDir = Join-Path $artifactsDir "publish-$Version-$Runtime"
$zipName = "Music-$Version.zip"
$zipPath = Join-Path $artifactsDir $zipName
$manifestPath = Join-Path $artifactsDir 'latest.json'

if (-not (Test-Path $project)) {
    throw "找不到项目文件：$project"
}

Write-Host "==> 发布 Music.Desktop $Version（$Runtime，自包含=$([bool]$SelfContained)）" -ForegroundColor Cyan

# ---------- 1. 清理 ----------
foreach ($path in @($stageDir, $zipPath, $manifestPath)) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# ---------- 2. 发布 ----------
# 版本号通过 -p:Version 注入，运行时会成为 AssemblyInformationalVersion，被 UpdateService 读作当前版本。
# 单文件与 AOT 必须关闭：libvlc 依赖 plugins 目录结构按路径加载。
& dotnet publish $project `
    -c $Configuration `
    -f $framework `
    -r $Runtime `
    "-p:Version=$Version" `
    "-p:SelfContained=$(if ($SelfContained) { 'true' } else { 'false' })" `
    -p:PublishSingleFile=false `
    -p:PublishAot=false `
    -o $stageDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（退出码 $LASTEXITCODE）"
}

# ---------- 3. 精简 libvlc 的多余架构 ----------
# VideoLAN.LibVLC.Windows 会带上 win-x64 / win-x86 / win-arm64 三份，只保留当前架构能省下大量体积。
$libvlcDir = Join-Path $stageDir 'libvlc'
if (Test-Path $libvlcDir) {
    Get-ChildItem $libvlcDir -Directory |
        Where-Object { $_.Name -ne $Runtime } |
        ForEach-Object {
            Write-Host "    移除多余架构：$($_.Name)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

# ---------- 4. 剔除调试符号 ----------
# pdb 对运行没有任何用处，但体积巨大（libSkiaSharp.pdb 80MB + libHarfBuzzSharp.pdb 20MB）。
# 需要排查线上崩溃时用 -KeepSymbols 单独出一份带符号的包。
if (-not $KeepSymbols) {
    $symbols = Get-ChildItem $stageDir -Recurse -File -Filter '*.pdb'
    if ($symbols.Count -gt 0) {
        $symbolMb = [Math]::Round(($symbols | Measure-Object Length -Sum).Sum / 1MB, 1)
        Write-Host "    剔除 $($symbols.Count) 个 pdb，省下 $symbolMb MB"
        $symbols | Remove-Item -Force
    }
}

# ---------- 5. 校验 zip 结构 ----------
# 升级脚本是「解压到安装目录」，因此 publish 产物必须直接位于 zip 根，不能多套一层目录。
$exePath = Join-Path $stageDir 'Music.Desktop.exe'
if (-not (Test-Path $exePath)) {
    throw "发布目录里找不到 Music.Desktop.exe，打出来的 zip 结构会不合法：$stageDir"
}

# ---------- 5. 打包 ----------
Write-Host '==> 打包中…'
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

# ---------- 6. 校验值 + 清单 ----------
$size = (Get-Item $zipPath).Length
$sizeMb = [Math]::Round($size / 1MB, 1)
$sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

$packageUrl = if ([string]::IsNullOrWhiteSpace($FeedBaseUrl)) {
    $zipName
} else {
    "$($FeedBaseUrl.TrimEnd('/'))/$zipName"
}

$manifest = [ordered]@{
    version = $Version
    url     = $packageUrl
    sha256  = $sha256
    notes   = $Notes
}

# 不带 BOM 的 UTF-8：部分 JSON 解析器会把 BOM 当成非法起始字节。
$json = $manifest | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))

# ---------- 7. 汇总 ----------
Write-Host ''
Write-Host '==> 发布产物（artifacts/ 已被 .gitignore 忽略）' -ForegroundColor Green
Write-Host ("    {0,-22} {1,8} MB" -f $zipName, $sizeMb)
Write-Host ("    {0,-22} {1}" -f 'latest.json', '')
Write-Host ''
Write-Host "zip 直链 / 文件名 : $packageUrl"
Write-Host "SHA256            : $sha256"
Write-Host ''

if ($sizeMb -gt 95) {
    Write-Warning 'zip 已超过 95MB。Gitee/GitCode 免费仓库单文件上限 100MB，建议改用发行版附件或对象存储。'
}

if (-not $SelfContained) {
    Write-Host '提示：精简包需要目标机已安装 .NET 10 桌面运行时；否则请加 -SelfContained。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '下一步：' -ForegroundColor Yellow
Write-Host '  1) 把 artifacts/Music-<版本>.zip 上传（建议放发行版附件，或直接提交到仓库）'
Write-Host '  2) 把 artifacts/latest.json 上传到可以直链访问的位置（例如仓库根目录）'
Write-Host '  3) 在应用「设置 → 在线升级 → 更新地址」填入 latest.json 的直链'
