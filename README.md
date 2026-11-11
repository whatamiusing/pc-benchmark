# What Am I Using? PC Performance Benchmark

A portable Windows x64 benchmark for the processor, memory, graphics and storage. The app produces a local HTML report with results, hardware details and links to upgrade guides. It also lets you save a baseline and compare a later run on the same PC.

## Running the preview

Open the EXE and select **Run Benchmark**. Allow about 90 seconds. The report opens in your default browser. Use **Print or save as PDF** to keep a copy.

The current preview is 0.12.0. The launch target is Windows 11 x64. The app runs as a standard user and uses .NET Framework 4.8.

## Building from source

Run this command from the repository root in Windows PowerShell.

```powershell
.\Build.ps1 -ReportTest
```

The script builds the EXE in `build/`, runs the report and reference-data checks and prints the executable's SHA-256 checksum. On a Windows PC with compatible Direct3D 11 hardware, run `.\Build.ps1 -SmokeTest` to exercise the full benchmark and its interface. The bundled animations can be regenerated on a Direct3D 11 PC using `.\RenderStageArtwork.ps1`.

## Understanding the results

The report grades Everyday use, Gaming and Workstation against the thresholds it lists. The Gaming grade uses the Clean Energy Circuit at 1080p. The ten-second all-core CPU test and 1440p Glassworks pass report separate measurements. Before-and-after comparisons use your own saved baseline.

## Privacy

The benchmark and report run locally. By default, the app can fetch a public scoring reference from WhatAmIUsing.com. Benchmark results and hardware details stay on your PC. Pass `--offline` to the EXE to use the bundled reference. Clicking the optional BIOS update search opens Google with the motherboard make and model.

For contact details and the site's privacy policy, visit [What Am I Using?](https://www.whatamiusing.com/).

## Licence

The standalone benchmark, including its bundled artwork, is licensed under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE) for copyright attribution. The EXE includes both texts under **Licence**.
