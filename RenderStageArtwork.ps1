$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$exe=Join-Path $build 'RenderStageArtwork.exe'
$shader=Join-Path $PSScriptRoot 'StageArt.hlsl'
$files=@('GraphicsBenchmark.cs','StageArtwork.cs','RenderStageArtwork.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /warnaserror /optimize+ /platform:x64 /target:exe "/out:$exe" "/resource:$shader,StageArt.hlsl" $files
if ($LASTEXITCODE -ne 0) { throw 'Stage artwork renderer build failed.' }
& $exe (Join-Path $PSScriptRoot 'stage-art')
if ($LASTEXITCODE -ne 0) { throw 'Stage artwork render failed.' }
