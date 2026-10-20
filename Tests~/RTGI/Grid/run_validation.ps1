param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$active=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($projectRoot.Replace('\','/'))};if($active){throw 'GI project already active'}
$results=Join-Path $PSScriptRoot 'results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$jobs=@(@{label='rg';entry='HoRTGIGridValidation.Run';report='ho-gi-grid-rg/report.json';marker='[HoGIGrid] PASS'},@{label='compat';entry='HoRTGIGridValidation.RunCompatibility';report='ho-gi-grid-compat/report.json';marker='[HoGIGrid] PASS'})
$sources=@('Runtime/RTGI/HoRTGIGrid.cs','Runtime/RTGI/Shaders/HoRTGIGrid.hlsl','Editor/RTGI/HoRTGIGridValidation.cs','Editor/RTGI/Shaders/HoRTGIGridAudit.compute','Runtime/RayTracing/HoRTPrimarySurfaceInputs.cs','Runtime/RayTracing/Shaders/HoRTPrimarySurface.hlsl','Runtime/SurfaceBuffer/Shaders/HoSurfaceAssociation.hlsl','Runtime/RTDI/NRD/HoRTDINrdNative.cs','Tests~/RTGI/Grid/run_validation.ps1')
$records=foreach($source in $sources){@{path=$source;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source)).Hash}}
$run=[ordered]@{step=13;startedUtc=[DateTime]::UtcNow.ToString('o');sources=@($records);activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot 'run.json'
foreach($job in $jobs){
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step013-'+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.'+$job.entry),'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$p.Id;entry=$job.entry;label=$job.label;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
    $abortReason=$null
    while(-not $p.WaitForExit(1000)){
        if((Test-Path -LiteralPath $log) -and @(Get-Content -LiteralPath $log -Tail 80|Select-String -SimpleMatch 'D3D12Fence::Wait').Count -gt 0){$abortReason='Unity D3D12Fence::Wait error'}
        if(([DateTime]::UtcNow-$started).TotalMinutes -gt 15){$abortReason='15 minute bounded validation timeout'}
        if($abortReason){$p.Kill();$p.WaitForExit();break}
    }
    $report=Join-Path $projectRoot $job.report
    $record=[ordered]@{label=$job.label;abortReason=$abortReason;exitCode=$p.ExitCode;log=$log;logSha256=(Get-FileHash -LiteralPath $log).Hash;marker=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0;fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started;shaderCsDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count}
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.marker -or -not $record.fresh -or -not $record.pass -or $record.shaderCsDiagnostics -ne 0){throw ('Grid validation failed: '+$job.label)}
}
