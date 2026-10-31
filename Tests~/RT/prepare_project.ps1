param(
    [string]$UnityVersion = '6000.3.15f1',
    [string]$ProjectName = 'RTValidation'
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskResearchRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot '.codex-research')) + [IO.Path]::DirectorySeparatorChar
if ([string]::IsNullOrWhiteSpace($ProjectName) -or [IO.Path]::GetFileName($ProjectName) -ne $ProjectName) {
    throw 'ProjectName must be a single directory name.'
}
$taskProject = [IO.Path]::GetFullPath((Join-Path $taskResearchRoot $ProjectName))
if (-not $taskProject.StartsWith($taskResearchRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Independent project must stay under .codex-research.'
}
if (Test-Path -LiteralPath $taskProject) {
    Write-Output ('Existing isolated project kept: ' + $taskProject)
    return
}
$taskPeers = [IO.Path]::GetFullPath((Join-Path $taskRoot '..'))
$taskLocal = @{
    'com.unity.render-pipelines.universal' = (Join-Path $taskPeers 'HoUrp17.3.0')
    'com.unity.render-pipelines.universal-config' = (Join-Path $taskPeers 'HoUrpConfig17.0.3')
    'jp.lilxyzw.liltoon' = (Join-Path $taskPeers 'lilToon/Assets/lilToon')
    'jp.lilxyzw.liltoon.urp.extensions' = $taskRoot
}
$taskDependencies = [ordered]@{}
foreach ($taskName in $taskLocal.Keys) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskLocal[$taskName] 'package.json'))) { throw ('Missing local package: ' + $taskLocal[$taskName]) }
    $taskDependencies[$taskName] = 'file:' + $taskLocal[$taskName].Replace('\','/')
}
foreach ($taskModule in @('imgui','jsonserialize','ui','uielements','imageconversion','unitywebrequest','terrain','physics','animation','particlesystem','audio','screencapture')) {
    $taskDependencies['com.unity.modules.'+$taskModule] = '1.0.0'
}
New-Item -ItemType Directory -Path (Join-Path $taskProject 'Assets'),(Join-Path $taskProject 'Packages'),(Join-Path $taskProject 'ProjectSettings') | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'Bootstrap/Assets/*') -Destination (Join-Path $taskProject 'Assets') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Bootstrap/ProjectSettings/GraphicsSettings.asset'),
    (Join-Path $PSScriptRoot 'Bootstrap/ProjectSettings/QualitySettings.asset') -Destination (Join-Path $taskProject 'ProjectSettings')
@{dependencies=$taskDependencies} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskProject 'Packages/manifest.json') -Encoding utf8
('m_EditorVersion: '+$UnityVersion) | Set-Content -LiteralPath (Join-Path $taskProject 'ProjectSettings/ProjectVersion.txt') -Encoding utf8
@'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  m_ActiveColorSpace: 1
'@ | Set-Content -LiteralPath (Join-Path $taskProject 'ProjectSettings/ProjectSettings.asset') -Encoding utf8
Write-Output ('Created isolated RT validation project with its URP bootstrap: ' + $taskProject)
