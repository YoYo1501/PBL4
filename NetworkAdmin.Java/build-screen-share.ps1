$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoots = @(
    (Join-Path $projectRoot "modules\screen-share-protocol\src\main\java"),
    (Join-Path $projectRoot "modules\screen-share-client\src\main\java"),
    (Join-Path $projectRoot "modules\screen-share-server\src\main\java"),
    (Join-Path $projectRoot "modules\network-core\src\main\java"),
    (Join-Path $projectRoot "modules\control-protocol\src\main\java"),
    (Join-Path $projectRoot "modules\control-transport\src\main\java"),
    (Join-Path $projectRoot "modules\client-runtime\src\main\java"),
    (Join-Path $projectRoot "modules\admin-runtime\src\main\java"),
    (Join-Path $projectRoot "modules\admin-app\src\main\java")
)
$outputRoot = Join-Path $projectRoot "build\classes"
$sources = $sourceRoots | ForEach-Object {
    Get-ChildItem -Path $_ -Recurse -Filter "*.java" | ForEach-Object FullName
}

if ($sources.Count -eq 0) {
    throw "No Java source files were found in the screen-share modules."
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
& javac --release 21 -encoding UTF-8 -d $outputRoot $sources
if ($LASTEXITCODE -ne 0) {
    throw "Java compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Compiled current Java modules to $outputRoot"
