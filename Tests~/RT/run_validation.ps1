param(
    [ValidateSet('R1','R2','R3DI')][string]$Stage = 'R1',
    [string]$Case = '',
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe',
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskProject = [IO.Path]::GetFullPath((Join-Path $taskRoot '.codex-research/RTValidation'))
$taskIsolatedRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot '.codex-research')) + [IO.Path]::DirectorySeparatorChar
if (-not $taskProject.StartsWith($taskIsolatedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Independent project required' }
if (-not (Test-Path -LiteralPath (Join-Path $taskProject 'Packages/manifest.json'))) { throw 'Initialize the independent validation project first' }
$taskLock = Join-Path $taskProject 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $taskLock) {
    $taskLockProbe = [IO.File]::Open($taskLock, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $taskLockProbe.Dispose()
    Remove-Item -LiteralPath $taskLock
}
$taskCases = @(
    @{ name='scene-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.Run'; marker='[HoRTSceneValidation] PASS' },
    @{ name='scene-compat'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.RunCompatibility'; marker='[HoRTSceneValidation] PASS' },
    @{ name='primary-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTPrimarySurfaceValidation.Run'; marker='[HoRTPrimarySurfaceValidation] PASS' },
    @{ name='primary-compat'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTPrimarySurfaceValidation.RunCompatibility'; marker='[HoRTPrimarySurfaceValidation] PASS' },
    @{ name='skinned-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.RunSkinned'; marker='[HoRTSceneValidation] PASS' },
    @{ name='skinned-compat'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.RunSkinnedCompatibility'; marker='[HoRTSceneValidation] PASS' },
    @{ name='di-cutout-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout'; marker='[HoRTDIValidation] PASS native cutout:' },
    @{ name='di-cutout-compat'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutoutCompatibility'; marker='[HoRTDIValidation] PASS native cutout:' },
    @{ name='gi-secondary-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGISecondaryValidation.RunCutout'; marker='[HoGICutoutHit] PASS' },
    @{ name='scene-unsupported'; api='d3d11'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.RunUnsupported'; marker='[HoRTSceneValidation] PASS unsupported capability branch' }
)
if ($Stage -eq 'R2') {
    $taskCases = @(
        @{ name='host-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTHostValidation.Run'; marker='[HoRTHostValidation] PASS' },
        @{ name='host-compat'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTHostValidation.RunCompatibility'; marker='[HoRTHostValidation] PASS' },
        @{ name='host-retirement-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTHostValidation.RunRetirement'; marker='[HoRTHostValidation] PASS' },
        @{ name='host-retirement-compat'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTHostValidation.RunRetirementCompatibility'; marker='[HoRTHostValidation] PASS' },
        @{ name='host-unsupported'; api='d3d11'; entry='lilToon.URP.Extensions.RT.HoRTHostValidation.RunUnsupported'; marker='[HoRTHostValidation] PASS' },
        @{ name='skinned-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.RunSkinned'; marker='[HoRTSceneValidation] PASS' },
        @{ name='skinned-compat'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.RunSkinnedCompatibility'; marker='[HoRTSceneValidation] PASS' },
        @{ name='di-cutout-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout'; marker='[HoRTDIValidation] PASS native cutout:' },
        @{ name='gi-secondary-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGISecondaryValidation.RunCutout'; marker='[HoGICutoutHit] PASS' }
    )
}
if ($Stage -eq 'R3DI') {
    $taskCases = @(
        @{ name='di-cutout-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout'; marker='[HoRTDIValidation] PASS native cutout:' },
        @{ name='di-cutout-compat'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutoutCompatibility'; marker='[HoRTDIValidation] PASS native cutout:' },
        @{ name='di-denoise-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunDenoise'; marker='[HoRTDIValidation] PASS real NRD' },
        @{ name='di-denoise-compat'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunDenoiseCompatibility'; marker='[HoRTDIValidation] PASS real NRD' },
        @{ name='di-host-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunHost'; marker='[HoRTDIHostValidation] PASS' },
        @{ name='di-host-compat'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunHostCompatibility'; marker='[HoRTDIHostValidation] PASS' },
        @{ name='di-unsupported'; api='d3d11'; entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunUnsupported'; marker='[HoRTDIValidation] PASS unsupported' },
        @{ name='host-rg'; api='d3d12'; entry='lilToon.URP.Extensions.RT.HoRTHostValidation.Run'; marker='[HoRTHostValidation] PASS' },
        @{ name='gi-secondary-rg'; api='d3d12'; entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGISecondaryValidation.RunCutout'; marker='[HoGICutoutHit] PASS' }
    )
    # This legacy full fixture grows AS instances and has a rejected baseline
    # control (F39 plus the existing power-proposal assertion). Preserve it as
    # an explicit diagnostic case, never relabel it as the bounded acceptance.
    if ($Case -in @('di-core-rg','di-core-compat')) {
        $taskCases += @{name=$Case;api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.'+$(if($Case -eq 'di-core-rg'){'Run'}else{'RunCompatibility'});marker='[HoRTDIValidation] PASS'}
    }
}
if ($Case) { $taskCases = @($taskCases | Where-Object { $_.name -eq $Case }); if ($taskCases.Count -eq 0) { throw 'Unknown validation case' } }
$taskResults = Join-Path $PSScriptRoot $(if ($Stage -eq 'R3DI') { 'R3/DI' } else { $Stage })
New-Item -ItemType Directory -Force -Path $taskResults | Out-Null
$taskStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskRunPath = Join-Path $taskResults ('run-' + $taskStamp + '.json')
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Runtime'),(Join-Path $taskRoot 'Editor') -Recurse -File | Where-Object { $_.Extension -in '.cs','.hlsl','.hlsli','.h','.compute','.shader','.raytrace','.asmdef','.dll','.meta' } | ForEach-Object {
    @{path=$_.FullName.Substring($taskRoot.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$taskSourcePath = Join-Path $taskResults ('sources-' + $taskStamp + '.json')
$taskSources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $taskSourcePath -Encoding utf8
$taskRun = [ordered]@{stage=$Stage;sourceCommit=(git -C $taskRoot rev-parse HEAD);startedUtc=[DateTime]::UtcNow.ToString('o');sourcesFile=[IO.Path]::GetFileName($taskSourcePath);runnerSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;activeProcess=$null;entries=@()}
$taskSettingsPath = Join-Path $taskProject 'ProjectSettings/ProjectSettings.asset'
$taskSettingsOriginal = [IO.File]::ReadAllBytes($taskSettingsPath)
$taskSettingsText = [IO.File]::ReadAllText($taskSettingsPath)
try {
foreach ($taskJob in $taskCases) {
    # This existing fixture asserts raw values of [Gamma] shader properties.
    # Its original RTInputValidation project is Gamma; keep that defined domain
    # rather than weakening the numeric assertion or silently converting it.
    $taskColorSpace = if ($taskJob.name.StartsWith('primary-')) { 0 } else { 1 }
    $taskCaseSettings = [regex]::Replace($taskSettingsText, 'm_ActiveColorSpace:\s*\d+', ('m_ActiveColorSpace: '+$taskColorSpace))
    [IO.File]::WriteAllText($taskSettingsPath,$taskCaseSettings,(New-Object Text.UTF8Encoding($false)))
    $taskLog = Join-Path $taskRoot ('.codex-research/ho-rt-' + $Stage.ToLower() + '-' + $taskJob.name + '-' + $taskStamp + '.log')
    $taskArgs = @('-batchmode',('-force-'+$taskJob.api),'-projectPath',('"'+$taskProject+'"'),'-executeMethod',$taskJob.entry,'-logFile',('"'+$taskLog+'"'))
    if ($taskJob.api -eq 'd3d12') { $taskArgs += '-force-d3d12-debug' }
    $taskChild = Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList $taskArgs
    $taskRun.activeProcess = @{id=$taskChild.Id;name=$taskJob.name;log=$taskLog}
    $taskRun | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $taskRunPath -Encoding utf8
    Write-Output ('START ' + $taskJob.name + ' PID=' + $taskChild.Id)
    $taskStarted = [DateTime]::UtcNow
    $taskAbort = $null
    while (-not $taskChild.WaitForExit(1000)) {
        if (([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt $TimeoutSeconds) { $taskAbort='bounded timeout' }
        if ((Test-Path -LiteralPath $taskLog) -and (Select-String -LiteralPath $taskLog -SimpleMatch 'D3D12Fence::Wait' -Quiet)) { $taskAbort='D3D12Fence::Wait' }
        if ($taskAbort) { $taskChild.Kill(); $taskChild.WaitForExit(); break }
    }
    $taskChild.Refresh()
    $taskText = if (Test-Path -LiteralPath $taskLog) { [IO.File]::ReadAllText($taskLog) } else { '' }
    $taskDiagnostics = @([regex]::Matches($taskText, '(?im)^.*(?:Shader error|Shader warning|error CS\d+|d3d12(?: validation)? (?:error|corruption)|Assertion failed|Exception:|\bFAIL\b).*$') | ForEach-Object { $_.Value })
    $taskRecord = [ordered]@{name=$taskJob.name;entry=$taskJob.entry;api=$taskJob.api;colorSpace=if($taskColorSpace -eq 0){'Gamma'}else{'Linear'};actualExit=$taskChild.ExitCode;abort=$taskAbort;marker=$taskText.Contains($taskJob.marker);diagnostics=$taskDiagnostics;logSha256=if(Test-Path -LiteralPath $taskLog){(Get-FileHash -LiteralPath $taskLog).Hash}else{''};seconds=([DateTime]::UtcNow-$taskStarted).TotalSeconds}
    $taskRecord.pass = $taskRecord.actualExit -eq 0 -and -not $taskAbort -and $taskRecord.marker -and $taskDiagnostics.Count -eq 0
    $taskRecord.logFile = [IO.Path]::GetFileName($taskLog)
    $taskRun.entries += $taskRecord
    $taskRun.activeProcess = $null
    $taskRun | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $taskRunPath -Encoding utf8
    Copy-Item -LiteralPath $taskLog -Destination (Join-Path $taskResults ([IO.Path]::GetFileName($taskLog)))
    Write-Output ('END ' + $taskJob.name + ' exit=' + $taskRecord.actualExit + ' marker=' + $taskRecord.marker + ' diagnostics=' + $taskDiagnostics.Count + ' pass=' + $taskRecord.pass)
    if (-not $taskRecord.pass) { throw ('Validation rejected: ' + $taskJob.name + '; inspect ' + $taskRunPath) }
}
foreach ($taskSource in $taskSources) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskRoot $taskSource.path)).Hash -ne $taskSource.sha256) { throw ('Source drift: '+$taskSource.path) }
}
Write-Output ('PASS ' + $Stage + ': ' + $taskCases.Count + ' cases; source fingerprints unchanged; ' + $taskRunPath)
} finally {
    [IO.File]::WriteAllBytes($taskSettingsPath,$taskSettingsOriginal)
}
