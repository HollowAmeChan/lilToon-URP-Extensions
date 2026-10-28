param(
    [ValidateSet('GI','GICompatibility')][string]$Mode='GI',
    [string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe'
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
$taskLock=Join-Path $taskProject 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $taskLock){$taskCheck=[IO.File]::Open($taskLock,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$taskCheck.Dispose();Remove-Item -LiteralPath $taskLock}
$taskAssets=Join-Path $taskProject 'Assets/RTGIPlayerProbe'
New-Item -ItemType Directory -Path $taskAssets -Force|Out-Null
$taskProbe=[IO.File]::ReadAllText((Join-Path $taskRoot 'Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs'))
$taskProbe='using lilToon.URP.Extensions.RT;'+[Environment]::NewLine+$taskProbe
$taskProbe=$taskProbe.Replace('Application.Quit(report.pass?0:1);',@'
foreach(var host in Resources.FindObjectsOfTypeAll<HoRTRendererFeature>())
{
    var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    Debug.Log("[HoRTPlayerLifetime] before domains="+host.GetType().GetProperty("ActiveDomainCount",flags).GetValue(host)+" views="+host.GetType().GetProperty("ViewCount",flags).GetValue(host));
    host.Dispose();
}
typeof(HoRTRendererFeature).GetMethod("CollectRetired",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
Debug.Log("[HoRTPlayerLifetime] retired="+typeof(HoRTRendererFeature).GetProperty("RetiredDomainCount",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
Application.Quit(report.pass?0:1);
'@)
[IO.File]::WriteAllText((Join-Path $taskAssets 'HoRTGIRuntimeProbe.cs'),$taskProbe,(New-Object Text.UTF8Encoding($false)))
$taskResults=Join-Path $PSScriptRoot 'R3/Player';New-Item -ItemType Directory -Path $taskResults -Force|Out-Null
$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskReportPath=Join-Path $taskResults ('run-'+$Mode+'-'+$taskStamp+'.json')
$taskBuildLog=Join-Path $taskRoot ('.codex-research/ho-rt-r3-player-build-'+$Mode+'-'+$taskStamp+'.log')
$taskSources=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Runtime'),(Join-Path $taskRoot 'Editor') -Recurse -File|Where-Object{$_.Extension -in '.cs','.hlsl','.hlsli','.h','.compute','.shader','.raytrace','.dll','.asmdef','.meta'}|ForEach-Object{@{path=$_.FullName.Substring($taskRoot.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$taskSourcesFile=Join-Path $taskResults ('sources-'+$taskStamp+'.json');$taskSources|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskSourcesFile -Encoding utf8
$taskReport=[ordered]@{stage='R3';mode=$Mode;sourceCommit=(git -C $taskRoot rev-parse HEAD);sourcesFile=[IO.Path]::GetFileName($taskSourcesFile);runtimeProbeSha256=(Get-FileHash -LiteralPath (Join-Path $taskAssets 'HoRTGIRuntimeProbe.cs')).Hash;activeProcess=$null;scope='Independent serialized Mono Player GI/NRD via explicit camera requests; mixed DI/GI is checked separately in Editor'}
function Wait-TaskProcess($taskChild,$taskLimit){
    $taskStarted=[DateTime]::UtcNow
    while(-not $taskChild.WaitForExit(1000)){if(([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt $taskLimit){$taskChild.Kill();$taskChild.WaitForExit();throw 'Owned validation process exceeded its bounded time'}}
    $taskChild.Refresh()
}
$taskBuildEntry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.'+$(if($Mode -eq 'GI'){'Build'}else{'BuildCompatibility'})
$taskBuild=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$taskProject+'"'),'-executeMethod',$taskBuildEntry,'-logFile',('"'+$taskBuildLog+'"'))
$taskReport.activeProcess=@{id=$taskBuild.Id;phase='build';log=$taskBuildLog};$taskReport|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskReportPath
Write-Output ('START '+$Mode+' Player build PID='+$taskBuild.Id)
Wait-TaskProcess $taskBuild 900
$taskText=[IO.File]::ReadAllText($taskBuildLog)
$taskReport.buildExit=$taskBuild.ExitCode;$taskReport.activeProcess=$null
$taskReport.buildLog=[IO.Path]::GetFileName($taskBuildLog);$taskReport.buildLogSha256=(Get-FileHash -LiteralPath $taskBuildLog).Hash
$taskKnown=(Get-Content -LiteralPath (Join-Path $taskRoot 'Tests~/RTGI/Backbuffer/build-warning-review.json') -Raw|ConvertFrom-Json).knownWarningLines
$taskDiagnosticLines=@([regex]::Matches($taskText,'(?im)^.*(?:Shader error|Shader warning|error CS\d+|d3d12(?: validation)? (?:error|corruption)|Exception:|\bFAIL\b).*$')|ForEach-Object{$_.Value.TrimEnd("`r")})
$taskReport.reviewedExistingWarnings=@($taskDiagnosticLines|Where-Object{$_ -in $taskKnown})
$taskReport.buildDiagnostics=@($taskDiagnosticLines|Where-Object{$_ -notin $taskKnown})
Copy-Item -LiteralPath $taskBuildLog -Destination (Join-Path $taskResults $taskReport.buildLog)
$taskReport|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskReportPath
if($taskBuild.ExitCode -ne 0 -or -not $taskText.Contains('[HoGIPlayerBuild] PASS') -or $taskReport.buildDiagnostics.Count -ne 0){throw 'Player build rejected; inspect exact diagnostics'}
$taskFolder=if($Mode -eq 'GI'){'PlayerGIDynamic'}else{'PlayerGIDynamicCompatibility'}
$taskExe=Join-Path $taskProject ($taskFolder+'/HoRTGIProbe.exe')
$taskOutput=Join-Path $taskResults ('report-'+$Mode+'-'+$taskStamp+'.json')
$taskPlayerLog=Join-Path $taskResults ('player-'+$Mode+'-'+$taskStamp+'.log')
$taskPlayer=Start-Process -FilePath $taskExe -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-logFile',('"'+$taskPlayerLog+'"'),'--result',('"'+$taskOutput+'"'))
$taskReport.activeProcess=@{id=$taskPlayer.Id;phase='player';log=$taskPlayerLog};$taskReport|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskReportPath
Write-Output ('START '+$Mode+' Player PID='+$taskPlayer.Id)
Wait-TaskProcess $taskPlayer 120
$taskPlayerText=[IO.File]::ReadAllText($taskPlayerLog)
$taskReport.playerExit=$taskPlayer.ExitCode;$taskReport.activeProcess=$null
$taskReport.playerLog=[IO.Path]::GetFileName($taskPlayerLog);$taskReport.playerLogSha256=(Get-FileHash -LiteralPath $taskPlayerLog).Hash
$taskReport.playerDiagnostics=@([regex]::Matches($taskPlayerText,'(?im)^.*(?:Shader error|Shader warning|d3d12(?: validation)? (?:error|corruption)|Exception:|\bFAIL\b|GarbageCollector disposing of GraphicsBuffer).*$')|ForEach-Object{$_.Value})
$taskOldIndex=Get-Content -LiteralPath (Join-Path $taskRoot 'Tests~/RTGI/Backbuffer/log-index.json') -Raw|ConvertFrom-Json
$taskOld=$taskOldIndex.PSObject.Properties|Where-Object{$_.Name -like '*rtgi-step025-player-rg-*'}|Select-Object -First 1
$taskOldPath=Join-Path $taskRoot ('Tests~/RTGI/Backbuffer/'+$taskOld.Value.path)
if((Get-FileHash -LiteralPath $taskOldPath).Hash -ne $taskOld.Value.sha256){throw 'Historical cleanup control SHA mismatch'}
$taskCleanupLine='GarbageCollector disposing of GraphicsBuffer. Please use GraphicsBuffer.Release() or .Dispose() to manually release the buffer.'
$taskHistoricalCleanup=@([regex]::Matches([IO.File]::ReadAllText($taskOldPath),'(?im)^'+[regex]::Escape($taskCleanupLine)+'\r?$')).Count
$taskCurrentCleanup=@($taskReport.playerDiagnostics|Where-Object{$_.TrimEnd("`r") -eq $taskCleanupLine}).Count
$taskReport.reviewedExistingCleanupWarnings=@()
if($taskHistoricalCleanup -eq 4 -and $taskCurrentCleanup -eq 4 -and $taskPlayerText.Contains('[HoRTPlayerLifetime] retired=0')){
    $taskReport.reviewedExistingCleanupWarnings=@($taskCleanupLine+' [4 exact occurrences; host retired=0; historical SHA verified]')
    $taskReport.cleanupControl=@{path='Tests~/RTGI/Backbuffer/'+$taskOld.Value.path;sha256=$taskOld.Value.sha256}
    $taskReport.playerDiagnostics=@($taskReport.playerDiagnostics|Where-Object{$_.TrimEnd("`r") -ne $taskCleanupLine})
}
$taskActual=if(Test-Path -LiteralPath $taskOutput){Get-Content -LiteralPath $taskOutput -Raw|ConvertFrom-Json}else{$null}
$taskReport.playerResult=[IO.Path]::GetFileName($taskOutput)
$taskReport.pass=$taskPlayer.ExitCode -eq 0 -and $taskReport.playerDiagnostics.Count -eq 0 -and $taskActual.pass
$taskReport|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskReportPath
foreach($taskSource in $taskSources){if((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskSource.path)).Hash -ne $taskSource.sha256){throw ('Product source drift: '+$taskSource.path)}}
Write-Output ('END '+$Mode+' Player exit='+$taskPlayer.ExitCode+' pass='+$taskReport.pass)
if(-not $taskReport.pass){throw 'Player qualification rejected'}
