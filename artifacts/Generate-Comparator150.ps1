$ErrorActionPreference = 'Stop'
$outputPath = Join-Path $PSScriptRoot 'Comparator_150_TestPoints.txt'
$header = "PointId`tName`tKind`tTopology`tOutputMode`tLowerThresholdV`tUpperThresholdV`tLowerHysteresisV`tUpperHysteresisV`tVccV`tVeeV`tOutputHighV`tOutputLowV`tOutputBehavior`tOptimize`tRSeries`tRMinOhm`tRMaxOhm"
$rows = [System.Collections.Generic.List[string]]::new()
$pointId = 0

function Add-Point {
    param(
        [string]$Name, [string]$Kind, [string]$Topology, [string]$OutputMode,
        [string]$Lower, [string]$Upper, [string]$LowerH, [string]$UpperH,
        [string]$Vcc, [string]$Vee, [string]$Voh, [string]$Vol,
        [string]$Behavior, [string]$Optimize, [string]$Series, [string]$Rmin, [string]$Rmax
    )
    $script:pointId++
    $fields = @($script:pointId, $Name, $Kind, $Topology, $OutputMode, $Lower, $Upper, $LowerH, $UpperH,
        $Vcc, $Vee, $Voh, $Vol, $Behavior, $Optimize, $Series, $Rmin, $Rmax)
    $script:rows.Add(($fields -join "`t"))
}

$schmittCases = @(
    @('1.5','3.5','5','0','4.9','0.1','1000','1000000'),
    @('0.8','2.2','3.3','0','3.25','0.05','100','100000'),
    @('-1','1','5','-5','4.8','-4.8','1000','1000000'),
    @('-3','4','12','-12','11.5','-11.5','10000','10000000'),
    @('0','5','8','-2','7.8','-1.8','1000','10000000')
)
foreach ($topology in @('Inverting','NonInverting')) {
    foreach ($series in @('E24','E48','E96')) {
        foreach ($optimize in @('0','1')) {
            for ($caseIndex = 0; $caseIndex -lt $schmittCases.Count; $caseIndex++) {
                $c = $schmittCases[$caseIndex]
                $behavior = if ((($pointId + $caseIndex) % 3) -eq 0) { 'OpenCollector' } else { 'PushPull' }
                Add-Point "Schmitt_VALID_${topology}_${series}_OPT${optimize}_$($caseIndex + 1)" 'SchmittTrigger' $topology 'InsideWindowHigh' `
                    $c[0] $c[1] '0' '0' $c[2] $c[3] $c[4] $c[5] $behavior $optimize $series $c[6] $c[7]
            }
        }
    }
}

$invalidSchmitt = @(
    @('ZERO_HYSTERESIS_1V5_VALID','1.5','1.5','5','0','4.9','0.1','1000','1000000'),
    @('REVERSED_THRESHOLDS','3','2','5','0','4.9','0.1','1000','1000000'),
    @('THRESHOLD_BELOW_RAIL','-0.1','2','5','0','4.9','0.1','1000','1000000'),
    @('THRESHOLD_ABOVE_RAIL','1','5.1','5','0','4.9','0.1','1000','1000000'),
    @('SUPPLY_REVERSED','1','2','0','5','4.9','0.1','1000','1000000'),
    @('OUTPUT_EQUAL','1','2','5','0','2','2','1000','1000000'),
    @('OUTPUT_LOW_BELOW_RAIL','1','2','5','0','4.9','-0.1','1000','1000000'),
    @('OUTPUT_HIGH_ABOVE_RAIL','1','2','5','0','5.1','0.1','1000','1000000'),
    @('HYSTERESIS_EXCEEDS_SWING','0.1','4.9','5','0','4.9','0.1','1000','1000000'),
    @('ZERO_RMIN','1','2','5','0','4.9','0.1','0','1000000'),
    @('REVERSED_R_RANGE','1','2','5','0','4.9','0.1','100000','1000'),
    @('NAN_THRESHOLD','NaN','2','5','0','4.9','0.1','1000','1000000'),
    @('INFINITE_THRESHOLD','1','Infinity','5','0','4.9','0.1','1000','1000000'),
    @('EDGE_THRESHOLD_ON_RAIL','0','1','5','0','4.9','0.1','1000','1000000'),
    @('ZERO_HYSTERESIS_2V5_VALID','2.5','2.5','5','0','4.9','0.1','1000','1000000')
)
for ($i = 0; $i -lt $invalidSchmitt.Count; $i++) {
    $c = $invalidSchmitt[$i]
    $classification = if ($c[0] -eq 'EDGE_THRESHOLD_ON_RAIL' -or $c[0].StartsWith('ZERO_HYSTERESIS')) { 'EDGE' } else { 'INVALID' }
    Add-Point "Schmitt_${classification}_$($c[0])" 'SchmittTrigger' $(if (($i % 2) -eq 0) {'Inverting'} else {'NonInverting'}) 'InsideWindowHigh' `
        $c[1] $c[2] '0' '0' $c[3] $c[4] $c[5] $c[6] 'PushPull' '1' 'E24' $c[7] $c[8]
}

$windowCases = @(
    @('1','4','0','0','5','0','4.9','0.1','1000','1000000'),
    @('1.2','3.8','0.1','0.1','5','0','4.9','0.1','1000','1000000'),
    @('0.6','2.6','0.05','0.15','3.3','0','3.2','0.1','100','100000'),
    @('-2','2','0.2','0.4','5','-5','4.8','-4.8','1000','1000000'),
    @('-6','7','0.5','0.25','12','-12','11.5','-11.5','10000','10000000')
)
foreach ($mode in @('InsideWindowHigh','OutsideWindowHigh')) {
    foreach ($series in @('E24','E48','E96')) {
        foreach ($optimize in @('0','1')) {
            for ($caseIndex = 0; $caseIndex -lt $windowCases.Count; $caseIndex++) {
                $c = $windowCases[$caseIndex]
                $behavior = if ((($pointId + $caseIndex) % 4) -eq 0) { 'OpenCollector' } else { 'PushPull' }
                Add-Point "Window_VALID_${mode}_${series}_OPT${optimize}_$($caseIndex + 1)" 'WindowComparator' 'Inverting' $mode `
                    $c[0] $c[1] $c[2] $c[3] $c[4] $c[5] $c[6] $c[7] $behavior $optimize $series $c[8] $c[9]
            }
        }
    }
}

$invalidWindow = @(
    @('EQUAL_THRESHOLDS','2','2','0','0','5','0','4.9','0.1','1000','1000000'),
    @('REVERSED_THRESHOLDS','3','2','0','0','5','0','4.9','0.1','1000','1000000'),
    @('NEGATIVE_LOWER_H','1','4','-0.1','0','5','0','4.9','0.1','1000','1000000'),
    @('NEGATIVE_UPPER_H','1','4','0','-0.1','5','0','4.9','0.1','1000','1000000'),
    @('LOWER_H_EXCEEDS_SWING','1','4','4.8','0','5','0','4.9','0.1','1000','1000000'),
    @('UPPER_H_EXCEEDS_SWING','1','4','0','4.8','5','0','4.9','0.1','1000','1000000'),
    @('HYSTERESIS_CONSUMES_WINDOW','2','3','1','1','5','0','4.9','0.1','1000','1000000'),
    @('THRESHOLD_BELOW_RAIL','-0.1','3','0','0','5','0','4.9','0.1','1000','1000000'),
    @('THRESHOLD_ABOVE_RAIL','1','5.1','0','0','5','0','4.9','0.1','1000','1000000'),
    @('SUPPLY_EQUAL','1','3','0','0','5','5','4.9','0.1','1000','1000000'),
    @('OUTPUT_REVERSED','1','3','0','0','5','0','0.1','4.9','1000','1000000'),
    @('ZERO_RMIN','1','3','0','0','5','0','4.9','0.1','0','1000000'),
    @('REVERSED_R_RANGE','1','3','0','0','5','0','4.9','0.1','100000','1000'),
    @('NAN_HYSTERESIS','1','3','NaN','0','5','0','4.9','0.1','1000','1000000'),
    @('IMPOSSIBLE_NARROW_R_RANGE','1','4','0.2','0.2','5','0','4.9','0.1','1000','1001')
)
for ($i = 0; $i -lt $invalidWindow.Count; $i++) {
    $c = $invalidWindow[$i]
    Add-Point "Window_INVALID_$($c[0])" 'WindowComparator' 'Inverting' $(if (($i % 2) -eq 0) {'InsideWindowHigh'} else {'OutsideWindowHigh'}) `
        $c[1] $c[2] $c[3] $c[4] $c[5] $c[6] $c[7] $c[8] 'PushPull' '1' 'E24' $c[9] $c[10]
}

if ($rows.Count -ne 150) { throw "Expected 150 rows; generated $($rows.Count)." }
[System.IO.File]::WriteAllLines($outputPath, @($header) + $rows, [System.Text.UTF8Encoding]::new($true))
Write-Host "Generated $($rows.Count) comparator test points: $outputPath"
