param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$lockPath=Join-Path $projectRoot 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $lockPath){$lockProbe=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$lockProbe.Dispose();Remove-Item -LiteralPath $lockPath}
$probe=Join-Path $workspaceRoot 'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs';Copy-Item -LiteralPath $probe -Destination (Join-Path $projectRoot 'Assets/RTGIPlayerProbe/HoRTGIRuntimeProbe.cs')
$results=Join-Path $PSScriptRoot 'player-results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$run=[ordered]@{step=17;startedUtc=[DateTime]::UtcNow.ToString('o');runtimeProbeSha256=(Get-FileHash -LiteralPath $probe).Hash;builderSha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot 'Editor/RTGI/HoRTGIPlayerValidation.cs')).Hash;normalizerSha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot 'Runtime/RayTracing/HoRTRayMeshIndices.cs')).Hash;activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot 'player-run.json'
foreach($mode in @('rg','compat')){
    $entry=if($mode -eq 'rg'){'BuildSkinnedBaseVertex'}else{'BuildSkinnedBaseVertexCompatibility'}
    $folder=if($mode -eq 'rg'){'PlayerGISkinnedBaseVertex'}else{'PlayerGISkinnedCompatibilityBaseVertex'}
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step017-build-base-'+$mode+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.'+$entry),'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$p.Id;phase='build';mode=$mode;log=$log};$run|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $runPath;Write-Output ('Build '+$mode+' PID '+$p.Id);$p.WaitForExit()
    $record=[ordered]@{mode=$mode;buildExit=$p.ExitCode;buildLog=$log;buildLogSha256=(Get-FileHash -LiteralPath $log).Hash;buildMarker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIPlayerBuild] PASS').Count -gt 0}
    if($p.ExitCode -ne 0 -or -not $record.buildMarker){$run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $runPath;throw 'Base-vertex Player build failed'}
    $exe=Join-Path $projectRoot ($folder+'/HoRTGIProbe.exe');$report=Join-Path $results ($mode+'.json');$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step017-player-base-'+$mode+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.log');$started=[DateTime]::UtcNow
    $record.exe=$exe;$record.exeSha256=(Get-FileHash -LiteralPath $exe).Hash;$record.probeDllAbsent=-not(Test-Path -LiteralPath (Join-Path $projectRoot ($folder+'/HoRTGIProbe_Data/Plugins/x86_64/HoGIGpuProbe.dll')))
    $p=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-screen-fullscreen','0','-screen-width','64','-screen-height','64','-logFile',('"'+$log+'"'),'--result',('"'+$report+'"'))
    $run.activeProcess=@{id=$p.Id;phase='player';mode=$mode;log=$log;report=$report};$run|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$mode+' PID '+$p.Id);$p.WaitForExit()
    $record.playerExit=$p.ExitCode;$record.log=$log;$record.logSha256=(Get-FileHash -LiteralPath $log).Hash;$record.fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.baseVertexProbe=$data.baseVertexProbe;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash}
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$mode+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.fresh -or -not $record.pass -or -not $record.baseVertexProbe -or -not $record.probeDllAbsent){throw 'Actual base-vertex Player regression failed'}
}
