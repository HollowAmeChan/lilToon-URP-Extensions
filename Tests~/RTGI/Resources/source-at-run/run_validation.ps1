param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$active=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($projectRoot.Replace('\','/'))};if($active){throw 'GI project already active'}
$results=Join-Path $PSScriptRoot 'results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$jobs=@(@{label='256-rg';entry='HoRTGICastleValidation.RunResources256';report='ho-gi-castle-256-rg-resources-gpu/report.json';marker='[HoGICastle] PASS'},@{label='512-rg';entry='HoRTGICastleValidation.RunResources512';report='ho-gi-castle-512-rg-resources-gpu/report.json';marker='[HoGICastle] PASS'},@{label='1080-rg';entry='HoRTGICastleValidation.RunResources1080';report='ho-gi-castle-1920x1080-rg-resources-gpu/report.json';marker='[HoGICastle] PASS'},@{label='1080-compat';entry='HoRTGICastleValidation.RunResources1080Compatibility';report='ho-gi-castle-1920x1080-compat-resources-gpu/report.json';marker='[HoGICastle] PASS'})
$sources=@('Runtime/RTGI/HoRTGIContext.cs','Runtime/RTGI/HoRTGIRendererFeature.cs','Runtime/RTGI/HoRTGICompositePass.cs','Editor/RTGI/HoRTGIGpuClock.cs','Editor/RTGI/HoRTGIGpuTimestampValidation.cs','Editor/RTGI/HoRTGICastleValidation.cs','Editor/RTGI/HoRTGIResourceAudit.cs','Editor/RTGI/Shaders/HoRTGIGpuProbe.compute','Tests~/RTGI/GpuTiming/Native/HoGIGpuProbe.cpp')
$sources+=@(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'Runtime/RTGI') -Recurse -File|ForEach-Object{$_.FullName.Substring($workspaceRoot.Length+1).Replace('\','/')})
$sources+=@('Runtime/RTDI/NRD/HoRTDINrdContext.cs','Runtime/RayTracing/HoRTScene.cs','Runtime/RayTracing/HoRTSkinnedMesh.cs','Tests~/RTGI/Resources/run_validation.ps1')
$sources=@($sources|Sort-Object -Unique)
$records=foreach($source in $sources){@{path=$source;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source)).Hash}}
$run=[ordered]@{step=12;startedUtc=[DateTime]::UtcNow.ToString('o');sources=@($records);nativeBuildSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot '../GpuTiming/native-build.json')).Hash;activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot 'run.json'
foreach($job in $jobs){
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step012-'+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.'+$job.entry),'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$p.Id;entry=$job.entry;label=$job.label;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
    $p.WaitForExit();$report=Join-Path $projectRoot $job.report
    $record=[ordered]@{label=$job.label;exitCode=$p.ExitCode;log=$log;logSha256=(Get-FileHash -LiteralPath $log).Hash;marker=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0;fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started;shaderCsDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count}
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.marker -or -not $record.fresh -or -not $record.pass -or $record.shaderCsDiagnostics -ne 0){throw ('Resource/cost validation failed: '+$job.label)}
}
