[CmdletBinding()]
param(
    [string]$SourceIconDark = (Join-Path $PSScriptRoot "..\assets-source\icon-dark.png"),
    [string]$SourceIconLight = (Join-Path $PSScriptRoot "..\assets-source\icon-light.png"),
    [string]$SourceSplashDark = (Join-Path $PSScriptRoot "..\assets-source\splash-dark.png"),
    [string]$SourceSplashLight = (Join-Path $PSScriptRoot "..\assets-source\splash-light.png"),
    [string]$SourceInstallerLogo = (Join-Path $PSScriptRoot "..\assets-source\installer-logo.png"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\src\PhotoMapStudio.App\Assets")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$IcoSizes = @(16, 24, 32, 48, 64, 128, 256)
$InstallerLogoSize = 400

function Resize-Image {
    param(
        [System.Drawing.Image]$Image,
        [int]$Width,
        [int]$Height
    )

    $resized = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($resized)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($Image, [System.Drawing.Rectangle]::new(0, 0, $Width, $Height))
    }
    finally {
        $graphics.Dispose()
    }

    return $resized
}

function Save-ScaledAsset {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$BaseName,
        [int]$BaseWidth,
        [int]$BaseHeight,
        [string]$DestinationDir,
        [int[]]$Scales = @(100, 125, 150, 200)
    )

    foreach ($scale in $Scales) {
        $width = [math]::Round($BaseWidth * $scale / 100)
        $height = [math]::Round($BaseHeight * $scale / 100)
        $resized = Resize-Image -Image $SourceImage -Width $width -Height $height
        try {
            $fileName = if ($scale -eq 100) { "$BaseName.png" } else { "$BaseName.scale-$scale.png" }
            $resized.Save((Join-Path $DestinationDir $fileName), [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "  生成: $fileName ($width x $height)"
        }
        finally {
            $resized.Dispose()
        }
    }
}

function Save-TargetSizeAsset {
    param(
        [System.Drawing.Image]$DarkImage,
        [System.Drawing.Image]$LightImage,
        [string]$BaseName,
        [int]$TargetSize,
        [string]$DestinationDir
    )

    $variants = @(
        @{ Suffix = ""; Image = $DarkImage },
        @{ Suffix = "_altform-unplated"; Image = $DarkImage },
        @{ Suffix = "_altform-lightunplated"; Image = $LightImage }
    )

    foreach ($variant in $variants) {
        $resized = Resize-Image -Image $variant.Image -Width $TargetSize -Height $TargetSize
        try {
            $fileName = "$BaseName.targetsize-$TargetSize$($variant.Suffix).png"
            $resized.Save((Join-Path $DestinationDir $fileName), [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "  生成: $fileName ($TargetSize x $TargetSize)"
        }
        finally {
            $resized.Dispose()
        }
    }
}

function Get-ContentBounds {
    param([System.Drawing.Bitmap]$Image, [int]$AlphaThreshold = 10)

    $minX = $Image.Width
    $maxX = -1
    $minY = $Image.Height
    $maxY = -1

    for ($y = 0; $y -lt $Image.Height; $y++) {
        for ($x = 0; $x -lt $Image.Width; $x++) {
            if ($Image.GetPixel($x, $y).A -gt $AlphaThreshold) {
                $minX = [math]::Min($minX, $x)
                $maxX = [math]::Max($maxX, $x)
                $minY = [math]::Min($minY, $y)
                $maxY = [math]::Max($maxY, $y)
            }
        }
    }

    if ($maxX -lt 0) {
        throw "不透明な画素がありません: $($Image.Width)x$($Image.Height)"
    }

    return [System.Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
}

function Save-SquareLogo {
    param(
        [System.Drawing.Bitmap]$SourceImage,
        [string]$OutputPath,
        [int]$Size
    )

    $bounds = Get-ContentBounds -Image $SourceImage
    $cropped = $SourceImage.Clone($bounds, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $scale = [math]::Min($Size / $cropped.Width, $Size / $cropped.Height)
        $width = [math]::Max(1, [int][math]::Round($cropped.Width * $scale))
        $height = [math]::Max(1, [int][math]::Round($cropped.Height * $scale))
        $resized = Resize-Image -Image $cropped -Width $width -Height $height
        try {
            $canvas = [System.Drawing.Bitmap]::new($Size, $Size)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($canvas)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.DrawImage($resized, [int](($Size - $width) / 2), [int](($Size - $height) / 2), $width, $height)
                }
                finally {
                    $graphics.Dispose()
                }

                $canvas.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $canvas.Dispose()
            }
        }
        finally {
            $resized.Dispose()
        }
    }
    finally {
        $cropped.Dispose()
    }
}

function Save-IconFile {
    param(
        [System.Drawing.Image]$SourceImage,
        [string]$OutputPath,
        [int[]]$Sizes
    )

    $blobs = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $Sizes) {
        $resized = Resize-Image -Image $SourceImage -Width $size -Height $size
        try {
            $stream = [System.IO.MemoryStream]::new()
            try {
                $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $blobs.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $resized.Dispose()
        }
    }

    $file = [System.IO.File]::Create($OutputPath)
    try {
        $writer = [System.IO.BinaryWriter]::new($file)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$Sizes.Count)

            $offset = 6 + 16 * $Sizes.Count
            for ($i = 0; $i -lt $Sizes.Count; $i++) {
                $dimension = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
                $writer.Write([byte]$dimension)
                $writer.Write([byte]$dimension)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$blobs[$i].Length)
                $writer.Write([uint32]$offset)
                $offset += $blobs[$i].Length
            }

            foreach ($blob in $blobs) {
                $writer.Write($blob)
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $file.Dispose()
    }
}

foreach ($source in @($SourceIconDark, $SourceIconLight, $SourceSplashDark, $SourceSplashLight, $SourceInstallerLogo)) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "原画が見つかりません: $source"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path

Get-ChildItem -LiteralPath $OutputDir -File |
    Where-Object { @(".png", ".ico") -contains $_.Extension.ToLowerInvariant() } |
    Remove-Item -Force

$iconDark = $null
$iconLight = $null
$splashDark = $null
$splashLight = $null
$installerLogo = $null
try {
    $iconDark = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourceIconDark))
    $iconLight = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourceIconLight))
    $splashDark = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourceSplashDark))
    $splashLight = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourceSplashLight))
    $installerLogo = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $SourceInstallerLogo))

    foreach ($logo in @(
            @{ Name = "Square44x44Logo"; Size = 44 },
            @{ Name = "Square71x71Logo"; Size = 71 },
            @{ Name = "Square150x150Logo"; Size = 150 },
            @{ Name = "Square310x310Logo"; Size = 310 },
            @{ Name = "StoreLogo"; Size = 50 }
        )) {
        Save-ScaledAsset -SourceImage $iconDark -BaseName $logo.Name -BaseWidth $logo.Size -BaseHeight $logo.Size -DestinationDir $OutputDir
    }

    foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 48, 60, 72, 80, 96, 256)) {
        Save-TargetSizeAsset -DarkImage $iconDark -LightImage $iconLight -BaseName "Square44x44Logo" -TargetSize $size -DestinationDir $OutputDir
    }

    Save-ScaledAsset -SourceImage $splashDark -BaseName "Wide310x150Logo" -BaseWidth 310 -BaseHeight 150 -DestinationDir $OutputDir
    Save-ScaledAsset -SourceImage $splashDark -BaseName "SplashScreen" -BaseWidth 620 -BaseHeight 300 -DestinationDir $OutputDir
    Save-ScaledAsset -SourceImage $splashLight -BaseName "SplashScreenLight" -BaseWidth 620 -BaseHeight 300 -DestinationDir $OutputDir
    Save-IconFile -SourceImage $iconDark -OutputPath (Join-Path $OutputDir "app.ico") -Sizes $IcoSizes
    Save-SquareLogo -SourceImage $installerLogo -OutputPath (Join-Path $OutputDir "InstallerLogo.png") -Size $InstallerLogoSize
}
finally {
    foreach ($image in @($iconDark, $iconLight, $splashDark, $splashLight, $installerLogo)) {
        if ($null -ne $image) {
            $image.Dispose()
        }
    }
}

$pngCount = (Get-ChildItem -LiteralPath $OutputDir -Filter "*.png" -File | Measure-Object).Count
$icoCount = (Get-ChildItem -LiteralPath $OutputDir -Filter "*.ico" -File | Measure-Object).Count
Write-Host "生成完了: $OutputDir に PNG $pngCount 件 / ICO $icoCount 件"
