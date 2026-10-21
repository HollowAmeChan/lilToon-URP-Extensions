param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
# User explicitly authorized short visible Player tests in this conversation.
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$hidden=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'player-run.json') -Raw|ConvertFrom-Json
$known=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-warning-review.json') -Raw|ConvertFrom-Json).knownWarningLines
$run=[ordered]@{step=24;debugLayerRequested=$true;windowStyle='Normal';authorization='User explicitly allowed short visible Player tests';sources=@($hidden.sources);activeProcess=$null;entries=@()}
$run.sources+=@{path='Tests~/RTGI/AutomaticCamera/run_visible.ps1';sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash}
$runPath=Join-Path $PSScriptRoot 'compat-visible-run.json'
foreach($s in $hidden.sources){if((Get-FileHash -LiteralPath (Join-Path $workspaceRoot $s.path)).Hash -ne $s.sha256){throw ('Source drift: '+$s.path)}}
$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step024-build-compat-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.log')
$child=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$projectRoot+'"'),'-executeMethod','lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.BuildAutomaticCompatibility','-logFile',('"'+$log+'"'))
$run.activeProcess=@{id=$child.Id;phase='build';log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath
Write-Output ('Build compat PID '+$child.Id)
$start=[DateTime]::UtcNow;$abort=$null
while(-not $child.WaitForExit(1000)){
    if((Test-Path -LiteralPath $log) -and @(Get-Content -LiteralPath $log -Tail 80|Select-String -SimpleMatch 'D3D12Fence::Wait').Count){$abort='D3D12Fence::Wait'}
    if(([DateTime]::UtcNow-$start).TotalSeconds -gt 900){$abort='bounded build timeout'}
    if($abort){$child.Kill();$child.WaitForExit();break}
}
$e=[ordered]@{mode='compat';buildPid=$child.Id;buildExit=$child.ExitCode;buildAbort=$abort;buildLog=$log;buildLogSha256=(Get-FileHash -LiteralPath $log).Hash}
$lines=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS'|ForEach-Object Line)
$e.buildKnownWarnings=@($lines|Where-Object{$known -ccontains $_});$e.buildUnhandledDiagnostics=@($lines|Where-Object{$known -cnotcontains $_})
$e.buildMarker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIPlayerBuild] PASS').Count -gt 0
if($e.buildExit -ne 0 -or -not $e.buildMarker -or $e.buildUnhandledDiagnostics.Count){$run.entries+=,$e;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;throw 'Build failed'}
$output=Join-Path $projectRoot 'PlayerGISkinnedCompatibilityAutomatic';$exe=Join-Path $output 'HoRTGIProbe.exe'
$out=Join-Path $PSScriptRoot 'visible-results/compat';New-Item -ItemType Directory -Force -Path $out|Out-Null
@(Get-ChildItem -LiteralPath $output -Recurse -File|ForEach-Object{@{path=$_.FullName.Substring($output.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'visible-results/compat-payload.json')
$report=Join-Path $out 'report.json';$log=Join-Path $out 'player.log';$started=[DateTime]::UtcNow
$child=Start-Process -FilePath $exe -WindowStyle Normal -PassThru -ArgumentList @('-force-d3d12','-force-d3d12-debug','-screen-fullscreen','0','-screen-width','64','-screen-height','64','-logFile',('"'+$log+'"'),'--result',('"'+$report+'"'))
$run.activeProcess=@{id=$child.Id;phase='player';log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Visible compat PID '+$child.Id)
$e.exe=$exe;$e.exeSha256=(Get-FileHash -LiteralPath $exe).Hash;$e.playerPid=$child.Id;$e.playerAbort=$null
if(-not $child.WaitForExit(15000)){$e.playerAbort='15 second bounded visible test';$child.Kill();$child.WaitForExit()}
$e.playerExit=$child.ExitCode;$e.elapsedSeconds=([DateTime]::UtcNow-$started).TotalSeconds;$e.log=$log;$e.logSha256=(Get-FileHash -LiteralPath $log).Hash
$e.fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
if($e.fresh){$d=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$e.pass=$d.pass;$e.automaticCameras=$d.renderedCameras;$e.reportSha256=(Get-FileHash -LiteralPath $report).Hash}
$e.diagnostics=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)|Shader error|Shader warning|error CS|Compute shader .*Property|UAV.*(error|not set)|D3D12Fence::Wait').Count
$e.sourceDrift=@($run.sources|Where-Object{(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_.path)).Hash -ne $_.sha256}).Count
$run.entries+=,$e;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath
Write-Output ($e|ConvertTo-Json -Depth 6)
if($e.playerExit -ne 0 -or -not $e.fresh -or -not $e.pass -or $e.automaticCameras -lt 240 -or $e.diagnostics -or $e.sourceDrift){throw 'Automatic compatibility test failed'}
