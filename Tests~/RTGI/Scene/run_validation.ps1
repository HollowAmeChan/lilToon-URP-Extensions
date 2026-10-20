param([switch]$Stability,[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$active=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($projectRoot.Replace('\','/'))};if($active){throw 'GI project already active'}
$results=Join-Path $PSScriptRoot 'results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$jobs=@(@{label='128-rg';entry='Run'},@{label='128-compat';entry='RunCompatibility'},@{label='256-rg';entry='Run256'},@{label='256-compat';entry='Run256Compatibility'})
if($Stability){$jobs=@(@{label='256-rg-stability';entry='RunStability'},@{label='256-compat-stability';entry='RunStabilityCompatibility'})}
$run=[ordered]@{step=10;startedUtc=[DateTime]::UtcNow.ToString('o');head=(& git -C $workspaceRoot rev-parse HEAD).Trim();builderSha256=(Get-FileHash (Join-Path $workspaceRoot 'Editor/RTGI/HoRTGICastleValidation.cs')).Hash;dependenciesSha256=(Get-FileHash (Join-Path $PSScriptRoot 'dependencies.json')).Hash;activeProcess=$null;entries=@()}
$runPath=Join-Path $PSScriptRoot $(if($Stability){'stability-run.json'}else{'run.json'})
foreach($job in $jobs){
    $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step010-'+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
    $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.'+$job.entry),'-logFile',('"'+$log+'"'))
    $run.activeProcess=@{id=$p.Id;entry=$job.entry;label=$job.label;log=$log};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
    $p.WaitForExit();$report=Join-Path $projectRoot ('ho-gi-castle-'+$job.label+'/report.json')
    $record=[ordered]@{label=$job.label;exitCode=$p.ExitCode;log=$log;logSha256=(Get-FileHash -LiteralPath $log).Hash;marker=@(Select-String -LiteralPath $log -SimpleMatch '[HoGICastle] PASS').Count -gt 0;fresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started;shaderCsDiagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count}
    if($record.fresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.pass=$data.pass;$record.reportSha256=(Get-FileHash -LiteralPath $report).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
    $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$record.pass)
    if($p.ExitCode -ne 0 -or -not $record.marker -or -not $record.fresh -or -not $record.pass -or $record.shaderCsDiagnostics -ne 0){throw ('Castle GI validation failed: '+$job.label)}
}
