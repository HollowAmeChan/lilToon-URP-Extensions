param([switch]$Compatibility,[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
$taskAssets=Join-Path $taskProject 'Assets/RTShadowPlayerSource'
New-Item -ItemType Directory -Path $taskAssets,(Join-Path $taskAssets 'Editor') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HoRTShadowPlayerProbe.cs.txt') -Destination (Join-Path $taskAssets 'HoRTShadowPlayerProbe.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BuildShadowPlayer.cs.txt') -Destination (Join-Path $taskAssets 'Editor/BuildShadowPlayer.cs')
$taskLock=Join-Path $taskProject 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $taskLock){$taskLockProbe=[IO.File]::Open($taskLock,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$taskLockProbe.Dispose();Remove-Item -LiteralPath $taskLock}
$taskMode=if($Compatibility){'compat'}else{'rg'};$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskRunPath=Join-Path $PSScriptRoot ('run-'+$taskMode+'-'+$taskStamp+'.json')
$taskSources=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Runtime'),(Join-Path $taskRoot 'Editor') -Recurse -File|Where-Object{$_.Extension -in '.cs','.hlsl','.hlsli','.h','.compute','.shader','.raytrace','.dll','.asmdef','.meta'}|ForEach-Object{@{path=[IO.Path]::GetRelativePath($taskRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$taskIncludes=[IO.Path]::GetFullPath((Join-Path $taskRoot '../lilToon/Assets/lilToon/Shader/Includes'))
$taskSources+=@(Get-ChildItem -LiteralPath $taskIncludes -Recurse -File -Filter '*.hlsl'|ForEach-Object{@{path=[IO.Path]::GetRelativePath($taskRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$taskSourcesFile='sources-'+$taskStamp+'.json';$taskSources|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $PSScriptRoot $taskSourcesFile)
$taskRun=[ordered]@{stage='RTShadow self-caster serialized Player';mode=$taskMode;sourceCommit=(git -C $taskRoot rev-parse HEAD);sourcesFile=$taskSourcesFile;probeSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'HoRTShadowPlayerProbe.cs.txt')).Hash;buildSourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'BuildShadowPlayer.cs.txt')).Hash;activeProcess=$null}
function Wait-TaskChild($taskChild,$taskLimit){$taskStarted=[DateTime]::UtcNow;while(-not $taskChild.WaitForExit(1000)){if(([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt $taskLimit){$taskChild.Kill();$taskChild.WaitForExit();throw 'Owned Shadow Player process exceeded time limit'}};$taskChild.Refresh()}
$taskBuildLog=Join-Path $PSScriptRoot ('build-'+$taskMode+'-'+$taskStamp+'.log')
$taskBuild=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$taskProject+'"'),'-executeMethod',('BuildShadowPlayer.'+$(if($Compatibility){'BuildCompatibility'}else{'Build'})),'-logFile',('"'+$taskBuildLog+'"'))
$taskRun.activeProcess=@{id=$taskBuild.Id;phase='build';log=$taskBuildLog};$taskRun|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $taskRunPath
Write-Output ('START Shadow Player '+$taskMode+' build PID='+$taskBuild.Id);Wait-TaskChild $taskBuild 900
$taskBuildText=[IO.File]::ReadAllText($taskBuildLog);$taskRun.activeProcess=$null;$taskRun.buildExit=$taskBuild.ExitCode;$taskRun.buildLog=[IO.Path]::GetFileName($taskBuildLog);$taskRun.buildLogSha256=(Get-FileHash -LiteralPath $taskBuildLog).Hash
$taskKnown=(Get-Content -LiteralPath (Join-Path $taskRoot 'Tests~/RTGI/Backbuffer/build-warning-review.json') -Raw|ConvertFrom-Json).knownWarningLines
$taskBuildDiagnostics=@([regex]::Matches($taskBuildText,'(?im)^.*(?:Shader error|Shader warning|error CS\d+|d3d12(?: validation)? (?:error|corruption)|Exception:|\bFAIL\b).*$')|ForEach-Object{$_.Value.TrimEnd("`r")})
$taskRun.reviewedExistingBuildWarnings=@($taskBuildDiagnostics|Where-Object{$_ -in $taskKnown});$taskRun.buildDiagnostics=@($taskBuildDiagnostics|Where-Object{$_ -notin $taskKnown})
$taskRun|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskRunPath
if($taskBuild.ExitCode -ne 0 -or -not $taskBuildText.Contains('[HoRTShadowSelfPlayerBuild] PASS') -or $taskRun.buildDiagnostics.Count -ne 0){throw 'Serialized Shadow Player build rejected'}
$taskFolder=Join-Path $taskProject ('PlayerRTShadowSelf'+$(if($Compatibility){'Compatibility'}else{''}));$taskExe=Join-Path $taskFolder 'HoRTShadowProbe.exe'
$taskPlugin=Join-Path $taskFolder 'HoRTShadowProbe_Data/Plugins/x86_64/HoRTDINRD.dll';$taskRun.packedNativeSha256=(Get-FileHash -LiteralPath $taskPlugin).Hash
if($taskRun.packedNativeSha256 -ne (Get-FileHash -LiteralPath (Join-Path $taskRoot 'Runtime/NRD/Plugins/x86_64/HoRTDINRD.dll')).Hash){throw 'Packed NRD library differs'}
$taskPlayerLog=Join-Path $PSScriptRoot ('player-'+$taskMode+'-'+$taskStamp+'.log');$taskReport=Join-Path $PSScriptRoot ('report-'+$taskMode+'-'+$taskStamp+'.json')
$taskArgs=@('-batchmode','-force-d3d12','-force-d3d12-debug','-logFile',('"'+$taskPlayerLog+'"'),'--result',('"'+$taskReport+'"'));if($Compatibility){$taskArgs+='--compat'}
$taskPlayer=Start-Process -FilePath $taskExe -WindowStyle Hidden -PassThru -ArgumentList $taskArgs
$taskRun.activeProcess=@{id=$taskPlayer.Id;phase='player';log=$taskPlayerLog};$taskRun|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskRunPath
Write-Output ('START Shadow Player '+$taskMode+' PID='+$taskPlayer.Id);Wait-TaskChild $taskPlayer 180
$taskPlayerText=[IO.File]::ReadAllText($taskPlayerLog);$taskRun.activeProcess=$null;$taskRun.playerExit=$taskPlayer.ExitCode;$taskRun.playerLog=[IO.Path]::GetFileName($taskPlayerLog);$taskRun.playerLogSha256=(Get-FileHash -LiteralPath $taskPlayerLog).Hash
$taskDiagnostics=@([regex]::Matches($taskPlayerText,'(?im)^.*(?:Shader error|Shader warning|d3d12(?: validation)? (?:error|corruption)|Dispatching ray generation shader.*failed|Exception:|\bFAIL\b|GarbageCollector disposing of GraphicsBuffer).*$')|ForEach-Object{$_.Value.TrimEnd("`r")})
# Match the already retained original cleanup control exactly; never suppress
# different counts or unrelated Player diagnostics.
$taskOldIndex=Get-Content -LiteralPath (Join-Path $taskRoot 'Tests~/RTGI/Backbuffer/log-index.json') -Raw|ConvertFrom-Json
$taskOld=$taskOldIndex.PSObject.Properties|Where-Object{$_.Name -like '*rtgi-step025-player-rg-*'}|Select-Object -First 1
$taskOldRelative='Tests~/RTGI/Backbuffer/'+$taskOld.Value.path;$taskOldPath=Join-Path $taskRoot $taskOldRelative
if((Get-FileHash -LiteralPath $taskOldPath).Hash -ne $taskOld.Value.sha256){throw 'Original cleanup control SHA mismatch'}
$taskCleanup='GarbageCollector disposing of GraphicsBuffer. Please use GraphicsBuffer.Release() or .Dispose() to manually release the buffer.'
$taskCurrentCount=@($taskDiagnostics|Where-Object{$_ -eq $taskCleanup}).Count
$taskOldCount=@([regex]::Matches([IO.File]::ReadAllText($taskOldPath),'(?im)^'+[regex]::Escape($taskCleanup)+'\r?$')).Count
$taskRun.reviewedExistingCleanupWarnings=@()
if($taskCurrentCount -eq 4 -and $taskOldCount -eq 4 -and $taskPlayerText.Contains('[HoRTShadowSelfPlayer] retiredDomains=0 retiredResources=0')){
    $taskDiagnostics=@($taskDiagnostics|Where-Object{$_ -ne $taskCleanup});$taskRun.reviewedExistingCleanupWarnings=@($taskCleanup+' [4 exact occurrences; both host retirement counts zero]');$taskRun.cleanupControl=@{path=$taskOldRelative;sha256=$taskOld.Value.sha256}
}
$taskRun.playerDiagnostics=$taskDiagnostics;$taskRun.playerReport=[IO.Path]::GetFileName($taskReport)
$taskActual=if(Test-Path -LiteralPath $taskReport){Get-Content -LiteralPath $taskReport -Raw|ConvertFrom-Json}else{$null}
$taskRun.pass=$taskPlayer.ExitCode -eq 0 -and $taskDiagnostics.Count -eq 0 -and $taskActual.pass -and $taskPlayerText.Contains('[HoRTShadowSelfPlayer] END pass=True')
$taskRun|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskRunPath
foreach($taskSource in $taskSources){if((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskSource.path)).Hash -ne $taskSource.sha256){throw ('Source drift: '+$taskSource.path)}}
Write-Output ('END Shadow Player '+$taskMode+' exit='+$taskPlayer.ExitCode+' pass='+$taskRun.pass)
if(-not $taskRun.pass){throw 'Shadow Player qualification rejected'}
