<#
.SYNOPSIS
PhotoMapStudio.Rendering の依存と、pack した nupkg が宣言する依存を検査する。

.DESCRIPTION
CI（ci.yml）と公開（publish-rendering.yml）が同じ検査を使うための、1 つのスクリプト。

プロジェクトの依存（dotnet list package --include-transitive）:
- WinUI・Windows App SDK・PhotoMapStudio.Core へ（推移的にも）依存していない。
- SkiaSharp 系のパッケージは、Rendering の csproj が宣言する SkiaSharp の版だけである（別の版を引いていない）。

nupkg（-NupkgPath を指定したとき）:
- 宣言する依存が PhotoMapStudio.Tiles と SkiaSharp だけである。
- -CheckNuGet を指定すると、宣言する Tiles への依存の版が nuget.org に公開済みである（公開前の取り違えを防ぐ）。

どれかが満たされなければ失敗する。

.EXAMPLE
pwsh -File .\scripts\Test-RenderingPackage.ps1

.EXAMPLE
pwsh -File .\scripts\Test-RenderingPackage.ps1 -NupkgPath .\nupkg\PhotoMapStudio.Rendering.0.1.0.nupkg -CheckNuGet
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = 'src/PhotoMapStudio.Rendering/PhotoMapStudio.Rendering.csproj',

    [string]$NupkgPath,

    [switch]$CheckNuGet
)

$ErrorActionPreference = 'Stop'

$project = [xml](Get-Content -LiteralPath $ProjectPath -Raw)
$skiaReference = @($project.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'SkiaSharp' })
if ($skiaReference.Count -ne 1) { throw "$ProjectPath に SkiaSharp の PackageReference が 1 つではありません。" }
$skiaVersion = $skiaReference[0].Version

$packages = dotnet list $ProjectPath package --include-transitive
if ($LASTEXITCODE -ne 0) { throw 'dotnet list package に失敗しました。' }

$forbidden = @($packages | Select-String -Pattern 'WindowsAppSDK|Microsoft\.WinUI|Microsoft\.Windows\.SDK|PhotoMapStudio\.Core')
if ($forbidden.Count -gt 0) {
    $forbidden | ForEach-Object { Write-Error $_.Line }
    throw 'PhotoMapStudio.Rendering は WinUI・Windows App SDK・PhotoMapStudio.Core に依存してはいけません。'
}

# 「> SkiaSharp  4.156.0  4.156.0」の形（パッケージ名・要求した版・解決した版）。名前が SkiaSharp で始まる全てを対象にする
$mismatched = @()
foreach ($line in $packages) {
    if ($line -match '^\s*>\s+(SkiaSharp\S*)\s+(?:(\S+)\s+)?(\S+)\s*$') {
        $resolved = $Matches[3]
        if ($resolved -ne $skiaVersion) { $mismatched += "$($Matches[1]) $resolved" }
    }
}
if ($mismatched.Count -gt 0) {
    $mismatched | ForEach-Object { Write-Error $_ }
    throw "SkiaSharp 系のパッケージは $skiaVersion だけである必要があります（Rendering の csproj の宣言）。"
}
Write-Host "プロジェクトの依存: OK（SkiaSharp $skiaVersion のみ。WinUI・Windows App SDK・Core への依存なし）"

if ($NupkgPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $NupkgPath).Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $entry) { throw "nupkg に nuspec がありません: $NupkgPath" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }

    $ns = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
    $ns.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
    $dependencies = @($nuspec.SelectNodes('//n:dependency', $ns) | ForEach-Object { [pscustomobject]@{ Id = $_.id; Version = $_.version } })
    $ids = ($dependencies | ForEach-Object Id | Sort-Object -Unique) -join ', '
    if ($ids -ne 'PhotoMapStudio.Tiles, SkiaSharp') {
        throw "nupkg が宣言する依存が想定と違います: $ids（想定: PhotoMapStudio.Tiles, SkiaSharp）"
    }

    $tiles = $dependencies | Where-Object Id -eq 'PhotoMapStudio.Tiles' | Select-Object -First 1
    Write-Host "nupkg の依存: OK（PhotoMapStudio.Tiles $($tiles.Version)、SkiaSharp）"

    if ($CheckNuGet) {
        # 宣言する版は「>= 最小の版」。範囲の記法（[0.1.0, ) など）の場合は下限を取り出す
        $minimum = ($tiles.Version -replace '^[\[\(]\s*', '' -replace '\s*,.*$', '' -replace '[\]\)]\s*$', '').Trim()
        $index = Invoke-RestMethod -Uri 'https://api.nuget.org/v3-flatcontainer/photomapstudio.tiles/index.json'
        if ($index.versions -notcontains $minimum.ToLowerInvariant()) {
            throw "nupkg が宣言する PhotoMapStudio.Tiles $minimum は nuget.org に公開されていません。Tiles を先に公開するか、Tiles の csproj の既定の Version を公開済みの版にしてください（docs/development/releasing-rendering.md）。"
        }
        Write-Host "PhotoMapStudio.Tiles $minimum は nuget.org に公開済みです。"
    }
}
