<#
.SYNOPSIS
PhotoMapStudio.Tiles に、最新の公開版（tiles-vX.Y.Z）から未公開の変更があるかを確認する。

.DESCRIPTION
最新の正式版のタグ（先行版 -rc.N などは除く）から HEAD までに、src/PhotoMapStudio.Tiles へ入った変更を分類する。

- None           : 変更なし。公開は不要。
- DependencyOnly : PackageReference の行だけの変更。コードと公開 API は変わらないが、パッケージが宣言する依存の版は変わる。公開するかは人が決める。
- Changed        : コードまたはプロジェクト設定の変更。公開を検討する。

公開はしない。結果は GitHub Actions のアノテーション（warning）とジョブのサマリーに出し、終了コードは常に 0（リリース準備を止めない）。
タグを参照するため、チェックアウトは fetch-depth: 0（タグを含む）で行う。

.EXAMPLE
pwsh -File .\scripts\Test-TilesUnpublishedChanges.ps1
#>
[CmdletBinding()]
param(
    [string]$PackagePath = 'src/PhotoMapStudio.Tiles',

    [string]$TagPrefix = 'tiles-v'
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') に失敗しました (exit $LASTEXITCODE)" }
    $output
}

$stablePattern = '^' + [regex]::Escape($TagPrefix) + '(\d+)\.(\d+)\.(\d+)$'
$latestTag = Invoke-Git tag --list "$TagPrefix*" |
    Where-Object { $_ -match $stablePattern } |
    Sort-Object { [version]($_.Substring($TagPrefix.Length)) } |
    Select-Object -Last 1

$kind = $null
$lines = [System.Collections.Generic.List[string]]::new()

if (-not $latestTag) {
    $kind = 'Changed'
    $lines.Add("正式版のタグ（${TagPrefix}X.Y.Z）が見つかりません。PhotoMapStudio.Tiles は未公開か、タグを取得できていません。")
}
else {
    $changedFiles = @(Invoke-Git diff --name-only $latestTag HEAD -- $PackagePath)
    if ($changedFiles.Count -eq 0) {
        $kind = 'None'
        $lines.Add("PhotoMapStudio.Tiles は、最新の公開版 $latestTag から変更がありません。")
    }
    else {
        $onlyProject = ($changedFiles.Count -eq 1) -and ($changedFiles[0] -like '*.csproj')
        $dependencyOnly = $false
        if ($onlyProject) {
            $changedLines = @(Invoke-Git diff -U0 $latestTag HEAD -- $changedFiles[0] |
                    Where-Object { $_ -match '^[+-]' -and $_ -notmatch '^(\+\+\+|---)' })
            $dependencyOnly = ($changedLines.Count -gt 0) -and -not ($changedLines | Where-Object { $_ -notmatch '^[+-]\s*<PackageReference\b' })
        }

        $commits = @(Invoke-Git log --oneline "$latestTag..HEAD" -- $PackagePath)
        if ($dependencyOnly) {
            $kind = 'DependencyOnly'
            $lines.Add("PhotoMapStudio.Tiles に、最新の公開版 $latestTag からの依存の更新（PackageReference）だけがあります。コードと公開 API は変わりませんが、パッケージが宣言する依存の版が変わります。公開するかを判断してください。")
        }
        else {
            $kind = 'Changed'
            $lines.Add("PhotoMapStudio.Tiles に、最新の公開版 $latestTag から未公開の変更があります。公開（docs/development/releasing-tiles.md）を検討してください。")
        }
        $lines.Add('')
        $lines.Add('変更したファイル:')
        $changedFiles | ForEach-Object { $lines.Add("- $_") }
        $lines.Add('')
        $lines.Add('コミット:')
        $commits | ForEach-Object { $lines.Add("- $_") }
    }
}

$text = $lines -join "`n"
Write-Host $text

if ($kind -ne 'None') {
    # アノテーションは 1 行で渡す（改行は %0A）
    $headline = $lines[0]
    Write-Host "::warning title=PhotoMapStudio.Tiles の未公開の変更::$headline"
}

if ($env:GITHUB_STEP_SUMMARY) {
    $heading = switch ($kind) {
        'None' { '公開が必要な変更はありません' }
        'DependencyOnly' { '依存の更新だけがあります（公開は判断）' }
        default { '未公開の変更があります' }
    }
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value "### PhotoMapStudio.Tiles: $heading`n`n$text`n"
}
