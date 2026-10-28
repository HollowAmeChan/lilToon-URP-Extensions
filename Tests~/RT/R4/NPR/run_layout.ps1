param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe',[ValidateSet('Run','RunRenderingAfterBootstrap','RunIntegerRendering','RunReimportRendering')][string]$Entry='Run')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
$taskAssets=Join-Path $taskProject 'Assets/Editor'
New-Item -ItemType Directory -Path $taskAssets -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LayoutAudit.cs.txt') -Destination (Join-Path $taskAssets 'HoRTNprLayoutAudit.cs')
$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskLog=Join-Path $PSScriptRoot ('layout-'+$taskStamp+'.log')
$taskRecord=[ordered]@{scope='Independent hidden ShaderData layout diagnosis only';entry=$Entry;sourceCommit=(git -C $taskRoot rev-parse HEAD);sourceSha256=(Get-FileHash -LiteralPath (Join-Path $taskAssets 'HoRTNprLayoutAudit.cs')).Hash;activeProcess=$null}
$taskRun=Join-Path $PSScriptRoot ('run-'+$taskStamp+'.json')
$taskChild=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$taskProject+'"'),'-executeMethod',('HoRTNprLayoutAudit.'+$Entry),'-logFile',('"'+$taskLog+'"'))
$taskRecord.activeProcess=@{id=$taskChild.Id;log=$taskLog};$taskRecord|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskRun
Write-Output ('START NPR layout audit PID='+$taskChild.Id)
$taskStarted=[DateTime]::UtcNow
while(-not $taskChild.WaitForExit(1000)){if(([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt 480){$taskChild.Kill();$taskChild.WaitForExit();throw 'Owned layout audit exceeded 480 seconds'}}
$taskChild.Refresh();$taskRecord.activeProcess=$null;$taskRecord.exit=$taskChild.ExitCode;$taskRecord.logSha256=(Get-FileHash -LiteralPath $taskLog).Hash
$taskOutput=Join-Path $PSScriptRoot ('layout-'+$taskStamp)
New-Item -ItemType Directory -Path $taskOutput -Force|Out-Null
if($Entry -eq 'Run') {
    Get-ChildItem -LiteralPath (Join-Path $taskProject 'ho-rt-npr-layout') -File -ErrorAction SilentlyContinue|Where-Object{$_.LastWriteTimeUtc -ge $taskStarted}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $taskOutput}
} else {
    $taskRenderReport=Join-Path $taskProject 'ho-rt-shadow/report.json'
    if((Test-Path -LiteralPath $taskRenderReport) -and (Get-Item -LiteralPath $taskRenderReport).LastWriteTimeUtc -ge $taskStarted) {Copy-Item -LiteralPath $taskRenderReport -Destination (Join-Path $taskOutput 'render-report.json')}
}
$taskRecord.output=[IO.Path]::GetFileName($taskOutput);$taskRecord|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskRun
Write-Output ('END NPR layout audit exit='+$taskChild.ExitCode)
if($taskChild.ExitCode -ne 0){throw 'Layout audit failed; evidence retained'}
