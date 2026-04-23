## Batch-convert all vanilla HKX files to XML via HKX2E.
##
## Mirrors the folder structure from binaries/ into xml/.
## Converts: behaviors, characters, skeletons (character assets), and project files.
## Skips: animations, NIFs, and other non-behavior HKX files.
##
## Usage:
##   powershell -ExecutionPolicy Bypass -File "build\convert_all_behaviors.ps1"
##   powershell -ExecutionPolicy Bypass -File "build\convert_all_behaviors.ps1" -Force

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$BinRoot = ".reference\vanilla skyrim behavior source\binaries\meshes\actors"
$XmlRoot = ".reference\vanilla skyrim behavior source\xml\meshes\actors"
$Project = "hkbuild\src\HKBuild.csproj"

if (-not (Test-Path $BinRoot)) {
    Write-Error "Binary source not found: $BinRoot"
    exit 1
}

# Find all relevant .hkx files:
#   - behaviors/         → behavior graphs
#   - characters/        → character data files
#   - character assets*/ → skeletons
#   - project files      → *.hkx in creature root dirs (not inside animations/)
$resolvedBin = (Resolve-Path $BinRoot).Path
$hkxFiles = Get-ChildItem $BinRoot -Recurse -Filter "*.hkx" |
    Where-Object {
        $relPath = $_.FullName.Substring($resolvedBin.Length + 1)
        # Skip anything under an animations directory
        if ($relPath -match '\\animations\\|\\animations$') { return $false }
        if ($relPath -match '\\_1stperson\\') { return $false }
        $dirName = $_.Directory.Name
        # Behaviors and characters subdirectories
        if ($dirName -eq "behaviors" -or $dirName -eq "characters") { return $true }
        # Skeleton files in character assets directories
        if ($dirName -match '^character\s*assets') { return $true }
        if ($dirName -eq "characterassets") { return $true }
        # Project files: HKX directly in a creature folder (1 or 2 levels under actors/)
        $parentName = $_.Directory.Parent.Name
        if ($parentName -eq "actors" -or $_.Directory.Parent.Parent.Name -eq "actors") {
            # Must be directly in the creature folder, not in a deeper subdirectory
            if ($dirName -notin @("behaviors","characters") -and $dirName -notmatch "^character\s*assets") {
                return $true
            }
        }
        return $false
    }

Write-Host "Found $($hkxFiles.Count) HKX files to convert"
Write-Host "Source: $BinRoot"
Write-Host "Output: $XmlRoot"
Write-Host ""

$success = 0
$failed  = 0
$skipped = 0

foreach ($hkx in $hkxFiles) {
    # Compute relative path from bin root, change extension to .xml.
    $relPath = $hkx.FullName.Substring((Resolve-Path $BinRoot).Path.Length + 1)
    $xmlRelPath = [System.IO.Path]::ChangeExtension($relPath, ".xml")
    $xmlFullPath = Join-Path $XmlRoot $xmlRelPath

    # Skip if already exists and -Force not set.
    if ((Test-Path $xmlFullPath) -and -not $Force) {
        $skipped++
        continue
    }

    # Ensure output directory exists.
    $xmlDir = Split-Path $xmlFullPath -Parent
    if (-not (Test-Path $xmlDir)) {
        New-Item -ItemType Directory -Path $xmlDir -Force | Out-Null
    }

    Write-Host "  Converting: $relPath" -NoNewline
    try {
        $output = & dotnet run --project $Project -c Release -- convert $hkx.FullName -o $xmlFullPath 2>&1
        if ($LASTEXITCODE -eq 0) {
            $size = (Get-Item $xmlFullPath).Length
            Write-Host " -> $([math]::Round($size/1024))KB" -ForegroundColor Green
            $success++
        } else {
            Write-Host " FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
            $output | Where-Object { $_ -match "ERROR" } | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            $failed++
        }
    } catch {
        Write-Host " ERROR: $_" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Done: $success converted, $skipped skipped, $failed failed (of $($hkxFiles.Count) total)"
