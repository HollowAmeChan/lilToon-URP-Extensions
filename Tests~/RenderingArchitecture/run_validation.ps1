param([ValidateSet('backend','small','gi','giCompat','composite','combined','combinedCompat','playerBuild')][string]$Mode='backend')
$ErrorActionPreference='Stop'
$archRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$archProject=Join-Path $archRoot '.codex-research/RTGIValidation'
$lock=Join-Path $archProject 'Temp/UnityLockfile'
if(Test-Path -LiteralPath $lock){$probe=[IO.File]::Open($lock,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$probe.Dispose();Remove-Item -LiteralPath $lock}
$entry=@{playerBuild='lilToon.URP.Extensions.Editor.RTGI.HoRTGIPlayerValidation.Build';combined='lilToon.URP.Extensions.Editor.NrdArchitecture.CombinedValidation.Run';combinedCompat='lilToon.URP.Extensions.Editor.NrdArchitecture.CombinedValidation.RunCompatibility';backend='lilToon.URP.Extensions.Editor.NrdArchitecture.ArchitectureValidation.Run';small='lilToon.URP.Extensions.Editor.RTGI.HoRTGICastleValidation.RunSharedInputSmall';gi='lilToon.URP.Extensions.Editor.RTGI.HoRTGINrdValidation.Run';giCompat='lilToon.URP.Extensions.Editor.RTGI.HoRTGINrdValidation.RunCompatibility';composite='lilToon.URP.Extensions.Editor.RTGI.HoRTGICompositeValidation.Run'}[$Mode]
$stamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff');$log=Join-Path $archRoot ('.codex-research/nrd-architecture-'+$Mode+'-'+$stamp+'.log')
$snapshot=Join-Path $PSScriptRoot ($Mode+'-'+$stamp);New-Item -ItemType Directory -Force -Path $snapshot|Out-Null
$files=@(Get-ChildItem (Join-Path $archRoot 'Runtime'),(Join-Path $archRoot 'Editor') -Recurse -File|Where-Object{$_.Extension -in '.cs','.compute','.hlsl','.hlsli','.asmdef'});$files+=@(Get-ChildItem (Join-Path $archProject 'Assets/HoNrdArchitecture') -File|Where-Object{$_.Extension -in '.cs','.compute','.asmref'})
$files+=@(Get-ChildItem (Join-Path $archProject 'Assets/RTGIPlayerProbe') -File -ErrorAction SilentlyContinue|Where-Object{$_.Extension -eq '.cs'})
$sources=@(foreach($f in $files){@{path=$f.FullName.Substring($archRoot.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $f.FullName).Hash}})
$run=@{mode=$Mode;entry=$entry;log=$log;startedUtc=[DateTime]::UtcNow.ToString('o');sources=$sources;activeProcess=$null}
$runPath=Join-Path $snapshot 'run.json';$run|ConvertTo-Json -Depth 8|Set-Content $runPath
$p=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-force-d3d12-debug','-projectPath',$archProject,'-executeMethod',$entry,'-logFile',$log)
$run.activeProcess=$p.Id;$run|ConvertTo-Json -Depth 8|Set-Content $runPath;Write-Output ($Mode+' PID '+$p.Id)
$started=[DateTime]::UtcNow;$abort=$null
while(-not $p.WaitForExit(1000)){if((Test-Path $log)-and (Select-String -Path $log -SimpleMatch 'D3D12Fence::Wait')){$abort='fence wait';break};if(([DateTime]::UtcNow-$started).TotalSeconds -gt 600){$abort='bounded timeout';break}}
if($abort){$p.Kill();$p.WaitForExit()};$run.activeProcess=$null;$run.exitCode=$p.ExitCode;$run.abort=$abort;$run.sourceDrift=@($sources|Where-Object{(Get-FileHash -LiteralPath (Join-Path $archRoot $_.path)).Hash -ne $_.sha256}).Count
$run.logSha256=(Get-FileHash $log).Hash;$run.diagnostics=@(Select-String -Path $log -Pattern 'error CS|Shader error|Shader warning|D3D12Fence::Wait|d3d12( validation)? (error|corruption)|Compute shader .*Property|Attempting to bind texture|UAV usage flag').Count
Copy-Item -LiteralPath $log -Destination (Join-Path $snapshot 'unity.log');$run|ConvertTo-Json -Depth 8|Set-Content $runPath
$reports=@{playerBuild='PlayerGIDynamic';combined='nrd-architecture-combined-rg';combinedCompat='nrd-architecture-combined-compat';backend='nrd-architecture';small='ho-gi-shared-small';gi='ho-gi-nrd-rg';giCompat='ho-gi-nrd-compat';composite='ho-gi-composite-rg'}
$outputDir=Join-Path $archProject $reports[$Mode]
if(Test-Path $outputDir){Get-ChildItem $outputDir -File|Where-Object{$_.LastWriteTimeUtc -ge $started -and $_.Extension -notin '.exe','.dll','.pdb'}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $snapshot}}
Write-Output ($Mode+' actual exit '+$p.ExitCode+' diagnostics='+$run.diagnostics+' drift='+$run.sourceDrift+' snapshot='+$snapshot)
if($p.ExitCode -ne 0 -or $abort -or $run.diagnostics -or $run.sourceDrift){throw 'Architecture validation rejected; preserve exact run before retry'}
