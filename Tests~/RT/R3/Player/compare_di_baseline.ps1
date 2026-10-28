$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$taskProject=Join-Path $taskRoot '.codex-research/RTValidation'
$taskReference=Join-Path $taskRoot '.codex-research/R3DIPlayerBaselinePackage'
$taskManifestPath=Join-Path $taskProject 'Packages/manifest.json'
if(Test-Path -LiteralPath $taskReference){throw 'Frozen Player reference already exists'}
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
if($LASTEXITCODE -ne 0){throw 'Baseline archive failed'}
$taskOriginal=[IO.File]::ReadAllBytes($taskManifestPath)
$taskStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskRecord=[ordered]@{stage='R3';referenceCommit='66a2bb80';referencePackage=$taskReference;scope='Pre-DI-host-migration full Player control, never a current-source pass';startedUtc=[DateTime]::UtcNow.ToString('o');runnerError=$null}
try{
    $taskManifest=[Text.Encoding]::UTF8.GetString($taskOriginal).TrimStart([char]0xfeff)|ConvertFrom-Json
    $taskManifest.dependencies.'jp.lilxyzw.liltoon.urp.extensions'='file:'+$taskReference.Replace('\','/')
    $taskManifest|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $taskManifestPath -Encoding utf8
    $taskRecord.manifestSha256=(Get-FileHash -LiteralPath $taskManifestPath).Hash
    try{& (Join-Path $PSScriptRoot '../../run_player.ps1') -Mode DI}catch{$taskRecord.runnerError=$_.Exception.Message}
    $taskLast=Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'run-DI-*.json'|Sort-Object LastWriteTime|Select-Object -Last 1
    $taskRun=Get-Content -LiteralPath $taskLast.FullName -Raw|ConvertFrom-Json
    $taskRecord.controlRun=$taskLast.Name;$taskRecord.actualBuildExit=$taskRun.buildExit;$taskRecord.actualPlayerExit=$taskRun.playerExit
    $taskPlayerText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot $taskRun.playerLog))
    $taskRecord.sameMixedProposalFailure=$taskPlayerText.Contains('Player mixed diffuse/specular proposal energy mismatch; temporal=False')
    $taskRecord.playerLogSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $taskRun.playerLog)).Hash
    Write-Output ('Baseline DI Player build='+$taskRun.buildExit+' player='+$taskRun.playerExit+' sameFailure='+$taskRecord.sameMixedProposalFailure)
}finally{
    [IO.File]::WriteAllBytes($taskManifestPath,$taskOriginal)
    $taskRecord|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot ('baseline-DI-'+$taskStamp+'.json')) -Encoding utf8
}
