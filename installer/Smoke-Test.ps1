param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,
    [string[]]$Modules = @('Dimmli', 'Awake', 'ClipPlain', 'PinWin', 'PowerFlip',
        'HashVerify', 'PortCheck', 'BatchRename', 'QuickView', 'FocusDim', 'CleanLink'),
    [switch]$ActivateDebugPro
)

$ErrorActionPreference = 'Stop'
if (!$env:RUNNER_TEMP) { throw 'Use a clean Windows runner profile for this test.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class TraySuiteSmokeNative
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@
$artifact = (Resolve-Path "$PSScriptRoot/../artifacts/public/$Runtime").Path
$setup = (Get-ChildItem "$artifact/*-Setup.exe").FullName
$root = Join-Path $env:RUNNER_TEMP "TraySuite smoke test $PID"
$hubDir = Join-Path $root 'Hub'
$hub = Join-Path $hubDir 'TraySuite.Hub.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$appsDir = Join-Path $env:LOCALAPPDATA 'TraySuite\Apps'
$hubProcess = $null
$testedModules = [System.Collections.Generic.List[string]]::new()

function Wait-For([string]$Description, [scriptblock]$Probe, [int]$Seconds = 25) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        try {
            $result = & $Probe
            if ($result) { return $result }
        } catch [System.Windows.Automation.ElementNotAvailableException] {
            # The Hub rebuilds its cards after each operation.
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Description."
}

function Invoke-Exe([string]$Exe, [string]$Arguments, [int]$Expected = 0) {
    $child = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru
    try {
        if (!$child.WaitForExit(120000)) { throw "Timed out: $Exe $Arguments" }
        if ($child.ExitCode -ne $Expected) {
            throw "$Exe $Arguments returned $($child.ExitCode), expected $Expected."
        }
    } finally {
        if (!$child.HasExited) { Stop-Process -Id $child.Id -Force }
        $child.Dispose()
    }
}

function Find-HubControl([string[]]$Names) {
    $pidCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $hubProcess.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $pidCondition)
    foreach ($window in $windows) {
        foreach ($name in $Names) {
            $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, $name)
            $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
            if ($control -and $control.Current.IsEnabled) { return $control }
        }
    }
}

function Invoke-HubButton([string[]]$Names) {
    $control = Wait-For ($Names -join ' / ') { Find-HubControl $Names }
    Invoke-NativeClick $control
}

function Invoke-NativeClick($Control) {
    $handle = [IntPtr]::new($Control.Current.NativeWindowHandle)
    if ($handle -eq [IntPtr]::Zero -or
        ![TraySuiteSmokeNative]::PostMessage($handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not post the click to the button.'
    }
}

function Get-ModuleProcess([string]$Name, [string]$Exe) {
    Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Select-Object -First 1
}

New-Item -ItemType Directory -Path $root | Out-Null
try {
    Invoke-Exe $setup "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=`"$hubDir`" /LOG=`"$root\setup.log`""
    $env:TRAYSUITE_LOCAL_PACKAGES = $artifact
    $hubProcess = Start-Process -FilePath $hub -ArgumentList '--open' -PassThru

    if ($ActivateDebugPro) {
        Invoke-HubButton @('Settings', 'Ustawienia')
        $key = Wait-For 'license key' { Find-HubControl @('License key', 'Klucz licencyjny') }
        $key.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('DEV-PRO-TEST')
        Invoke-HubButton @('Activate', 'Aktywuj')
        Wait-For 'Pro activation' { Test-Path (Join-Path $env:LOCALAPPDATA 'TraySuite\license.dat') } | Out-Null
        Invoke-HubButton @('Back', 'Wróć')
    }

    foreach ($name in $Modules) {
        $moduleDirectory = Join-Path $appsDir $name
        $exe = Join-Path $moduleDirectory "$name.exe"
        if (Test-Path $moduleDirectory) { throw "$moduleDirectory already exists before the smoke test." }
        $testedModules.Add($name)
        Invoke-HubButton @("Install $name", "Zainstaluj $name")
        Wait-For "$name installation" { Test-Path -LiteralPath $exe } | Out-Null
        Write-Host "Hub installed $name ($Runtime)."
        Invoke-Exe $hub '--open'

        Invoke-HubButton @("Open $name", "Otwórz $name")
        $module = Wait-For "$name process launched by Hub" { Get-ModuleProcess $name $exe }
        # Retain a process handle so Windows PowerShell can read the exit code after removal.
        $null = $module.Handle
        if ($module.WaitForExit(1400)) { throw "$name exited during startup ($($module.ExitCode))." }
        Write-Host "Hub opened $name ($Runtime)."
        if ($name -eq 'Dimmli') {
            Invoke-Exe $exe '--list'
            Invoke-Exe $exe '--brightness 101' 2
            Invoke-Exe $exe '--profile Invalid' 2
        }

        Invoke-Exe $hub '--open'
        Invoke-HubButton @("Uninstall $name", "Odinstaluj $name")
        $confirmation = Wait-For 'the uninstall confirmation' { Find-HubControl @('Yes', 'Tak') }
        Invoke-NativeClick $confirmation
        Wait-For "$name uninstallation" { !(Test-Path -LiteralPath $moduleDirectory) } | Out-Null
        if (!$module.WaitForExit(15000)) { throw "$name did not exit during removal." }
        if ($module.ExitCode -ne 0) { throw "$name exited with code $($module.ExitCode) during removal." }
        $module.Dispose()
        Write-Host "Hub installed, opened and uninstalled $name ($Runtime)."
    }

    Invoke-Exe $hub '--exit'
    if (!$hubProcess.WaitForExit(15000) -or $hubProcess.ExitCode -ne 0) { throw 'Hub did not exit normally.' }
    if (!(Test-Path $runKey)) { New-Item -Path $runKey | Out-Null }
    Set-ItemProperty $runKey Hub "`"$hub`" --start"
    $uninstaller = (Get-ChildItem "$hubDir/unins*.exe").FullName
    Invoke-Exe $uninstaller "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$root\uninstall.log`""
    Wait-For 'the Hub executable to be removed' { !(Test-Path -LiteralPath $hub) } | Out-Null
    Wait-For 'the Hub autostart value to be removed' {
        $null -eq (Get-Item -LiteralPath $runKey).GetValue('Hub')
    } | Out-Null
    Write-Host "The installer removed the Hub and its autostart entry ($Runtime)."
} catch {
    Write-Host $_.Exception.Message
    $log = Join-Path $root 'setup.log'
    if (Test-Path $log) { Get-Content -Tail 12 -LiteralPath $log | Write-Host }
    $uninstallLog = Join-Path $root 'uninstall.log'
    if (Test-Path $uninstallLog) { Get-Content -Tail 30 -LiteralPath $uninstallLog | Write-Host }
    $testedExecutables = (@('TraySuite.Hub') + @($testedModules) | ForEach-Object { [Regex]::Escape($_ + '.exe') }) -join '|'
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-5) } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error' -and $_.Message -match $testedExecutables } |
        Select-Object -First 5 -ExpandProperty Message | Write-Host
    if ($hubProcess) {
        $hubProcess.Refresh()
        if ($hubProcess.HasExited) {
            Write-Host "Hub exited with code $($hubProcess.ExitCode)."
        } else {
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $hubProcess.Id)
            foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children, $condition)) {
                $details = $window.Current
                Write-Host "Hub window: name='$($details.Name)' offscreen=$($details.IsOffscreen) enabled=$($details.IsEnabled)"
                foreach ($element in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)) {
                    $details = $element.Current
                    if ($details.Name) { Write-Host "  '$($details.Name)' enabled=$($details.IsEnabled)" }
                }
            }
        }
    }
    throw
} finally {
    if ($hubProcess) {
        if (!$hubProcess.HasExited) { Stop-Process -Id $hubProcess.Id -Force }
        $hubProcess.Dispose()
    }
    foreach ($name in $testedModules) {
        $exe = Join-Path (Join-Path $appsDir $name) "$name.exe"
        Get-ModuleProcess $name $exe | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
