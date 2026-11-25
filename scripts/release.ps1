<#
.SYNOPSIS
    Builds the SDS release packages: the server and the sample web client, both self-contained.

.DESCRIPTION
    For every runtime identifier the script publishes Sds.Server and Sds.Web as self-contained
    applications (the .NET runtime is included, nothing has to be installed on the target
    machine), then packs each one into its own ZIP file:

        artifacts\release\<Version>\SdsServer-<Version>-<Runtime>.zip
        artifacts\release\<Version>\SdsWeb-<Version>-<Runtime>.zip
        artifacts\release\<Version>\SHA256SUMS.txt

    The server package also carries SharpPXE (the network boot server, git submodule in
    external\SharpPXE) in its SharpPXE folder: SharpPXE.exe (Native AOT), SharpPXE.Gui.exe
    (self-contained single file), SharpPXE.ini and files\. SharpPXE is 64-bit only, so it is
    left out of win-x86 packages.

    Steps:
      1. Check the .NET SDK and clear the output folder of this version.
      2. Run the test suite (skip with -SkipTests).
      3. Publish each project for each runtime into a staging folder, and SharpPXE into the
         SharpPXE folder of the server package.
      4. Remove development only files and add LICENSE and README.md.
      5. Zip each staging folder and write SHA256 checksums.

    Works with Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER Version
    Version stamped on the assemblies and used in the package names. Defaults to the latest
    git tag (a leading "v" is removed), or 1.0.0 when there is no tag.

.PARAMETER Runtime
    Runtime identifiers to build. Defaults to win-x64. SDS needs Windows (wimgapi.dll, NTFS
    ACLs, SMB shares), so only win-* identifiers are accepted.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputDirectory
    Root folder of the packages. Defaults to artifacts\release at the repository root.

.PARAMETER SkipSharpPXE
    Do not add SharpPXE to the server package. Native AOT needs the Visual Studio
    "Desktop development with C++" workload (and the ARM64 C++ tools for win-arm64); use this
    switch on a machine without them.

.PARAMETER SkipTests
    Do not run dotnet test before packing. The imaging and API tests capture WIM files with
    wimgapi, which needs administrator privileges: run the script from an elevated prompt, or
    the tests fail with Win32 error 1314 and no package is built.

.EXAMPLE
    .\scripts\release.ps1 -Version 1.2.0

.EXAMPLE
    .\scripts\release.ps1 -Version 1.2.0 -Runtime win-x64, win-arm64 -SkipTests

.EXAMPLE
    .\scripts\release.ps1 -Version 1.2.0 -SkipSharpPXE
#>
[CmdletBinding()]
param(
    [string] $Version,

    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string[]] $Runtime = @('win-x64'),

    [string] $Configuration = 'Release',

    [string] $OutputDirectory,

    [switch] $SkipTests,

    [switch] $SkipSharpPXE
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot

# Projects to pack: package name prefix and project path.
$projects = @(
    @{ Name = 'SdsServer'; Path = Join-Path $repoRoot 'src\Sds.Server\Sds.Server.csproj' },
    @{ Name = 'SdsWeb';    Path = Join-Path $repoRoot 'src\Sds.Web\Sds.Web.csproj' }
)

# SharpPXE (git submodule), packed into the server package.
$sharpPxeRoot = Join-Path $repoRoot 'external\SharpPXE'
$sharpPxeServer = Join-Path $sharpPxeRoot 'src\SharpPXE\SharpPXE.csproj'
$sharpPxeGui = Join-Path $sharpPxeRoot 'src\SharpPXE.Gui\SharpPXE.Gui.csproj'
$sharpPxeRuntimes = @('win-x64', 'win-arm64')

# Files that only make sense on a developer machine.
$developmentFiles = @('appsettings.Development.json')

function Invoke-Native {
    # Runs an external command and stops the script when it fails.
    param([string] $FilePath, [string[]] $Arguments)

    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Get-DefaultVersion {
    # Latest git tag such as v1.2.0, else 1.0.0.
    if (Get-Command git -ErrorAction SilentlyContinue) {
        # No tag makes git write to stderr and exit non zero; that is not an error here.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $tag = git -C $repoRoot describe --tags --abbrev=0 2>$null
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $previous

        if ($exitCode -eq 0 -and $tag) {
            return ([string]$tag).Trim().TrimStart('v', 'V')
        }
    }

    return '1.0.0'
}

function Publish-SharpPXE {
    # Same output as SharpPXE's own build.ps1, except that the GUI is self-contained like the
    # rest of the release (no .NET Desktop Runtime needed). SharpPXE keeps its own version.
    param([string] $Rid, [string] $Destination)

    Write-Host "Publishing SharpPXE ($Rid)" -ForegroundColor Cyan
    Invoke-Native dotnet @('publish', $sharpPxeServer, '-c', $Configuration, '-r', $Rid, '-o', $Destination, '--nologo')
    Invoke-Native dotnet @(
        'publish', $sharpPxeGui,
        '-c', $Configuration,
        '-r', $Rid,
        '--self-contained', 'true',
        '-o', $Destination,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '--nologo'
    )

    # Debug symbols and leftovers are not needed to run (as in SharpPXE's build.ps1).
    Get-ChildItem $Destination -Filter *.pdb | Remove-Item -Force
    Get-ChildItem $Destination -Filter *.staticwebassets.endpoints.json | Remove-Item -Force

    Copy-Item (Join-Path $sharpPxeRoot 'LICENSE') $Destination
    Copy-Item (Join-Path $sharpPxeRoot 'README.md') $Destination
}

# ---------------------------------------------------------------------
# Step 1: environment and output folder
# ---------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found on PATH.'
}

if (-not $Version) {
    $Version = Get-DefaultVersion
}

# Assembly versions must be numeric; a suffix such as -beta.1 is allowed in the file version text.
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid version (for example 1.2.0 or 1.2.0-beta.1)."
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\release'
}

$releaseDirectory = Join-Path $OutputDirectory $Version
$stagingRoot = Join-Path $repoRoot "artifacts\staging\$Version"

foreach ($dir in @($releaseDirectory, $stagingRoot)) {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

if (-not $SkipSharpPXE) {
    # A clone without --recurse-submodules has an empty external\SharpPXE.
    if (-not (Test-Path $sharpPxeServer)) {
        Write-Host 'Fetching the SharpPXE submodule' -ForegroundColor Cyan
        Invoke-Native git @('-C', $repoRoot, 'submodule', 'update', '--init', 'external/SharpPXE')
    }

    # Native AOT's linker setup calls vswhere.exe, which is not always on PATH.
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if ((Test-Path $installer) -and -not (($env:PATH -split ';') -contains $installer)) {
        $env:PATH = "$installer;$env:PATH"
    }
}

Write-Host "SDS release $Version ($Configuration) for $($Runtime -join ', ')" -ForegroundColor Cyan

# ---------------------------------------------------------------------
# Step 2: tests
# ---------------------------------------------------------------------
if ($SkipTests) {
    Write-Host 'Skipping tests.' -ForegroundColor Yellow
}
else {
    Invoke-Native dotnet @('test', (Join-Path $repoRoot 'Sds.sln'), '-c', $Configuration, '--nologo')
}

# ---------------------------------------------------------------------
# Steps 3 and 4: publish and tidy each package
# ---------------------------------------------------------------------
$packages = @()
foreach ($rid in $Runtime) {
    foreach ($project in $projects) {
        $packageName = "$($project.Name)-$Version-$rid"
        $staging = Join-Path $stagingRoot $packageName

        Write-Host "Publishing $packageName" -ForegroundColor Cyan
        Invoke-Native dotnet @(
            'publish', $project.Path,
            '-c', $Configuration,
            '-r', $rid,
            '--self-contained', 'true',
            '-o', $staging,
            "-p:Version=$Version",
            '--nologo'
        )

        foreach ($file in $developmentFiles) {
            $path = Join-Path $staging $file
            if (Test-Path $path) {
                Remove-Item $path -Force
            }
        }

        Copy-Item (Join-Path $repoRoot 'LICENSE') $staging
        Copy-Item (Join-Path $repoRoot 'README.md') $staging

        if ($project.Name -eq 'SdsServer' -and -not $SkipSharpPXE) {
            if ($sharpPxeRuntimes -notcontains $rid) {
                Write-Warning "SharpPXE is 64-bit only; $packageName does not include it."
            }
            else {
                Publish-SharpPXE -Rid $rid -Destination (Join-Path $staging 'SharpPXE')
            }
        }

        $packages += @{ Name = $packageName; Staging = $staging }
    }
}

# ---------------------------------------------------------------------
# Step 5: zip and checksums
# ---------------------------------------------------------------------
# ZipFile handles files over 2 GB and is much faster than Compress-Archive on 5.1.
Add-Type -AssemblyName System.IO.Compression.FileSystem

$checksums = @()
foreach ($package in $packages) {
    $zip = Join-Path $releaseDirectory "$($package.Name).zip"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($package.Staging, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksums += "$hash  $(Split-Path $zip -Leaf)"
}

# Plain ASCII and LF line endings so sha256sum -c can read the file too.
[System.IO.File]::WriteAllText((Join-Path $releaseDirectory 'SHA256SUMS.txt'), (($checksums -join "`n") + "`n"), [System.Text.Encoding]::ASCII)

Remove-Item $stagingRoot -Recurse -Force

Write-Host ''
Write-Host "Packages in $releaseDirectory" -ForegroundColor Green
Get-ChildItem $releaseDirectory | ForEach-Object {
    Write-Host ("  {0,-40} {1,10:N1} MB" -f $_.Name, ($_.Length / 1MB))
}
