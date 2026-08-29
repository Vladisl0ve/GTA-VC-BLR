[CmdletBinding()]
param(
    [string] $InputPath,
    [string] $OutputPath,
    [switch] $Verify
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InputPath)) {
    $InputPath = Join-Path $workspaceRoot "Assets\ViceCity\belarusian.fontmetrics.json"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $workspaceRoot "native\generated\BelarusianFontMetrics.generated.h"
}

$metricCount = 210
$minimumCode = 0x20
$maximumCode = $minimumCode + $metricCount - 1
$knownContexts = @(
    "Default",
    "Gameplay",
    "Subtitles",
    "MainMenu",
    "SaveLoad",
    "ExitConfirmation",
    "Heading"
)
$knownFonts = @("Font2", "Font1")

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string[]] $Expected,
        [Parameter(Mandatory = $true)] [string] $Location
    )

    if ($null -eq $Object) {
        throw "$Location is missing."
    }

    $actual = @($Object.PSObject.Properties.Name)
    $unexpected = @($actual | Where-Object { $Expected -cnotcontains $_ })
    $missing = @($Expected | Where-Object { $actual -cnotcontains $_ })
    if ($unexpected.Count -ne 0 -or $missing.Count -ne 0) {
        throw "$Location must contain exactly: $($Expected -join ', '). Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
    }
}

function Convert-ToUInt16 {
    param(
        [Parameter(Mandatory = $true)] $Value,
        [Parameter(Mandatory = $true)] [string] $Location
    )

    if ($Value -is [bool] -or $Value -is [string]) {
        throw "$Location must be an integer in the uint16 range."
    }

    try {
        $number = [decimal]$Value
    }
    catch {
        throw "$Location must be an integer in the uint16 range."
    }

    if ($number -ne [decimal]::Truncate($number) -or $number -lt 0 -or $number -gt [uint16]::MaxValue) {
        throw "$Location must be an integer in the uint16 range."
    }

    return [uint16]$number
}

function Read-MetricRow {
    param(
        [Parameter(Mandatory = $true)] $Table,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    Assert-ExactProperties $Table @("advances") $Name
    $values = @($Table.advances)
    if ($values.Count -ne $metricCount) {
        throw "$Name.advances must contain exactly $metricCount values; found $($values.Count)."
    }

    $row = New-Object 'System.UInt16[]' $metricCount
    for ($index = 0; $index -lt $metricCount; $index++) {
        $row[$index] = Convert-ToUInt16 $values[$index] "$Name.advances[$index]"
    }
    return ,$row
}

function Get-ContextIdentifier {
    param([Parameter(Mandatory = $true)] [string] $Context)
    return "BELARUSIAN_FONT_CONTEXT_$($Context.ToUpperInvariant())"
}

function Get-FontIdentifier {
    param([Parameter(Mandatory = $true)] [string] $Font)
    return "BELARUSIAN_FONT_ROW_$($Font.ToUpperInvariant())"
}

function Append-MetricRow {
    param(
        [Parameter(Mandatory = $true)] [Text.StringBuilder] $Builder,
        [Parameter(Mandatory = $true)] [uint16[]] $Values,
        [Parameter(Mandatory = $true)] [string] $Comment
    )

    [void]$Builder.AppendLine("    { /* $Comment */")
    for ($offset = 0; $offset -lt $Values.Length; $offset += 14) {
        $last = [Math]::Min($offset + 13, $Values.Length - 1)
        $lineValues = for ($index = $offset; $index -le $last; $index++) {
            "$($Values[$index])u"
        }
        $suffix = if ($last -lt $Values.Length - 1) { "," } else { "" }
        [void]$Builder.AppendLine("        $($lineValues -join ', ')$suffix")
    }
    [void]$Builder.Append("    }")
}

if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
    throw "Font metrics input does not exist: $InputPath"
}

try {
    $profile = Get-Content -LiteralPath $InputPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    throw "Font metrics input is not valid JSON: $($_.Exception.Message)"
}

Assert-ExactProperties $profile @("version", "font2", "font1", "overrides") "root"
$version = Convert-ToUInt16 $profile.version "version"
if ($version -ne 1) {
    throw "Unsupported font metrics version $version; expected 1."
}

# Physical game order is deliberately fixed: Size[0]/font2, then Size[1]/font1.
$font2 = Read-MetricRow $profile.font2 "font2"
$font1 = Read-MetricRow $profile.font1 "font1"

$overrides = @()
$overrideKeys = @{}
$overrideIndex = 0
foreach ($item in @($profile.overrides)) {
    Assert-ExactProperties $item @("context", "font", "code", "advance") "overrides[$overrideIndex]"

    $context = [string]$item.context
    if ($knownContexts -cnotcontains $context) {
        throw "overrides[$overrideIndex].context '$context' is unsupported."
    }

    $font = [string]$item.font
    if ($knownFonts -cnotcontains $font) {
        throw "overrides[$overrideIndex].font '$font' is unsupported."
    }

    $codeText = [string]$item.code
    if ($codeText -cnotmatch '^0x[0-9A-Fa-f]{2}$') {
        throw "overrides[$overrideIndex].code must use the exact 0xNN byte format."
    }
    $code = [Convert]::ToByte($codeText.Substring(2), 16)
    if ($code -lt $minimumCode -or $code -gt $maximumCode) {
        throw "overrides[$overrideIndex].code $codeText is outside the metric range 0x20-0xF1."
    }

    $advance = Convert-ToUInt16 $item.advance "overrides[$overrideIndex].advance"
    $key = "$context|$font|$code"
    if ($overrideKeys.ContainsKey($key)) {
        throw "Duplicate override for $context, $font, $codeText."
    }
    $overrideKeys[$key] = $true

    $overrides += [pscustomobject]@{
        Context = $context
        Font = $font
        Code = $code
        Advance = $advance
    }
    $overrideIndex++
}

$tableBytes = New-Object 'System.Byte[]' (2 * $metricCount * 2)
$byteOffset = 0
foreach ($value in @($font2) + @($font1)) {
    $tableBytes[$byteOffset] = [byte]($value -band 0xFF)
    $tableBytes[$byteOffset + 1] = [byte](($value -shr 8) -band 0xFF)
    $byteOffset += 2
}
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $tableHash = ([BitConverter]::ToString($sha256.ComputeHash($tableBytes))).Replace("-", "")
}
finally {
    $sha256.Dispose()
}

$builder = New-Object Text.StringBuilder
[void]$builder.AppendLine("/* AUTO-GENERATED by scripts/generate-belarusian-font-metrics.ps1.")
[void]$builder.AppendLine(" * Source: Assets/ViceCity/belarusian.fontmetrics.json")
[void]$builder.AppendLine(" * DO NOT EDIT. Run the generator after changing the JSON source of truth.")
[void]$builder.AppendLine(" */")
[void]$builder.AppendLine("#ifndef GTA_GXT_BELARUSIAN_FONT_METRICS_GENERATED_H")
[void]$builder.AppendLine("#define GTA_GXT_BELARUSIAN_FONT_METRICS_GENERATED_H")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("#include <stdint.h>")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("#define BELARUSIAN_FONT_METRIC_ROW_COUNT 2u")
[void]$builder.AppendLine("#define BELARUSIAN_FONT_METRIC_COUNT 210u")
[void]$builder.AppendLine("#define BELARUSIAN_FONT_METRIC_TABLE_SHA256 `"$tableHash`"")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("typedef enum BelarusianFontMetricRow {")
[void]$builder.AppendLine("    BELARUSIAN_FONT_ROW_FONT2 = 0, /* Vice City Size[0], TXD font2 */")
[void]$builder.AppendLine("    BELARUSIAN_FONT_ROW_FONT1 = 1  /* Vice City Size[1], TXD font1 */")
[void]$builder.AppendLine("} BelarusianFontMetricRow;")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("typedef enum BelarusianFontRenderContext {")
for ($index = 0; $index -lt $knownContexts.Count; $index++) {
    $suffix = if ($index -lt $knownContexts.Count - 1) { "," } else { "" }
    [void]$builder.AppendLine("    $(Get-ContextIdentifier $knownContexts[$index]) = $index$suffix")
}
[void]$builder.AppendLine("} BelarusianFontRenderContext;")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("typedef struct BelarusianFontMetricOverride {")
[void]$builder.AppendLine("    uint8_t context;")
[void]$builder.AppendLine("    uint8_t font_row;")
[void]$builder.AppendLine("    uint8_t code;")
[void]$builder.AppendLine("    uint16_t advance;")
[void]$builder.AppendLine("} BelarusianFontMetricOverride;")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("static const uint16_t kBelarusianFontMetrics[BELARUSIAN_FONT_METRIC_ROW_COUNT][BELARUSIAN_FONT_METRIC_COUNT] = {")
Append-MetricRow $builder $font2 "physical row 0: font2"
[void]$builder.AppendLine(",")
Append-MetricRow $builder $font1 "physical row 1: font1"
[void]$builder.AppendLine("")
[void]$builder.AppendLine("};")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("#define BELARUSIAN_FONT_METRIC_OVERRIDE_COUNT $($overrides.Count)u")
[void]$builder.AppendLine("static const BelarusianFontMetricOverride kBelarusianFontMetricOverrides[BELARUSIAN_FONT_METRIC_OVERRIDE_COUNT] = {")
for ($index = 0; $index -lt $overrides.Count; $index++) {
    $item = $overrides[$index]
    $suffix = if ($index -lt $overrides.Count - 1) { "," } else { "" }
    [void]$builder.AppendLine(("    {{ {0}, {1}, 0x{2:X2}u, {3}u }}{4}" -f `
        (Get-ContextIdentifier $item.Context),
        (Get-FontIdentifier $item.Font),
        $item.Code,
        $item.Advance,
        $suffix))
}
[void]$builder.AppendLine("};")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("#endif /* GTA_GXT_BELARUSIAN_FONT_METRICS_GENERATED_H */")

$expected = $builder.ToString().Replace("`r`n", "`n")
if ($Verify) {
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Generated font metrics header is missing: $OutputPath"
    }

    $actual = [IO.File]::ReadAllText($OutputPath).Replace("`r`n", "`n")
    if ($actual -cne $expected) {
        throw "Generated font metrics header is stale. Run scripts/generate-belarusian-font-metrics.ps1 and commit the result."
    }

    Write-Host "Verified generated font metrics header ($tableHash)."
    return
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $outputDirectory -Force)
}
[IO.File]::WriteAllText($OutputPath, $expected, (New-Object Text.UTF8Encoding($false)))
Write-Host "Generated $OutputPath ($tableHash)."
