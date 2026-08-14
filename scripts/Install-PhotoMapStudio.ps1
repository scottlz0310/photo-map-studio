<#
.SYNOPSIS
PhotoMapStudio をインストールする（署名証明書のインポート + .appinstaller 経由の導入）。

.DESCRIPTION
GitHub Releases の最新リリースから公開証明書（.cer）と .appinstaller を取得して、
署名証明書を LocalMachine\TrustedPeople ストアへ登録し、アプリをインストールする。

LocalMachine の証明書ストアへの書き込みには管理者権限が必要なため、非管理者の
PowerShell から実行した場合は UAC プロンプトで昇格して再実行する。

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass -File .\Install-PhotoMapStudio.ps1 -Architecture x64
#>
[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") { "arm64" } else { "x64" }),
    [string]$Repo = "scottlz0310/photo-map-studio",
    # 自己昇格で再起動されたことを示す内部用スイッチ
    [switch]$Elevated,
    # テスト用：証明書登録・アプリインストールを実行せず、終了コードを出力する
    [switch]$Test,
    # テスト用：管理者権限チェックを偽装するためのオブジェクト
    [object]$PrincipalOverride = $null,
    # テスト用：Start-Process の動作を偽装するためのスクリプトブロック
    [scriptblock]$StartProcessOverride = $null
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$packageName = "PhotoMapStudio"

function Invoke-Exit {
    param([int]$Code)

    if ($Test) {
        return $Code
    }

    exit $Code
}

function Save-RemoteFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing -ErrorAction Stop
}

function Get-AppInstallerMainPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $node = (Select-Xml -Path $Path -XPath "/*[local-name()='AppInstaller']/*[local-name()='MainPackage']" -ErrorAction Stop).Node
    if ($null -eq $node) {
        throw ".appinstaller に MainPackage 要素がありません: $Path"
    }

    if ([string]::IsNullOrWhiteSpace([string]$node.Name) -or
        [string]::IsNullOrWhiteSpace([string]$node.Version) -or
        [string]::IsNullOrWhiteSpace([string]$node.ProcessorArchitecture)) {
        throw ".appinstaller の MainPackage 属性が不完全です: $Path"
    }

    return $node
}

function Wait-ForInstalledPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedVersion,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedArchitecture
    )

    $deadline = (Get-Date).AddSeconds(60)
    $lastState = "パッケージがまだ登録されていません"

    do {
        $package = Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $package) {
            $installLocation = [string]$package.InstallLocation
            $architecture = ([string]$package.Architecture).ToLowerInvariant()
            $manifestPath = if ([string]::IsNullOrWhiteSpace($installLocation)) {
                $null
            } else {
                Join-Path $installLocation "AppxManifest.xml"
            }

            if ($package.Status -eq "Ok" -and
                [string]$package.Version -eq $ExpectedVersion -and
                $architecture -eq $ExpectedArchitecture -and
                -not [string]::IsNullOrWhiteSpace($installLocation) -and
                (Test-Path -LiteralPath $installLocation) -and
                (Test-Path -LiteralPath $manifestPath)) {
                return $package
            }

            $lastState = "Status=$($package.Status), Version=$($package.Version), Architecture=$architecture, InstallLocation=$installLocation"
        }

        if ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)

    throw "AppX パッケージの登録完了を確認できませんでした（期待値: Name=$Name, Version=$ExpectedVersion, Architecture=$ExpectedArchitecture）。最終状態: $lastState"
}

if ($null -ne $PrincipalOverride) {
    $principal = $PrincipalOverride
} else {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
}

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "管理者権限が必要なため、UAC プロンプトで昇格して再実行します..."
    try {
        $arguments = @(
            "-NoProfile",
            "-ExecutionPolicy", "Bypass",
            "-File", "`"$PSCommandPath`"",
            "-Repo", $Repo,
            "-Architecture", $Architecture,
            "-Elevated"
        )
        if ($Test) {
            $arguments += "-Test"
        }

        if ($StartProcessOverride) {
            $process = & $StartProcessOverride
        } else {
            $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        }

        if ($null -eq $process -or $null -eq $process.ExitCode) {
            throw "昇格した PowerShell プロセスの終了コードを取得できませんでした。"
        }

        Invoke-Exit ([int]$process.ExitCode)
        return
    } catch {
        Write-Host "管理者権限への昇格に失敗しました: $($_.Exception.Message)"
        Invoke-Exit 1
        return
    }
}

$exitCode = 0
try {
    $baseUrl = "https://github.com/$Repo/releases/latest/download"
    $workDir = Join-Path $env:TEMP "PhotoMapStudio-install"
    $cerPath = Join-Path $workDir "PhotoMapStudio.cer"
    $appInstallerPath = Join-Path $workDir "PhotoMapStudio-$Architecture.appinstaller"

    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    Write-Host "署名証明書を取得しています..."
    Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio.cer" -Path $cerPath

    $certStorePath = "Cert:\LocalMachine\TrustedPeople"
    if (-not (Test-Path -LiteralPath $certStorePath)) {
        throw "証明書ストアを解決できません: $certStorePath"
    }

    if (-not $Test) {
        Write-Host "署名証明書を LocalMachine\TrustedPeople ストアへ登録しています..."
        $certificate = Get-PfxCertificate -FilePath $cerPath -ErrorAction Stop
        Import-Certificate -FilePath $cerPath -CertStoreLocation $certStorePath | Out-Null
        $registeredCertificate = Get-ChildItem -Path $certStorePath -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
            Select-Object -First 1
        if ($null -eq $registeredCertificate) {
            throw "署名証明書を $certStorePath に登録できませんでした。"
        }
    }

    Write-Host "App Installer 定義を取得しています（アーキテクチャ: $Architecture）..."
    Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio-$Architecture.appinstaller" -Path $appInstallerPath

    $mainPackage = Get-AppInstallerMainPackage -Path $appInstallerPath
    $expectedArchitecture = ([string]$mainPackage.ProcessorArchitecture).ToLowerInvariant()
    if ([string]$mainPackage.Name -ne $packageName) {
        throw ".appinstaller のパッケージ名が想定と異なります: $($mainPackage.Name)"
    }
    if ($expectedArchitecture -ne $Architecture.ToLowerInvariant()) {
        throw ".appinstaller のアーキテクチャが指定値と異なります: $expectedArchitecture"
    }
    $expectedVersion = [string]$mainPackage.Version
    $null = [version]$expectedVersion

    if ($Test) {
        Write-Host "テストモードのため、証明書登録とアプリインストールは実行しません。"
    } else {
        Write-Host "アプリをインストールしています（.appinstaller 経由）..."
        # -AppInstallerFile はスイッチであり、.appinstaller はローカルパスを -Path に渡す。
        Add-AppxPackage -Path $appInstallerPath -AppInstallerFile -ErrorAction Stop
        $installedPackage = Wait-ForInstalledPackage `
            -Name $packageName `
            -ExpectedVersion $expectedVersion `
            -ExpectedArchitecture $expectedArchitecture

        Write-Host "インストールが完了しました（バージョン: $($installedPackage.Version)、アーキテクチャ: $($installedPackage.Architecture)）。"
        Write-Host "スタートメニューから PhotoMapStudio を起動できます。"
        Write-Host "新バージョンはアプリ起動時に自動チェックされます。"
    }
} catch {
    Write-Host "PhotoMapStudio のインストールに失敗しました: $($_.Exception.Message)"
    $exitCode = 1
} finally {
    if ($Elevated -and -not $Test) {
        $null = Read-Host "Enter キーを押すと閉じます"
    }
}

Invoke-Exit $exitCode
