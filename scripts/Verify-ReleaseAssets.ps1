<#
.SYNOPSIS
GitHub Release の資産（MSIX・証明書・App Installer・インストールスクリプト）を検証する。

.DESCRIPTION
指定したタグの Release（draft を含む）から資産を取得し、公開してよい状態かを検証する。
これまで手作業で確認していた次の項目を自動化する。失敗した項目はすべて列挙して、終了コード 1 で終了する。

- 6 つの資産がそろっていること（MSIX × 2、.cer、.appinstaller × 2、Install スクリプト）
- MSIX の Identity（名前・版・アーキテクチャ・Publisher）がタグと一致すること
- MSIX が署名されていて、署名が壊れていないこと
- .cer の拇印が、両方の MSIX の署名者と一致すること
- .appinstaller の版・URL・更新設定（AutomaticBackgroundTask のみ）が正しいこと
- Install スクリプトが構文として解釈でき、（指定時）リポジトリのものと一致すること

draft の取得には書き込み権限のトークンが必要なため、GitHub CLI（gh）の認証が必要。

.PARAMETER Tag
検証する Release のタグ（例: v0.3.0）。

.PARAMETER Repo
対象リポジトリ（owner/repo）。既定は環境変数 GITHUB_REPOSITORY、なければ本リポジトリ。

.PARAMETER ExpectedInstallScriptPath
指定すると、Release の Install スクリプトがこのファイルと一致することを確認する。

.PARAMETER RunInstallTest
指定すると、Install スクリプトを -Test で実行する。このオプションは最新の公開 Release の資産を取得するため、
draft の検証には使えない。公開済みの Release を確認するときに指定する。

.EXAMPLE
pwsh -File .\scripts\Verify-ReleaseAssets.ps1 -Tag v0.2.3 -RunInstallTest
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Tag,

    [string]$Repo = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { "scottlz0310/photo-map-studio" }),

    [string]$ExpectedInstallScriptPath,

    [switch]$RunInstallTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageName = "PhotoMapStudio"
$publisher = "CN=scottlz0310"
$version = "$($Tag.TrimStart('v')).0"
$assetBaseUrl = "https://github.com/$Repo/releases"
$msixAssets = @(
    @{ Name = "PhotoMapStudio_x64.msix"; Architecture = "x64"; AppInstaller = "PhotoMapStudio-x64.appinstaller" },
    @{ Name = "PhotoMapStudio_ARM64.msix"; Architecture = "arm64"; AppInstaller = "PhotoMapStudio-arm64.appinstaller" }
)
$certificateAsset = "PhotoMapStudio.cer"
$installScriptAsset = "Install-PhotoMapStudio.ps1"
$expectedAssets = @($msixAssets.ForEach({ $_.Name }) + $msixAssets.ForEach({ $_.AppInstaller }) + $certificateAsset + $installScriptAsset)

$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message)
    Write-Host "  NG: $Message"
}

function Write-Ok {
    param([string]$Message)
    Write-Host "  OK: $Message"
}

function Invoke-Gh {
    param([string[]]$Arguments)
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') に失敗しました。"
    }
    return $output
}

# gh の認証トークン（GH_TOKEN があればそれ、なければ gh auth login 済みのもの）。表示しない
$token = if ($env:GH_TOKEN) { $env:GH_TOKEN } elseif ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { (Invoke-Gh @("auth", "token")).Trim() }

# --- Release と資産の取得 -------------------------------------------------------------------------
Write-Host "[1/6] Release $Tag の資産を取得します（$Repo）"
# draft は tags/{tag} では取得できないため、一覧から探す
$releasesJson = Invoke-Gh @("api", "--paginate", "--slurp", "repos/$Repo/releases?per_page=100")
$releases = @(($releasesJson -join "`n" | ConvertFrom-Json) | ForEach-Object { $_ })
$matching = @($releases | Where-Object { $_.tag_name -eq $Tag })
if ($matching.Count -ne 1) {
    throw "タグ $Tag の Release が 1 件ではありません: $($matching.Count) 件"
}

$release = $matching[0]
Write-Host "  Release: draft=$($release.draft), 資産 $(@($release.assets).Count) 件"

$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "pms-verify-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $workDirectory | Out-Null

try {
    $assetNames = @($release.assets | ForEach-Object { $_.name })
    $missing = @($expectedAssets | Where-Object { $_ -notin $assetNames })
    $unexpected = @($assetNames | Where-Object { $_ -notin $expectedAssets })
    if ($missing.Count -gt 0) { Add-Failure "資産が不足しています: $($missing -join ', ')" }
    if ($unexpected.Count -gt 0) { Add-Failure "想定外の資産があります: $($unexpected -join ', ')" }
    if ($missing.Count -eq 0 -and $unexpected.Count -eq 0) { Write-Ok "6 つの資産がそろっています" }

    foreach ($name in $expectedAssets | Where-Object { $_ -in $assetNames }) {
        $asset = $release.assets | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if ($asset.size -le 0) {
            Add-Failure "資産が空です: $name"
            continue
        }

        $path = Join-Path $workDirectory $name
        try {
            # draft の資産は、トークン付きの資産 API からだけ取得できる（リダイレクト先へは認証情報が転送されない）
            Invoke-WebRequest -Uri "https://api.github.com/repos/$Repo/releases/assets/$($asset.id)" `
                -Headers @{ Authorization = "Bearer $token"; Accept = "application/octet-stream" } `
                -OutFile $path
        }
        catch {
            Add-Failure "資産を取得できません: $name（$($_.Exception.Message)）"
        }
    }

    # --- MSIX -------------------------------------------------------------------------------------
    Write-Host "[2/6] MSIX の Identity を検証します"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($msix in $msixAssets) {
        $path = Join-Path $workDirectory $msix.Name
        if (-not (Test-Path -LiteralPath $path)) { continue }

        $zip = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $entry = $zip.GetEntry("AppxManifest.xml")
            if (-not $entry) {
                Add-Failure "$($msix.Name): AppxManifest.xml がありません"
                continue
            }

            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        finally {
            $zip.Dispose()
        }

        $identity = $manifest.Package.Identity
        $expectations = [ordered]@{
            Name = $packageName
            Publisher = $publisher
            Version = $version
            ProcessorArchitecture = $msix.Architecture
        }
        foreach ($key in $expectations.Keys) {
            $actual = $identity.GetAttribute($key)
            if ($actual -ne $expectations[$key]) {
                Add-Failure "$($msix.Name): Identity の $key が想定と異なります（期待 $($expectations[$key]) / 実際 $actual）"
            }
            else {
                Write-Ok "$($msix.Name): $key = $actual"
            }
        }
    }

    # --- 署名と証明書 -----------------------------------------------------------------------------
    Write-Host "[3/6] MSIX の署名と .cer を検証します"
    $certificatePath = Join-Path $workDirectory $certificateAsset
    $cerThumbprint = $null
    if (Test-Path -LiteralPath $certificatePath) {
        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
        $cerThumbprint = $certificate.Thumbprint
        if ($certificate.Subject -ne $publisher) {
            Add-Failure "${certificateAsset}: Subject が Publisher と一致しません（$($certificate.Subject)）"
        }
        else {
            Write-Ok "${certificateAsset}: Subject = $($certificate.Subject)"
        }
    }

    foreach ($msix in $msixAssets) {
        $path = Join-Path $workDirectory $msix.Name
        if (-not (Test-Path -LiteralPath $path)) { continue }

        $signature = Get-AuthenticodeSignature -FilePath $path
        # 自己署名の証明書は、信頼済みストアに入れていない環境では UnknownError / NotTrusted になる。
        # 署名の欠落（NotSigned）と改ざん（HashMismatch）だけを失敗として扱う
        if (-not $signature.SignerCertificate -or $signature.Status -notin @("Valid", "UnknownError", "NotTrusted")) {
            Add-Failure "$($msix.Name): 署名を確認できません（Status = $($signature.Status)）"
            continue
        }

        Write-Ok "$($msix.Name): 署名あり（Status = $($signature.Status)）"
        if ($cerThumbprint -and $signature.SignerCertificate.Thumbprint -ne $cerThumbprint) {
            Add-Failure "$($msix.Name): 署名者が $certificateAsset と一致しません"
        }
        elseif ($cerThumbprint) {
            Write-Ok "$($msix.Name): 署名者が $certificateAsset と一致"
        }
    }

    # --- App Installer ----------------------------------------------------------------------------
    Write-Host "[4/6] .appinstaller を検証します"
    foreach ($msix in $msixAssets) {
        $path = Join-Path $workDirectory $msix.AppInstaller
        if (-not (Test-Path -LiteralPath $path)) { continue }

        $xml = [xml](Get-Content -LiteralPath $path -Raw -Encoding UTF8)
        $root = $xml.AppInstaller
        $main = $root.MainPackage
        $architectureName = $msix.AppInstaller
        $expectedRootUri = "$assetBaseUrl/latest/download/$($msix.AppInstaller)"
        $expectedPackageUri = "$assetBaseUrl/download/$Tag/$($msix.Name)"

        $checks = [ordered]@{
            "ルートの Version" = @($root.Version, $version)
            "ルートの Uri" = @($root.Uri, $expectedRootUri)
            "MainPackage の Name" = @($main.Name, $packageName)
            "MainPackage の Publisher" = @($main.Publisher, $publisher)
            "MainPackage の Version" = @($main.Version, $version)
            "MainPackage の ProcessorArchitecture" = @($main.ProcessorArchitecture, $msix.Architecture)
            "MainPackage の Uri" = @($main.Uri, $expectedPackageUri)
        }
        foreach ($label in $checks.Keys) {
            $actual, $expected = $checks[$label]
            if ($actual -ne $expected) {
                Add-Failure "${architectureName}: $label が想定と異なります（期待 $expected / 実際 $actual）"
            }
            else {
                Write-Ok "${architectureName}: $label"
            }
        }

        # 起動時の更新確認画面を避けるため、バックグラウンド確認（AutomaticBackgroundTask）だけを指定する
        $updateChildren = @($root.UpdateSettings.ChildNodes | Where-Object { $_.NodeType -eq "Element" } | ForEach-Object { $_.LocalName })
        if ($updateChildren.Count -ne 1 -or $updateChildren[0] -ne "AutomaticBackgroundTask") {
            Add-Failure "${architectureName}: UpdateSettings は AutomaticBackgroundTask だけを指定してください（実際: $($updateChildren -join ', ')）"
        }
        else {
            Write-Ok "${architectureName}: UpdateSettings = AutomaticBackgroundTask のみ"
        }
    }

    # --- Install スクリプト -----------------------------------------------------------------------
    Write-Host "[5/6] Install スクリプトを検証します"
    $scriptPath = Join-Path $workDirectory $installScriptAsset
    if (Test-Path -LiteralPath $scriptPath) {
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$null, [ref]$parseErrors)
        if ($parseErrors -and $parseErrors.Count -gt 0) {
            Add-Failure "${installScriptAsset}: 構文エラーがあります（$($parseErrors[0].Message)）"
        }
        else {
            Write-Ok "${installScriptAsset}: 構文を解釈できます"
        }

        if ($ExpectedInstallScriptPath) {
            $expectedHash = (Get-FileHash -LiteralPath $ExpectedInstallScriptPath -Algorithm SHA256).Hash
            $actualHash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
            if ($expectedHash -ne $actualHash) {
                Add-Failure "${installScriptAsset}: リポジトリのスクリプトと一致しません"
            }
            else {
                Write-Ok "${installScriptAsset}: リポジトリのスクリプトと一致"
            }
        }

        Write-Host "[6/6] Install スクリプトの -Test"
        if ($RunInstallTest) {
            $architecture = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
            & pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Test -Architecture $architecture -Repo $Repo
            if ($LASTEXITCODE -ne 0) {
                Add-Failure "${installScriptAsset}: -Test に失敗しました"
            }
            else {
                Write-Ok "${installScriptAsset}: -Test に成功"
            }
        }
        else {
            Write-Host "  スキップ: -Test は最新の公開 Release の資産を取得するため、draft の検証では実行しません（-RunInstallTest）"
        }
    }
}
finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "検証に失敗しました（$($failures.Count) 件）:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host ""
Write-Host "Release $Tag の資産の検証に成功しました。"
