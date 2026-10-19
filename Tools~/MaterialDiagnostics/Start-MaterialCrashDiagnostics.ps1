[CmdletBinding()]
param(
    [string] $ProjectPath = 'D:\Unity_Project\BREAK_URP',
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe',
    [switch] $PrintOnly
)

$ErrorActionPreference = 'Stop'
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path.TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $resolvedProject 'ProjectSettings\ProjectVersion.txt'))) {
    throw 'ProjectPath must point to a Unity project.'
}
if (-not (Test-Path -LiteralPath $UnityPath)) { throw 'UnityPath does not exist.' }

# Startup flags cannot be added to an existing Unity process. Never terminate it:
# the user may still have unsaved scenes, materials or renderer settings.
$activeEditor = Get-CimInstance Win32_Process -Filter "name='Unity.exe'" | Where-Object {
    $command = $_.CommandLine
    if ($command -match '(?i)(?:^|\s)-projectpath\s+(?:"([^"]+)"|([^\s]+))') {
        $candidate = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
        $candidate.Replace('/', '\').TrimEnd('\') -ieq $resolvedProject.Replace('/', '\')
    }
}

$logFolder = Join-Path $resolvedProject ('Logs\MaterialCrash-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$diagnosticLog = Join-Path $logFolder 'Editor-D3D12-debug.log'
$launchArguments = @(
    '-projectPath', ('"' + $resolvedProject + '"'),
    '-force-d3d12', '-force-d3d12-debug',
    '-logFile', ('"' + $diagnosticLog + '"')
)
if ($PrintOnly) {
    [PSCustomObject]@{ Unity = $UnityPath; Arguments = $launchArguments; AlreadyOpen = [bool]$activeEditor } |
        ConvertTo-Json -Depth 3
    return
}
if ($activeEditor) {
    throw 'Save and close this project in Unity first, then run this script again. No process was stopped.'
}

New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
Write-Host "Diagnostic log: $diagnosticLog"
# Only this launch uses the D3D12 debug layer; project settings and caches are preserved.
# This is the interactive editor the user will use to reproduce the click, not a worker.
Start-Process -FilePath $UnityPath -ArgumentList $launchArguments -WindowStyle Normal
