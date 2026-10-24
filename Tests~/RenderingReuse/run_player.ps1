$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$project=Join-Path $root '.codex-research/RTGIValidation';$build=Get-ChildItem $PSScriptRoot -Directory -Filter 'playerBuild-*'|Sort-Object Name|Select-Object -Last 1
$data=Get-Content (Join-Path $build.FullName 'run.json') -Raw|ConvertFrom-Json
if($data.exitCode -ne 0){throw 'Latest build failed'}
$lines=@(Get-Content (Join-Path $build.FullName 'unity.log')|Select-String -Pattern 'Shader error|Shader warning|error CS'|ForEach-Object Line)
$policyPath=Join-Path $root 'Tests~/RTGI/Backbuffer/build-warning-review.json';$known=(Get-Content $policyPath -Raw|ConvertFrom-Json).knownWarningLines
if(@($lines|Where-Object{$known -cnotcontains $_}).Count){throw 'New build diagnostics are not accepted'}
if(@($data.sources|Where-Object{(Get-FileHash -LiteralPath (Join-Path $root $_.path)).Hash -ne $_.sha256}).Count){throw 'Build source drift'}
$exeRoot=Join-Path $project 'PlayerGIDynamic';$exe=Join-Path $exeRoot 'HoRTGIProbe.exe';$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff');$output=Join-Path $PSScriptRoot ('player-'+$stamp);New-Item -ItemType Directory -Path $output|Out-Null
$log=Join-Path $output 'unity.log';$report=Join-Path $output 'report.json'
$native=Join-Path $exeRoot 'HoRTGIProbe_Data/Plugins/x86_64/HoRTDINRD.dll';if((Get-FileHash $native).Hash -ne (Get-FileHash (Join-Path $root 'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')).Hash){throw 'Packaged native differs'}
$payload=@(Get-ChildItem $exeRoot -Recurse -File|Where-Object{$_.Name -in 'HoRTGIProbe.exe','HoRTDINRD.dll','Assembly-CSharp.dll','jp.lilxyzw.liltoon.urp.extensions.Runtime.dll'}|ForEach-Object{@{path=$_.FullName.Substring($exeRoot.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash $_.FullName).Hash}});$payload|ConvertTo-Json -Depth 5|Set-Content (Join-Path $output 'payload.json')
$run=@{mode='player';entry=$exe;log=$log;sources=$data.sources;startedUtc=[DateTime]::UtcNow.ToString('o');window='hidden';batchmode=$true;activeProcess=$null}
$p=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-screen-fullscreen','0','-screen-width','64','-screen-height','64','-logFile',$log,'--result',$report)
$run.activeProcess=$p.Id;$run|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'run.json');Write-Output ('Player PID '+$p.Id);$watch=[Diagnostics.Stopwatch]::StartNew();$abort=$null
while(-not $p.WaitForExit(1000)){if($watch.Elapsed.TotalSeconds -gt 45){$abort='bounded timeout';break};if((Test-Path $log)-and (Select-String -LiteralPath $log -SimpleMatch 'D3D12Fence::Wait')){$abort='fence';break}}
if($abort){$p.Kill();$p.WaitForExit()};$run.activeProcess=$null;$run.exitCode=$p.ExitCode;$run.abort=$abort;$run.logSha256=(Get-FileHash $log).Hash;$run.sourceDrift=@($data.sources|Where-Object{(Get-FileHash -LiteralPath (Join-Path $root $_.path)).Hash -ne $_.sha256}).Count;$run.diagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|D3D12Fence::Wait|d3d12( validation)? (error|corruption)|DllNotFoundException|EntryPointNotFoundException').Count;$run|ConvertTo-Json -Depth 8|Set-Content (Join-Path $output 'run.json')
$result=Get-Content $report -Raw|ConvertFrom-Json;Write-Output ('Player actual exit '+$p.ExitCode+' pass='+$result.pass+' callbacks='+$result.renderCallbacks)
if($p.ExitCode -ne 0 -or $abort -or $run.diagnostics -or $run.sourceDrift -or -not $result.pass -or $result.renderCallbacks -ne 304){throw 'Release Player qualification failed'}
