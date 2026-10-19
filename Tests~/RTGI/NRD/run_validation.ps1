param([switch]$Smoke,[switch]$RegressionOnly,[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
& (Join-Path $PSScriptRoot 'snapshot_validation.ps1')
$settingsPath=Join-Path $projectRoot 'ProjectSettings/ProjectSettings.asset'
$original=[IO.File]::ReadAllBytes($settingsPath);$settingsText=[IO.File]::ReadAllText($settingsPath)
$results=Join-Path $PSScriptRoot 'results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$jobs=foreach($color in @('Linear','Gamma')){
    @{label="$color-nrd-rg";color=$color;entry='RTGI.HoRTGINrdValidation.Run';report='ho-gi-nrd-rg/report.json';marker='[HoGINrd] END pass=True'}
    @{label="$color-nrd-compat";color=$color;entry='RTGI.HoRTGINrdValidation.RunCompatibility';report='ho-gi-nrd-compat/report.json';marker='[HoGINrd] END pass=True'}
}
if($Smoke){$jobs=@($jobs[0])}
else{
    if($RegressionOnly){$jobs=@()}
    $jobs+=@{label='Linear-di-nrd-regression';color='Linear';entry='RTDI.HoRTDIValidation.RunDenoise';marker='[HoRTDIValidation] PASS real NRD core/Unity RELAX dispatch'}
    $jobs+=@{label='Linear-raw-composite-regression';color='Linear';entry='RTGI.HoRTGICompositeValidation.Run';report='ho-gi-composite-rg/report.json';marker='[HoGIComposite] END pass=True'}
    $jobs+=@{label='Gamma-raw-environment-regression';color='Gamma';entry='RTGI.HoRTGIEnvironmentValidation.RunCompatibility';report='ho-gi-secondary-environment-compat/report.json';marker='[HoGIEnvironment] END pass=True'}
}
$run=[ordered]@{step=5;startedUtc=[DateTime]::UtcNow.ToString('o');project=$projectRoot;snapshotSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json') -Algorithm SHA256).Hash;activeProcess=$null;entries=@()}
$runPath=Join-Path $results $(if($Smoke){'smoke.json'}elseif($RegressionOnly){'run-regressions.json'}else{'run.json'})
try{
    foreach($job in $jobs){
        $value=if($job.color -eq 'Linear'){1}else{0}
        [IO.File]::WriteAllText($settingsPath,[regex]::Replace($settingsText,'m_ActiveColorSpace: [01]',('m_ActiveColorSpace: '+$value)))
        $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step005-'+$(if($Smoke){'smoke-'}else{'final-'})+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
        $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.'+$job.entry),'-logFile',('"'+$log+'"'))
        $run.activeProcess=[ordered]@{id=$p.Id;label=$job.label;entry=$job.entry;log=$log;startedUtc=$started.ToString('o')}
        $run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
        $p.WaitForExit();$marker=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0
        $diagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count
        $record=[ordered]@{label=$job.label;entry=$job.entry;colorSpace=$job.color;exitCode=$p.ExitCode;passMarker=$marker;shaderCsDiagnostics=$diagnostics;log=$log;logSha256=(Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash}
        if($job.report){
            $report=Join-Path $projectRoot $job.report;$record.reportFresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
            if($record.reportFresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.reportPass=$data.pass -and $data.colorSpace -eq $job.color;$record.reportSha256=(Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
            if(-not $record.reportFresh -or -not $record.reportPass){$marker=$false}
        }
        $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath
        Write-Output ($job.label+' exit '+$p.ExitCode+', pass='+$marker+', diagnostics='+$diagnostics)
        if($p.ExitCode -ne 0 -or -not $marker -or $diagnostics -ne 0){throw ('GI NRD validation failed: '+$job.label)}
    }
}finally{[IO.File]::WriteAllBytes($settingsPath,$original)}
