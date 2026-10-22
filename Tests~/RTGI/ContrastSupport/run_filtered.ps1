param([ValidateSet('qualification','matrix','control','control-matrix')][string]$Mode='qualification',[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$staleLockRemoved=$false;$lockPath=Join-Path $projectRoot 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $lockPath){
    # A nonzero Editor exit may leave the file. Exclusive open fails if an
    # actual project owner still holds it; never stop that owner or remove it.
    $lockProbe=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $lockProbe.Dispose();Remove-Item -LiteralPath $lockPath;$staleLockRemoved=$true
}
$results=Join-Path $PSScriptRoot 'filtered-results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$job=if($Mode -eq 'qualification'){@{label='qualification';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.RunPropagationQualification';report='ho-gi-castle-256-rg-propagation-qualification/report.json';marker='[HoGILocalStages] COMPLETE segments=2';segments=2}}else{@{label='matrix';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.RunPropagation';report='ho-gi-castle-256-rg-propagation/report.json';marker='[HoGILocalStages] COMPLETE segments=4';segments=4}}
if($Mode -eq 'control'){$job=@{label='control';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.RunPropagationControlQualification';report='ho-gi-castle-256-rg-propagation-control-qualification/report.json';marker='[HoGILocalStages] COMPLETE segments=2';segments=2}}
if($Mode -eq 'control-matrix'){$job=@{label='control-matrix';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.RunPropagationControl';report='ho-gi-castle-256-rg-propagation-control/report.json';marker='[HoGILocalStages] COMPLETE segments=4';segments=4}}
$jobs=@($job)
$sources=@()
foreach($folder in @('Runtime/RTGI','Runtime/RayTracing','Runtime/RTDI/Shaders','Runtime/RTDI/ThirdParty/RTXDI')){
    $sources+=@(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot $folder) -Recurse -File|ForEach-Object{$_.FullName.Substring($workspaceRoot.Length+1).Replace('\','/')})
}
$sources+=@('Editor/RTGI/HoRTGIAtrousTrace.cs','Editor/RTGI/HoRTGIAtrousTrace.cs.meta','Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl','Editor/RTGI/HoRTGILocalStageAudit.cs','Editor/RTGI/HoRTGILocalStageAudit.cs.meta','Editor/RTGI/HoRTGIPoolPrecisionAudit.cs','Editor/RTGI/Shaders/HoRTGIPrecisionCopy.compute','Runtime/RTDI/NRD/HoRTDINrdNative.cs','Runtime/RTDI/ThirdParty/NRD/NRD.hlsli','Runtime/RTDI/ThirdParty/NRD/NRDConfig.hlsli','.codex-research/RTGIValidation/Assets/mmd场景测试/血月古堡/血月古堡.unity','Editor/RTGI/HoRTGISeedAudit.cs','Editor/RTGI/HoRTGICastleValidation.cs','Editor/RTGI/HoRTGISpatialAudit.cs','Editor/RTGI/HoRTGIDenseReferenceFeature.cs','Editor/RTGI/HoRTGIDenseReferenceAudit.cs','Editor/RTGI/Shaders/HoRTGIDensePrimary.hlsl','Editor/RTGI/Shaders/HoRTGIDenseReference.compute','Editor/RTGI/Shaders/HoRTGIDenseReference.raytrace','Runtime/RTDI/NRD/HoRTDINrdContext.cs','Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll','Runtime/RTDI/ThirdParty/NRD/RELAX_TemporalAccumulation.cs.hlsl','Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryClamping.cs.hlsl','Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryFix.cs.hlsl','Tests~/RTGI/ContrastSupport/run_filtered.ps1')
$records=foreach($source in $sources){@{path=$source;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source)).Hash}}
$run=[ordered]@{step=34;debugLayerRequested=$true;staleLockRemoved=$staleLockRemoved;startedUtc=[DateTime]::UtcNow.ToString('o');sources=@($records);activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot 'filtered-run.json'
$snapshot=Join-Path $PSScriptRoot 'filtered-original';New-Item -ItemType Directory -Force -Path $snapshot|Out-Null
foreach($item in $records){if($item.path.EndsWith('.dll')){continue};$destination=Join-Path $snapshot ('source/'+$item.path);New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null;Copy-Item -LiteralPath (Join-Path $workspaceRoot $item.path) -Destination $destination}
$rejected=$false
foreach($job in $jobs){
    $entryName=if($job.entry.StartsWith('lilToon.')){$job.entry}else{'lilToon.URP.Extensions.Editor.RTGI.'+$job.entry}
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step034-filtered-'+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
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
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.localFunctional=$data.localStageAudit.complete;$record.segments=$data.localStageAudit.segments.Count;$record.copyBitExact=$data.localStageAudit.precision.copyBitExact;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
    $streamDest=Join-Path $results $job.label;New-Item -ItemType Directory -Force -Path $streamDest|Out-Null
    $record.streams=@(Get-ChildItem -LiteralPath (Split-Path -Parent $report) -Filter '*.bin'|Where-Object{$_.LastWriteTimeUtc -ge $started}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $streamDest;@{path=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    if($record.fresh -and $data.localStageAudit.trace){$gen=Join-Path $PSScriptRoot ('generated/'+$job.label);New-Item -ItemType Directory -Force -Path $gen|Out-Null;foreach($source in @($data.localStageAudit.trace.bodyPath,$data.localStageAudit.trace.wrapperPath)){Copy-Item -LiteralPath (Join-Path $projectRoot $source) -Destination $gen}}
    $record.sourceDrift=@($records|Where-Object{(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_.path)).Hash -ne $_.sha256}).Count
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.marker -or -not $record.fresh -or $record.debugErrors -ne 0 -or $record.shaderCsDiagnostics -ne 0 -or -not $record.pass -or -not $record.localFunctional -or -not $record.copyBitExact -or $record.segments -ne $job.segments -or $record.sourceDrift -ne 0){$rejected=$true}
}
Copy-Item -LiteralPath $runPath -Destination (Join-Path $snapshot 'run.json')
if($rejected){throw 'Seed validation rejected: inspect actual exit, reports and D3D12 diagnostics in run.json'}
