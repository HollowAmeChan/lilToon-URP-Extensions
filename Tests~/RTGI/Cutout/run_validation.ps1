param([switch]$Skinned,[string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$caseRoot=if($Skinned){Join-Path $PSScriptRoot '../Skinned'}else{$PSScriptRoot}
if($Skinned){
    $snapshotName='RTGIValidationSkinnedPackage'
    $initialize=-not(Test-Path -LiteralPath (Join-Path $workspaceRoot ('.codex-research/'+$snapshotName)))
    & (Join-Path $PSScriptRoot '../NRD/snapshot_validation.ps1') -Initialize:$initialize -SnapshotName $snapshotName -MetadataPath (Join-Path $caseRoot 'snapshot.json') -AdditionalSources @('Runtime/RayTracing')
}else{& (Join-Path $PSScriptRoot '../NRD/snapshot_validation.ps1') -MetadataPath (Join-Path $PSScriptRoot 'snapshot.json') -AdditionalSources @('Runtime/RayTracing')}
$settingsPath=Join-Path $projectRoot 'ProjectSettings/ProjectSettings.asset'
$original=[IO.File]::ReadAllBytes($settingsPath);$text=[IO.File]::ReadAllText($settingsPath)
$results=Join-Path $caseRoot 'results';New-Item -ItemType Directory -Force -Path $results|Out-Null
$jobs=foreach($color in @('Linear','Gamma')){
    @{label="$color-hit";color=$color;entry='RTGI.HoRTGISecondaryValidation.RunCutout';report='ho-gi-cutout-hit/report.json';marker='[HoGICutoutHit] PASS'}
    @{label="$color-full-rg";color=$color;entry='RTGI.HoRTGIEnvironmentValidation.RunCutout';report='ho-gi-cutout-rg/report.json';marker='[HoGICutout] PASS'}
    @{label="$color-full-compat";color=$color;entry='RTGI.HoRTGIEnvironmentValidation.RunCutoutCompatibility';report='ho-gi-cutout-compat/report.json';marker='[HoGICutout] PASS'}
}
$jobs+=@{label='Linear-opaque-environment-rg';color='Linear';entry='RTGI.HoRTGIEnvironmentValidation.Run';report='ho-gi-secondary-environment-rg/report.json';marker='[HoGIEnvironment] END pass=True'}
$jobs+=@{label='Gamma-opaque-environment-compat';color='Gamma';entry='RTGI.HoRTGIEnvironmentValidation.RunCompatibility';report='ho-gi-secondary-environment-compat/report.json';marker='[HoGIEnvironment] END pass=True'}
$jobs+=@{label='Linear-opaque-secondary';color='Linear';entry='RTGI.HoRTGISecondaryValidation.Run';marker='[HoRTGISecondaryValidation] PASS real DXR'}
$jobs+=@{label='Linear-di-cutout-regression';color='Linear';entry='RTDI.HoRTDIValidation.RunCutout';marker='[HoRTDIValidation] PASS native cutout:'}
if($Skinned){
    $jobs=foreach($color in @('Linear','Gamma')){
        @{label="$color-hit";color=$color;entry='RTGI.HoRTGISecondaryValidation.RunSkinned';report='ho-gi-skinned-hit/report.json';marker='[HoGISkinnedHit] PASS'}
        @{label="$color-full-rg";color=$color;entry='RTGI.HoRTGIEnvironmentValidation.RunSkinned';report='ho-gi-skinned-rg/report.json';marker='[HoGISkinnedLighting] PASS'}
        @{label="$color-full-compat";color=$color;entry='RTGI.HoRTGIEnvironmentValidation.RunSkinnedCompatibility';report='ho-gi-skinned-compat/report.json';marker='[HoGISkinnedLighting] PASS'}
    }
}
$run=[ordered]@{step=$(if($Skinned){9}else{8});startedUtc=[DateTime]::UtcNow.ToString('o');project=$projectRoot;snapshotSha256=(Get-FileHash -LiteralPath (Join-Path $caseRoot 'snapshot.json') -Algorithm SHA256).Hash;activeProcess=$null;entries=@()}
$runPath=Join-Path $results 'run.json'
try{
    foreach($job in $jobs){
        $value=if($job.color -eq 'Linear'){1}else{0};[IO.File]::WriteAllText($settingsPath,[regex]::Replace($text,'m_ActiveColorSpace: [01]',('m_ActiveColorSpace: '+$value)))
        $started=[DateTime]::UtcNow;$log=Join-Path $workspaceRoot ('.codex-research/rtgi-step'+$(if($Skinned){'009-'}else{'008-'})+$job.label+'-'+$started.ToString('yyyyMMddTHHmmssfff')+'.log')
        $p=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod',('lilToon.URP.Extensions.Editor.'+$job.entry),'-logFile',('"'+$log+'"'))
        $run.activeProcess=@{id=$p.Id;entry=$job.entry;label=$job.label;log=$log;startedUtc=$started.ToString('o')};$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' PID '+$p.Id)
        $p.WaitForExit();$pass=@(Select-String -LiteralPath $log -SimpleMatch $job.marker).Count -gt 0;$diagnostics=@(Select-String -LiteralPath $log -Pattern 'Shader error|Shader warning|error CS').Count
        $record=[ordered]@{label=$job.label;entry=$job.entry;colorSpace=$job.color;exitCode=$p.ExitCode;marker=$pass;shaderCsDiagnostics=$diagnostics;log=$log;logSha256=(Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash}
        if($job.report){
            $report=Join-Path $projectRoot $job.report;$record.reportFresh=(Test-Path -LiteralPath $report) -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
            if($record.reportFresh){$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$record.reportPass=$data.pass -and $data.colorSpace -eq $job.color;$record.reportSha256=(Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash;Copy-Item -LiteralPath $report -Destination (Join-Path $results ($job.label+'.json'))}
            if(-not $record.reportFresh -or -not $record.reportPass){$pass=$false}
        }
        $run.entries+=$record;$run.activeProcess=$null;$run|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $runPath;Write-Output ($job.label+' exit '+$p.ExitCode+' pass='+$pass+' diagnostics='+$diagnostics)
        if($p.ExitCode -ne 0 -or -not $pass -or $diagnostics -ne 0){throw ('GI Cutout validation failed: '+$job.label)}
    }
}finally{[IO.File]::WriteAllBytes($settingsPath,$original)}
