param(
    [string]$Config = ""
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
$ScriptPath = $MyInvocation.MyCommand.Path
$Project = Join-Path $RootDir "SVL.Avalonia/SVL.Avalonia.csproj"
$ExecutableName = "SVL.Avalonia"
$ArtifactPrefix = "SVL.Desktop"
$PackageVersion = if ($env:PACKAGE_VERSION) { $env:PACKAGE_VERSION } else { "1.1.8.6" }

$rawBuildConfig = $env:BUILD_CONFIGURATION
$rawPublishConfigEnv = $env:PUBLISH_CONFIG
$publishConfig = if ($env:BUILD_CONFIGURATION) { $env:BUILD_CONFIGURATION } elseif ($env:PUBLISH_CONFIG) { $env:PUBLISH_CONFIG } else { "Release" }
$packageTargets = if ($env:PACKAGE_TARGETS) { $env:PACKAGE_TARGETS } else { "all" }

if ([string]::IsNullOrWhiteSpace($Config) -and [string]::IsNullOrWhiteSpace($rawBuildConfig) -and [string]::IsNullOrWhiteSpace($rawPublishConfigEnv)) {
    Write-Host "[config] no configuration provided, running Debug and Release"
    & $ScriptPath -Config "Debug"
    & $ScriptPath -Config "Release"
    exit 0
}

if (-not [string]::IsNullOrWhiteSpace($Config)) {
    switch -Regex ($Config.ToLowerInvariant()) {
        '^(all|both)$' {
            Write-Host "[config] dual build enabled: Debug and Release"
            & $ScriptPath -Config "Debug"
            & $ScriptPath -Config "Release"
            exit 0
        }
        '^debug$' {
            $publishConfig = "Debug"
        }
        '^release$' {
            $publishConfig = "Release"
        }
        default {
            Write-Host "[error] invalid configuration: $Config"
            Write-Host "[usage] .\scripts\package-avalonia.ps1 [Debug|Release|all]"
            Write-Host '[usage] $env:BUILD_CONFIGURATION=Debug; .\scripts\package-avalonia.ps1'
            exit 1
        }
    }
}

$packageProfile = if ($env:PACKAGE_PROFILE) { $env:PACKAGE_PROFILE } else { $publishConfig }
if (($env:ALLOW_PROFILE_CONFIG_MISMATCH -ne "1") -and ($packageProfile -ne $publishConfig)) {
    Write-Host "[warn] PACKAGE_PROFILE does not match build config; using $publishConfig"
    $packageProfile = $publishConfig
}

Write-Host "[config] VERSION=$PackageVersion PROFILE=$packageProfile BUILD=$publishConfig"
Write-Host "[config] TARGETS=$packageTargets HOST=$([System.Environment]::OSVersion.Platform)"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[error] dotnet not found"
    exit 1
}

$targetLower = $packageTargets.ToLowerInvariant()
$validTargets = @("all", "windows", "macos")
if ($validTargets -notcontains $targetLower) {
    Write-Host "[error] invalid TARGETS: $packageTargets"
    Write-Host '[usage] $env:PACKAGE_TARGETS=all|windows|macos; .\scripts\package-avalonia.ps1 [Debug|Release]'
    exit 1
}

$outBase = Join-Path $RootDir "artifacts/${ArtifactPrefix}_v${PackageVersion}_${packageProfile}"

Write-Host "[clean] $outBase"
if (Test-Path $outBase) {
    Remove-Item -Recurse -Force $outBase
}
New-Item -ItemType Directory -Path $outBase | Out-Null

$configMarker = if ($packageProfile -eq $publishConfig) { $packageProfile } else { "$packageProfile-$publishConfig" }

function Publish-WindowsSingleFile {
    param(
        [string]$Rid,
        [string]$PublishDir
    )

    Write-Host ("[publish] {0} ({1} single-file)" -f $Rid, $publishConfig)
    dotnet publish $Project -c $publishConfig -r $Rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $PublishDir
}

function Invoke-WindowsArch {
    param([string]$Rid)

    $archOut = Join-Path $outBase $Rid
    $publishDir = Join-Path $archOut "publish"
    $archName = $Rid.Substring(4)
    $artifactName = "${ArtifactPrefix}_v${PackageVersion}_${configMarker}_Windows_${archName}"
    $payloadDir = Join-Path $outBase $artifactName
    $zipPath = Join-Path $outBase "${artifactName}.zip"
    $namedExePath = Join-Path $outBase "${artifactName}.exe"

    Publish-WindowsSingleFile -Rid $Rid -PublishDir $publishDir

    if (Test-Path $payloadDir) {
        Remove-Item -Recurse -Force $payloadDir
    }
    New-Item -ItemType Directory -Path $payloadDir | Out-Null
    Copy-Item -Path (Join-Path $publishDir "*") -Destination $payloadDir -Recurse -Force

    $mainExe = Join-Path $payloadDir "${ExecutableName}.exe"
    if (-not (Test-Path $mainExe)) {
        $fallbackExe = Get-ChildItem -Path $payloadDir -Filter *.exe -File | Select-Object -First 1
        if ($null -eq $fallbackExe) {
            Write-Host "[error] no executable found for RID=$Rid"
            exit 1
        }
        $mainExe = $fallbackExe.FullName
    }

    Copy-Item $mainExe (Join-Path $payloadDir "${artifactName}.exe") -Force
    Copy-Item $mainExe $namedExePath -Force

    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }
    Compress-Archive -Path $payloadDir -DestinationPath $zipPath -CompressionLevel Optimal -Force

    Write-Host "[ok] $Rid output:"
    Write-Host "- $payloadDir"
    Write-Host "- $namedExePath"
    Write-Host "- $zipPath"
}

$shouldBuildWindows = $targetLower -eq "all" -or $targetLower -eq "windows"
$shouldBuildMacOS = $targetLower -eq "all" -or $targetLower -eq "macos"

if ($shouldBuildWindows) {
    Invoke-WindowsArch -Rid "win-x64"
}

if ($shouldBuildMacOS) {
    Write-Host "[warn] macOS packaging skipped on Windows host. Use scripts/package-avalonia.sh on macOS."
}

Write-Host "[done] artifacts: $outBase"
Write-Host "[done] sample exe: ${ArtifactPrefix}_v${PackageVersion}_${packageProfile}_Windows_x64.exe"
