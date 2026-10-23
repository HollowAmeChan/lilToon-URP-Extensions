param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe',[ValidateSet('rg','compat')][string]$Mode='rg')
# Short visible Player tests explicitly authorized by the user; each is bounded to 10 seconds.
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$lockPath=Join-Path $projectRoot 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $lockPath){$lockProbe=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$lockProbe.Dispose();Remove-Item -LiteralPath $lockPath}
$probe=Join-Path $workspaceRoot 'Tests~/RTGI/ComplexMotion/HoRTGIComplexMotionProbe.cs'
Copy-Item -LiteralPath $probe -Destination (Join-Path $projectRoot 'Assets/RTGIPlayerProbe/HoRTGIComplexMotionProbe.cs')
$results=Join-Path $PSScriptRoot 'player-results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$sourcePaths=@(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'Runtime') -Recurse -File|Where-Object{$_.Extension -in @('.cs','.hlsl','.h','.compute','.raytrace','.shader','.asmdef','.meta')}|ForEach-Object{$_.FullName.Substring($workspaceRoot.Length+1).Replace('\','/')})
$sourcePaths+=@('.codex-research/RTGIValidation/Packages/manifest.json','Editor/RTGI/HoRTGIPlayerValidation.cs','Tests~/RTGI/ComplexMotion/HoRTGIComplexMotionProbe.cs','Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll','Tests~/RTGI/ComplexMotion/run_players.ps1')
$sources=@($sourcePaths|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_)).Hash}})
$snapshot=Join-Path $PSScriptRoot 'player-original';New-Item -ItemType Directory -Force -Path $snapshot|Out-Null
foreach($source in $sources){if($source.path.EndsWith('.dll')){continue};$dest=Join-Path $snapshot ('source/'+$source.path);New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest)|Out-Null;Copy-Item -LiteralPath (Join-Path $workspaceRoot $source.path) -Destination $dest}
$known=(Get-Content -LiteralPath (Join-Path $PSScriptRoot '../Backbuffer/build-warning-review.json') -Raw|ConvertFrom-Json).knownWarningLines
$reuse=$null
if($reuse){foreach($source in $reuse.sources){if($source.path -eq 'Tests~/RTGI/ComplexMotion/run_players.ps1'){continue};if((Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source.path)).Hash -ne $source.sha256){throw ('Cannot reuse build after source change: '+$source.path)}}}
$run=[ordered]@{step=40;debugLayerRequested=$true;startedUtc=[DateTime]::UtcNow.ToString('o');sources=$sources;activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot ($Mode+'-run.json')
function Wait-OwnedProcess($child,$log,$seconds){
    $started=[DateTime]::UtcNow;$abort=$null
    while(-not $child.WaitForExit(1000)){
        if((Test-Path -LiteralPath $log) -and @(Get-Content -LiteralPath $log -Tail 80|Select-String -SimpleMatch 'D3D12Fence::Wait').Count -gt 0){$abort='D3D12Fence::Wait'}
        if(([DateTime]::UtcNow-$started).TotalSeconds -gt $seconds){$abort='bounded timeout'}
        if($abort){$child.Kill();$child.WaitForExit();break}
    }
    return $abort
}
foreach($backend in @($Mode)){
    $entry=if($backend -eq 'rg'){'BuildComplexMotion'}else{'BuildComplexMotionCompatibility'}
    $folder=if($backend -eq 'rg'){'PlayerGISkinnedComplexMotion'}else{'PlayerGISkinnedCompatibilityComplexMotion'}
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step040-build-'+$backend+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    if($reuse -and $backend -eq 'rg'){
        $record=[ordered]@{};foreach($property in $reuse.entries[0].PSObject.Properties){$record[$property.Name]=$property.Value}
        $record.reusedBuild=$true;$record.buildOriginalRun='rejected-build-warnings/run.json';$record.buildOriginalRunSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $record.buildOriginalRun)).Hash
        Write-Output 'Reuse reviewed RG build: actual original Unity exit 0, source content unchanged'
    }else{
    $child=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.'+$entry),'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$child.Id;phase='build';mode=$backend;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Build '+$backend+' PID '+$child.Id)
    $abort=Wait-OwnedProcess $child $log 900
    $record=[ordered]@{mode=$backend;buildAbort=$abort;buildExit=$child.ExitCode;buildLog=$log;buildLogSha256=(Get-FileHash -LiteralPath $log).Hash;buildMarker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIPlayerBuild] PASS').Count -gt 0;buildDebugErrors=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)').Count;buildDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count}
    }
    $run.activeProcess=$null;$run.entries=@($record);$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath
    $buildLines=@(Select-String -LiteralPath $record.buildLog -Pattern 'Shader error|Shader warning|error CS'|ForEach-Object Line)
    $record.buildKnownWarnings=@($buildLines|Where-Object{$known -ccontains $_})
    $record.buildUnhandledDiagnostics=@($buildLines|Where-Object{$known -cnotcontains $_})
    if($record.buildExit -ne 0 -or -not $record.buildMarker -or $record.buildDebugErrors -ne 0 -or $record.buildUnhandledDiagnostics.Count -ne 0){$run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;throw 'History Player build failed or has new diagnostics'}
    $outputRoot=Join-Path $projectRoot $folder;$exe=Join-Path $outputRoot 'HoRTGIProbe.exe'
    $payload=@(Get-ChildItem -LiteralPath $outputRoot -Recurse -File|ForEach-Object{@{path=$_.FullName.Substring($outputRoot.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    $payload|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $results ($backend+'-payload.json'))
    $record.exe=$exe;$record.exeSha256=(Get-FileHash -LiteralPath $exe).Hash;$record.probeDllAbsent=@($payload|Where-Object{$_.path -match '(^|/)HoGIGpuProbe\.dll$'}).Count -eq 0
    $backendResults=Join-Path $results $backend;New-Item -ItemType Directory -Force -Path $backendResults|Out-Null;$report=Join-Path $backendResults 'report.json';$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step040-player-'+$backend+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.log');$started=[DateTime]::UtcNow
    $child=Start-Process -FilePath $exe -WindowStyle Normal -PassThru -ArgumentList @('-force-d3d12','-force-d3d12-debug','-screen-fullscreen','0','-screen-width','512','-screen-height','512','-logFile',('"'+$log+'"'),'--result',('"'+$report+'"'))
    $run.activeProcess=@{id=$child.Id;phase='player';mode=$backend;log=$log;report=$report};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$backend+' PID '+$child.Id)
    $record.playerAbort=Wait-OwnedProcess $child $log 10
    $record.playerExit=$child.ExitCode;$run.activeProcess=$null;$run.entries=@($record);$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;$record.log=$log;$record.logSha256=(Get-FileHash -LiteralPath $log).Hash;$record.marker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIComplexMotion] END pass=True').Count -gt 0
    $record.debugErrors=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)').Count;$record.diagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count
    $record.fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.automaticCameras=$data.rendered;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash}
    $record.sourceDrift=@($sources|Where-Object{(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_.path)).Hash -ne $_.sha256}).Count
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$backend+' exit '+$child.ExitCode+' pass='+$record.pass)
    if($child.ExitCode -ne 0 -or -not $record.fresh -or -not $record.marker -or -not $record.pass -or $record.automaticCameras -lt 240 -or -not $record.probeDllAbsent -or $record.debugErrors -ne 0 -or $record.diagnostics -ne 0 -or $record.sourceDrift -ne 0){throw 'Actual production history Player regression failed'}
}
Copy-Item -LiteralPath $runPath -Destination (Join-Path $snapshot 'run.json')
