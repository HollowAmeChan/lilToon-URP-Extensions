param([string]$UnityApi='C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Data/PluginAPI')
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'));$project=Join-Path $workspaceRoot '.codex-research/RTGIValidation'
$active=Get-CimInstance Win32_Process -Filter "Name='Unity.exe'"|Where-Object{$_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($project.Replace('\','/'))};if($active){throw 'GI project active; do not overwrite a loaded test DLL'}
$build=Join-Path $workspaceRoot '.codex-research/RTGIGpuProbeBuild';$plugins=Join-Path $project 'Assets/Plugins/x86_64'
New-Item -ItemType Directory -Force -Path $build,$plugins|Out-Null
$source=Join-Path $PSScriptRoot 'Native/HoGIGpuProbe.cpp';$output=Join-Path $plugins 'HoGIGpuProbe.dll'
$batch=Join-Path $build 'build.cmd'
$lines=@('@echo off','call "D:\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul','if errorlevel 1 exit /b 1',('cl /nologo /utf-8 /std:c++17 /EHsc /MD /O2 /LD /I "'+$UnityApi+'" "'+$source+'" /Fo"'+(Join-Path $build 'HoGIGpuProbe.obj')+'" /Fe"'+$output+'" /link d3d12.lib dxgi.lib /IMPLIB:"'+(Join-Path $build 'HoGIGpuProbe.lib')+'"'),'exit /b %errorlevel%')
[IO.File]::WriteAllLines($batch,$lines,[Text.Encoding]::ASCII)
& $env:ComSpec /d /c ('"'+$batch+'"')
if($LASTEXITCODE -ne 0){throw 'Test timestamp bridge compilation failed'}
@{sourceSha256=(Get-FileHash -LiteralPath $source).Hash;dll=$output;dllSha256=(Get-FileHash -LiteralPath $output).Hash;unityApi=$UnityApi;compiler='VS2022 14.44.35207 x64';builtUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'native-build.json')
