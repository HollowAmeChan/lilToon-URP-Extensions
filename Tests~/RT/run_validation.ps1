param(
    [ValidateSet('R1','R2','R3DI','R3GI','R4Query','R4','R5Sigma','R5Cast','R5Npr','R5Export','R5Thin','R6','Controls','Spatial')][string]$Stage = 'R1',
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
if ($Stage -eq 'R3GI') {
    $taskCases = @(
        @{name='gi-host-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIValidation.RunHost';marker='[HoRTGIHostValidation] PASS'},
        @{name='gi-host-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIValidation.RunHostCompatibility';marker='[HoRTGIHostValidation] PASS'},
        @{name='gi-environment-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIEnvironmentValidation.Run';marker='[HoGIEnvironment] END pass=True'},
        @{name='gi-environment-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIEnvironmentValidation.RunCompatibility';marker='[HoGIEnvironment] END pass=True'},
        @{name='gi-nrd-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGINrdValidation.Run';marker='[HoGINrd] END pass=True'},
        @{name='gi-nrd-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGINrdValidation.RunCompatibility';marker='[HoGINrd] END pass=True'},
        @{name='gi-unsupported';api='d3d11';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIValidation.RunUnsupported';marker='[HoRTGIValidation] PASS unsupported'},
        @{name='gi-native-cutout';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGISecondaryValidation.RunCutout';marker='[HoGICutoutHit] PASS'},
        @{name='di-cutout-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout';marker='[HoRTDIValidation] PASS native cutout:'}
    )
}
if ($Stage -eq 'R4Query') {
    $taskCases = @(
        @{name='native-query-rg';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTVisibilityValidation.Run';marker='[HoRTVisibilityValidation] PASS'},
        @{name='native-query-compat';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTVisibilityValidation.RunCompatibility';marker='[HoRTVisibilityValidation] PASS'},
        @{name='di-cutout-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout';marker='[HoRTDIValidation] PASS native cutout:'},
        @{name='di-cutout-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutoutCompatibility';marker='[HoRTDIValidation] PASS native cutout:'},
        @{name='gi-native-cutout';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGISecondaryValidation.RunCutout';marker='[HoGICutoutHit] PASS'},
        @{name='scene-rg';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTSceneValidation.Run';marker='[HoRTSceneValidation] PASS'}
    )
}
if ($Stage -eq 'R4') {
    $taskCases = @(
        @{name='shadow-raw-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRaw';marker='[HoRTShadowValidation] PASS scope=raw-only; NPR acceptance pending'},
        @{name='shadow-raw-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRawCompatibility';marker='[HoRTShadowValidation] PASS scope=raw-only; NPR acceptance pending'},
        @{name='primary-rg';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTPrimarySurfaceValidation.Run';marker='[HoRTPrimarySurfaceValidation] PASS'},
        @{name='primary-compat';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTPrimarySurfaceValidation.RunCompatibility';marker='[HoRTPrimarySurfaceValidation] PASS'},
        @{name='host-retirement-rg';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTHostValidation.RunRetirement';marker='[HoRTHostValidation] PASS'},
        @{name='host-retirement-compat';api='d3d12';entry='lilToon.URP.Extensions.RT.HoRTHostValidation.RunRetirementCompatibility';marker='[HoRTHostValidation] PASS'},
        @{name='gi-host-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIValidation.RunHost';marker='[HoRTGIHostValidation] PASS'},
        @{name='di-cutout-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout';marker='[HoRTDIValidation] PASS native cutout:'}
    )
    # Complete NPR acceptance remains rejected. Explicit diagnosis only until
    # actual material/ramp behavior is qualified; never a raw-stage pass.
    if($Case -in @('shadow-rg','shadow-compat','raster-fallback')) {
        $taskCases+=@{name=$Case;api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.'+$(if($Case -eq 'raster-fallback'){'RunRasterFallback'}elseif($Case -eq 'shadow-compat'){'RunCompatibility'}else{'Run'});marker=$(if($Case -eq 'raster-fallback'){'[HoRTShadowRasterFallback] PASS'}else{'[HoRTShadowValidation] PASS scope=NPR'})}
    }
}
if($Stage -eq 'R5Sigma') {
    $taskCases=@(
        @{name='sigma-independent';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowSigmaValidation.Run';marker='[HoRTShadowSigmaValidation] PASS'},
        @{name='di-relax-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunDenoise';marker='[HoRTDIValidation] PASS real NRD'},
        @{name='di-relax-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunDenoiseCompatibility';marker='[HoRTDIValidation] PASS real NRD'},
        @{name='gi-relax-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGINrdValidation.Run';marker='[HoGINrd] END pass=True'},
        @{name='gi-relax-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGINrdValidation.RunCompatibility';marker='[HoGINrd] END pass=True'}
    )
}
if($Stage -eq 'R5Cast') {
    $taskCases=@(
        @{name='shadow-sigma-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSigma';marker='[HoRTShadowProductionSigma] PASS'},
        @{name='shadow-sigma-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSigmaCompatibility';marker='[HoRTShadowProductionSigma] PASS'},
        @{name='shadow-raw-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRaw';marker='[HoRTShadowValidation] PASS scope=raw-only; NPR acceptance pending'},
        @{name='shadow-raw-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRawCompatibility';marker='[HoRTShadowValidation] PASS scope=raw-only; NPR acceptance pending'},
        @{name='sigma-independent';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowSigmaValidation.Run';marker='[HoRTShadowSigmaValidation] PASS'}
    )
}
if($Stage -eq 'R5Npr') {
    $taskCases=@(
        @{name='npr-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunNpr';marker='[HoRTShadowNprValidation] PASS'},
        @{name='npr-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunNprCompatibility';marker='[HoRTShadowNprValidation] PASS'},
        @{name='raster-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRasterRegression';marker='[HoRTShadowNprRasterRegression] PASS'},
        @{name='raster-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRasterRegressionCompatibility';marker='[HoRTShadowNprRasterRegression] PASS'},
        @{name='raster-d3d11';api='d3d11';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRasterRegression';marker='[HoRTShadowNprRasterRegression] PASS'},
        @{name='shadow-sigma-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSigma';marker='[HoRTShadowProductionSigma] PASS'},
        @{name='shadow-sigma-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSigmaCompatibility';marker='[HoRTShadowProductionSigma] PASS'},
        @{name='di-cutout-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunCutout';marker='[HoRTDIValidation] PASS native cutout:'},
        @{name='gi-native-cutout';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGISecondaryValidation.RunCutout';marker='[HoGICutoutHit] PASS'}
    )
}
if($Stage -eq 'R5Export') {
    $taskCases=@(
        @{name='export-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSingleFrame';marker='[HoRTShadowSingleFrame] PASS'},
        @{name='export-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSingleFrameCompatibility';marker='[HoRTShadowSingleFrame] PASS'},
        @{name='npr-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunNpr';marker='[HoRTShadowNprValidation] PASS'},
        @{name='npr-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunNprCompatibility';marker='[HoRTShadowNprValidation] PASS'},
        @{name='shadow-sigma-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSigma';marker='[HoRTShadowProductionSigma] PASS'},
        @{name='shadow-raw-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunRaw';marker='[HoRTShadowValidation] PASS scope=raw-only; NPR acceptance pending'}
    )
}
if($Stage -eq 'R5Thin') {
    $taskCases=@(
        @{name='thin-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunThin';marker='[HoRTShadowThin] PASS'},
        @{name='thin-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunThinCompatibility';marker='[HoRTShadowThin] PASS'},
        @{name='export-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSingleFrame';marker='[HoRTShadowSingleFrame] PASS'},
        @{name='gi-host-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTGI.HoRTGIValidation.RunHost';marker='[HoRTGIHostValidation] PASS'}
    )
}
if($Stage -eq 'R6') {
    $taskCases=@(
        @{name='multi-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunMulti';marker='[HoRTShadowMulti] PASS'},
        @{name='multi-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunMultiCompatibility';marker='[HoRTShadowMulti] PASS'},
        @{name='npr-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunNpr';marker='[HoRTShadowNprValidation] PASS'},
        @{name='npr-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunNprCompatibility';marker='[HoRTShadowNprValidation] PASS'},
        @{name='shadow-sigma-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSigma';marker='[HoRTShadowProductionSigma] PASS'},
        @{name='export-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSingleFrame';marker='[HoRTShadowSingleFrame] PASS'}
    )
}
if($Stage -eq 'Controls') {
    $taskCases=@(
        @{name='controls-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunControls';marker='[HoRTShadowControls] PASS'},
        @{name='controls-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunControlsCompatibility';marker='[HoRTShadowControls] PASS'},
        @{name='multi-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunMulti';marker='[HoRTShadowMulti] PASS'},
        @{name='multi-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunMultiCompatibility';marker='[HoRTShadowMulti] PASS'}
    )
}
if($Stage -eq 'Spatial') {
    $taskCases=@(
        @{name='spatial-rg';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSpatial';marker='[HoRTShadowSpatial] PASS'},
        @{name='spatial-compat';api='d3d12';entry='lilToon.URP.Extensions.Editor.RTShadow.HoRTShadowValidation.RunSpatialCompatibility';marker='[HoRTShadowSpatial] PASS'}
    )
}
if ($Case) { $taskCases = @($taskCases | Where-Object { $_.name -eq $Case }); if ($taskCases.Count -eq 0) { throw 'Unknown validation case' } }
$taskResults = Join-Path $PSScriptRoot $(if ($Stage -eq 'R3DI') { 'R3/DI' } elseif ($Stage -eq 'R3GI') { 'R3/GI' } elseif ($Stage -eq 'R4Query') { 'R4/Query' } elseif($Stage -eq 'R5Sigma') {'R5'} elseif($Stage -eq 'R5Cast') {'R5/Production'} elseif($Stage -eq 'R5Npr') {'R5/NPR'} elseif($Stage -eq 'R5Export') {'R5/Export'} elseif($Stage -eq 'R5Thin') {'R5/Thin'} else { $Stage })
New-Item -ItemType Directory -Force -Path $taskResults | Out-Null
$taskStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$taskRunPath = Join-Path $taskResults ('run-' + $taskStamp + '.json')
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Runtime'),(Join-Path $taskRoot 'Editor') -Recurse -File | Where-Object { $_.Extension -in '.cs','.hlsl','.hlsli','.h','.compute','.shader','.raytrace','.asmdef','.dll','.meta' } | ForEach-Object {
    @{path=$_.FullName.Substring($taskRoot.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$taskSourcePath = Join-Path $taskResults ('sources-' + $taskStamp + '.json')
if($Stage -in @('R5Npr','R5Export','R5Thin','R6','Controls','Spatial')) {
    $taskSharedIncludes=[IO.Path]::GetFullPath((Join-Path $taskRoot '../lilToon/Assets/lilToon/Shader/Includes'))
    $taskSources+=@(Get-ChildItem -LiteralPath $taskSharedIncludes -Recurse -File -Filter '*.hlsl'|ForEach-Object{@{path=[IO.Path]::GetRelativePath($taskRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
}
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
    $taskDiagnostics = @([regex]::Matches($taskText, '(?im)^.*(?:Shader error|Shader warning|error CS\d+|d3d12(?: validation)? (?:error|corruption)|Dispatching ray generation shader.*failed|Assertion failed|Exception:|\bFAIL\b).*$') | ForEach-Object { $_.Value })
    $taskRecord = [ordered]@{name=$taskJob.name;entry=$taskJob.entry;api=$taskJob.api;colorSpace=if($taskColorSpace -eq 0){'Gamma'}else{'Linear'};actualExit=$taskChild.ExitCode;abort=$taskAbort;marker=$taskText.Contains($taskJob.marker);diagnostics=$taskDiagnostics;logSha256=if(Test-Path -LiteralPath $taskLog){(Get-FileHash -LiteralPath $taskLog).Hash}else{''};seconds=([DateTime]::UtcNow-$taskStarted).TotalSeconds}
    $taskRecord.pass = $taskRecord.actualExit -eq 0 -and -not $taskAbort -and $taskRecord.marker -and $taskDiagnostics.Count -eq 0
    $taskRecord.logFile = [IO.Path]::GetFileName($taskLog)
    $taskRun.entries += $taskRecord
    $taskRun.activeProcess = $null
    $taskRun | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $taskRunPath -Encoding utf8
    Copy-Item -LiteralPath $taskLog -Destination (Join-Path $taskResults ([IO.Path]::GetFileName($taskLog)))
    if ($taskJob.name.StartsWith('native-query-')) {
        $taskQueryReport=Join-Path $taskProject 'ho-rt-visibility/report.json'
        if(Test-Path -LiteralPath $taskQueryReport){Copy-Item -LiteralPath $taskQueryReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
    }
    if ($taskJob.name.StartsWith('shadow-')) {
        $taskShadowReport=Join-Path $taskProject 'ho-rt-shadow/report.json'
        if((Test-Path -LiteralPath $taskShadowReport) -and (Get-Item -LiteralPath $taskShadowReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskShadowReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
        if($taskJob.name.StartsWith('shadow-sigma-')) {
            foreach($taskSignal in @('reference','raw','sigma')) {foreach($taskExtension in @('png','rgba32')) {
                $taskImage=Join-Path $taskProject ('ho-rt-shadow/'+$taskSignal+'.'+$taskExtension)
                if((Test-Path -LiteralPath $taskImage) -and (Get-Item -LiteralPath $taskImage).LastWriteTimeUtc -ge $taskStarted) {
                    Copy-Item -LiteralPath $taskImage -Destination (Join-Path $taskResults ($taskJob.name+'-'+$taskStamp+'-'+$taskSignal+'.'+$taskExtension))
                }
            }}
        }
    }
    if($taskJob.name -eq 'sigma-independent') {
        $taskSigmaReport=Join-Path $taskProject 'ho-rt-sigma/report.json'
        if((Test-Path -LiteralPath $taskSigmaReport) -and (Get-Item -LiteralPath $taskSigmaReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskSigmaReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
    }
    if($taskJob.name.StartsWith('npr-')) {
        $taskNprReport=Join-Path $taskProject 'ho-rt-shadow/report.json'
        if((Test-Path -LiteralPath $taskNprReport) -and (Get-Item -LiteralPath $taskNprReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskNprReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
        foreach($taskSignal in @('narrow','wide')) {foreach($taskExtension in @('png','rgba32')) {
            $taskImage=Join-Path $taskProject ('ho-rt-shadow/npr-'+$taskSignal+'.'+$taskExtension)
            if((Test-Path -LiteralPath $taskImage) -and (Get-Item -LiteralPath $taskImage).LastWriteTimeUtc -ge $taskStarted) {Copy-Item -LiteralPath $taskImage -Destination (Join-Path $taskResults ($taskJob.name+'-'+$taskStamp+'-'+$taskSignal+'.'+$taskExtension))}
        }}
    }
    if($taskJob.name.StartsWith('export-')) {
        $taskExportReport=Join-Path $taskProject 'ho-rt-shadow/single-frame-report.json'
        if((Test-Path -LiteralPath $taskExportReport) -and (Get-Item -LiteralPath $taskExportReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskExportReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
        Get-ChildItem -LiteralPath (Join-Path $taskProject 'ho-rt-shadow') -Filter 'export-*' -File |Where-Object{$_.LastWriteTimeUtc -ge $taskStarted}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskResults ($taskJob.name+'-'+$taskStamp+'-'+$_.Name))}
    }
    if($taskJob.name.StartsWith('thin-')) {
        $taskThinReport=Join-Path $taskProject 'ho-rt-shadow/thin-report.json'
        if((Test-Path -LiteralPath $taskThinReport) -and (Get-Item -LiteralPath $taskThinReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskThinReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
        Get-ChildItem -LiteralPath (Join-Path $taskProject 'ho-rt-shadow') -Filter 'thin-*' -File |Where-Object{$_.LastWriteTimeUtc -ge $taskStarted}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskResults ($taskJob.name+'-'+$taskStamp+'-'+$_.Name))}
    }
    if($taskJob.name.StartsWith('multi-')) {
        $taskMultiReport=Join-Path $taskProject 'ho-rt-shadow/multi-report.json'
        if((Test-Path -LiteralPath $taskMultiReport) -and (Get-Item -LiteralPath $taskMultiReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskMultiReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
    }
    if($taskJob.name.StartsWith('controls-')) {
        $taskControlsReport=Join-Path $taskProject 'ho-rt-shadow/report.json'
        if((Test-Path -LiteralPath $taskControlsReport) -and (Get-Item -LiteralPath $taskControlsReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskControlsReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
        Get-ChildItem -LiteralPath (Join-Path $taskProject 'ho-rt-shadow') -Filter 'controls-*' -File |Where-Object{$_.LastWriteTimeUtc -ge $taskStarted}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskResults ($taskJob.name+'-'+$taskStamp+'-'+$_.Name))}
    }
    if($taskJob.name.StartsWith('spatial-')) {
        $taskSpatialReport=Join-Path $taskProject 'ho-rt-shadow/spatial-report.json'
        if((Test-Path -LiteralPath $taskSpatialReport) -and (Get-Item -LiteralPath $taskSpatialReport).LastWriteTimeUtc -ge $taskStarted){Copy-Item -LiteralPath $taskSpatialReport -Destination (Join-Path $taskResults ('report-'+$taskJob.name+'-'+$taskStamp+'.json'))}
        Get-ChildItem -LiteralPath (Join-Path $taskProject 'ho-rt-shadow') -Filter 'spatial-*' -File |Where-Object{$_.LastWriteTimeUtc -ge $taskStarted}|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskResults ($taskJob.name+'-'+$taskStamp+'-'+$_.Name))}
    }
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
