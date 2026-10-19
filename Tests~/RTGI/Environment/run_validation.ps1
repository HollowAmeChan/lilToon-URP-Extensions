param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot = Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$settingsPath = Join-Path $projectRoot 'ProjectSettings/ProjectSettings.asset'
if (-not (Test-Path -LiteralPath $settingsPath)) { throw 'Prepare the dedicated RTGIValidation project first' }
$active = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($projectRoot.Replace('\','/')) }
if ($active) { throw 'Dedicated GI project already running; inspect the live process before changing files' }
$originalSettings = [IO.File]::ReadAllBytes($settingsPath)
$settingsText = [IO.File]::ReadAllText($settingsPath)
$evidenceRoot = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
$jobs = foreach ($color in @('Linear','Gamma')) {
    @{label="$color-environment-rg";color=$color;entry='HoRTGIEnvironmentValidation.Run';report='ho-gi-secondary-environment-rg/report.json';pass='[HoGIEnvironment] END pass=True'}
    @{label="$color-environment-compat";color=$color;entry='HoRTGIEnvironmentValidation.RunCompatibility';report='ho-gi-secondary-environment-compat/report.json';pass='[HoGIEnvironment] END pass=True'}
    @{label="$color-legacy-rg";color=$color;entry='HoRTGIValidation.Run';pass='[HoRTGIValidation] PASS'}
    @{label="$color-legacy-compat";color=$color;entry='HoRTGIValidation.RunCompatibility';pass='[HoRTGIValidation] PASS'}
    @{label="$color-secondary";color=$color;entry='HoRTGISecondaryValidation.Run';pass='[HoRTGISecondaryValidation] PASS'}
}
$jobs += @{label='Linear-dx11-inactive';color='Linear';entry='HoRTGIValidation.RunUnsupported';api='-force-d3d11';pass='[HoRTGIValidation] PASS unsupported platform'}
$run = [ordered]@{step=3;startedUtc=[DateTime]::UtcNow.ToString('o');project=$projectRoot;activeProcess=$null;entries=@()}
$runPath = Join-Path $evidenceRoot 'run.json'
try {
    foreach ($job in $jobs) {
        $colorValue = if ($job.color -eq 'Linear') {1} else {0}
        [IO.File]::WriteAllText($settingsPath,[regex]::Replace($settingsText,'m_ActiveColorSpace: [01]',('m_ActiveColorSpace: '+$colorValue)))
        $started = [DateTime]::UtcNow
        $log = Join-Path $workspaceRoot ('.codex-research/rtgi-step003-' + $job.label + '.log')
        $api = if ($job.api) {$job.api} else {'-force-d3d12'}
        $process = Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode',$api,
            '-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.'+$job.entry),'-logFile',('"'+$log+'"'))
        $run.activeProcess = [ordered]@{id=$process.Id;label=$job.label;entry=$job.entry;log=$log;startedUtc=$started.ToString('o')}
        $run | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $runPath
        Write-Output ($job.label+' PID '+$process.Id)
        $process.WaitForExit()
        $marker = @(Select-String -LiteralPath $log -SimpleMatch $job.pass).Count -gt 0
        $diagnostics = @(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count
        $record = [ordered]@{label=$job.label;entry=$job.entry;colorSpace=$job.color;api=$api;exitCode=$process.ExitCode;passMarker=$marker;shaderCsDiagnostics=$diagnostics;log=$log;logSha256=(Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash}
        if ($job.report) {
            $report = Join-Path $projectRoot $job.report
            $record.reportFresh = (Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
            if ($record.reportFresh) {
                $data = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
                $record.reportPass = $data.pass -and $data.colorSpace -eq $job.color
                $record.reportSha256 = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
                Copy-Item -LiteralPath $report -Destination (Join-Path $evidenceRoot ($job.label+'.json'))
            }
            if (-not $record.reportFresh -or -not $record.reportPass) {$marker=$false}
        }
        $run.entries += $record
        $run.activeProcess = $null
        $run | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $runPath
        Write-Output ($job.label+' exit '+$process.ExitCode+', passMarker='+$marker+', shaderCsDiagnostics='+$diagnostics)
        if ($process.ExitCode -ne 0 -or -not $marker -or $diagnostics -ne 0) {throw ('GI validation failed: '+$job.label+'; inspect '+$log)}
    }
}
finally { [IO.File]::WriteAllBytes($settingsPath,$originalSettings) }
