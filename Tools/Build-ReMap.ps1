[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$RsxRoot,
    [string]$MSBuildPath,
    [string]$Version,

    [ValidateSet("IfMissing", "Always", "Never")]
    [string]$BuildRsx = "IfMissing",

    [switch]$Clean,
    [switch]$ValidateOnly,
    [switch]$Development,
    [switch]$DeveloperTools,
    [switch]$Interactive
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
$projectSettings = Join-Path $projectRoot "ProjectSettings\ProjectSettings.asset"
$versionMatch = Select-String -LiteralPath $projectSettings -Pattern '^\s*bundleVersion:\s*(.+)$' | Select-Object -First 1
if ($null -eq $versionMatch) {
    throw "Unable to read bundleVersion from $projectSettings."
}
$projectVersion = $versionMatch.Matches[0].Groups[1].Value.Trim()

$interactiveMode = $Interactive -or $PSBoundParameters.Count -eq 0
if ($interactiveMode) {
    Write-Host ""
    Write-Host "ReMap build tool" -ForegroundColor Cyan
    Write-Host "Current application version: $projectVersion"
    Write-Host ""
    Write-Host "  1. Build ReMap locally (optimized)"
    Write-Host "  2. Clean rebuild of RSX and ReMap"
    Write-Host "  3. Validate local prerequisites"
    $publisher = Join-Path $projectRoot "Tools\.local\Publish-ReMap.ps1"
    if (Test-Path -LiteralPath $publisher -PathType Leaf) {
        Write-Host "  4. Package a personal/test build"
        Write-Host "  5. Package a public release"
        Write-Host "  6. Package an optimized beta build"
        Write-Host "  7. Package an optimized release candidate"
    }
    Write-Host "  Q. Cancel"
    Write-Host ""
    $choice = (Read-Host "Choose an action [1]").Trim()
    if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }
    switch ($choice.ToUpperInvariant()) {
        "1" { $BuildRsx = "IfMissing"; $DeveloperTools = $true }
        "2" { $BuildRsx = "Always"; $Clean = $true; $DeveloperTools = $true }
        "3" { $ValidateOnly = $true }
        "4" {
            if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
                throw "The personal release packager is not installed at $publisher."
            }
            & $publisher -Mode Personal
            exit 0
        }
        "5" {
            if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
                throw "The personal release packager is not installed at $publisher."
            }
            & $publisher -Mode Release
            exit 0
        }
        "6" {
            if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
                throw "The personal release packager is not installed at $publisher."
            }
            & $publisher -Mode Beta
            exit 0
        }
        "7" {
            if (-not (Test-Path -LiteralPath $publisher -PathType Leaf)) {
                throw "The personal release packager is not installed at $publisher."
            }
            & $publisher -Mode ReleaseCandidate
            exit 0
        }
        "Q" { exit 0 }
        default { throw "Unknown action: $choice" }
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = if ($Development) { "$projectVersion-dev" } else { $projectVersion }
}
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Application version must use semantic versioning, for example 0.1.0 or 0.1.0-beta.1."
}

$unityVersion = Get-UnityVersion -ProjectRoot $projectRoot
$resolvedUnity = Find-UnityEditor -RequestedPath $UnityPath -Version $unityVersion

if ([string]::IsNullOrWhiteSpace($RsxRoot)) {
    $RsxRoot = Join-Path $projectRoot "..\rsx"
}
$resolvedRsxRoot = [IO.Path]::GetFullPath($RsxRoot)
$rsxSolution = Join-Path $resolvedRsxRoot "rsx.sln"
$rsxExecutable = Join-Path $resolvedRsxRoot "bin\Release\rsx.exe"
$rsxSessionMarker = "$rsxExecutable.remap-session-v1"
$rsxBatchMarker = "$rsxExecutable.remap-session-v2"
$rsxGeometryMarker = "$rsxExecutable.remap-session-v3"
$rsxLicense = Join-Path $resolvedRsxRoot "LICENSE"
$rsxNotices = Join-Path $resolvedRsxRoot "thirdpartylegalnotices.txt"
$needsRsxBuild = -not (Test-Path -LiteralPath $rsxExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $rsxSessionMarker -PathType Leaf) -or
    -not (Test-Path -LiteralPath $rsxBatchMarker -PathType Leaf) -or
    -not (Test-Path -LiteralPath $rsxGeometryMarker -PathType Leaf)

Assert-File -Path $rsxSolution -Description "The RSX solution"

$shouldBuildRsx = $BuildRsx -eq "Always" -or ($BuildRsx -eq "IfMissing" -and $needsRsxBuild)
$resolvedMSBuild = $null
if ($shouldBuildRsx -or $ValidateOnly) {
    $resolvedMSBuild = Find-MSBuild -RequestedPath $MSBuildPath
}

Write-Host "ReMap project : $projectRoot"
Write-Host "App version   : $Version"
Write-Host "Build type    : $(if ($Development) { 'Unity development/debug' } elseif ($DeveloperTools) { 'optimized + developer tools' } else { 'optimized release' })"
Write-Host "Unity         : $resolvedUnity"
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
Assert-File -Path $rsxBatchMarker -Description "The ReMap RSX batch session marker"
Assert-File -Path $rsxGeometryMarker -Description "The ReMap RSX geometry fallback marker"
Assert-File -Path $rsxLicense -Description "The RSX AGPL license"
Assert-File -Path $rsxNotices -Description "The RSX third-party notices"

$windowsBuildFolder = if ($Development) { "Windows-Development" } else { "Windows" }
$windowsBuildRoot = Join-Path $projectRoot "Builds\$windowsBuildFolder"
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
$unityLog = Join-Path $logDirectory $(if ($Development) { "unity-development-build.log" } else { "unity-build.log" })

$previousRsxRoot = $env:REMAP_RSX_ROOT
$previousBuildVersion = $env:REMAP_BUILD_VERSION
$previousDevelopmentBuild = $env:REMAP_DEVELOPMENT_BUILD
$previousDeveloperTools = $env:REMAP_DEVELOPER_TOOLS
$previousBuildOutput = $env:REMAP_BUILD_OUTPUT
try {
    $env:REMAP_RSX_ROOT = $resolvedRsxRoot
    $env:REMAP_BUILD_VERSION = $Version
    $env:REMAP_DEVELOPMENT_BUILD = if ($Development) { "1" } else { "0" }
    $env:REMAP_DEVELOPER_TOOLS = if ($DeveloperTools) { "1" } else { "0" }
    $env:REMAP_BUILD_OUTPUT = Join-Path $windowsBuildRoot "ReMap.exe"
    Write-Host "Building ReMap for Windows x64..."
    $unityArguments = @(
        "-batchmode",
        "-quit",
        "-projectPath", "`"$projectRoot`"",
        "-executeMethod", "ReMap.Standalone.Editor.ProjectSetup.BuildWindows",
        "-logFile", "`"$unityLog`""
    )
    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $resolvedUnity
    $processInfo.Arguments = $unityArguments -join " "
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $unityProcess = [Diagnostics.Process]::new()
    $unityProcess.StartInfo = $processInfo
    if (-not $unityProcess.Start()) { throw "Unity could not be started." }
    $unityProcess.WaitForExit()
    $unityExitCode = $unityProcess.ExitCode
    $unityProcess.Dispose()
}
finally {
    if ($null -eq $previousRsxRoot) {
        Remove-Item Env:REMAP_RSX_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:REMAP_RSX_ROOT = $previousRsxRoot
    }
    if ($null -eq $previousBuildVersion) {
        Remove-Item Env:REMAP_BUILD_VERSION -ErrorAction SilentlyContinue
    }
    else {
        $env:REMAP_BUILD_VERSION = $previousBuildVersion
    }
    if ($null -eq $previousDevelopmentBuild) {
        Remove-Item Env:REMAP_DEVELOPMENT_BUILD -ErrorAction SilentlyContinue
    }
    else {
        $env:REMAP_DEVELOPMENT_BUILD = $previousDevelopmentBuild
    }
    if ($null -eq $previousDeveloperTools) {
        Remove-Item Env:REMAP_DEVELOPER_TOOLS -ErrorAction SilentlyContinue
    }
    else {
        $env:REMAP_DEVELOPER_TOOLS = $previousDeveloperTools
    }
    if ($null -eq $previousBuildOutput) {
        Remove-Item Env:REMAP_BUILD_OUTPUT -ErrorAction SilentlyContinue
    }
    else {
        $env:REMAP_BUILD_OUTPUT = $previousBuildOutput
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
    "rsx.exe.remap-session-v2",
    "rsx.exe.remap-session-v3",
    "RSX-LICENSE.txt",
    "RSX-THIRD-PARTY-NOTICES.txt"
)) {
    Assert-File -Path (Join-Path $windowsBuildRoot $artifact) -Description "Build artifact $artifact"
}

Write-Host "Build completed: $windowsBuildRoot"
Write-Host "Unity log     : $unityLog"
