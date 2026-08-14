[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") { "arm64" } else { "x64" }),
    [string]$Repo = "scottlz0310/photo-map-studio",
    [switch]$Test
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$baseUrl = "https://github.com/$Repo/releases/latest/download"
$workDir = Join-Path $env:TEMP "PhotoMapStudio-install"
$cerPath = Join-Path $workDir "PhotoMapStudio.cer"
$appInstallerPath = Join-Path $workDir "PhotoMapStudio-$Architecture.appinstaller"

New-Item -ItemType Directory -Force -Path $workDir | Out-Null

try {
    Write-Host "署名証明書を取得しています..."
    Invoke-WebRequest -Uri "$baseUrl/PhotoMapStudio.cer" -OutFile $cerPath

    $certStorePath = "Cert:\CurrentUser\TrustedPeople"
    if (-not (Test-Path -LiteralPath $certStorePath)) {
        throw "証明書ストアを解決できません: $certStorePath"
    }

    if (-not $Test) {
        Write-Host "署名証明書を現在のユーザーの TrustedPeople ストアへ登録しています..."
        Import-Certificate -FilePath $cerPath -CertStoreLocation $certStorePath | Out-Null
    }

    Write-Host "App Installer 定義を取得しています（アーキテクチャ: $Architecture）..."
    Invoke-WebRequest -Uri "$baseUrl/PhotoMapStudio-$Architecture.appinstaller" -OutFile $appInstallerPath

    if (-not $Test) {
        Add-AppxPackage -Path $appInstallerPath -AppInstallerFile
    }

    Write-Host "インストールが完了しました。スタートメニューから PhotoMapStudio を起動できます。"
    Write-Host "新バージョンはアプリ起動時に自動チェックされます。"
}
catch {
    Write-Error "PhotoMapStudio のインストールに失敗しました: $_"
    throw
}
