param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$lockPath=Join-Path $projectRoot 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $lockPath){$lockProbe=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$lockProbe.Dispose();Remove-Item -LiteralPath $lockPath}
$probe=Join-Path $workspaceRoot 'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs'
Copy-Item -LiteralPath $probe -Destination (Join-Path $projectRoot 'Assets/RTGIPlayerProbe/HoRTGIRuntimeProbe.cs')
$results=Join-Path $PSScriptRoot 'player-results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$sourcePaths=@(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'Runtime') -Recurse -File|Where-Object{$_.Extension -in @('.cs','.hlsl','.h','.compute','.raytrace','.shader','.asmdef','.meta')}|ForEach-Object{$_.FullName.Substring($workspaceRoot.Length+1).Replace('\','/')})
$sourcePaths+=@('Editor/RTGI/HoRTGIPlayerValidation.cs','Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs','Runtime/RTDI/Plugins/x86_64/HoRTDINRD.dll','Tests~/RTGI/PrecisionQualification/run_players.ps1')
$sources=@($sourcePaths|ForEach-Object{@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_)).Hash}})
$snapshot=Join-Path $PSScriptRoot 'player-original';New-Item -ItemType Directory -Force -Path $snapshot|Out-Null
foreach($source in $sources){if($source.path.EndsWith('.dll')){continue};$dest=Join-Path $snapshot ('source/'+$source.path);New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest)|Out-Null;Copy-Item -LiteralPath (Join-Path $workspaceRoot $source.path) -Destination $dest}
$known=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-warning-review.json') -Raw|ConvertFrom-Json).knownWarningLines
$reuse=$null
if($reuse){foreach($source in $reuse.sources){if($source.path -eq 'Tests~/RTGI/PrecisionQualification/run_players.ps1'){continue};if((Get-FileHash -LiteralPath (Join-Path $workspaceRoot $source.path)).Hash -ne $source.sha256){throw ('Cannot reuse build after source change: '+$source.path)}}}
$run=[ordered]@{step=23;debugLayerRequested=$true;startedUtc=[DateTime]::UtcNow.ToString('o');sources=$sources;activeProcess=$null;entries=@()};$runPath=Join-Path $PSScriptRoot 'player-run.json'
function Wait-OwnedProcess($child,$log,$seconds){
    $started=[DateTime]::UtcNow;$abort=$null
    while(-not $child.WaitForExit(1000)){
        if((Test-Path -LiteralPath $log) -and @(Get-Content -LiteralPath $log -Tail 80|Select-String -SimpleMatch 'D3D12Fence::Wait').Count -gt 0){$abort='D3D12Fence::Wait'}
        if(([DateTime]::UtcNow-$started).TotalSeconds -gt $seconds){$abort='bounded timeout'}
        if($abort){$child.Kill();$child.WaitForExit();break}
    }
    return $abort
}
foreach($mode in @('rg','compat')){
    $entry=if($mode -eq 'rg'){'BuildPrecisionPool'}else{'BuildPrecisionPoolCompatibility'}
    $folder=if($mode -eq 'rg'){'PlayerGISkinnedPrecisionPool'}else{'PlayerGISkinnedCompatibilityPrecisionPool'}
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step023-build-'+$mode+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    if($reuse -and $mode -eq 'rg'){
        $record=[ordered]@{};foreach($property in $reuse.entries[0].PSObject.Properties){$record[$property.Name]=$property.Value}
        $record.reusedBuild=$true;$record.buildOriginalRun='rejected-build-warnings/run.json';$record.buildOriginalRunSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $record.buildOriginalRun)).Hash
        Write-Output 'Reuse reviewed RG build: actual original Unity exit 0, source content unchanged'
    }else{
    $child=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.'+$entry),'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$child.Id;phase='build';mode=$mode;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Build '+$mode+' PID '+$child.Id)
    $abort=Wait-OwnedProcess $child $log 900
    $record=[ordered]@{mode=$mode;buildAbort=$abort;buildExit=$child.ExitCode;buildLog=$log;buildLogSha256=(Get-FileHash -LiteralPath $log).Hash;buildMarker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIPlayerBuild] PASS').Count -gt 0;buildDebugErrors=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)').Count;buildDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count}
    }
    $buildLines=@(Select-String -LiteralPath $record.buildLog -Pattern 'Shader error|Shader warning|error CS'|ForEach-Object Line)
    $record.buildKnownWarnings=@($buildLines|Where-Object{$known -ccontains $_})
    $record.buildUnhandledDiagnostics=@($buildLines|Where-Object{$known -cnotcontains $_})
    if($record.buildExit -ne 0 -or -not $record.buildMarker -or $record.buildDebugErrors -ne 0 -or $record.buildUnhandledDiagnostics.Count -ne 0){$run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;throw 'History Player build failed or has new diagnostics'}
    $outputRoot=Join-Path $projectRoot $folder;$exe=Join-Path $outputRoot 'HoRTGIProbe.exe'
    $payload=@(Get-ChildItem -LiteralPath $outputRoot -Recurse -File|ForEach-Object{@{path=$_.FullName.Substring($outputRoot.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    $payload|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $results ($mode+'-payload.json'))
    $record.exe=$exe;$record.exeSha256=(Get-FileHash -LiteralPath $exe).Hash;$record.probeDllAbsent=@($payload|Where-Object{$_.path -match '(^|/)HoGIGpuProbe\.dll$'}).Count -eq 0
    $modeResults=Join-Path $results $mode;New-Item -ItemType Directory -Force -Path $modeResults|Out-Null;$report=Join-Path $modeResults 'report.json';$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step023-player-'+$mode+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.log');$started=[DateTime]::UtcNow
    $child=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-screen-fullscreen','0','-screen-width','64','-screen-height','64','-logFile',('"'+$log+'"'),'--result',('"'+$report+'"'))
    $run.activeProcess=@{id=$child.Id;phase='player';mode=$mode;log=$log;report=$report};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$mode+' PID '+$child.Id)
    $record.playerAbort=Wait-OwnedProcess $child $log 120
    $record.playerExit=$child.ExitCode;$record.log=$log;$record.logSha256=(Get-FileHash -LiteralPath $log).Hash;$record.marker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIRuntime] END pass=True').Count -gt 0
    $record.debugErrors=@(Select-String -LiteralPath $log -Pattern 'd3d12( validation)? (error|corruption)').Count;$record.diagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count
    $record.fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.baseVertexProbe=$data.baseVertexProbe;$record.historyProbe=$data.historyProbe;$record.precisionProbe=$data.precisionProbe;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash}
    $record.sourceDrift=@($sources|Where-Object{(Get-FileHash -LiteralPath (Join-Path $workspaceRoot $_.path)).Hash -ne $_.sha256}).Count
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$mode+' exit '+$child.ExitCode+' pass='+$record.pass)
    if($child.ExitCode -ne 0 -or -not $record.fresh -or -not $record.marker -or -not $record.pass -or -not $record.historyProbe -or -not $record.precisionProbe -or -not $record.baseVertexProbe -or -not $record.probeDllAbsent -or $record.debugErrors -ne 0 -or $record.diagnostics -ne 0 -or $record.sourceDrift -ne 0){throw 'Actual production history Player regression failed'}
}
Copy-Item -LiteralPath $runPath -Destination (Join-Path $snapshot 'run.json')
