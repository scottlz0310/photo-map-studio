<#
.SYNOPSIS
PhotoMapStudio をインストールする（署名証明書のインポート + .appinstaller 経由の導入）。

.DESCRIPTION
GitHub Releases の最新リリースから公開証明書（.cer）と .appinstaller を取得して、
署名証明書を LocalMachine\TrustedPeople ストアへ登録し、元のユーザーとしてアプリをインストールする。

証明書が未登録の場合だけ UAC で証明書登録用の子プロセスを起動する。
AppX のインストールと結果確認は、常にこのスクリプトを起動したユーザーのプロセスで実行する。

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass -File .\Install-PhotoMapStudio.ps1 -Architecture x64
#>
[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = $(if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") { "arm64" } else { "x64" }),
    [string]$Repo = "scottlz0310/photo-map-studio",
    [ValidateRange(30, 900)]
    [int]$InstallTimeoutSeconds = 180,
    # テスト用：正式な Release 資産の取得と定義検証だけを行う
    [switch]$Test,
    # 内部用：LocalMachine への証明書登録だけを昇格プロセスで行う
    [switch]$ImportCertificateOnly,
    # 内部用：証明書登録対象のローカルパス
    [string]$CertificatePath = $null
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$packageName = "PhotoMapStudio"
$certStorePath = "Cert:\LocalMachine\TrustedPeople"

function Invoke-Exit {
    param([int]$Code)

    if ($Test) {
        return $Code
    }

    exit $Code
}

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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

    $name = $node.GetAttribute("Name")
    $version = $node.GetAttribute("Version")
    $processorArchitecture = $node.GetAttribute("ProcessorArchitecture")
    if ([string]::IsNullOrWhiteSpace($name) -or
        [string]::IsNullOrWhiteSpace($version) -or
        [string]::IsNullOrWhiteSpace($processorArchitecture)) {
        throw ".appinstaller の MainPackage 属性が不完全です: $Path"
    }

    return [PSCustomObject]@{
        Name                  = $name
        Version               = $version
        ProcessorArchitecture = $processorArchitecture
    }
}

function Get-RegisteredCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StorePath,
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )

    $matches = @(Get-ChildItem -Path $StorePath -ErrorAction Stop |
            Where-Object { $_.Thumbprint -eq $Thumbprint })
    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[0]
}

function Import-CertificateToLocalMachine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-IsAdministrator)) {
        throw "証明書登録用プロセスに管理者権限がありません。"
    }
    if (-not (Test-Path -LiteralPath $certStorePath)) {
        throw "証明書ストアを解決できません: $certStorePath"
    }

    $certificate = Get-PfxCertificate -FilePath $Path -ErrorAction Stop
    Import-Certificate -FilePath $Path -CertStoreLocation $certStorePath | Out-Null
    $registeredCertificate = Get-RegisteredCertificate -StorePath $certStorePath -Thumbprint $certificate.Thumbprint
    if ($null -eq $registeredCertificate) {
        throw "署名証明書を $certStorePath に登録できませんでした。"
    }
}

function Wait-ForInstalledPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedVersion,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedArchitecture,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
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

    throw "APPX_INSTALL_PENDING: AppX パッケージの登録完了を $TimeoutSeconds 秒以内に確認できませんでした（期待値: Name=$Name, Version=$ExpectedVersion, Architecture=$ExpectedArchitecture）。最終状態: $lastState"
}

$exitCode = 0
try {
    $baseUrl = "https://github.com/$Repo/releases/latest/download"
    $workDir = Join-Path $env:TEMP "PhotoMapStudio-install"
    $cerPath = if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        Join-Path $workDir "PhotoMapStudio.cer"
    } else {
        $CertificatePath
    }
    $appInstallerPath = Join-Path $workDir "PhotoMapStudio-$Architecture.appinstaller"

    if ($ImportCertificateOnly) {
        if ($Test) {
            throw "-Test と -ImportCertificateOnly は同時に指定できません。"
        }
        if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
            throw "-ImportCertificateOnly には -CertificatePath が必要です。"
        }

        Write-Host "署名証明書を LocalMachine\TrustedPeople ストアへ登録しています..."
        Import-CertificateToLocalMachine -Path $CertificatePath
    } elseif ($Test) {
        New-Item -ItemType Directory -Force -Path $workDir | Out-Null

        Write-Host "署名証明書を取得しています..."
        Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio.cer" -Path $cerPath
        $null = Get-PfxCertificate -FilePath $cerPath -ErrorAction Stop

        Write-Host "App Installer 定義を取得しています（アーキテクチャ: $Architecture）..."
        Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio-$Architecture.appinstaller" -Path $appInstallerPath
        $mainPackage = Get-AppInstallerMainPackage -Path $appInstallerPath
        if ($mainPackage.Name -ne $packageName) {
            throw ".appinstaller のパッケージ名が想定と異なります: $($mainPackage.Name)"
        }
        if ($mainPackage.ProcessorArchitecture.ToLowerInvariant() -ne $Architecture.ToLowerInvariant()) {
            throw ".appinstaller のアーキテクチャが指定値と異なります: $($mainPackage.ProcessorArchitecture)"
        }
        $null = [version]$mainPackage.Version
        Write-Host "テストモードのため、証明書登録とアプリインストールは実行しません。"
    } else {
        New-Item -ItemType Directory -Force -Path $workDir | Out-Null

        Write-Host "署名証明書を取得しています..."
        Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio.cer" -Path $cerPath
        $certificate = Get-PfxCertificate -FilePath $cerPath -ErrorAction Stop
        if (-not (Test-Path -LiteralPath $certStorePath)) {
            throw "証明書ストアを解決できません: $certStorePath"
        }

        $registeredCertificate = Get-RegisteredCertificate -StorePath $certStorePath -Thumbprint $certificate.Thumbprint
        if ($null -eq $registeredCertificate) {
            if (Test-IsAdministrator) {
                Write-Host "署名証明書を LocalMachine\TrustedPeople ストアへ登録しています..."
                Import-CertificateToLocalMachine -Path $cerPath
            } else {
                Write-Host "署名証明書の登録に必要な場合だけ、UAC で管理者権限を要求します..."
                $arguments = @(
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-File", "`"$PSCommandPath`"",
                    "-ImportCertificateOnly",
                    "-CertificatePath", "`"$cerPath`""
                )
                $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
                if ($null -eq $process -or $null -eq $process.ExitCode) {
                    throw "証明書登録用の昇格プロセスの終了コードを取得できませんでした。"
                }
                if ([int]$process.ExitCode -ne 0) {
                    throw "証明書登録用の昇格プロセスが終了コード $($process.ExitCode) で終了しました。"
                }
            }

            $registeredCertificate = Get-RegisteredCertificate -StorePath $certStorePath -Thumbprint $certificate.Thumbprint
            if ($null -eq $registeredCertificate) {
                throw "署名証明書を $certStorePath に登録できませんでした。"
            }
        }

        Write-Host "App Installer 定義を取得しています（アーキテクチャ: $Architecture）..."
        Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio-$Architecture.appinstaller" -Path $appInstallerPath

        $mainPackage = Get-AppInstallerMainPackage -Path $appInstallerPath
        if ($mainPackage.Name -ne $packageName) {
            throw ".appinstaller のパッケージ名が想定と異なります: $($mainPackage.Name)"
        }
        $expectedArchitecture = $mainPackage.ProcessorArchitecture.ToLowerInvariant()
        if ($expectedArchitecture -ne $Architecture.ToLowerInvariant()) {
            throw ".appinstaller のアーキテクチャが指定値と異なります: $($mainPackage.ProcessorArchitecture)"
        }
        $expectedVersion = [string]$mainPackage.Version
        $null = [version]$expectedVersion

        Write-Host "アプリをインストールしています（.appinstaller 経由）..."
        # -AppInstallerFile はスイッチであり、.appinstaller はローカルパスを -Path に渡す。
        Add-AppxPackage -Path $appInstallerPath -AppInstallerFile -ErrorAction Stop
        $installedPackage = Wait-ForInstalledPackage `
            -Name $packageName `
            -ExpectedVersion $expectedVersion `
            -ExpectedArchitecture $expectedArchitecture `
            -TimeoutSeconds $InstallTimeoutSeconds

        Write-Host "インストールが完了しました（バージョン: $($installedPackage.Version)、アーキテクチャ: $($installedPackage.Architecture)）。"
        Write-Host "スタートメニューから PhotoMapStudio を起動できます。"
        Write-Host "新バージョンはアプリ起動時に自動チェックされます。"
    }
} catch {
    $message = $_.Exception.Message
    if ($message.StartsWith("APPX_INSTALL_PENDING:")) {
        Write-Host ("PhotoMapStudio のインストール要求は開始されましたが、完了を確認できませんでした。" + $message.Substring("APPX_INSTALL_PENDING:".Length))
        Write-Host "Deployment Service の処理が継続中の可能性があります。数分後に Get-AppxPackage -Name PhotoMapStudio で状態を再確認してください。"
        $exitCode = 2
    } else {
        Write-Host "PhotoMapStudio のインストールに失敗しました: $message"
        $exitCode = 1
    }
}

Invoke-Exit $exitCode
