$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$projectRoot=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
& (Join-Path $PSScriptRoot 'build_native.ps1')
$editor=Join-Path $projectRoot 'Assets/Editor';New-Item -ItemType Directory -Force -Path $editor|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConfigureEditorPlugin.cs') -Destination (Join-Path $editor 'HoGIGpuPluginConfiguration.cs')
$log=Join-Path $workspaceRoot ('.codex-research/rtgi-gpu-plugin-config-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')+'.log')
$p=Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe' -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-force-d3d12','-projectPath',('"'+$projectRoot+'"'),'-executeMethod','HoGIGpuPluginConfiguration.Configure','-logFile',('"'+$log+'"'))
$p.WaitForExit();if($p.ExitCode -ne 0){throw ('Editor-only timestamp importer configuration failed: '+$log)}
Write-Output 'Test-only timestamp bridge compiled and restricted to the Windows Editor'
