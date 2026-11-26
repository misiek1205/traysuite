param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,
    [string[]]$Modules = @('Dimmli', 'Awake', 'ClipPlain'),
    [switch]$ActivateDebugPro
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$artifact = (Resolve-Path "$PSScriptRoot/../artifacts/public/$Runtime").Path
$setup = (Get-ChildItem "$artifact/*-Setup.exe").FullName
$root = Join-Path $env:RUNNER_TEMP "TraySuite smoke test $PID"
$hubDir = Join-Path $root 'Hub'
$hub = Join-Path $hubDir 'TraySuite.Hub.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$appsDir = Join-Path $env:LOCALAPPDATA 'TraySuite\Apps'
$hubProcess = $null

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
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-ModuleProcess([string]$Name, [string]$Exe) {
    Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Select-Object -First 1
}

function Confirm-Removal {
    # Keep the confirmation on a separate automation thread in case Invoke waits for the modal dialog.
    Start-Job -ArgumentList $hubProcess.Id -ScriptBlock {
        param($HubId)
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
        $pidCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $HubId)
        $yesCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, '6')
        $deadline = [DateTime]::UtcNow.AddSeconds(25)
        do {
            $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children, $pidCondition)
            foreach ($window in $windows) {
                $yes = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $yesCondition)
                if ($yes -and $yes.Current.IsEnabled -and !$yes.Current.IsOffscreen) {
                    $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                    return
                }
            }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        throw 'The Hub did not present an uninstall confirmation.'
    }
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
        Invoke-HubButton @("Install $name", "Zainstaluj $name")
        Wait-For "$name installation" { Test-Path -LiteralPath $exe } | Out-Null

        Invoke-HubButton @("Open $name", "Otwórz $name")
        $module = Wait-For "$name process launched by Hub" { Get-ModuleProcess $name $exe }
        if ($module.WaitForExit(1400)) { throw "$name exited during startup ($($module.ExitCode))." }
        if ($name -eq 'Dimmli') {
            Invoke-Exe $exe '--list'
            Invoke-Exe $exe '--brightness 101' 2
            Invoke-Exe $exe '--profile Invalid' 2
        }

        Invoke-Exe $hub '--open'
        $confirmation = Confirm-Removal
        try {
            Invoke-HubButton @("Uninstall $name", "Odinstaluj $name")
            if (!(Wait-Job $confirmation -Timeout 30)) { throw 'Uninstall confirmation timed out.' }
            Receive-Job $confirmation -ErrorAction Stop
        } finally {
            Remove-Job $confirmation -Force
        }
        Wait-For "$name uninstallation" { !(Test-Path -LiteralPath $moduleDirectory) } | Out-Null
        if (!$module.WaitForExit(15000) -or $module.ExitCode -ne 0) {
            throw "$name did not exit normally during removal."
        }
        $module.Dispose()
        Write-Host "Hub installed, opened and uninstalled $name ($Runtime)."
    }

    Invoke-Exe $hub '--exit'
    if (!$hubProcess.WaitForExit(15000) -or $hubProcess.ExitCode -ne 0) { throw 'Hub did not exit normally.' }
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty $runKey Hub "`"$hub`" --start"
    $uninstaller = (Get-ChildItem "$hubDir/unins*.exe").FullName
    Invoke-Exe $uninstaller "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$root\uninstall.log`""
    if ((Test-Path $hub) -or (Get-ItemProperty $runKey -Name Hub -ErrorAction SilentlyContinue)) {
        throw 'The Hub executable or its autostart entry survived uninstall.'
    }
} catch {
    $log = Join-Path $root 'setup.log'
    if (Test-Path $log) { Get-Content -Tail 30 -LiteralPath $log | Write-Host }
    throw
} finally {
    if ($hubProcess) {
        if (!$hubProcess.HasExited) { Stop-Process -Id $hubProcess.Id -Force }
        $hubProcess.Dispose()
    }
    foreach ($name in $Modules) {
        $exe = Join-Path (Join-Path $appsDir $name) "$name.exe"
        Get-ModuleProcess $name $exe | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
