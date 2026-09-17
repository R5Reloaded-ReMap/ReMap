[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$RsxRoot,
    [string]$MSBuildPath,

    [ValidateSet("IfMissing", "Always", "Never")]
    [string]$BuildRsx = "IfMissing",

    [switch]$Clean,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ExecutablePath {
    param(
        [string]$Candidate,
        [string]$ExecutableName
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Candidate)
    if (Test-Path -LiteralPath $expanded -PathType Container) {
        $expanded = Join-Path $expanded $ExecutableName
    }

    if (Test-Path -LiteralPath $expanded -PathType Leaf) {
        return [IO.Path]::GetFullPath($expanded)
    }

    return $null
}

function Get-UnityVersion {
    param([string]$ProjectRoot)

    $versionFile = Join-Path $ProjectRoot "ProjectSettings\ProjectVersion.txt"
    $match = Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(.+)$' | Select-Object -First 1
    if ($null -eq $match) {
        throw "Unable to read the Unity version from $versionFile."
    }

    return $match.Matches[0].Groups[1].Value.Trim()
}

function Find-UnityEditor {
    param(
        [string]$RequestedPath,
        [string]$Version
    )

    foreach ($candidate in @($RequestedPath, $env:REMAP_UNITY_PATH, $env:UNITY_EDITOR_PATH)) {
        $resolved = Resolve-ExecutablePath -Candidate $candidate -ExecutableName "Unity.exe"
        if ($null -ne $resolved) {
            return $resolved
        }
    }

    $installRoots = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        $secondaryPathFile = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
        if (Test-Path -LiteralPath $secondaryPathFile -PathType Leaf) {
            try {
                $secondaryPath = Get-Content -LiteralPath $secondaryPathFile -Raw | ConvertFrom-Json
                if ($secondaryPath -is [string] -and -not [string]::IsNullOrWhiteSpace($secondaryPath)) {
                    $installRoots.Add($secondaryPath)
                }
            }
            catch {
                Write-Warning "Could not read Unity Hub's secondary install path: $($_.Exception.Message)"
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $installRoots.Add((Join-Path $env:ProgramFiles "Unity\Hub\Editor"))
    }

    foreach ($root in $installRoots) {
        $candidate = Join-Path $root "$Version\Editor\Unity.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    throw "Unity $Version was not found. Install it with Unity Hub or pass -UnityPath."
}

function Find-MSBuild {
    param([string]$RequestedPath)

    foreach ($candidate in @($RequestedPath, $env:MSBUILD_EXE_PATH)) {
        $resolved = Resolve-ExecutablePath -Candidate $candidate -ExecutableName "MSBuild.exe"
        if ($null -ne $resolved) {
            return $resolved
        }
    }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $vswhereCandidates = @(
        (Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")
    )

    foreach ($vswhere in $vswhereCandidates) {
        if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
            continue
        }

        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($found) -and (Test-Path -LiteralPath $found -PathType Leaf)) {
            return [IO.Path]::GetFullPath($found)
        }
    }

    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw "MSBuild was not found. Install Visual Studio with Desktop development with C++, or pass -MSBuildPath."
}

function Assert-File {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$unityVersion = Get-UnityVersion -ProjectRoot $projectRoot
$resolvedUnity = Find-UnityEditor -RequestedPath $UnityPath -Version $unityVersion

if ([string]::IsNullOrWhiteSpace($RsxRoot)) {
    $RsxRoot = Join-Path $projectRoot "..\rsx"
}
$resolvedRsxRoot = [IO.Path]::GetFullPath($RsxRoot)
$rsxSolution = Join-Path $resolvedRsxRoot "rsx.sln"
$rsxExecutable = Join-Path $resolvedRsxRoot "bin\Release\rsx.exe"
$rsxSessionMarker = "$rsxExecutable.remap-session-v1"
$rsxLicense = Join-Path $resolvedRsxRoot "LICENSE"
$rsxNotices = Join-Path $resolvedRsxRoot "thirdpartylegalnotices.txt"
$needsRsxBuild = -not (Test-Path -LiteralPath $rsxExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $rsxSessionMarker -PathType Leaf)

Assert-File -Path $rsxSolution -Description "The RSX solution"

$shouldBuildRsx = $BuildRsx -eq "Always" -or ($BuildRsx -eq "IfMissing" -and $needsRsxBuild)
$resolvedMSBuild = $null
if ($shouldBuildRsx -or $ValidateOnly) {
    $resolvedMSBuild = Find-MSBuild -RequestedPath $MSBuildPath
}

Write-Host "ReMap project : $projectRoot"
Write-Host "Unity        : $resolvedUnity"
Write-Host "RSX source   : $resolvedRsxRoot"
if ($null -ne $resolvedMSBuild) {
    Write-Host "MSBuild      : $resolvedMSBuild"
}

if ($ValidateOnly) {
    Write-Host "Prerequisite validation completed. No build was started."
    exit 0
}

if ($BuildRsx -eq "Never" -and $needsRsxBuild) {
    throw "RSX is not built. Run without -BuildRsx Never, or build rsx.sln in Release/x64."
}

if ($shouldBuildRsx) {
    Write-Host "Building RSX (Release/x64)..."
    & $resolvedMSBuild $rsxSolution /nologo /m /restore /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "The RSX build failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host "RSX is already available; its build was skipped."
}

Assert-File -Path $rsxExecutable -Description "The RSX executable"
Assert-File -Path $rsxSessionMarker -Description "The ReMap RSX session marker"
Assert-File -Path $rsxLicense -Description "The RSX AGPL license"
Assert-File -Path $rsxNotices -Description "The RSX third-party notices"

$windowsBuildRoot = Join-Path $projectRoot "Builds\Windows"
if ($Clean -and (Test-Path -LiteralPath $windowsBuildRoot)) {
    $resolvedBuildRoot = [IO.Path]::GetFullPath($windowsBuildRoot)
    $expectedPrefix = $projectRoot.TrimEnd('\') + '\Builds\'
    if (-not $resolvedBuildRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected build path: $resolvedBuildRoot"
    }
    Remove-Item -LiteralPath $resolvedBuildRoot -Recurse -Force
}

$logDirectory = Join-Path $projectRoot "Builds\Logs"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$unityLog = Join-Path $logDirectory "unity-build.log"

$previousRsxRoot = $env:REMAP_RSX_ROOT
try {
    $env:REMAP_RSX_ROOT = $resolvedRsxRoot
    Write-Host "Building ReMap for Windows x64..."
    $unityArguments = @(
        "-batchmode",
        "-quit",
        "-projectPath", "`"$projectRoot`"",
        "-executeMethod", "ReMap.Standalone.Editor.ProjectSetup.BuildWindows",
        "-logFile", "`"$unityLog`""
    )
    $unityProcess = Start-Process -FilePath $resolvedUnity -ArgumentList $unityArguments `
        -WindowStyle Hidden -Wait -PassThru
    $unityExitCode = $unityProcess.ExitCode
}
finally {
    if ($null -eq $previousRsxRoot) {
        Remove-Item Env:REMAP_RSX_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:REMAP_RSX_ROOT = $previousRsxRoot
    }
}

if ($unityExitCode -ne 0) {
    Write-Host "Last lines from $unityLog"
    Get-Content -LiteralPath $unityLog -Tail 80
    throw "The Unity build failed with exit code $unityExitCode."
}

foreach ($artifact in @(
    "ReMap.exe",
    "ReMapLiveBridge.exe",
    "rsx.exe",
    "rsx.exe.remap-session-v1",
    "RSX-LICENSE.txt",
    "RSX-THIRD-PARTY-NOTICES.txt"
)) {
    Assert-File -Path (Join-Path $windowsBuildRoot $artifact) -Description "Build artifact $artifact"
}

Write-Host "Build completed: $windowsBuildRoot"
Write-Host "Unity log     : $unityLog"
