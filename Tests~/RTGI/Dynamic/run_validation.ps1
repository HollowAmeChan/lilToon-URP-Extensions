param([switch]$CompatibilityOnly,[switch]$Cutout,[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$active=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($projectRoot.Replace('\','/'))}
if($active){throw 'Dedicated GI project already active'}
$manifestPath=Join-Path $projectRoot 'Packages/manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
if($manifest.dependencies.'jp.lilxyzw.liltoon.urp.extensions' -ne ('file:'+$workspaceRoot.Replace('\','/'))){throw 'Use the actual checkout after the recorded snapshot MonoScript mapping failure'}
$destination=Join-Path $projectRoot 'Assets/RTGIPlayerProbe';New-Item -ItemType Directory -Force -Path $destination|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HoRTGIRuntimeProbe.cs') -Destination (Join-Path $destination 'HoRTGIRuntimeProbe.cs')
$results=if($Cutout){Join-Path $PSScriptRoot '../Cutout/player-results'}else{Join-Path $PSScriptRoot 'results'};New-Item -ItemType Directory -Force -Path $results|Out-Null
$modes=if($CompatibilityOnly){@('compat')}else{@('rg','compat')}
$run=[ordered]@{step=$(if($Cutout){8}else{6});variant=$(if($Cutout){'cutout'}else{'application-frames'});head=(& git -C $workspaceRoot rev-parse HEAD).Trim();startedUtc=[DateTime]::UtcNow.ToString('o');runtimeProbeSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'HoRTGIRuntimeProbe.cs') -Algorithm SHA256).Hash;editorBuilderSha256=(Get-FileHash -LiteralPath (Join-Path $workspaceRoot 'Editor/RTGI/HoRTGIPlayerValidation.cs') -Algorithm SHA256).Hash;activeProcess=$null;entries=@()}
$runPath=if($Cutout){Join-Path $PSScriptRoot '../Cutout/player-run.json'}else{Join-Path $PSScriptRoot 'formal-run.json'}
foreach($mode in $modes){
    $entry=if($mode -eq 'rg'){'Build'}else{'BuildCompatibility'}
    if($Cutout){$entry=if($mode -eq 'rg'){'BuildCutout'}else{'BuildCutoutCompatibility'}}
    $label=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'-'+$mode
    $buildLog=Join-Path $workspaceRoot ('.codex-research/rtgi-step'+$(if($Cutout){'008-'}else{'006-'})+'build-'+$label+'.log')
    $started=[DateTime]::UtcNow
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.'+$entry),'-logFile',('"'+$buildLog+'"'))
    $run.activeProcess=@{id=$p.Id;phase='build';mode=$mode;log=$buildLog};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Build '+$mode+' PID '+$p.Id)
    $p.WaitForExit();$buildExit=$p.ExitCode;$buildMarker=@(Select-String -LiteralPath $buildLog -SimpleMatch '[HoGIPlayerBuild] PASS').Count -gt 0
    $record=[ordered]@{mode=$mode;buildExit=$buildExit;buildMarker=$buildMarker;buildLog=$buildLog;buildLogSha256=(Get-FileHash -LiteralPath $buildLog -Algorithm SHA256).Hash;buildWarnings=@(Select-String -LiteralPath $buildLog -Pattern 'Shader warning'|ForEach-Object{$_.Line})}
    if($buildExit -ne 0 -or -not $buildMarker){$run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;throw 'Player build failed'}
    $folder=if($mode -eq 'rg'){'PlayerGIDynamic'}else{'PlayerGIDynamicCompatibility'}
    if($Cutout){$folder=if($mode -eq 'rg'){'PlayerGICutout'}else{'PlayerGICutoutCompatibility'}}
    $exe=Join-Path $projectRoot ($folder+'/HoRTGIProbe.exe');$record.exeSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $native=Join-Path $projectRoot ($folder+'/HoRTGIProbe_Data/Plugins/x86_64/HoRTDINRD.dll');$record.nativeSha256=(Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash
    $report=Join-Path $results ('linear-'+$mode+'.json');$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step'+$(if($Cutout){'008-'}else{'006-'})+'player-'+$label+'.log');$playerStarted=[DateTime]::UtcNow
    $p=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-screen-fullscreen','0','-screen-width','64','-screen-height','64','-logFile',('"'+$log+'"'),'--result',('"'+$report+'"'))
    $run.activeProcess=@{id=$p.Id;phase='player';mode=$mode;log=$log;report=$report};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ('Player '+$mode+' PID '+$p.Id)
    $p.WaitForExit();$record.playerExit=$p.ExitCode;$record.log=$log;$record.logSha256=(Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash
    $record.reportFresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $playerStarted
    if($record.reportFresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.reportPass=$data.pass;$record.reportSha256=(Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash}
    $record.playerMarker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGIRuntime] END pass=True').Count -gt 0
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath
    Write-Output ('Player '+$mode+' exit '+$p.ExitCode+' pass='+$record.reportPass)
    if($p.ExitCode -ne 0 -or -not $record.reportFresh -or -not $record.reportPass -or -not $record.playerMarker){throw ('Runtime GI validation failed: '+$mode)}
}
