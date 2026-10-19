param(
    [string]$ProjectRoot,
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe',
    [ValidateSet('RunUnityNoReuse','RunUnityRaw','RunUnityRR','RunNativeRelax')]
    [string[]]$Entries = @('RunUnityNoReuse','RunUnityRaw','RunUnityRR','RunNativeRelax')
)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $ProjectRoot) { $ProjectRoot = Join-Path $workspaceRoot '.codex-research/UnityPathTracing-6000.3.15' }
$resolvedProject = (Resolve-Path -LiteralPath $ProjectRoot).Path
if ($resolvedProject.Replace('\','/') -notmatch '/\.codex-research/') { throw 'An isolated reference project is required' }
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity executable missing: $Unity" }
$activeProject = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($resolvedProject.Replace('\','/')) }
if ($activeProject) { throw 'Reference project already has a Unity process; finish it before changing its probe' }
$probe = Join-Path $PSScriptRoot 'HoReferenceGIProbe.cs'
Copy-Item -LiteralPath $probe -Destination (Join-Path $resolvedProject 'Assets/Editor/HoReferenceGIProbe.cs')
$evidenceRoot = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$modeNames = @{
    RunUnityNoReuse = 'unity-no-reuse'
    RunUnityRaw = 'unity-raw'
    RunUnityRR = 'unity-rr'
    RunNativeRelax = 'native-relax'
}
$run = [ordered]@{
    probeSha256 = (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash
    unity = $Unity
    project = $resolvedProject
    startedUtc = [DateTime]::UtcNow.ToString('o')
    entries = @()
}
$previousPath = $env:PATH
try {
    $env:PATH = (Join-Path $resolvedProject 'Assets/Plugins/x86_64') + ';' +
        (Join-Path $resolvedProject 'Packages/top.kuanmi.native-rendering/Plugins/x86_64') + ';' + $previousPath
    foreach ($entry in $Entries) {
        $started = [DateTime]::UtcNow
        $log = Join-Path $workspaceRoot ('.codex-research/rtgi-v01-canonical-' + $entry + '.log')
        $process = Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @(
            '-batchmode','-force-d3d12','-projectPath',('"' + $resolvedProject + '"'),
            '-executeMethod',('HoReferenceGIProbe.' + $entry),'-logFile',('"' + $log + '"'))
        Write-Output ($entry + ' PID: ' + $process.Id)
        $process.WaitForExit()
        $record = [ordered]@{entry=$entry;exitCode=$process.ExitCode;startedUtc=$started.ToString('o');log=$log;reportFresh=$false;reportPass=$false}
        $report = Join-Path $resolvedProject ('ho-reference-gi-v01-' + $modeNames[$entry] + '/report.json')
        if (Test-Path -LiteralPath $report) {
            $record.reportFresh = (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
            if ($record.reportFresh) {
                $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
                $record.reportPass = $result.pass
                $record.reportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
                Copy-Item -LiteralPath $report -Destination (Join-Path $evidenceRoot ($modeNames[$entry] + '.json'))
            }
        }
        $run.entries += $record
        $run | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'run.json')
        Write-Output ($entry + ' exit: ' + $process.ExitCode + ', fresh=' + $record.reportFresh + ', reportPass=' + $record.reportPass)
        if ($process.ExitCode -ne 0 -or -not $record.reportFresh -or -not $record.reportPass) {
            throw ('Reference GI entry failed: ' + $entry + '; keep the failed report and inspect ' + $log)
        }
    }
}
finally { $env:PATH = $previousPath }
