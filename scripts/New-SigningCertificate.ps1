[CmdletBinding()]
param(
    [string]$Subject = "CN=scottlz0310",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\artifacts\signing"),
    [int]$ValidYears = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($ValidYears -lt 1) {
    throw "ValidYears は1以上で指定してください。"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$passwordBytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($passwordBytes)
$password = [Convert]::ToBase64String($passwordBytes)
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force
$cert = $null

try {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -FriendlyName "PhotoMapStudio MSIX signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears($ValidYears)

    $pfxPath = Join-Path $OutDir "PhotoMapStudio-signing.pfx"
    $cerPath = Join-Path $OutDir "PhotoMapStudio.cer"
    $base64Path = Join-Path $OutDir "pfx-base64.txt"
    $passwordPath = Join-Path $OutDir "signing-password.txt"

    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    [IO.File]::WriteAllText($base64Path, [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($passwordPath, $password, [Text.UTF8Encoding]::new($false))

    Write-Output "証明書を生成しました（有効期限: $($cert.NotAfter.ToString('yyyy-MM-dd'))、Subject: $Subject）"
    Write-Output "出力先: $OutDir"
    Write-Output "秘密鍵とパスワードは artifacts/ 配下から移動・コミットしないでください。"
    Write-Output "GitHub Secrets 登録例:"
    Write-Output "  Get-Content '$base64Path' -Raw | gh secret set SIGNING_CERTIFICATE_BASE64"
    Write-Output "  Get-Content '$passwordPath' -Raw | gh secret set SIGNING_CERTIFICATE_PASSWORD"
}
finally {
    if ($null -ne $cert) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
}
