[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TileCachePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][double]$Latitude,
    [Parameter(Mandatory)][double]$Longitude,
    [int[]]$Zoom = @(16, 17, 18, 19),
    [string]$RepositoryPath = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
$cache = (Resolve-Path -LiteralPath $TileCachePath).Path
$repo = (Resolve-Path -LiteralPath $RepositoryPath).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$projectPath = Join-Path $repo 'src/PhotoMapStudio.Core/PhotoMapStudio.Core.csproj'
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Core プロジェクトがありません: $projectPath"
}
if ($Zoom | Where-Object { $_ -lt 0 -or $_ -gt 19 }) {
    throw '比較画像のズームは 0〜19 で指定してください。'
}

# 既存キャッシュだけを使い、通信や配布版の設定変更をせずに比較する。
$runner = Join-Path ([System.IO.Path]::GetTempPath()) ('pms-map-comparison-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runner -Force | Out-Null
$escapedProject = [System.Security.SecurityElement]::Escape($projectPath)
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><ProjectReference Include="$escapedProject" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $runner 'Comparison.csproj') -Encoding utf8

@'
using System.Globalization;
using PhotoMapStudio.Core.Geo;
using PhotoMapStudio.Core.Tiles;
using PhotoMapStudio.Core.Tiles.Vector;
using SkiaSharp;

string cache = args[0], output = args[1];
var center = new GeoCoordinate(double.Parse(args[2], CultureInfo.InvariantCulture), double.Parse(args[3], CultureInfo.InvariantCulture));
Directory.CreateDirectory(output);
using var style = new OpenMapTilesStyle();
var renderer = new VectorMapRenderer(new LocalSource(cache), style);
foreach (string value in args.Skip(4))
{
    int zoom = int.Parse(value, CultureInfo.InvariantCulture);
    using var bitmap = await renderer.RenderMapAsync(center, zoom, 800, 600);
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    string path = Path.Combine(output, $"z{zoom}.png");
    File.WriteAllBytes(path, data.ToArray());
    Console.WriteLine(path);
}

sealed class LocalSource(string cache) : IVectorTileSource
{
    public string SourceId => "comparison-local-openfreemap";
    public int MaxZoom => 14;
    public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(cache, $"{z}_{x}_{y}.pbf");
        if (!File.Exists(path)) { throw new FileNotFoundException("比較に必要な保存タイルがありません", path); }
        return Task.FromResult<byte[]?>(File.ReadAllBytes(path));
    }
}
'@ | Set-Content -LiteralPath (Join-Path $runner 'Program.cs') -Encoding utf8

$culture = [System.Globalization.CultureInfo]::InvariantCulture
& dotnet run --project $runner -c Release -- $cache $output $Latitude.ToString($culture) $Longitude.ToString($culture) @Zoom
if ($LASTEXITCODE -ne 0) { throw "比較画像の生成に失敗しました（exit=$LASTEXITCODE）。検証用プロジェクト: $runner" }
Write-Output "検証用プロジェクト: $runner"
