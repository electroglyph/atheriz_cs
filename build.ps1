#Requires -Version 5.1
<#
.SYNOPSIS
    Build webclient (only if webclient/src changed) + .NET engine.

.DESCRIPTION
    PowerShell port of build.cmd. Mirrors its incremental logic:
    standalone, webclient required. Computes a hash of webclient/src
    plus config, skips the vite build when nothing changed, stages
    webclient/dist into src/Atheriz.Server/wwwroot, then builds .NET.

    Runs on Windows PowerShell 5.1 and PowerShell 7+.

.PARAMETER Force
    Force rebuild of webclient even if unchanged. Aliases: -f.
    The double-dash form --force binds to the same switch.

.PARAMETER Help
    Show usage. Aliases: -h. The double-dash form --help binds to
    the same switch.

.EXAMPLE
    .\build.ps1
    Incremental build (skips vite when webclient is unchanged).

.EXAMPLE
    .\build.ps1 -Force
    Force a webclient rebuild, then build .NET.

.NOTES
    House rule for this script: only Get-WebclientHash and
    Test-NeedsWebBuild return values via the output stream (callers
    capture them with $x = ...). Every other function communicates
    its exit code via $script:BuildExitCode and is always called
    WITHOUT capturing, so Write-Output status lines and external
    program output flow to stdout instead of polluting a return value.

    Intentional parity fixes vs build.cmd (matching build.sh):
      * A missing atheriz_draw/index.html also forces a rebuild
        (build.cmd only re-checked the webclient side in its second
        gate, so a deleted draw entry would be missed).
      * Cleans the staged webclient/ dir and nested staging artifacts
        (wwwroot/assets/assets, wwwroot/webclient/webclient) like
        build.sh; build.cmd left webclient/ contents stale.
      * Builds .NET in both Release and Debug like build.sh
        (build.cmd built Release only, leaving the Debug DLL used by
        integration tests and boot checks stale).
      * Unknown args are an error (like build.sh); build.cmd silently
        ignored them. Hash-file comparison trims whitespace so legacy
        hash files with a trailing space still match.
    The content hash covers exactly the same inputs as build.cmd
    (webclient/src + vite.config.ts + package.json) so the two agree
    whenever collation agrees (see ordinal-sort note below).
    build.sh additionally folds in package-lock.json + tsconfig.json
    and will disagree once those change without src/config changes.

    Two Get-ChildItem quirks are load-bearing here, kept verbatim:
      * Enumerating the package.json *file* path with -Recurse uses
        wildcard-child semantics: it matches every file NAMED
        package.json under webclient/ (root + node_modules), not just
        the root one. Same in build.cmd. Do NOT "fix" the path list —
        it would desync the skip decision.
      * Sort-Object is culture-sensitive AND differs between runtimes
        (.NET Framework/NLS on Windows PowerShell 5.1 vs ICU on
        PowerShell 7+), so this script sorts the path list with an
        explicit ordinal comparer instead. That makes the hash
        byte-identical on 5.1, 7+, and Linux pwsh. It can differ from
        build.cmd/atheriz.cmd (which inherit 5.1 NLS order) on trees
        whose NLS and ordinal orders disagree — worst case is one
        extra rebuild when alternating scripts, then each is stable
        with itself.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Alias('f')]
    [switch] $Force,

    [Alias('h')]
    [switch] $Help,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Remaining
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Exit code channel for effect-functions (see .NOTES). Initialized here
# so Set-StrictMode never trips on it, also when dot-sourced for tests.
$script:BuildExitCode = 0

function Show-Usage {
    Write-Output 'Usage: build.ps1 [-Force] [-Help]'
    Write-Output '  -Force (-f, --force)   force rebuild of webclient even if unchanged'
    Write-Output '  -Help (-h, --help)     show this help'
}

function Write-Stderr {
    param([Parameter(Mandatory = $true)][string] $Message)
    [Console]::Error.WriteLine($Message)
}

function Fail-Build {
    param([Parameter(Mandatory = $true)][int] $Code)
    $script:BuildExitCode = $Code
}

function Get-WebclientHash {
    <#
    .SYNOPSIS
        Hash of webclient sources, same algorithm as build.cmd.
    .DESCRIPTION
        Concatenates the raw bytes of every file under webclient/src
        plus vite.config.ts and package.json (sorted by FullName) into
        one SHA256. Returns '' when no input files are found.
    #>
    param([Parameter(Mandatory = $true)][string] $WebclientDir)

    $inputs = @(
        (Join-Path $WebclientDir 'src'),
        (Join-Path $WebclientDir 'vite.config.ts'),
        (Join-Path $WebclientDir 'package.json')
    )
    # Ordinal sort (see .NOTES): Sort-Object is culture-sensitive and
    # NLS (.NET Framework) vs ICU (PowerShell 7+) disagree on some
    # trees, which would flap the skip decision across hosts.
    $paths = @()
    foreach ($p in $inputs) {
        if (Test-Path $p) {
            foreach ($f in (Get-ChildItem -Recurse -File -Path $p -ErrorAction SilentlyContinue)) {
                $paths += $f.FullName
            }
        }
    }
    [Array]::Sort($paths, [System.StringComparer]::Ordinal)

    if ($paths.Count -eq 0) {
        return ''
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($fullName in $paths) {
            $bytes = [System.IO.File]::ReadAllBytes($fullName)
            if ($bytes.Length -gt 0) {
                # 0-byte files contribute nothing; skipping the
                # TransformBlock call keeps this total (avoids a
                # zero-length TransformBlock edge case).
                [void]$sha.TransformBlock($bytes, 0, $bytes.Length, $null, $null)
            }
        }
        [void]$sha.TransformFinalBlock([byte[]]::new(0), 0, 0)
        return ([System.BitConverter]::ToString($sha.Hash).Replace('-', '').ToLowerInvariant())
    }
    finally {
        $sha.Dispose()
    }
}

function Test-NeedsWebBuild {
    <#
    .SYNOPSIS
        $true when the vite build + stage step must run.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $SrcHash,
        [Parameter(Mandatory = $true)][string] $DestWwwroot,
        [Parameter(Mandatory = $true)][string] $SrcHashFile,
        [switch] $Force
    )

    if ($Force) {
        return $true
    }
    if (-not (Test-Path $SrcHashFile -PathType Leaf)) {
        return $true
    }
    if (-not (Test-Path (Join-Path $DestWwwroot 'webclient/index.html') -PathType Leaf)) {
        return $true
    }
    # build.cmd only re-checked webclient/index.html here; a missing
    # draw entry must also force a rebuild (matches build.sh).
    if (-not (Test-Path (Join-Path $DestWwwroot 'atheriz_draw/index.html') -PathType Leaf)) {
        return $true
    }
    $assetHits = @(Get-ChildItem -Path (Join-Path $DestWwwroot 'assets') -Filter 'webclient-*.js' -ErrorAction SilentlyContinue)
    if ($assetHits.Count -eq 0) {
        return $true
    }
    $stored = ''
    try {
        $stored = [System.IO.File]::ReadAllText($SrcHashFile)
    }
    catch {
        return $true
    }
    # Trim: tolerates legacy hash files with a trailing space/CRLF.
    if ($stored.Trim() -ceq $SrcHash) {
        return $false
    }
    return $true
}

function Invoke-WebclientNpmBuild {
    <#
    .SYNOPSIS
        Runs npm ci (fallback: npm install) + npm run build.
    .DESCRIPTION
        Never call with $x = ... (see .NOTES): npm output flows to
        stdout. Sets $script:BuildExitCode; returns nothing.
    #>
    param([Parameter(Mandatory = $true)][string] $WebclientDir)

    Push-Location $WebclientDir
    try {
        & npm ci --silent
        if ($LASTEXITCODE -ne 0) {
            & npm install
        }
        & npm run build
        if ($LASTEXITCODE -ne 0) {
            Write-Stderr 'error: vite build failed'
            Fail-Build 1
            return
        }
        Fail-Build 0
        return
    }
    finally {
        Pop-Location
    }
}

function Copy-WebDistToWwwroot {
    <#
    .SYNOPSIS
        Stages webclient/dist into the server wwwroot.
    .DESCRIPTION
        Never call with $x = ... (see .NOTES): status lines flow to
        stdout. Sets $script:BuildExitCode; returns nothing.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $WebclientDir,
        [Parameter(Mandatory = $true)][string] $SrcDist,
        [Parameter(Mandatory = $true)][string] $DestWwwroot,
        [Parameter(Mandatory = $true)][string] $SrcHash,
        [Parameter(Mandatory = $true)][string] $SrcHashFile
    )

    if (-not (Test-Path $SrcDist -PathType Container)) {
        Write-Stderr "error: vite build did not produce $SrcDist"
        Fail-Build 1
        return
    }
    $drawIndex = Join-Path $SrcDist 'index.html'
    $webIndex = Join-Path $SrcDist 'webclient/index.html'
    $srcAssets = Join-Path $SrcDist 'assets'
    if (-not (Test-Path $webIndex -PathType Leaf)) {
        Write-Stderr "error: vite build did not produce $webIndex"
        Fail-Build 1
        return
    }
    if (-not (Test-Path $drawIndex -PathType Leaf)) {
        Write-Stderr "error: vite build did not produce $drawIndex"
        Fail-Build 1
        return
    }

    if (-not (Test-Path $DestWwwroot -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $DestWwwroot | Out-Null
    }

    $assetsDest = Join-Path $DestWwwroot 'assets'
    $drawDest = Join-Path $DestWwwroot 'atheriz_draw'
    $webDest = Join-Path $DestWwwroot 'webclient'

    # Clean old built assets — remove the whole dirs so stale hashed
    # webclient-*.js do not accumulate (matches build.sh; build.cmd
    # left webclient/ in place).
    foreach ($d in @($assetsDest, $drawDest, $webDest)) {
        if (Test-Path $d) {
            Remove-Item -Recurse -Force $d
        }
    }
    # Remove nested artifacts from prior bad staging runs
    # (wwwroot/assets/assets, wwwroot/webclient/webclient).
    foreach ($d in @((Join-Path $assetsDest 'assets'), (Join-Path $webDest 'webclient'))) {
        if (Test-Path $d) {
            Remove-Item -Recurse -Force $d
        }
    }
    # Do NOT wipe gfonts/chafa on every build — they are large and vite
    # may not emit gfonts every time. Clean them only if dist has them.
    if ((Test-Path (Join-Path $SrcDist 'gfonts') -PathType Container) -and (Test-Path (Join-Path $DestWwwroot 'gfonts'))) {
        Remove-Item -Recurse -Force (Join-Path $DestWwwroot 'gfonts')
    }
    $hasSrcChafaTop = Test-Path (Join-Path $SrcDist 'chafa.wasm') -PathType Leaf
    $hasSrcChafaHashed = (@(Get-ChildItem -Path $srcAssets -Filter 'chafa-*.wasm' -ErrorAction SilentlyContinue)).Count -gt 0
    if ($hasSrcChafaTop -or $hasSrcChafaHashed) {
        $destChafa = Join-Path $DestWwwroot 'chafa.wasm'
        if (Test-Path $destChafa) {
            Remove-Item -Force $destChafa
        }
    }

    # Stage dist -> wwwroot (mirrors WebDeployer.StageDist).
    New-Item -ItemType Directory -Force -Path $assetsDest | Out-Null
    Copy-Item -Path (Join-Path $srcAssets '*') -Destination $assetsDest -Recurse -Force
    # Defensive: remove accidental nested copy if it appeared.
    $nestedAssets = Join-Path $assetsDest 'assets'
    if (Test-Path $nestedAssets) {
        Remove-Item -Recurse -Force $nestedAssets
    }
    # fonts from webclient/fonts (Fira_Custom etc.)
    $srcFonts = Join-Path $WebclientDir 'fonts'
    if (Test-Path $srcFonts -PathType Container) {
        $destFonts = Join-Path $DestWwwroot 'fonts'
        New-Item -ItemType Directory -Force -Path $destFonts | Out-Null
        Copy-Item -Path (Join-Path $srcFonts '*') -Destination $destFonts -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $webDest | Out-Null
    Copy-Item -Path $webIndex -Destination (Join-Path $webDest 'index.html') -Force
    New-Item -ItemType Directory -Force -Path $drawDest | Out-Null
    Copy-Item -Path $drawIndex -Destination (Join-Path $drawDest 'index.html') -Force
    # Stable server-log viewer name for the landing page (hashed names
    # change per build). It lives next to its hashed chunk neighbors so
    # their relative imports keep resolving. The glob needs the dash so
    # it never matches serverlog.js itself.
    foreach ($f in @(Get-ChildItem -Path $srcAssets -Filter 'serverlog-*.js' -ErrorAction SilentlyContinue)) {
        Copy-Item -Path $f.FullName -Destination (Join-Path $assetsDest 'serverlog.js') -Force
        break
    }
    $srcChafaTop = Join-Path $SrcDist 'chafa.wasm'
    if (Test-Path $srcChafaTop -PathType Leaf) {
        Copy-Item -Path $srcChafaTop -Destination (Join-Path $DestWwwroot 'chafa.wasm') -Force
    }
    else {
        foreach ($f in @(Get-ChildItem -Path $srcAssets -Filter 'chafa-*.wasm' -ErrorAction SilentlyContinue)) {
            Copy-Item -Path $f.FullName -Destination (Join-Path $DestWwwroot 'chafa.wasm') -Force
            break
        }
    }
    $srcGfonts = Join-Path $SrcDist 'gfonts'
    if (Test-Path $srcGfonts -PathType Container) {
        $destGfonts = Join-Path $DestWwwroot 'gfonts'
        New-Item -ItemType Directory -Force -Path $destGfonts | Out-Null
        Copy-Item -Path (Join-Path $srcGfonts '*') -Destination $destGfonts -Recurse -Force
    }

    # No trailing space: it would land in the hash file and break the
    # unchanged-skip comparison on the next run. CRLF matches build.cmd's
    # `echo %SRC_HASH%>file` byte-for-byte (ASCII, no BOM).
    [System.IO.File]::WriteAllText($SrcHashFile, "$SrcHash`r`n", [System.Text.Encoding]::ASCII)
    Write-Output "Webclient deployed to $DestWwwroot"
    Fail-Build 0
    return
}

function Invoke-AtherizBuild {
    <#
    .SYNOPSIS
        Main build flow. Never call with $x = ... (see .NOTES).
    .DESCRIPTION
        Reads $script:BuildExitCode for the result. All status lines
        flow to stdout.
    #>
    param(
        [switch] $Force,
        [switch] $Help,
        [string[]] $Remaining
    )

    if ($null -ne $Remaining) {
        foreach ($a in $Remaining) {
            $k = $a.ToLowerInvariant()
            if ($k -in @('--force', '-f', '/f', '-force')) {
                $Force = $true
            }
            elseif ($k -in @('--help', '-h', '-help', '-?', '/?', '/help', '/h')) {
                $Help = $true
            }
            else {
                Write-Stderr "unknown arg: $a"
                Show-Usage
                Fail-Build 1
                return
            }
        }
    }

    if ($Help) {
        Show-Usage
        Fail-Build 0
        return
    }

    if ($PSScriptRoot) {
        $scriptDir = $PSScriptRoot
    }
    else {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    $webclientDir = Join-Path $scriptDir 'webclient'
    $destWwwroot = Join-Path $scriptDir 'src/Atheriz.Server/wwwroot'
    $srcHashFile = Join-Path $destWwwroot '.webclient-hash'
    $solutionFile = Join-Path $scriptDir 'Atheriz.sln'

    if (-not (Test-Path (Join-Path $webclientDir 'package.json') -PathType Leaf)) {
        Write-Stderr "error: webclient/package.json not found at $webclientDir"
        Fail-Build 1
        return
    }
    if ($null -eq (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-Stderr 'error: node >=18 required'
        Fail-Build 1
        return
    }
    if ($null -eq (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Stderr 'error: npm required'
        Fail-Build 1
        return
    }
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Stderr 'error: dotnet 10.0.100+ required'
        Fail-Build 1
        return
    }

    $srcHash = Get-WebclientHash -WebclientDir $webclientDir
    if ([string]::IsNullOrEmpty($srcHash)) {
        Write-Stderr 'error: failed to compute webclient/src hash'
        Fail-Build 1
        return
    }

    $needWebBuild = Test-NeedsWebBuild -SrcHash $srcHash -DestWwwroot $destWwwroot -SrcHashFile $srcHashFile -Force:$Force

    if (-not $needWebBuild) {
        Write-Output "Webclient unchanged ($srcHash) - skipping vite build"
    }
    else {
        Write-Output "Webclient changed ($srcHash) - rebuilding..."
        Invoke-WebclientNpmBuild -WebclientDir $webclientDir
        if ($script:BuildExitCode -ne 0) {
            return
        }
        $srcDist = Join-Path $webclientDir 'dist'
        Copy-WebDistToWwwroot -WebclientDir $webclientDir -SrcDist $srcDist -DestWwwroot $destWwwroot -SrcHash $srcHash -SrcHashFile $srcHashFile
        if ($script:BuildExitCode -ne 0) {
            return
        }
    }

    # Always build .NET in both configs like build.sh: Release (preferred
    # at runtime) and Debug (integration tests + boot checks run it).
    Write-Output 'Building .NET (Release + Debug)...'
    & dotnet build $solutionFile -c Release
    if ($LASTEXITCODE -ne 0) {
        Fail-Build $LASTEXITCODE
        return
    }
    & dotnet build $solutionFile -c Debug
    if ($LASTEXITCODE -ne 0) {
        Fail-Build $LASTEXITCODE
        return
    }

    if (-not $needWebBuild) {
        Write-Output 'Build complete - webclient unchanged + engine built'
    }
    else {
        Write-Output 'Build complete - webclient rebuilt + engine built'
    }
    Write-Output 'Run .\atheriz.cmd --help'
    Fail-Build 0
    return
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-AtherizBuild -Force:$Force -Help:$Help -Remaining $Remaining
    exit $script:BuildExitCode
}
