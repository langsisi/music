<#
.SYNOPSIS
    一键发布（桌面 + 安卓）：构建安装包 → 生成 latest.json → 建 Gitea Release → 上传附件 → 提交清单。

.DESCRIPTION
    流程：清理 → dotnet publish 桌面 → 精简 libvlc → 打包 zip → 计算 SHA256
          →（可选）构建安卓 apk → 生成 latest.json（含 androidUrl/androidSha256）
          → 用 Gitea API 建/取目标 Tag 的 Release → 上传 zip/apk 附件
          → 通过 contents API 把 latest.json 提交到仓库分支。

    产物都在 artifacts/ 目录（已被 .gitignore 忽略）：
      artifacts/Music-<版本>.zip     桌面安装包，解压后覆盖安装目录
      artifacts/Music-<版本>.apk     安卓安装包
      latest.json                    版本清单（本地生成后由脚本提交到仓库）

    鉴权：需要一个 Gitea Personal Access Token（repository 的读写权限）。
          用参数 -Token 传入，或先设置环境变量 MUSIC_GITEA_TOKEN。
          令牌只用于本次 API 调用，不会写进仓库或脚本。

.EXAMPLE
    $env:MUSIC_GITEA_TOKEN = '<你的令牌>'
    ./scripts/publish-release.ps1 -Version 1.1.1 -Notes "修复若干问题"

.EXAMPLE
    # 只出包和本地 latest.json，不碰服务器
    ./scripts/publish-release.ps1 -Version 1.1.1 -SkipUpload

.EXAMPLE
    # 默认就是自包含包，跳过安卓
    ./scripts/publish-release.ps1 -Version 1.1.1 -SkipAndroid

.EXAMPLE
    # 出精简包（体积小，但需要目标机已装 .NET 10 桌面运行时）
    ./scripts/publish-release.ps1 -Version 1.1.1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+(\.\d+){1,3}$')]
    [string]$Version,

    [string]$Notes = '',

    # Gitea 站点根地址与仓库（owner/repo）
    [string]$GiteaBaseUrl = 'https://www.294713.xyz',
    [string]$Repo = 'zhusenlin/Music',

    # 提交 latest.json 所依据的分支
    [string]$Branch = 'master',

    # 访问令牌；默认取环境变量 MUSIC_GITEA_TOKEN
    [string]$Token = $env:MUSIC_GITEA_TOKEN,

    # Release 的 Tag 名。默认 v<版本>，与包名保持一致，避免 tag 与包版本对不上。
    [string]$Tag = '',

    # 覆盖下载地址前缀。默认由 GiteaBaseUrl/Repo 推导（.../releases/download）。
    [string]$ReleaseBaseUrl = '',

    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-x86')]
    [string]$Runtime = 'win-x64',

    # 默认发布自包含包（目标机无需预装 .NET 10 桌面运行时，体积更大）。
    # 自建仓库没有单文件大小限制，用自包含包最省心；需要小体积时加 -FrameworkDependent。
    [switch]$FrameworkDependent,

    # 保留 pdb 符号文件。默认剔除：仅 libSkiaSharp.pdb 就有 80MB，会让安装包体积翻倍。
    [switch]$KeepSymbols,

    # 跳过安卓构建（只发桌面）。
    [switch]$SkipAndroid,

    # 安卓构建配置。Release 需要自行配置签名 keystore，默认 Debug：开箱即可安装。
    [ValidateSet('Debug', 'Release')]
    [string]$AndroidConfiguration = 'Debug',

    # 只出包 + 本地 latest.json，不上传 Gitea。
    [switch]$SkipUpload
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 默认可能不启用 TLS 1.2，访问 HTTPS 的 Gitea API 会失败。
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Music.Desktop\Music.Desktop.csproj'
$androidProject = Join-Path $repoRoot 'src\Music.Android\Music.Android.csproj'
$framework = 'net10.0-windows10.0.19041.0'
# 默认自包含；-FrameworkDependent 时改为依赖目标机的 .NET 10 桌面运行时（包更小）。
$selfContained = -not $FrameworkDependent
$artifactsDir = Join-Path $repoRoot 'artifacts'
$stageDir = Join-Path $artifactsDir "publish-$Version-$Runtime"
$zipName = "Music-$Version.zip"
$zipPath = Join-Path $artifactsDir $zipName
$apkName = "Music-$Version.apk"
$apkPath = Join-Path $artifactsDir $apkName

# 清单直接写到仓库根目录，方便提交推送；应用读取它的 raw 直链。
$manifestPath = Join-Path $repoRoot 'latest.json'

if (-not (Test-Path $project)) {
    throw "找不到项目文件：$project"
}

Write-Host "==> 发布 Music $Version（桌面 $Runtime，自包含=$selfContained；安卓=$(-not $SkipAndroid)）" -ForegroundColor Cyan

# ---------- 1. 清理 ----------
foreach ($path in @($stageDir, $zipPath, $apkPath, $manifestPath)) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

# ---------- 2. 发布桌面 ----------
# 版本号通过 -p:Version 注入，运行时会成为 AssemblyInformationalVersion，被 UpdateService 读作当前版本。
# 单文件与 AOT 必须关闭：libvlc 依赖 plugins 目录结构按路径加载。
& dotnet publish $project `
    -c $Configuration `
    -f $framework `
    -r $Runtime `
    "-p:Version=$Version" `
    "-p:SelfContained=$(if ($selfContained) { 'true' } else { 'false' })" `
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

# ---------- 6. 打包 zip ----------
Write-Host '==> 打包桌面安装包…'
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$zipSizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
$zipSha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

# ---------- 7. 构建安卓 apk ----------
$apkSha = $null
if (-not $SkipAndroid) {
    if (-not (Test-Path $androidProject)) {
        throw "找不到安卓项目：$androidProject"
    }

    # 把版本号同时注入 AssemblyInformationalVersion（UpdateService 读它）与 versionName/versionCode。
    $parts = $Version.Split('.')
    $versionCode = ([int]$parts[0] * 10000) + ([int]$parts[1] * 100) + $(if ($parts.Length -ge 3) { [int]$parts[2] } else { 0 })

    Write-Host "==> 构建安卓 apk（$AndroidConfiguration，versionCode=$versionCode）…" -ForegroundColor Cyan
    & dotnet build $androidProject `
        -c $AndroidConfiguration `
        "-p:Version=$Version" `
        "-p:ApplicationDisplayVersion=$Version" `
        "-p:ApplicationVersion=$versionCode"

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build（安卓）失败（退出码 $LASTEXITCODE）"
    }

    $androidBin = Join-Path $repoRoot "src\Music.Android\bin\$AndroidConfiguration\net10.0-android"
    $apk = Get-ChildItem $androidBin -Filter '*-Signed.apk' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $apk) {
        $apk = Get-ChildItem $androidBin -Filter '*-unsigned.apk' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($apk) {
            throw "只找到未签名的 apk（$($apk.FullName)）。请配置 Android 签名 keystore，或改用 -AndroidConfiguration Debug。"
        }
        throw "在 $androidBin 下找不到 apk 产物。"
    }

    Copy-Item $apk.FullName $apkPath -Force
    $apkSizeMb = [Math]::Round((Get-Item $apkPath).Length / 1MB, 1)
    $apkSha = (Get-FileHash $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------- 8. 生成 latest.json ----------
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = "v$Version"
}

$base = if ([string]::IsNullOrWhiteSpace($ReleaseBaseUrl)) {
    "$($GiteaBaseUrl.TrimEnd('/'))/$Repo/releases/download"
} else {
    $ReleaseBaseUrl.TrimEnd('/')
}

$manifest = [ordered]@{
    version = $Version
    url     = "$base/$Tag/$zipName"
    sha256  = $zipSha
}
if ($apkSha) {
    $manifest.androidUrl = "$base/$Tag/$apkName"
    $manifest.androidSha256 = $apkSha
}
$manifest.notes = $Notes

# 不带 BOM 的 UTF-8：部分 JSON 解析器会把 BOM 当成非法起始字节。
$json = $manifest | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))

# ---------- 9. 上传到 Gitea ----------
$uploaded = $false
if ($SkipUpload) {
    Write-Host '==> 已跳过上传（-SkipUpload）。' -ForegroundColor Yellow
} elseif ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Warning '未提供令牌：跳过上传。请设置环境变量 MUSIC_GITEA_TOKEN，或用 -Token 传入。'
} else {
    if (-not (Get-Command Invoke-RestMethod)) {
        throw '当前 PowerShell 缺少 Invoke-RestMethod。'
    }

    $api = "$($GiteaBaseUrl.TrimEnd('/'))/api/v1/repos/$Repo"
    $headers = @{ Authorization = "token $Token" }

    Write-Host '==> 创建 / 获取 Gitea Release…' -ForegroundColor Cyan
    $release = $null
    try {
        $release = Invoke-RestMethod -Method Get -Uri "$api/releases/tags/$Tag" -Headers $headers -ErrorAction Stop
    } catch {
        $release = $null
    }

    if (-not $release) {
        $body = @{
            tag_name         = $Tag
            name             = $Tag
            body             = $Notes
            draft            = $false
            prerelease       = $false
            target_commitish = $Branch
        } | ConvertTo-Json
        $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers `
            -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($body))
        Write-Host "    已创建 Release $Tag（id=$($release.id)）"
    } else {
        Write-Host "    复用已有 Release $Tag（id=$($release.id)）"
    }

    # 上传附件：同名附件 Gitea 会拒绝，先删掉旧的。
    function Send-Asset {
        param([string]$FilePath)
        $name = [IO.Path]::GetFileName($FilePath)

        foreach ($asset in @($release.assets)) {
            if ($asset -and $asset.name -eq $name) {
                Invoke-RestMethod -Method Delete -Uri "$api/releases/$($release.id)/assets/$($asset.id)" -Headers $headers | Out-Null
                Write-Host "    删除旧附件 $name"
            }
        }

        Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
        $client = New-Object System.Net.Http.HttpClient
        $client.DefaultRequestHeaders.Authorization =
            New-Object System.Net.Http.Headers.AuthenticationHeaderValue('token', $Token)
        $stream = [IO.File]::OpenRead($FilePath)
        try {
            $multipart = New-Object System.Net.Http.MultipartFormDataContent
            $fileContent = New-Object System.Net.Http.StreamContent($stream)
            $fileContent.Headers.ContentType =
                New-Object System.Net.Http.Headers.MediaTypeHeaderValue('application/octet-stream')
            $multipart.Add($fileContent, 'attachment', $name)

            $uri = "$api/releases/$($release.id)/assets?name=$name"
            $response = $client.PostAsync($uri, $multipart).GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                throw "上传 $name 失败：$($response.StatusCode) $text"
            }
        } finally {
            $stream.Dispose()
            $client.Dispose()
        }
        Write-Host "    已上传 $name" -ForegroundColor Green
    }

    Write-Host '==> 上传附件…' -ForegroundColor Cyan
    Send-Asset $zipPath
    if ($apkSha) {
        Send-Asset $apkPath
    }

    Write-Host '==> 提交 latest.json…' -ForegroundColor Cyan
    $contentsUri = "$api/contents/latest.json"
    $existing = $null
    try {
        $existing = Invoke-RestMethod -Method Get -Uri "${contentsUri}?ref=$Branch" -Headers $headers -ErrorAction Stop
    } catch {
        $existing = $null
    }

    $putBody = [ordered]@{
        content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
        message = "chore: 更新 latest.json 到 $Version"
        branch  = $Branch
    }
    if ($existing -and $existing.sha) {
        $putBody.sha = $existing.sha
    }

    Invoke-RestMethod -Method Put -Uri $contentsUri -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes(($putBody | ConvertTo-Json -Depth 3))) | Out-Null
    Write-Host '    已提交 latest.json' -ForegroundColor Green

    $uploaded = $true
}

# ---------- 10. 汇总 ----------
Write-Host ''
Write-Host '==> 发布产物' -ForegroundColor Green
Write-Host ("    {0,-22} {1,8} MB" -f "artifacts\$zipName", $zipSizeMb)
if ($apkSha) {
    Write-Host ("    {0,-22} {1,8} MB" -f "artifacts\$apkName", $apkSizeMb)
}
Write-Host ("    {0,-22}" -f 'latest.json')
Write-Host ''
Write-Host '清单内容：' -ForegroundColor Cyan
Write-Host $json
Write-Host ''
Write-Host "桌面包 : $($manifest.url)"
if ($manifest.androidUrl) {
    Write-Host "安卓包 : $($manifest.androidUrl)"
}
Write-Host "SHA256 : $zipSha"
if ($apkSha) {
    Write-Host "安卓SHA: $apkSha"
}
Write-Host "清单直链: $($GiteaBaseUrl.TrimEnd('/'))/$Repo/raw/branch/$Branch/latest.json"
Write-Host ''

if ($zipSizeMb -gt 95) {
    Write-Host '提示：zip 较大（自包含包正常）。若换用有单文件大小限制的托管（如 Gitee/GitCode 免费仓库），' -ForegroundColor Yellow
    Write-Host '      需要改用发行版附件、对象存储，或用 -FrameworkDependent 出精简包。' -ForegroundColor Yellow
}

if (-not $selfContained) {
    Write-Host '提示：当前是精简包，需要目标机已安装 .NET 10 桌面运行时。' -ForegroundColor Yellow
}

if ($uploaded) {
    Write-Host '完成：Release 与 latest.json 都已更新，应用「检查更新」即可看到新版本。' -ForegroundColor Green
} else {
    Write-Host '下一步（手动）：' -ForegroundColor Yellow
    Write-Host "  1) 在 Gitea 用 Tag '$Tag' 新建 Release，上传 artifacts\$zipName（及 apk）"
    Write-Host '  2) 提交并推送仓库根目录的 latest.json'
    Write-Host ''
    Write-Host '注意：清单里的 sha256 是本次产出的包算出来的。如果仓库上已有同名附件，' -ForegroundColor Yellow
    Write-Host '      必须用本次产出的包覆盖，否则哈希对不上，升级会被判定为校验失败并丢弃。' -ForegroundColor Yellow
}