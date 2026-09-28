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
# Windows PowerShell 5.1 の Invoke-WebRequest は進捗表示を有効にすると大幅に遅くなる。
# 取得するのは数 KB の .cer と .appinstaller だけなので、進捗は取得後のファイルサイズで示す。
$ProgressPreference = "SilentlyContinue"

$packageName = "PhotoMapStudio"
$certStorePath = "Cert:\LocalMachine\TrustedPeople"
$installStepCount = 5
# 長時間の処理中も 10 秒以上無出力にならないよう、この間隔で経過を出力する。
$progressReportIntervalSeconds = 5
$stepStopwatch = New-Object Diagnostics.Stopwatch

function Write-StepStart {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Number,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[$Number/$installStepCount] $Message"
    $stepStopwatch.Restart()
}

function Write-StepDetail {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "      $Message"
}

function Write-StepResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-StepDetail -Message ("{0}（{1:N1} 秒）" -f $Message, $stepStopwatch.Elapsed.TotalSeconds)
}

function Write-StepSkipped {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Number,
        [Parameter(Mandatory = $true)]
        [string]$Title,
        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    Write-Host "[$Number/$installStepCount] ${Title}: スキップ（$Reason）"
}

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
    return (Get-Item -LiteralPath $Path).Length
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

function Install-AppInstallerFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    # Add-AppxPackage は完了まで制御を返さないため、別 Runspace で実行して待機中に経過時間を出力する。
    $powerShell = [PowerShell]::Create()
    try {
        # -AppInstallerFile はスイッチであり、.appinstaller はローカルパスを -Path に渡す。
        $null = $powerShell.AddCommand("Add-AppxPackage").AddParameter("Path", $Path).AddParameter("AppInstallerFile").AddParameter("ErrorAction", "Stop")
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $asyncResult = $powerShell.BeginInvoke()
        while (-not $asyncResult.AsyncWaitHandle.WaitOne($progressReportIntervalSeconds * 1000)) {
            Write-StepDetail -Message ("処理中です（経過 {0:N0} 秒）..." -f $stopwatch.Elapsed.TotalSeconds)
        }

        try {
            $null = $powerShell.EndInvoke($asyncResult)
        } catch [System.Management.Automation.MethodInvocationException] {
            # EndInvoke の例外ラッパーを外し、Add-AppxPackage を直接実行した場合と同じエラーとして伝播する。
            $innerException = $_.Exception.InnerException
            if ($innerException -is [System.Management.Automation.IContainsErrorRecord]) {
                throw $innerException.ErrorRecord
            }
            throw
        }
    } finally {
        $powerShell.Dispose()
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

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $nextReportSeconds = $progressReportIntervalSeconds

    do {
        $lastState = "パッケージがまだ登録されていません"
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

        $elapsedSeconds = $stopwatch.Elapsed.TotalSeconds
        if ($elapsedSeconds -ge $nextReportSeconds) {
            Write-StepDetail -Message ("待機中です（経過 {0:N0}/{1} 秒、状態: {2}）..." -f $elapsedSeconds, $TimeoutSeconds, $lastState)
            $nextReportSeconds += $progressReportIntervalSeconds
        }

        if ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            Start-Sleep -Seconds 2
        }
    } while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds)

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
    } else {
        New-Item -ItemType Directory -Force -Path $workDir | Out-Null

        Write-StepStart -Number 1 -Message "署名証明書を取得しています..."
        $cerSize = Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio.cer" -Path $cerPath
        $certificate = Get-PfxCertificate -FilePath $cerPath -ErrorAction Stop
        Write-StepResult -Message ("完了: {0:N0} バイト、Thumbprint: {1}" -f $cerSize, $certificate.Thumbprint)

        if ($Test) {
            Write-StepSkipped -Number 2 -Title "署名証明書の登録" -Reason "テストモード"
        } else {
            Write-StepStart -Number 2 -Message "署名証明書の登録状態を確認しています（$certStorePath）..."
            if (-not (Test-Path -LiteralPath $certStorePath)) {
                throw "証明書ストアを解決できません: $certStorePath"
            }

            $registeredCertificate = Get-RegisteredCertificate -StorePath $certStorePath -Thumbprint $certificate.Thumbprint
            if ($null -ne $registeredCertificate) {
                Write-StepResult -Message "スキップ: 登録済みです"
            } elseif (Test-IsAdministrator) {
                Write-StepDetail -Message "管理者として実行中のため、署名証明書を直接登録します..."
                Import-CertificateToLocalMachine -Path $cerPath
                Write-StepResult -Message "完了: 登録しました"
            } else {
                Write-StepDetail -Message "署名証明書の登録には管理者権限が必要です。UAC の確認ダイアログで「はい」を選択してください。"
                Write-StepDetail -Message "承認すると別ウィンドウで証明書を登録し、その完了後にこのウィンドウで処理を続行します。"
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
                Write-StepDetail -Message "証明書登録用の昇格プロセスが終了しました（終了コード: 0）。"

                $registeredCertificate = Get-RegisteredCertificate -StorePath $certStorePath -Thumbprint $certificate.Thumbprint
                if ($null -eq $registeredCertificate) {
                    throw "署名証明書を $certStorePath に登録できませんでした。"
                }
                Write-StepResult -Message "完了: 登録を確認しました"
            }
        }

        Write-StepStart -Number 3 -Message "App Installer 定義を取得しています（アーキテクチャ: $Architecture）..."
        $appInstallerSize = Save-RemoteFile -Uri "$baseUrl/PhotoMapStudio-$Architecture.appinstaller" -Path $appInstallerPath
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
        Write-StepResult -Message ("完了: {0:N0} バイト、バージョン: {1}、アーキテクチャ: {2}" -f $appInstallerSize, $expectedVersion, $expectedArchitecture)

        if ($Test) {
            Write-StepSkipped -Number 4 -Title "アプリのインストール" -Reason "テストモード"
            Write-StepSkipped -Number 5 -Title "インストール完了の確認" -Reason "テストモード"
            Write-Host "テストモードのため、証明書登録とアプリインストールは実行しませんでした。"
        } else {
            Write-StepStart -Number 4 -Message "アプリをインストールしています（.appinstaller 経由）..."
            Write-StepDetail -Message "MSIX のダウンロード・展開・登録を行うため、数分かかる場合があります。"
            Install-AppInstallerFile -Path $appInstallerPath
            Write-StepResult -Message "完了"

            Write-StepStart -Number 5 -Message "インストール完了を確認しています（タイムアウト: $InstallTimeoutSeconds 秒）..."
            $installedPackage = Wait-ForInstalledPackage `
                -Name $packageName `
                -ExpectedVersion $expectedVersion `
                -ExpectedArchitecture $expectedArchitecture `
                -TimeoutSeconds $InstallTimeoutSeconds
            Write-StepResult -Message "完了: 登録を確認しました"

            Write-Host "インストールが完了しました（バージョン: $($installedPackage.Version)、アーキテクチャ: $($installedPackage.Architecture)）。"
            Write-Host "スタートメニューから PhotoMapStudio を起動できます。"
            Write-Host "新バージョンはアプリ起動時に自動チェックされます。"
        }
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
