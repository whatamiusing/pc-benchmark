param([switch]$SmokeTest,[switch]$ReportTest)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The 64-bit .NET Framework compiler is required.' }
$output = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$app = Join-Path $output 'WAIU-PC-Benchmark-0.12.0-x64.exe'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$icon = Join-Path $PSScriptRoot 'BenchmarkIcon.ico'
$sources = @('Benchmark.cs', 'DeepBenchmark.cs', 'ComponentScores.cs', 'ReferenceTargets.cs', 'WifiInventory.cs', 'PcReport.cs', 'StageStrip.cs', 'StageArtwork.cs', 'GraphicsBenchmark.cs', 'StorageBenchmark.cs', 'LocalReport.cs', 'ReportWindow.cs', 'Program.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$references = '/reference:System.Management.dll,System.Web.Extensions.dll,System.Net.Http.dll'
$resources = @(
    '/resource:' + (Join-Path $PSScriptRoot 'CleanEnergy.hlsl') + ',CleanEnergy.hlsl'
    '/resource:' + (Join-Path $PSScriptRoot 'report/report.css') + ',Report.css'
    '/resource:' + (Join-Path $PSScriptRoot 'ReportIcons.svg') + ',ReportIcons.svg'
    '/resource:' + (Join-Path $PSScriptRoot 'reference-v2.json') + ',ReferenceV2.json'
    '/resource:' + (Join-Path $PSScriptRoot 'BenchmarkIcon.svg') + ',BenchmarkIcon.svg'
    '/resource:' + (Join-Path $PSScriptRoot 'LICENSE') + ',ApacheLicense.txt'
    '/resource:' + (Join-Path $PSScriptRoot 'NOTICE') + ',BenchmarkNotice.txt'
)
foreach ($name in @('processor.jpg','memory.jpg','storage.jpg','inventory.jpg')) {
    $resources += '/resource:' + (Join-Path $PSScriptRoot "stage-art/$name") + ",$name"
}
& $compiler /nologo /warnaserror /optimize+ /platform:x64 /target:winexe $references $resources "/win32manifest:$manifest" "/win32icon:$icon" "/out:$app" $sources
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }

if ($SmokeTest -or $ReportTest) {
    $test = Join-Path $output 'SmokeTest.exe'
    & $compiler /nologo /warnaserror /optimize+ /platform:x64 /target:exe $references $resources "/win32icon:$icon" /main:WaiuBenchmark.SmokeTest "/out:$test" $sources (Join-Path $PSScriptRoot 'SmokeTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test build failed.' }
    if ($ReportTest) { & $test --report-only } else { & $test }
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test failed.' }
}

Get-FileHash -LiteralPath $app -Algorithm SHA256
