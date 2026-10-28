param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
$taskReference=Join-Path $taskRoot '.codex-research/R3DIBaselinePackage'
$taskManifestPath=Join-Path $taskProject 'Packages/manifest.json'
$taskLock=Join-Path $taskProject 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $taskLock){$taskCheck=[IO.File]::Open($taskLock,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$taskCheck.Dispose();Remove-Item -LiteralPath $taskLock}
if(Test-Path -LiteralPath $taskReference){throw 'Frozen reference already exists; inspect before reuse'}
$env:HO_RT_BASELINE_DEST=$taskReference
@'
import io,os,pathlib,subprocess,zipfile
root=pathlib.Path.cwd();dest=pathlib.Path(os.environ['HO_RT_BASELINE_DEST'])
assert dest.parent==root/'.codex-research'
blob=subprocess.check_output(['git','archive','--format=zip','66a2bb80','package.json','Runtime','Editor','Runtime.meta','Editor.meta'])
with zipfile.ZipFile(io.BytesIO(blob)) as z:
    assert all((dest/n.filename).resolve().is_relative_to(dest.resolve()) for n in z.infolist())
    z.extractall(dest)
'@ | python -
if($LASTEXITCODE -ne 0){throw 'Reference archive failed'}
$taskOriginal=[IO.File]::ReadAllBytes($taskManifestPath)
$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskLog=Join-Path $taskRoot ('.codex-research/ho-rt-r3di-baseline-core-'+$taskStamp+'.log')
$taskReport=Join-Path $PSScriptRoot ('baseline-core-'+$taskStamp+'.json')
try{
    $taskManifest=[Text.Encoding]::UTF8.GetString($taskOriginal).TrimStart([char]0xfeff)|ConvertFrom-Json
    $taskManifest.dependencies.'jp.lilxyzw.liltoon.urp.extensions'='file:'+$taskReference.Replace('\','/')
    $taskManifest|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
    $taskChild=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$taskProject+'"'),'-executeMethod','lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.Run','-logFile',('"'+$taskLog+'"'))
    @{stage='R3-DI';baseCommit='66a2bb80';activeProcess=@{id=$taskChild.Id;log=$taskLog}}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskReport
    Write-Output ('START pre-migration DI core PID='+$taskChild.Id)
    $taskStart=[DateTime]::UtcNow
    while(-not $taskChild.WaitForExit(1000)){if(([DateTime]::UtcNow-$taskStart).TotalSeconds -gt 600){$taskChild.Kill();$taskChild.WaitForExit();throw 'Control timeout'}}
    $taskChild.Refresh();$taskText=[IO.File]::ReadAllText($taskLog)
    $taskResult=[ordered]@{stage='R3-DI';baseCommit='66a2bb80';activeProcess=$null;actualExit=$taskChild.ExitCode;powerProposalFailure=$taskText.Contains('Power proposal did not follow the new bright light');asScratchDiagnostics=@([regex]::Matches($taskText,'d3d12 validation error.*BuildRaytracingAccelerationStructure')).Count;logFile=[IO.Path]::GetFileName($taskLog);logSha256=(Get-FileHash -LiteralPath $taskLog).Hash;scope='Historical full core fixture remains rejected; not a migration acceptance run'}
    $taskResult|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskReport
    Copy-Item -LiteralPath $taskLog -Destination (Join-Path $PSScriptRoot $taskResult.logFile)
    Write-Output ('END baseline exit='+$taskChild.ExitCode+' proposalFailure='+$taskResult.powerProposalFailure+' asDiagnostics='+$taskResult.asScratchDiagnostics)
}finally{[IO.File]::WriteAllBytes($taskManifestPath,$taskOriginal)}
