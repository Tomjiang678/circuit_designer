param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'PrecisionAnalogComputing_500_TestPoints.txt')
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$header = "PointId`tName`tOperation`tInputs`tOutputOffsetV`tVinLowV`tVinHighV`tVoutLowV`tVoutHighV`tGainK`tHalfWavePolarity`tOutputPolarity`tVinMinV`tVinMaxV`tMaxFrequencyHz`tReferenceVoltageV`tLogScaleV`tKiPerSecond`tKdSecond`tLowerCornerHz`tUpperCornerHz`tVccV`tVeeV`tMaxErrorPercent`tRSeries`tRMinOhm`tRMaxOhm`tCSeries`tCMinF`tCMaxF`tOptimize`tBiasCompensation`tIdealOpAmp`tGBWHz`tSlewVPerSecond`tJunctionModel`tSaturationCurrentA`tEmissionCoefficient`tTemperatureC`tMaxJunctionCurrentA"
$operations = @(
    'GeneralLinearCombination', 'VoltageRangeMapper',
    'PrecisionHalfWaveRectifier', 'PrecisionFullWaveRectifier',
    'LogarithmicAmplifier', 'ExponentialAmplifier',
    'PracticalIntegrator', 'PracticalDifferentiator'
)
$series = @('E12', 'E24', 'E48', 'E96')

function N([double]$value) { $value.ToString('G17', $culture) }

$rows = [Collections.Generic.List[string]]::new(500)
for ($point = 1; $point -le 500; $point++) {
    $operationIndex = ($point - 1) % $operations.Count
    $case = [math]::Floor(($point - 1) / $operations.Count)
    $operation = $operations[$operationIndex]
    $familyCase = [int]$case + 1
    $stress = $case % 10
    $rSeries = $series[$case % $series.Count]
    $cSeries = $series[($case + 1) % $series.Count]
    $optimize = if (($case % 7) -eq 0) { '0' } else { '1' }
    $bias = if (($case % 5) -eq 0) { '0' } else { '1' }
    $ideal = if (($case % 4) -eq 0) { '0' } else { '1' }
    $gbw = @(100000, 1000000, 10000000, 100000000)[$case % 4]
    $slew = @(10000, 100000, 1000000, 10000000)[$case % 4]
    $rail = @(3.3, 5, 12, 15)[$case % 4]
    $errorPercent = @(0.1, 0.5, 1, 2, 5)[$case % 5]
    $rMin = @(100, 1000, 10000)[$case % 3]
    $rMax = @(100000, 1000000, 10000000)[$case % 3]
    $cMin = @(1e-12, 10e-12, 100e-12)[$case % 3]
    $cMax = @(1e-6, 10e-6, 100e-6)[$case % 3]
    $vinMagnitude = @(0.1, 0.5, 1, 2, 5, 10)[$case % 6]
    $frequency = @(1, 10, 100, 1000, 10000, 100000)[$case % 6]
    $gain = @(0.1, 0.25, 0.5, 1, 2, 5, 10)[$case % 7]

    $inputs = 'U1:1:-1:1:0'
    $offset = 0.0
    $vinLow = 0.0; $vinHigh = 5.0; $voutLow = 0.0; $voutHigh = 3.3
    $halfPolarity = if (($case % 2) -eq 0) { 'Positive' } else { 'Negative' }
    $outputPolarity = if (($case % 2) -eq 0) { 'Positive' } else { 'Negative' }
    $vinMin = -$vinMagnitude; $vinMax = $vinMagnitude
    $vref = @(0.1, 0.25, 0.5, 1, 2)[$case % 5]
    $logScale = @(0.025, 0.05, 0.1, 0.2, 0.5)[$case % 5]
    $ki = @(1, 10, 100, 1000, 10000)[$case % 5]
    $kd = @(1e-6, 10e-6, 100e-6, 1e-3, 10e-3)[$case % 5]
    $lower = @(0.1, 1, 10, 100, 1000)[$case % 5]
    $upper = $lower * @(20, 50, 100, 500)[$case % 4]
    $junctionModel = 'ShockleyJunction'
    $is = @(1e-15, 1e-14, 1e-13, 1e-12, 1e-11)[$case % 5]
    $emission = @(1, 1.1, 1.5, 2)[$case % 4]
    $temperature = @(-40, 0, 25, 85, 125)[$case % 5]
    $junctionLimit = @(1e-5, 1e-4, 1e-3, 1e-2)[$case % 4]
    $profile = 'Nominal'

    switch ($operation) {
        'GeneralLinearCombination' {
            $termCount = 1 + ($case % 4)
            $terms = for ($term = 1; $term -le $termCount; $term++) {
                $coefficient = ((($case + $term) % 9) - 4) * 0.25
                if ($coefficient -eq 0) { $coefficient = 0.5 }
                "U$term`:$((N $coefficient))`:$((N (-$vinMagnitude)))`:$((N $vinMagnitude))`:0"
            }
            $inputs = $terms -join '|'
            $offset = (($case % 7) - 3) * 0.25
            if ($stress -eq 8) { $errorPercent = 0.01; $profile = 'AccuracyStress' }
            if ($stress -eq 9) { $rMin = 900000; $rMax = 1000000; $profile = 'NarrowRRange' }
        }
        'VoltageRangeMapper' {
            $vinLow = @(-10, -5, -1, 0, 0.5)[$case % 5]
            $vinHigh = $vinLow + @(0.1, 1, 2, 5, 10)[$case % 5]
            $voutLow = @(-5, -1, 0, 1)[$case % 4]
            $voutHigh = $voutLow + @(0.1, 1, 3.3, 5)[$case % 4]
            if ($case % 2) { $swap = $voutLow; $voutLow = $voutHigh; $voutHigh = $swap }
            if ($stress -eq 8) { $vinHigh = $vinLow; $profile = 'InvalidSameInputEndpoints' }
            if ($stress -eq 9) { $voutHigh = $voutLow; $profile = 'InvalidSameOutputEndpoints' }
        }
        'PrecisionHalfWaveRectifier' {
            if ($stress -eq 8) { $gain = 50; $rail = 3.3; $profile = 'OutputSwingStress' }
            if ($stress -eq 9) { $gbw = 100; $slew = 100; $ideal = '0'; $profile = 'OpAmpBandwidthStress' }
        }
        'PrecisionFullWaveRectifier' {
            if ($stress -eq 8) { $gain = 100; $rail = 5; $profile = 'HighGainStress' }
            if ($stress -eq 9) { $frequency = 1000000; $gbw = 10000; $slew = 1000; $ideal = '0'; $profile = 'DynamicStress' }
        }
        'LogarithmicAmplifier' {
            $vinMin = @(1e-6, 1e-4, 1e-3, 0.01, 0.1)[$case % 5]
            $vinMax = @(0.1, 0.5, 1, 2, 5)[$case % 5]
            if ($vinMax -le $vinMin) { $vinMax = $vinMin * 100 }
            if ($stress -eq 8) { $vinMin = 0; $profile = 'InvalidNonPositiveInput' }
            if ($stress -eq 9) { $junctionLimit = 1e-9; $profile = 'JunctionCurrentStress' }
        }
        'ExponentialAmplifier' {
            $vinMin = -4 * $logScale; $vinMax = 4 * $logScale
            if ($stress -eq 8) { $vinMin = -81 * $logScale; $profile = 'InvalidExponentRange' }
            if ($stress -eq 9) { $junctionLimit = 1e-9; $profile = 'JunctionCurrentStress' }
        }
        'PracticalIntegrator' {
            if ($stress -eq 8) { $upper = $lower; $profile = 'InvalidBandEdges' }
            if ($stress -eq 9) { $cMin = 1e-6; $cMax = 1e-6; $rMin = 100; $rMax = 100; $profile = 'ComponentRangeStress' }
        }
        'PracticalDifferentiator' {
            if ($stress -eq 8) { $upper = 0; $profile = 'InvalidUsefulFrequency' }
            if ($stress -eq 9) { $gbw = 100; $slew = 100; $ideal = '0'; $profile = 'OpAmpDynamicStress' }
        }
    }

    $name = ('{0}_{1:D3}_{2}' -f $operation, $familyCase, $profile)
    $fields = @(
        $point, $name, $operation, $inputs,
        (N $offset), (N $vinLow), (N $vinHigh), (N $voutLow), (N $voutHigh), (N $gain),
        $halfPolarity, $outputPolarity, (N $vinMin), (N $vinMax), (N $frequency),
        (N $vref), (N $logScale), (N $ki), (N $kd), (N $lower), (N $upper),
        (N $rail), (N (-$rail)), (N $errorPercent), $rSeries, (N $rMin), (N $rMax),
        $cSeries, (N $cMin), (N $cMax), $optimize, $bias, $ideal, (N $gbw), (N $slew),
        $junctionModel, (N $is), (N $emission), (N $temperature), (N $junctionLimit)
    )
    if ($fields.Count -ne 40) { throw "Point $point produced $($fields.Count) fields instead of 40." }
    $rows.Add($fields -join "`t")
}

if ($rows.Count -ne 500) { throw "Generated $($rows.Count) rows instead of 500." }
$content = $header + "`r`n" + ($rows -join "`r`n") + "`r`n"
[IO.File]::WriteAllText($OutputPath, $content, [Text.UTF8Encoding]::new($true))
Write-Host "Generated 500 test points: $OutputPath"
