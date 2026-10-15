<#
.SYNOPSIS
  Packs PNG files into a single Windows .ico (PNG-compressed entries, Vista+ format).
.EXAMPLE
  ./tools/make-ico.ps1 -Output src/MonitorControl.App/Assets/AppIcon.ico -Png a16.png, a32.png, a256.png
#>
param(
    [Parameter(Mandatory)] [string] $Output,
    [Parameter(Mandatory)] [string[]] $Png
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$images = foreach ($path in $Png) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $path))
    $bitmap = [System.Drawing.Image]::FromFile((Resolve-Path $path))
    try {
        if ($bitmap.Width -gt 256 -or $bitmap.Width -ne $bitmap.Height) { throw "$path must be square and <= 256 px" }
        [pscustomobject]@{ Size = $bitmap.Width; Bytes = $bytes }
    } finally { $bitmap.Dispose() }
}

$stream = [System.IO.File]::Create($Output)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $dim = if ($img.Size -eq 256) { 0 } else { $img.Size }
        $writer.Write([byte]$dim); $writer.Write([byte]$dim)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$img.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) { $writer.Write($img.Bytes) }
} finally { $writer.Dispose() }
