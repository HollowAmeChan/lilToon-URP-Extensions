param([switch]$Initialize)
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$snapshotRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidationPackage'
$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$active=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($projectRoot.Replace('\','/'))}
if($active){throw 'GI validation project is active; do not modify its package snapshot'}
$base=(& git -C $workspaceRoot rev-parse HEAD).Trim()
if($Initialize){
    if(Test-Path -LiteralPath $snapshotRoot){throw 'Snapshot already exists; inspect provenance instead of overwriting'}
    $zip=Join-Path $workspaceRoot '.codex-research/rtgi-validation-package.zip'
    & git -C $workspaceRoot archive --format=zip --output=$zip HEAD
    if($LASTEXITCODE -ne 0){throw 'git archive failed'}
    Expand-Archive -LiteralPath $zip -DestinationPath $snapshotRoot
    [IO.File]::WriteAllText((Join-Path $snapshotRoot 'snapshot-base.txt'),$base)
}
if(-not(Test-Path -LiteralPath (Join-Path $snapshotRoot 'snapshot-base.txt'))){throw 'Initialize snapshot first'}
# The existing ShadowCast contract validator matches LF-only numeric defines.
# git archive emits this old tracked header with CRLF; keep the isolated copy's
# line endings consistent with the working checkout without editing production.
$contract='Runtime/ShadowCast/Shaders/HoShadowCastShaderContract.hlsl'
$contractPath=Join-Path $snapshotRoot $contract
[IO.File]::WriteAllText($contractPath,([IO.File]::ReadAllText($contractPath).Replace("`r`n","`n")))
$selected=@('Runtime/RTGI','Editor/RTGI','Runtime/RTDI/NRD/HoRTDINrdContext.cs','Runtime/RTDI/NRD/HoRTDINrdNative.cs')
$records=@()
foreach($relative in $selected){
    $source=Join-Path $workspaceRoot $relative
    $files=if(Test-Path -LiteralPath $source -PathType Container){Get-ChildItem -LiteralPath $source -File -Recurse}else{Get-Item -LiteralPath $source}
    foreach($file in $files){
        $path=[IO.Path]::GetRelativePath($workspaceRoot,$file.FullName)
        $destination=Join-Path $snapshotRoot $path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))|Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $records+=@{path=$path.Replace('\','/');sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash}
    }
}
$manifestPath=Join-Path $projectRoot 'Packages/manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$original=$manifest.dependencies.'jp.lilxyzw.liltoon.urp.extensions'
$manifest.dependencies.'jp.lilxyzw.liltoon.urp.extensions'='file:'+($snapshotRoot.Replace('\','/'))
$manifest|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $manifestPath
@{baseCommit=([IO.File]::ReadAllText((Join-Path $snapshotRoot 'snapshot-base.txt')));snapshot=$snapshotRoot;project=$projectRoot;originalManifestPackage=$original;updatedUtc=[DateTime]::UtcNow.ToString('o');lineEndingNormalization=@{path=$contract;operation='CRLF to LF';sha256=(Get-FileHash -LiteralPath $contractPath -Algorithm SHA256).Hash};overlays=$records}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'snapshot.json')
Write-Output ('GI validation snapshot: '+$snapshotRoot+' / '+$records.Count+' overlays')
