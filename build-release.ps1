param([switch]$VerifyUI)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$project = Join-Path $root 'SideTodo.csproj'
[xml]$metadata = Get-Content -LiteralPath $project
$version = [string]$metadata.Project.PropertyGroup.Version
$name = "SideTodo-$version-win-x64"
$stage = Join-Path $root "artifacts\$name"
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $stage,$dist -Force | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$exe = Join-Path $stage 'SideTodo.exe'
$test = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { throw 'Data and export tests failed. See self-test-result.txt.' }
if ($VerifyUI) {
    $test = Start-Process -FilePath $exe -ArgumentList '--ui-smoke' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'UI tests failed. See ui-smoke-result.txt.' }
}
Copy-Item -LiteralPath (Join-Path $root 'docs\START-HERE.ko.md') -Destination (Join-Path $stage 'START-HERE.ko.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $stage -Force
$notices = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $root 'obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$runtimePackages = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies } | Where-Object { $_.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$' })
if ($runtimePackages.Count -ne 2) { throw 'Expected .NET and Windows Desktop runtime package metadata.' }
foreach ($entry in $runtimePackages) {
    $runtimeVersion = $entry.version.Trim('[',']').Split(',')[0].Trim()
    $packagePath = $entry.name.ToLowerInvariant() + '/' + $runtimeVersion
    $packageDir = $null
    foreach ($packageRoot in $packageRoots) {
        $candidate = Join-Path $packageRoot $packagePath
        if (Test-Path -LiteralPath $candidate) { $packageDir = $candidate; break }
    }
    if (!$packageDir) { throw "Missing runtime package notices: $($entry.name)" }
    $prefix = $entry.name + '-' + $runtimeVersion
    $files = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match 'LICENSE|THIRD.PARTY' })
    if (!$files.Count) { throw "No license notices found for $($entry.name)" }
    foreach ($notice in $files) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices "$prefix-$($notice.Name)") -Force }
}
$zip = Join-Path $dist "$name.zip"
# Explicit package allowlist: no task data, diagnostic output or source files.
Compress-Archive -LiteralPath @($exe, (Join-Path $stage 'START-HERE.ko.md'), (Join-Path $stage 'THIRD-PARTY-NOTICES.md'), $notices) -DestinationPath $zip -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $dist "$name.sha256"), "$hash  $name.zip`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Package: $zip"
Write-Output "SHA256:  $hash"
