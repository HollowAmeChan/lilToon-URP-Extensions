param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe',[string]$CaptureCli='C:/Program Files/RenderDoc/renderdoccmd.exe',[ValidateSet('Official','Mcp')][string]$Backend='Mcp')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
if($Backend -eq 'Mcp'){$env:PATH='D:/Unity_Fork/renderdoc-mcp/bin;'+$env:PATH;$CaptureCli=(Get-Command renderdoc-cli -ErrorAction Stop).Source}
if(-not(Test-Path -LiteralPath $CaptureCli)){throw 'RenderDoc capture CLI is unavailable'}
$taskAssets=Join-Path $taskProject 'Assets/Editor'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CaptureForward.cs.txt') -Destination (Join-Path $taskAssets 'HoRTNprCaptureForward.cs')
$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskLog=Join-Path $PSScriptRoot ('capture-forward-'+$taskStamp+'.log')
$taskCapture=Join-Path $taskRoot ('.codex-research/RTValidation/npr-forward-'+$taskStamp)
$taskArgs=@('capture','-w','-c',('"'+$taskCapture+'"'),('"'+$Unity+'"'),'-batchmode','-force-d3d12','-projectPath',('"'+$taskProject+'"'),'-executeMethod','HoRTNprCaptureForward.Run','-logFile',('"'+$taskLog+'"'))
if($Backend -eq 'Mcp'){
    $taskUnityArgs='-batchmode -force-d3d12 -projectPath '+$taskProject+' -executeMethod HoRTNprCaptureForward.Run -logFile '+$taskLog
    $taskArgs=@('capture',('"'+$Unity+'"'),'-a',('"'+$taskUnityArgs+'"'),'-d','1000','-o',('"'+$taskCapture+'.rdc"'))
}
$taskChild=Start-Process -FilePath $CaptureCli -WindowStyle Hidden -PassThru -ArgumentList $taskArgs -RedirectStandardOutput (Join-Path $PSScriptRoot ('launcher-'+$taskStamp+'.log')) -RedirectStandardError (Join-Path $PSScriptRoot ('launcher-error-'+$taskStamp+'.log'))
$taskRecord=[ordered]@{scope='Independent hidden forward draw capture; RT feature and casters disabled';backend=$Backend;captureCli=$CaptureCli;sourceCommit=(git -C $taskRoot rev-parse HEAD);sourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'CaptureForward.cs.txt')).Hash;activeProcess=@{id=$taskChild.Id;log=$taskLog};captureTemplate=$taskCapture}
$taskRecordPath=Join-Path $PSScriptRoot ('capture-'+$taskStamp+'.json');$taskRecord|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskRecordPath
Write-Output ('START hidden RenderDoc forward capture PID='+$taskChild.Id)
$taskStarted=[DateTime]::UtcNow
while(-not $taskChild.WaitForExit(1000)){if(([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt 240){throw 'Capture launcher still running after bounded observation; inspect owned process before stopping or retrying'}}
$taskChild.Refresh();$taskRecord.activeProcess=$null;$taskRecord.launcherExit=$taskChild.ExitCode
# The CLI may time out while Unity is still compiling. Observe only the exact
# child launched for this fixture; a frontend timeout is not native completion.
$taskTargets=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($taskChild.Id)"|Where-Object{$_.Name -eq 'Unity.exe' -and $_.CommandLine.Contains($taskProject) -and $_.CommandLine.Contains('HoRTNprCaptureForward.Run')})
foreach($taskTarget in $taskTargets){
    $taskNative=Get-Process -Id $taskTarget.ProcessId -ErrorAction SilentlyContinue
    if($null -eq $taskNative){continue}
    $taskRecord.activeProcess=@{id=$taskNative.Id;phase='native capture after launcher exit';log=$taskLog};$taskRecord|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $taskRecordPath
    while(-not $taskNative.WaitForExit(1000)){if(([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt 240){throw 'Owned native capture still live; keep this handle and inspect before retrying'}}
}
$taskRecord.activeProcess=$null
if(Test-Path -LiteralPath $taskLog){$taskRecord.logSha256=(Get-FileHash -LiteralPath $taskLog).Hash}
$taskRecord.captures=@(Get-ChildItem -LiteralPath $taskProject -Filter ('npr-forward-'+$taskStamp+'*.rdc')|ForEach-Object{@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;bytes=$_.Length}})
$taskRecord|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $taskRecordPath
Write-Output ('END RenderDoc launcher exit='+$taskChild.ExitCode+' captures='+$taskRecord.captures.Count)
if($taskRecord.captures.Count -eq 0){throw 'No frame capture; retain log and use bounded fallback diagnosis'}
