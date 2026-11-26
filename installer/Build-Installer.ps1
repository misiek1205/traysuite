param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/TrayLuminance/TrayLuminance.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version
$architecture = $Runtime.Substring(4)
$output = Join-Path $root "artifacts/$Runtime"
$app = Join-Path $output 'app'

dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -warnaserror -o $app
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Copy-Item (Join-Path $root 'LICENSE') $app
$licenses = New-Item -ItemType Directory -Path (Join-Path $app 'licenses') -Force
$assetsPath = Join-Path (Split-Path $project) 'obj/project.assets.json'
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$packages = @{}
foreach ($library in $assets.libraries.GetEnumerator()) {
    $name = $library.Key.Split('/')[0]
    $packages[$name] = $library.Value.path
}
foreach ($download in $assets.project.frameworks.Values.downloadDependencies) {
    $versionRange = $download.version.Trim([char[]]'[]')
    $packageVersion = $versionRange.Split(',')[0]
    $packages[$download.name] = "$($download.name.ToLowerInvariant())/$packageVersion"
}
foreach ($name in @("Microsoft.NETCore.App.Runtime.$Runtime", "Microsoft.WindowsDesktop.App.Runtime.$Runtime", 'System.Management', 'System.CodeDom')) {
    $folder = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $packages[$name] } |
        Where-Object { Test-Path $_ -PathType Container } | Select-Object -First 1
    if (!$folder) { throw "NuGet package $name is missing." }

    Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(license|third-party-notices)(\.txt)?$' } |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses.FullName "$name-$($_.Name)") }
}

$compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (!$compiler) {
    $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
}
& $compiler "/DAppVersion=$version" "/DAppArchitecture=$architecture" "/DAppFiles=$app" "/O$output" (Join-Path $PSScriptRoot 'TrayLuminance.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

Compress-Archive -Path (Join-Path $app '*') -DestinationPath (Join-Path $output "TrayLuminance-$version-$Runtime.zip") -Force
