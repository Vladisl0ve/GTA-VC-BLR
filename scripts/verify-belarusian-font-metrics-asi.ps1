[CmdletBinding()]
param(
    [string] $AsiPath,
    [string] $MetricsPath
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($MetricsPath)) {
    $MetricsPath = Join-Path $workspaceRoot "Assets\ViceCity\belarusian.fontmetrics.json"
}
if ([string]::IsNullOrWhiteSpace($AsiPath)) {
    $AsiPath = $env:GTA_VC_BLR_CANONICAL_ASI
}

if ([string]::IsNullOrWhiteSpace($AsiPath)) {
    Write-Host "Canonical ASI verification skipped (set GTA_VC_BLR_CANONICAL_ASI or pass -AsiPath)."
    return
}

$expectedFileLength = 26112
$expectedFileHash = "17D98CC31D63067EB9040A44453D2EB09C983ED19C2AFAB276A227B2FBA523A7"
$tableOffset = 0x485E
$rowCount = 2
$metricCount = 210
$tableLength = $rowCount * $metricCount * 2
$expectedTableHash = "50E8D5CDC3C7904875FC014CB343BDEEC0D8C9CF57C234F4B5AB53B60DABC1EA"

if (-not (Test-Path -LiteralPath $AsiPath -PathType Leaf)) {
    throw "Canonical ASI does not exist: $AsiPath"
}
if (-not (Test-Path -LiteralPath $MetricsPath -PathType Leaf)) {
    throw "Font metrics JSON does not exist: $MetricsPath"
}

$binary = [IO.File]::ReadAllBytes($AsiPath)
if ($binary.Length -ne $expectedFileLength) {
    throw "Canonical ASI size mismatch: expected $expectedFileLength bytes, found $($binary.Length)."
}

$fileHash = (Get-FileHash -LiteralPath $AsiPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($fileHash -cne $expectedFileHash) {
    throw "Canonical ASI SHA-256 mismatch: expected $expectedFileHash, found $fileHash."
}

if ($tableOffset + $tableLength -gt $binary.Length) {
    throw "Canonical table range 0x{0:X}-0x{1:X} is outside the ASI." -f $tableOffset, ($tableOffset + $tableLength - 1)
}

$tableSlice = New-Object 'System.Byte[]' $tableLength
[Array]::Copy($binary, $tableOffset, $tableSlice, 0, $tableLength)
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $tableHash = ([BitConverter]::ToString($sha256.ComputeHash($tableSlice))).Replace("-", "")
}
finally {
    $sha256.Dispose()
}
if ($tableHash -cne $expectedTableHash) {
    throw "Canonical ASI table SHA-256 mismatch at offset 0x{0:X}: expected {1}, found {2}." -f $tableOffset, $expectedTableHash, $tableHash
}

try {
    $profile = Get-Content -LiteralPath $MetricsPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    throw "Font metrics JSON cannot be read: $($_.Exception.Message)"
}

$font2 = @($profile.font2.advances)
$font1 = @($profile.font1.advances)
if ($font2.Count -ne $metricCount -or $font1.Count -ne $metricCount) {
    throw "Font metrics JSON must contain exactly two rows of $metricCount advances."
}

$expectedBytes = New-Object 'System.Byte[]' $tableLength
$offset = 0
foreach ($item in $font2 + $font1) {
    try {
        $value = [uint16]$item
    }
    catch {
        throw "Font metrics JSON contains a value outside the uint16 range."
    }
    $expectedBytes[$offset] = [byte]($value -band 0xFF)
    $expectedBytes[$offset + 1] = [byte](($value -shr 8) -band 0xFF)
    $offset += 2
}

for ($index = 0; $index -lt $tableLength; $index++) {
    if ($tableSlice[$index] -ne $expectedBytes[$index]) {
        $row = [Math]::Floor($index / ($metricCount * 2))
        $entry = [Math]::Floor(($index % ($metricCount * 2)) / 2)
        throw "Font metrics JSON differs from canonical ASI physical row $row, metric index $entry."
    }
}

# Prove that the JSON payload occurs exactly once and begins at the recorded raw offset.
$matches = @()
for ($candidate = 0; $candidate -le $binary.Length - $tableLength; $candidate++) {
    if ($binary[$candidate] -ne $expectedBytes[0]) {
        continue
    }
    $isMatch = $true
    for ($index = 1; $index -lt $tableLength; $index++) {
        if ($binary[$candidate + $index] -ne $expectedBytes[$index]) {
            $isMatch = $false
            break
        }
    }
    if ($isMatch) {
        $matches += $candidate
    }
}
if ($matches.Count -ne 1 -or $matches[0] -ne $tableOffset) {
    $matchText = if ($matches.Count -eq 0) { "none" } else { ($matches | ForEach-Object { "0x{0:X}" -f $_ }) -join ", " }
    throw "Canonical metric payload offset mismatch: expected only 0x{0:X}; found {1}." -f $tableOffset, $matchText
}

Write-Host ("Verified canonical ASI {0}: file SHA-256, 840-byte payload, JSON rows, and unique offset 0x{1:X}." -f $fileHash, $tableOffset)
