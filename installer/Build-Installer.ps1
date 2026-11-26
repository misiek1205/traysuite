param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,
    [ValidateSet('public', 'pro')]
    [string]$Scope = 'public',
    [switch]$NoSetup
)

$ErrorActionPreference = 'Stop'
$buildArgs = @('build', '--scope', $Scope, '--runtime', $Runtime)
if ($NoSetup) { $buildArgs += '--no-setup' }
python (Join-Path $PSScriptRoot 'build.py') @buildArgs
if ($LASTEXITCODE -ne 0) { throw "TraySuite build failed with exit code $LASTEXITCODE." }
