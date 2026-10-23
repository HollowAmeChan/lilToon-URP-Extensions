param([ValidateSet('qualification')][string]$Mode='qualification',[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
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
$job=@{label='qualification';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIReplayContractAudit.Run';report='ho-gi-replay-contract/report.json';marker='[HoGIReplayContract] END pass=True'}
$jobs=@($job)
$sources=@()
foreach($folder in @('Runtime/RTGI','Runtime/RayTracing','Runtime/RTDI/Shaders','Runtime/RTDI/ThirdParty/RTXDI')){
    $sources+=@(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot $folder) -Recurse -File|ForEach-Object{$_.FullName.Substring($workspaceRoot.Length+1).Replace('\','/')})
}
$sources+=@('Editor/RTGI/HoRTGISharedInputAudit.cs','Editor/RTGI/HoRTGISharedInputAudit.cs.meta','Runtime/RTGI/NRD/HoRTGINrdContext.cs','Editor/RTGI/HoRTGIInputStateAudit.cs','Editor/RTGI/HoRTGIInputStateAudit.cs.meta','Runtime/RTDI/HoRTDIRendererFeature.cs','Editor/RTGI/HoRTGIAtrousTrace.cs','Editor/RTGI/HoRTGIAtrousTrace.cs.meta','Runtime/RTDI/ThirdParty/NRD/RELAX_Atrous.cs.hlsl','Editor/RTGI/HoRTGILocalStageAudit.cs','Editor/RTGI/HoRTGILocalStageAudit.cs.meta','Editor/RTGI/HoRTGIPoolPrecisionAudit.cs','Editor/RTGI/Shaders/HoRTGIPrecisionCopy.compute','Runtime/RTDI/NRD/HoRTDINrdNative.cs','Runtime/RTDI/ThirdParty/NRD/NRD.hlsli','Runtime/RTDI/ThirdParty/NRD/NRDConfig.hlsli','.codex-research/RTGIValidation/Assets/mmd场景测试/血月古堡/血月古堡.unity','Editor/RTGI/HoRTGISeedAudit.cs','Editor/RTGI/HoRTGICastleValidation.cs','Editor/RTGI/HoRTGISpatialAudit.cs','Editor/RTGI/HoRTGIDenseReferenceFeature.cs','Editor/RTGI/HoRTGIDenseReferenceAudit.cs','Editor/RTGI/Shaders/HoRTGIDensePrimary.hlsl','Editor/RTGI/Shaders/HoRTGIDenseReference.compute','Editor/RTGI/Shaders/HoRTGIDenseReference.raytrace','Runtime/RTDI/NRD/HoRTDINrdContext.cs','Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll','Runtime/RTDI/ThirdParty/NRD/RELAX_TemporalAccumulation.cs.hlsl','Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryClamping.cs.hlsl','Runtime/RTDI/ThirdParty/NRD/RELAX_HistoryFix.cs.hlsl','Tests~/RTGI/ReplayContract/run_validation.ps1')
$sources+=@('Editor/RTGI/HoRTGIReplayContractAudit.cs','Editor/RTGI/HoRTGIReplayContractAudit.cs.meta','Editor/RTGI/Shaders/HoRTGIReplayContract.compute','Editor/RTGI/Shaders/HoRTGIReplayContract.compute.meta','Editor/RTGI/HoRTGIHistoryFeedbackAudit.cs','Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs')
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs') -Destination (Join-Path $projectRoot 'Assets/RTGIPlayerProbe/HoRTGIRuntimeProbe.cs')
$records=foreach($source in $sources){@{path=$source;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source)).Hash}}
$run=[ordered]@{step=41;debugLayerRequested=$true;staleLockRemoved=$staleLockRemoved;startedUtc=[DateTime]::UtcNow.ToString('o');sources=@($records);activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot ($Mode+'-run.json')
$snapshot=Join-Path $PSScriptRoot ($Mode+'-original');New-Item -ItemType Directory -Force -Path $snapshot|Out-Null
foreach($item in $records){if($item.path.EndsWith('.dll')){continue};$destination=Join-Path $snapshot ('source/'+$item.path);New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null;Copy-Item -LiteralPath (Join-Path $workspaceRoot $item.path) -Destination $destination}
$rejected=$false
foreach($job in $jobs){
    $entryName=if($job.entry.StartsWith('lilToon.')){$job.entry}else{'lilToon.URP.Extensions.Editor.RTGI.'+$job.entry}
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step041-'+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',$entryName,'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$p.Id;entry=$job.entry;label=$job.label;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
    $abortReason=$null
    while(-not $p.WaitForExit(1000)){
        if((Test-Path -LiteralPath $log) -and @(Get-Content -LiteralPath $log -Tail 80|Select-String -SimpleMatch 'D3D12Fence::Wait').Count -gt 0){$abortReason='Unity D3D12Fence::Wait error'}
        if(([DateTime]::UtcNow-$started).TotalSeconds -gt $(if($Mode -eq 'matrix'){900}else{120})){$abortReason='bounded mode timeout'}
        if($abortReason){$p.Kill();$p.WaitForExit();break}
    }
    $report=Join-Path $projectRoot $job.report
    $record=[ordered]@{label=$job.label;abortReason=$abortReason;exitCode=$p.ExitCode;log=$log;logSha256=(Get-FileHash -LiteralPath $log).Hash;marker=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0;fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started;debugErrors=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)').Count;shaderCsDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS|Compute shader .*Property|Attempting to bind texture|UAV usage flag').Count}
    $run.activeProcess=$null;$run.entries=@($record);$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw -Encoding UTF8|ConvertFrom-Json;$record.pass=$data.pass;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'));$record.cases=$data.cases;$record.wordChecks=$data.cpuEqualWordChecks;$record.collisions=$data.collisions}
    $outputDirectory=Split-Path -Parent $report;$dest=Join-Path $results $job.label;New-Item -ItemType Directory -Force -Path $dest|Out-Null
    if(Test-Path -LiteralPath $outputDirectory){Get-ChildItem -LiteralPath $outputDirectory -Filter '*.bin'|Where-Object{$_.LastWriteTimeUtc -ge $started}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $dest}}
    $record.sourceDrift=@($records|Where-Object{(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_.path)).Hash -ne $_.sha256}).Count
    $run.entries=@($record);$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.marker -or -not $record.fresh -or $record.debugErrors -ne 0 -or $record.shaderCsDiagnostics -ne 0 -or -not $record.pass -or $record.cases -ne 64 -or $record.wordChecks -ne 1024 -or $record.collisions -ne 32 -or $record.sourceDrift -ne 0){$rejected=$true}
}
Copy-Item -LiteralPath $runPath -Destination (Join-Path $snapshot 'run.json')
if($rejected){throw 'Seed validation rejected: inspect actual exit, reports and D3D12 diagnostics in run.json'}
