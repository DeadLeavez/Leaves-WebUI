<#
.SYNOPSIS
  Zips THIS mod's folder into ModWorkspace\Backups\.
.DESCRIPTION
  Drop this file in a mod's root and run it - it backs up the folder it lives in. Excludes build
  output (bin/obj/dist), the per-profile assets\generated caches, and backups\ - none worth
  versioning. Identical in every mod (it keys off its own location), so copy it wherever needed.
  Output: ModWorkspace\Backups\<ModName>_<yyyy-MM-dd>[_<desc>].zip
.PARAMETER Description
  Optional label appended to the filename, e.g. "pre-partial-split".
.PARAMETER IncludeGenerated
  Also include assets\generated (per-profile test data). Off by default.
.EXAMPLE
  .\Backup-Mod.ps1 pre-partial-split
#>
param(
    [string]$Description = "",
    [switch]$IncludeGenerated
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Zips $SourceRoot into $BackupsDir\<Name>_<date>[_<desc>].zip, preserving the tree rooted at the
# source folder's NAME (so extracting into the parent restores it in place). Skips build output and
# regenerable/noise folders. Kept deliberately verbose - it's a utility you read before you trust it.
function New-WorkspaceBackup {
    param(
        [string]$SourceRoot,
        [string]$BackupsDir,
        [string]$Name,
        [string]$Description,
        [switch]$IncludeGenerated
    )

    $SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
    if (-not (Test-Path $SourceRoot)) { throw "Source not found: $SourceRoot" }
    if (-not (Test-Path $BackupsDir)) { New-Item -ItemType Directory -Path $BackupsDir -Force | Out-Null }

    # Directory names we never back up: build output + regenerable caches + backup noise.
    $excluded = @('bin', 'obj', '.vs', '.git', '.idea', 'dist', 'backups')
    if (-not $IncludeGenerated) { $excluded += 'generated' }

    # Entries are relative to the PARENT of the source, so the top-level folder name is part of every
    # path and extracting the zip into that parent drops the tree back where it came from.
    $baseDir = Split-Path $SourceRoot -Parent

    $files = New-Object System.Collections.Generic.List[string]
    foreach ($f in Get-ChildItem -Path $SourceRoot -Recurse -File -Force) {
        $rel = $f.FullName.Substring($baseDir.Length).TrimStart('\')
        $skip = $false
        foreach ($seg in ($rel -split '\\')) {
            if ($excluded -contains $seg.ToLowerInvariant()) { $skip = $true; break }
        }
        if (-not $skip) { $files.Add($f.FullName) }
    }
    if ($files.Count -eq 0) { throw "Nothing to back up under $SourceRoot" }

    # <Name>_<date>[_<desc>].zip. A time suffix is added only if that name already exists, so two
    # backups on the same day don't clobber each other.
    $date = Get-Date -Format 'yyyy-MM-dd'
    if ([string]::IsNullOrWhiteSpace($Description)) {
        $stem = "{0}_{1}" -f $Name, $date
    } else {
        $clean = ($Description -replace '[^\w\.-]', '-')
        $stem = "{0}_{1}_{2}" -f $Name, $date, $clean
    }
    $zipPath = Join-Path $BackupsDir ($stem + '.zip')
    if (Test-Path $zipPath) {
        $zipPath = Join-Path $BackupsDir ($stem + '_' + (Get-Date -Format 'HH-mm-ss') + '.zip')
    }

    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $entry = $file.Substring($baseDir.Length).TrimStart('\').Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }

    $sizeMB = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host ("[backup] {0} file(s) -> {1} ({2} MB)" -f $files.Count, $zipPath, $sizeMB) -ForegroundColor Cyan
}

$modRoot = $PSScriptRoot
$backupsDir = [System.IO.Path]::GetFullPath((Join-Path $modRoot '..\..\Backups'))
$name = Split-Path $modRoot -Leaf
New-WorkspaceBackup -SourceRoot $modRoot -BackupsDir $backupsDir -Name $name `
    -Description $Description -IncludeGenerated:$IncludeGenerated
