param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('1366x768-125', '1920x1080-150')]
    [string]$Case,

    [string]$OutputRoot = 'TestResults/UI-Spike-Device',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

if (-not $SkipBuild) {
    dotnet restore NodeKit.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE" }

    dotnet build NodeKit.csproj --no-restore --configuration Release /p:TreatWarningsAsErrors=true /p:EnforceCodeStyleInBuild=true
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
}

$caseDir = Join-Path $OutputRoot $Case
New-Item -ItemType Directory -Force -Path $caseDir | Out-Null

function Invoke-SpikeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height,
        [string]$FocusTarget,
        [switch]$Unclamped
    )

    $env:NODEKIT_UI_SPIKE = 'v16'
    $env:NODEKIT_UI_CAPTURE = Join-Path $caseDir "$Name.png"
    $env:NODEKIT_UI_CAPTURE_WIDTH = "$Width"
    $env:NODEKIT_UI_CAPTURE_HEIGHT = "$Height"

    if ([string]::IsNullOrWhiteSpace($FocusTarget)) {
        Remove-Item Env:NODEKIT_UI_CAPTURE_FOCUS -ErrorAction SilentlyContinue
    }
    else {
        $env:NODEKIT_UI_CAPTURE_FOCUS = $FocusTarget
    }

    if ($Unclamped) {
        $env:NODEKIT_UI_CAPTURE_MIN_WIDTH = '0'
        $env:NODEKIT_UI_CAPTURE_MIN_HEIGHT = '0'
    }
    else {
        Remove-Item Env:NODEKIT_UI_CAPTURE_MIN_WIDTH -ErrorAction SilentlyContinue
        Remove-Item Env:NODEKIT_UI_CAPTURE_MIN_HEIGHT -ErrorAction SilentlyContinue
    }

    dotnet run --project NodeKit.csproj --no-build --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Capture $Name failed with exit code $LASTEXITCODE"
    }
}

# The target device must already be configured to the Windows display scaling named by -Case.
# Every capture JSON records Avalonia's observed Screen Bounds, WorkingArea, and Scaling;
# do not accept the case based on the label alone.
Invoke-SpikeCapture -Name 'startup_980x600' -Width 980 -Height 600
Invoke-SpikeCapture -Name 'candidate_980x560' -Width 980 -Height 560 -Unclamped
Invoke-SpikeCapture -Name 'focus_center_980x600' -Width 980 -Height 600 -FocusTarget 'center-input'
Invoke-SpikeCapture -Name 'focus_activity_980x600' -Width 980 -Height 600 -FocusTarget 'activity'

$readme = @"
Avalonia v16 real-device capture case: $Case

Before accepting this evidence, verify the JSON siblings rather than trusting this folder name:
- screenBoundsPixelWidth / screenBoundsPixelHeight
- workingAreaPixelWidth / workingAreaPixelHeight
- screenScaling
- clientWidth / clientHeight

Required operator checks:
1. Confirm Windows display resolution and scaling correspond to the requested case.
2. Inspect startup_980x600.png for clipping and Center/Supporting balance.
3. Inspect candidate_980x560.png as a capture-only minimum-height candidate.
4. Inspect focus_center_980x600.png for a visible keyboard focus treatment.
5. Inspect focus_activity_980x600.png for a visible keyboard focus treatment.
6. Manually scroll Center to section 04 and confirm it remains reachable and usable.

This helper does not change normal NodeKit startup minimums and does not promote any numeric value into a product Rule Set.
"@

$readme | Set-Content -Path (Join-Path $caseDir 'README.txt') -Encoding utf8

Write-Host "Capture complete: $caseDir"
