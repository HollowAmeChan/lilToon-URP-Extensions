# A bounded pre-migration control for the existing transform-update assertion.
# It switches only the independent project's package reference and restores it.
param([string]$Unity='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
$taskReference=Join-Path $taskRoot '.codex-research/R1BaselinePackage'
$taskManifestPath=Join-Path $taskProject 'Packages/manifest.json'
if(Test-Path -LiteralPath $taskReference){throw 'Reference already exists; do not overwrite a frozen control'}
$env:HO_RT_BASELINE_DEST=$taskReference
@'
import io, os, pathlib, subprocess, zipfile
root=pathlib.Path.cwd()
dest=pathlib.Path(os.environ['HO_RT_BASELINE_DEST'])
assert dest.parent==root/'.codex-research'
blob=subprocess.check_output(['git','archive','--format=zip','bddd8268','package.json','Runtime','Editor','Runtime.meta','Editor.meta'])
with zipfile.ZipFile(io.BytesIO(blob)) as archive:
    for item in archive.infolist():
        target=(dest/item.filename).resolve()
        assert target.is_relative_to(dest.resolve())
    archive.extractall(dest)
print('Prepared exact pre-migration package bddd8268')
'@ | python -
if($LASTEXITCODE -ne 0){throw 'Reference archive failed'}
$taskOriginal=[IO.File]::ReadAllBytes($taskManifestPath)
$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskLog=Join-Path $taskRoot ('.codex-research/ho-rt-r1-baseline-transform-'+$taskStamp+'.log')
$taskRecord=[ordered]@{stage='R1';controlCommit='bddd8268';scope='CPU/GPU pre-migration control; failure is not accepted as a quality pass';activeProcess=$null}
$taskReport=Join-Path $PSScriptRoot ('baseline-control-'+$taskStamp+'.json')
try {
    $taskManifest=[Text.Encoding]::UTF8.GetString($taskOriginal).TrimStart([char]0xfeff)|ConvertFrom-Json
    $taskManifest.dependencies.'jp.lilxyzw.liltoon.urp.extensions'='file:'+$taskReference.Replace('\','/')
    $taskManifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
    $taskArgs=@('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',('"'+$taskProject+'"'),'-executeMethod','lilToon.URP.Extensions.RayTracing.HoRTSceneValidation.Run','-logFile',('"'+$taskLog+'"'))
    $taskChild=Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList $taskArgs
    $taskRecord.activeProcess=@{id=$taskChild.Id;log=$taskLog};$taskRecord|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $taskReport
    Write-Output ('START baseline transform control PID='+$taskChild.Id)
    $taskStart=[DateTime]::UtcNow
    while(-not $taskChild.WaitForExit(1000)) {
        if(([DateTime]::UtcNow-$taskStart).TotalSeconds -gt 600){$taskChild.Kill();$taskChild.WaitForExit();throw 'Baseline control timed out'}
    }
    $taskChild.Refresh();$taskText=[IO.File]::ReadAllText($taskLog)
    $taskRecord.activeProcess=$null
    $taskRecord.actualExit=$taskChild.ExitCode
    $taskRecord.sameAssertion=$taskText.Contains('Exception: distance 0: expected 4, got 3')
    $taskRecord.otherShaderErrors=@([regex]::Matches($taskText,'(?im)^.*(?:Shader error|error CS\d+|d3d12(?: validation)? (?:error|corruption)).*$')|ForEach-Object{$_.Value})
    $taskRecord.logFile=[IO.Path]::GetFileName($taskLog)
    $taskRecord.logSha256=(Get-FileHash -LiteralPath $taskLog).Hash
    $taskRecord.seconds=([DateTime]::UtcNow-$taskStart).TotalSeconds
    $taskRecord|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $taskReport
    Copy-Item -LiteralPath $taskLog -Destination (Join-Path $PSScriptRoot $taskRecord.logFile)
    Write-Output ('END baseline control exit='+$taskRecord.actualExit+' sameAssertion='+$taskRecord.sameAssertion)
    if(-not $taskRecord.sameAssertion -or $taskRecord.otherShaderErrors.Count -ne 0){throw 'Baseline differs; investigate before accepting migration'}
} finally {
    [IO.File]::WriteAllBytes($taskManifestPath,$taskOriginal)
}
