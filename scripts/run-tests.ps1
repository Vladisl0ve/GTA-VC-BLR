[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [string] $Filter
)

$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $workspaceRoot "tests\GTA GXT Editor.Tests\GTA GXT Editor.Tests.csproj"
$fontMetricsGenerator = Join-Path $PSScriptRoot "generate-belarusian-font-metrics.ps1"
$canonicalAsiVerifier = Join-Path $PSScriptRoot "verify-belarusian-font-metrics-asi.ps1"

# The JSON asset is the source of truth for native metrics. Fail before compiling
# tests if the committed native header drifted. Canonical ASI verification remains
# optional because the binary is intentionally not a repository dependency.
& $fontMetricsGenerator -Verify
& $canonicalAsiVerifier

function Get-TerminalWidth {
    try {
        $width = $Host.UI.RawUI.WindowSize.Width
    }
    catch {
        $width = 120
    }

    return [Math]::Min(160, [Math]::Max(70, $width))
}

function Fit-Cell {
    param(
        [AllowEmptyString()]
        [string] $Text,
        [int] $Width,
        [switch] $AlignRight
    )

    if ($null -eq $Text) {
        $Text = ""
    }

    if ($Text.Length -gt $Width) {
        $Text = $Text.Substring(0, $Width - 1) + [char]0x2026
    }

    if ($AlignRight) {
        return $Text.PadLeft($Width)
    }

    return $Text.PadRight($Width)
}

$box = @{
    Horizontal   = [string][char]0x2500
    Vertical     = [string][char]0x2502
    TopLeft      = [string][char]0x256D
    TopJoin      = [string][char]0x252C
    TopRight     = [string][char]0x256E
    MiddleLeft   = [string][char]0x251C
    MiddleJoin   = [string][char]0x253C
    MiddleRight  = [string][char]0x2524
    BottomLeft   = [string][char]0x2570
    BottomJoin   = [string][char]0x2534
    BottomRight  = [string][char]0x256F
}

function Write-TableBorder {
    param(
        [string] $Left,
        [string] $Join,
        [string] $Right,
        [int[]] $Widths
    )

    $segments = foreach ($width in $Widths) {
        $box.Horizontal * ($width + 2)
    }

    Write-Host ($Left + ($segments -join $Join) + $Right) -ForegroundColor DarkGray
}

function Write-TestRow {
    param(
        [string] $Status,
        [string] $Name,
        [string] $Duration,
        [ConsoleColor] $StatusColor,
        [int] $StatusWidth,
        [int] $NameWidth,
        [int] $DurationWidth
    )

    Write-Host ($box.Vertical + " ") -NoNewline -ForegroundColor DarkGray
    Write-Host (Fit-Cell $Status $StatusWidth) -NoNewline -ForegroundColor $StatusColor
    Write-Host (" " + $box.Vertical + " ") -NoNewline -ForegroundColor DarkGray
    Write-Host (Fit-Cell $Name $NameWidth) -NoNewline
    Write-Host (" " + $box.Vertical + " ") -NoNewline -ForegroundColor DarkGray
    Write-Host (Fit-Cell $Duration $DurationWidth -AlignRight) -NoNewline
    Write-Host (" " + $box.Vertical) -ForegroundColor DarkGray
}

$dotnetArguments = @(
    "run",
    "--project", $testProject,
    "--configuration", $Configuration,
    "--",
    "--output", "Detailed",
    "--progress", "off",
    "--ansi", "off",
    "--show-stdout", "Failed",
    "--show-stderr", "Failed",
    "--no-banner"
)

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $dotnetArguments += @("--filter", $Filter)
}

$previousCliLanguage = $env:DOTNET_CLI_UI_LANGUAGE
$previousErrorActionPreference = $ErrorActionPreference
try {
    # Stable English status words make the parser independent of the OS language.
    $env:DOTNET_CLI_UI_LANGUAGE = "en-US"
    # Windows PowerShell wraps native stderr in non-terminating ErrorRecord objects.
    $ErrorActionPreference = "Continue"
    $runnerOutput = @(& dotnet @dotnetArguments 2>&1 | ForEach-Object { $_.ToString() })
    $runnerExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
    if ($null -eq $previousCliLanguage) {
        Remove-Item Env:DOTNET_CLI_UI_LANGUAGE -ErrorAction SilentlyContinue
    }
    else {
        $env:DOTNET_CLI_UI_LANGUAGE = $previousCliLanguage
    }
}

$resultPattern = "^(?<outcome>passed|failed|skipped)\s+(?<name>.+)\s+\((?<duration>(?:<\s*)?\d+(?:[.,]\d+)?(?:ms|s|m|h)(?:\s+\d+(?:[.,]\d+)?(?:ms|s|m|h))*)\)$"
$testResults = @()
$failureDetails = @()
$activeFailure = $null

foreach ($line in $runnerOutput) {
    $match = [regex]::Match($line.TrimEnd(), $resultPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if ($match.Success) {
        if ($null -ne $activeFailure) {
            $failureDetails += $activeFailure
            $activeFailure = $null
        }

        $outcome = $match.Groups["outcome"].Value.ToLowerInvariant()
        $result = [pscustomobject]@{
            Outcome  = $outcome
            Name     = $match.Groups["name"].Value
            Duration = $match.Groups["duration"].Value
        }
        $testResults += $result

        if ($outcome -eq "failed") {
            $activeFailure = [pscustomobject]@{
                Name  = $result.Name
                Lines = @()
            }
        }

        continue
    }

    if ($null -ne $activeFailure) {
        if ($line -match "^Test run summary:") {
            $failureDetails += $activeFailure
            $activeFailure = $null
        }
        else {
            $activeFailure.Lines += $line
        }
    }
}

if ($null -ne $activeFailure) {
    $failureDetails += $activeFailure
}

if ($testResults.Count -eq 0) {
    $runnerOutput | ForEach-Object { Write-Host $_ }
    Write-Error "The test runner did not produce any parseable test results."
    if ($runnerExitCode -ne 0) {
        exit $runnerExitCode
    }
    exit 1
}

$terminalWidth = Get-TerminalWidth
$statusWidth = 6
$durationWidth = 10
$nameWidth = $terminalWidth - $statusWidth - $durationWidth - 10
$columnWidths = @($statusWidth, $nameWidth, $durationWidth)

Write-Host ""
Write-Host " TEST RESULTS " -ForegroundColor Cyan
Write-TableBorder $box.TopLeft $box.TopJoin $box.TopRight $columnWidths
Write-TestRow "STATUS" "TEST" "TIME" Cyan $statusWidth $nameWidth $durationWidth
Write-TableBorder $box.MiddleLeft $box.MiddleJoin $box.MiddleRight $columnWidths

foreach ($result in $testResults) {
    switch ($result.Outcome) {
        "passed"  { Write-TestRow "PASS" $result.Name $result.Duration Green $statusWidth $nameWidth $durationWidth }
        "failed"  { Write-TestRow "FAIL" $result.Name $result.Duration Red $statusWidth $nameWidth $durationWidth }
        "skipped" { Write-TestRow "SKIP" $result.Name $result.Duration Yellow $statusWidth $nameWidth $durationWidth }
    }
}

Write-TableBorder $box.BottomLeft $box.BottomJoin $box.BottomRight $columnWidths

if ($failureDetails.Count -gt 0) {
    Write-Host ""
    Write-Host " FAILURE DETAILS " -ForegroundColor Red

    foreach ($failure in $failureDetails) {
        Write-Host ("- " + $failure.Name) -ForegroundColor Red
        foreach ($detailLine in $failure.Lines) {
            Write-Host $detailLine
        }
    }
}

$passedCount = @($testResults | Where-Object Outcome -eq "passed").Count
$failedCount = @($testResults | Where-Object Outcome -eq "failed").Count
$skippedCount = @($testResults | Where-Object Outcome -eq "skipped").Count
$summaryDuration = "-"

foreach ($line in $runnerOutput) {
    if ($line -match "^\s*duration:\s*(?<duration>.+)\s*$") {
        $summaryDuration = $Matches["duration"]
    }
}

$summary = "Total: {0}  Passed: {1}  Failed: {2}  Skipped: {3}  Time: {4}" -f `
    $testResults.Count, $passedCount, $failedCount, $skippedCount, $summaryDuration
$summaryWidth = $terminalWidth - 4
$summaryColor = if ($failedCount -eq 0 -and $runnerExitCode -eq 0) { "Green" } else { "Red" }

Write-Host ""
Write-Host ($box.TopLeft + ($box.Horizontal * ($summaryWidth + 2)) + $box.TopRight) -ForegroundColor DarkGray
Write-Host ($box.Vertical + " ") -NoNewline -ForegroundColor DarkGray
Write-Host (Fit-Cell $summary $summaryWidth) -NoNewline -ForegroundColor $summaryColor
Write-Host (" " + $box.Vertical) -ForegroundColor DarkGray
Write-Host ($box.BottomLeft + ($box.Horizontal * ($summaryWidth + 2)) + $box.BottomRight) -ForegroundColor DarkGray

exit $runnerExitCode
