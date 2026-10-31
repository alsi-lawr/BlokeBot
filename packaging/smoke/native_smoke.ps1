param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Executable,
    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'native_smoke.py') $Executable --version $Version
exit $LASTEXITCODE
