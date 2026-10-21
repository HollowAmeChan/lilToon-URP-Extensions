param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$staleLockRemoved=$false;$lockPath=Join-Path $projectRoot 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $lockPath){
    # A nonzero Editor exit may leave the file. Exclusive open fails if an
    # actual project owner still holds it; never stop that owner or remove it.
    $lockProbe=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $lockProbe.Dispose();Remove-Item -LiteralPath $lockPath;$staleLockRemoved=$true
}
$results=Join-Path $PSScriptRoot 'results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$jobs=@(@{label='final-gpu';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.RunGpu256';report='ho-gi-castle-256-rg-gpu/report.json';marker='[HoGICastle] PASS'})
$sources=@()
foreach($folder in @('Runtime/RTGI','Runtime/RayTracing','Runtime/RTDI/Shaders','Runtime/RTDI/ThirdParty/RTXDI')){
    $sources+=@(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot $folder) -Recurse -File|ForEach-Object{$_.FullName.Substring($workspaceRoot.Length+1).Replace('\','/')})
}
$sources+=@('Editor/RTGI/HoRTGIGpuClock.cs','Editor/RTGI/HoRTGIGpuTimestampValidation.cs','Editor/RTGI/Shaders/HoRTGIGpuProbe.compute','Tests~/RTGI/GpuTiming/Native/HoGIGpuProbe.cpp','.codex-research/RTGIValidation/Assets/Plugins/x86_64/HoGIGpuProbe.dll','.codex-research/RTGIValidation/Assets/Plugins/x86_64/HoGIGpuProbe.dll.meta','Editor/RTGI/HoRTGIPoolPrecisionAudit.cs','Editor/RTGI/Shaders/HoRTGIPrecisionCopy.compute','Editor/RTGI/HoRTGINrdValidation.cs','Editor/RTGI/HoRTGIPrecisionRangeValidation.cs','Editor/RTGI/HoRTGIProductionHistoryAudit.cs','Editor/RTGI/HoRTGIHistoryFeedbackAudit.cs','Editor/RTGI/HoRTGISeedAudit.cs','Editor/RTGI/HoRTGICastleValidation.cs','Editor/RTGI/HoRTGISpatialAudit.cs','Editor/RTGI/HoRTGIDenseReferenceFeature.cs','Editor/RTGI/HoRTGIDenseReferenceAudit.cs','Editor/RTGI/Shaders/HoRTGIDensePrimary.hlsl','Editor/RTGI/Shaders/HoRTGIDenseReference.compute','Editor/RTGI/Shaders/HoRTGIDenseReference.raytrace','Runtime/RTDI/NRD/HoRTDINrdContext.cs','Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll','Runtime/RTDI/ThirdParty/NRD/RELAX_TemporalAccumulation.cs.hlsl','Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryClamping.cs.hlsl','Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryFix.cs.hlsl','Tests~/RTGI/PrecisionQualification/run_final_gpu.ps1')
$records=foreach($source in $sources){@{path=$source;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source)).Hash}}
$run=[ordered]@{step=23;debugLayerRequested=$true;staleLockRemoved=$staleLockRemoved;startedUtc=[DateTime]::UtcNow.ToString('o');sources=@($records);activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot 'final-gpu-run.json'
$snapshot=Join-Path $PSScriptRoot 'final-gpu-original';New-Item -ItemType Directory -Force -Path $snapshot|Out-Null
foreach($item in $records){if($item.path.EndsWith('.dll')){continue};$destination=Join-Path $snapshot ('source/'+$item.path);New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null;Copy-Item -LiteralPath (Join-Path $workspaceRoot $item.path) -Destination $destination}
$rejected=$false
foreach($job in $jobs){
    $entryName=if($job.entry.StartsWith('lilToon.')){$job.entry}else{'lilToon.URP.Extensions.Editor.RTGI.'+$job.entry}
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step023-'+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',$entryName,'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$p.Id;entry=$job.entry;label=$job.label;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
    $abortReason=$null
    while(-not $p.WaitForExit(1000)){
        if((Test-Path -LiteralPath $log) -and @(Get-Content -LiteralPath $log -Tail 80|Select-String -SimpleMatch 'D3D12Fence::Wait').Count -gt 0){$abortReason='Unity D3D12Fence::Wait error'}
        if(([DateTime]::UtcNow-$started).TotalMinutes -gt 15){$abortReason='15 minute bounded validation timeout'}
        if($abortReason){$p.Kill();$p.WaitForExit();break}
    }
    $report=Join-Path $projectRoot $job.report
    $record=[ordered]@{label=$job.label;abortReason=$abortReason;exitCode=$p.ExitCode;log=$log;logSha256=(Get-FileHash -LiteralPath $log).Hash;marker=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0;fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started;debugErrors=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)').Count;shaderCsDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag').Count}
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;if($data.pass){$record.marker=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0;if(-not $record.marker){$record.marker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGICastle] PASS').Count -gt 0}};$record.productionFunctional=$data.pass;$record.energyGatePass=$data.energyGatePass;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
    $record.sourceDrift=@($records|Where-Object{(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_.path)).Hash -ne $_.sha256}).Count
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.marker -or -not $record.fresh -or $record.debugErrors -ne 0 -or $record.shaderCsDiagnostics -ne 0 -or -not $record.productionFunctional -or $record.sourceDrift -ne 0){$rejected=$true}
}
Copy-Item -LiteralPath $runPath -Destination (Join-Path $snapshot 'final-gpu-run.json')
if($rejected){throw 'Production history validation rejected: inspect run.json'}
