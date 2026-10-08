#Requires -Version 5.1
<#
.SYNOPSIS
    AtheriZ game server launcher (start|stop|restart|reload|reset|create|new|test …).

.DESCRIPTION
    PowerShell port of atheriz.sh (which ports atheriz.atheriz:main).
    Prefers the built Atheriz.Server.dll so the game folder (current
    directory) is preserved; falls back to `dotnet run --project` when
    nothing is built yet. Engine requires webclient — run build.ps1
    first on a fresh clone.

    Webclient staleness handling (warnings only, never blocks startup):
      L1: webclient/src newer than the staged server copy (wwwroot).
          Serve commands (start/restart/reload) warn only; scaffold
          commands (new/create) rebuild first via build.ps1, since
          scaffolding would otherwise bake the stale stage into the
          new game folder.
      L2: this game's staged copy (CWD/web/static) differs from wwwroot
          -> dotnet <server.dll> deploy game --web-root "<game>/web".
    L2 compares the entry HTML files: they embed the hashed asset
    names, so any rebuild changes them.

    Runs on Windows PowerShell 5.1 and PowerShell 7+.

.NOTES
    House rule (same as build.ps1): status lines use Write-Output and
    flow to stdout uncaptured; errors use [Console]::Error so
    `2>`-style redirection keeps working; the process exit code is the
    only return channel ($script:AtherizExitCode, never captured calls).

    Hash note: atheriz.sh hashes 5 inputs (src + vite.config.ts +
    package.json + package-lock.json + tsconfig.json) with a
    hash-of-hashes. On Windows the staged hash file is written by
    build.cmd/build.ps1 (3 inputs: src + vite.config.ts +
    package.json, raw SHA256 concat), so this script uses the same
    3-input raw hash as build.ps1 — otherwise the L1 comparison could
    never agree with the Windows build scripts. Lockfile/tsconfig-only
    changes therefore do not trigger the warning; everything else
    behaves exactly like atheriz.sh.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Remaining
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:AtherizExitCode = 0

function Write-Stderr {
    param([Parameter(Mandatory = $true)][string] $Message)
    [Console]::Error.WriteLine($Message)
}

function Fail-Run {
    param([Parameter(Mandatory = $true)][int] $Code)
    $script:AtherizExitCode = $Code
}

function Get-WebclientSrcHash {
    <#
    .SYNOPSIS
        Current-sources hash, same inputs/algorithm as build.ps1.
    .DESCRIPTION
        Returns '' when webclient/src is missing or unreadable.
    #>
    param([Parameter(Mandatory = $true)][string] $WebclientDir)

    if (-not (Test-Path (Join-Path $WebclientDir 'src') -PathType Container)) {
        return ''
    }
    $inputs = @(
        (Join-Path $WebclientDir 'src'),
        (Join-Path $WebclientDir 'vite.config.ts'),
        (Join-Path $WebclientDir 'package.json')
    )
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

function Test-WebSrcStale {
    <#
    .SYNOPSIS
        $true when webclient sources differ from the last staged build.
    .DESCRIPTION
        Compares the content hash build.ps1 records (not mtimes, which
        copies and checkouts can fake). Missing/empty inputs count as
        fresh (nothing to compare); an unreadable hash file counts as
        stale (rebuild, the safe direction).
    #>
    param(
        [Parameter(Mandatory = $true)][string] $WebclientDir,
        [Parameter(Mandatory = $true)][string] $Wwwroot,
        [Parameter(Mandatory = $true)][string] $SrcHashFile
    )

    if (-not (Test-Path (Join-Path $WebclientDir 'src') -PathType Container)) {
        return $false
    }
    if (-not (Test-Path $SrcHashFile -PathType Leaf)) {
        return $true
    }
    if (-not (Test-Path (Join-Path $Wwwroot 'atheriz_draw/index.html') -PathType Leaf)) {
        return $true
    }
    $current = Get-WebclientSrcHash -WebclientDir $WebclientDir
    if ([string]::IsNullOrEmpty($current)) {
        return $false
    }
    try {
        $stored = [System.IO.File]::ReadAllText($SrcHashFile)
    }
    catch {
        return $true
    }
    # Whitespace-stripped: unreadable/empty counts as stale, and legacy
    # trailing-space hash files compare clean.
    $stored = $stored.Trim()
    if ([string]::IsNullOrEmpty($stored)) {
        return $true
    }
    return ($stored -cne $current)
}

function Test-StagedEntry {
    <#
    .SYNOPSIS
        $true (stale) when the game's staged copy needs refreshing.
    .DESCRIPTION
        $GameEntry = game copy, $Reference = wwwroot reference, $Label
        for messages. Warns (does not throw) when stale.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $GameEntry,
        [Parameter(Mandatory = $true)][string] $Reference,
        [Parameter(Mandatory = $true)][string] $Label
    )

    if (-not (Test-Path $Reference -PathType Leaf)) {
        return $false
    }
    if (-not (Test-Path $GameEntry -PathType Leaf)) {
        if (-not (Test-Path './web' -PathType Container)) {
            return $false
        }
        Write-Stderr "WARNING: this game has no staged $Label entry ($GameEntry missing)."
        return $true
    }
    # Resolve against the PowerShell location, not the process CWD:
    # Push-Location does not move [Environment]::CurrentDirectory, so
    # .NET APIs would resolve a game-relative path against the wrong
    # directory (Test-Path above is provider-based and unaffected).
    $gameFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($GameEntry)
    $refFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Reference)
    try {
        $a = [System.IO.File]::ReadAllBytes($gameFull)
        $b = [System.IO.File]::ReadAllBytes($refFull)
        $same = ($a.Length -eq $b.Length)
        if ($same) {
            $same = [System.Linq.Enumerable]::SequenceEqual($a, $b)
        }
    }
    catch {
        return $false
    }
    if (-not $same) {
        Write-Stderr "WARNING: this game's staged $Label differs from the server copy."
        return $true
    }
    return $false
}

function Invoke-Atheriz {
    <#
    .SYNOPSIS
        Main launcher flow. Never call with $x = ... (see .NOTES).
    #>
    param([string[]] $ServerArgs)

    if ($PSScriptRoot) {
        $projectRoot = $PSScriptRoot
    }
    else {
        $projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    $serverProj = Join-Path $projectRoot 'src/Atheriz.Server/Atheriz.Server.csproj'
    $serverDllDebug = Join-Path $projectRoot 'src/Atheriz.Server/bin/Debug/net10.0/Atheriz.Server.dll'
    $serverDllRelease = Join-Path $projectRoot 'src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll'
    $publishDll = Join-Path $projectRoot 'publish/Atheriz.Server.dll'

    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Stderr 'error: dotnet SDK 10.0.100+ required (see global.json, dotnet --version)'
        Fail-Run 1
        return
    }

    $dlls = @($serverDllRelease, $serverDllDebug, $publishDll)
    $haveDll = $false
    foreach ($d in $dlls) {
        if (Test-Path $d -PathType Leaf) {
            $haveDll = $true
            break
        }
    }

    $webclientDir = Join-Path $projectRoot 'webclient'
    $wwwroot = Join-Path $projectRoot 'src/Atheriz.Server/wwwroot'
    $srcHashFile = Join-Path $wwwroot '.webclient-hash'

    # --- webclient staleness handling (mirrors atheriz.sh) ---
    $verb = ''
    if (($null -ne $ServerArgs) -and ($ServerArgs.Count -gt 0)) {
        $verb = $ServerArgs[0].ToLowerInvariant()
    }
    if ($verb -in @('start', 'restart', 'reload')) {
        if (Test-WebSrcStale -WebclientDir $webclientDir -Wwwroot $wwwroot -SrcHashFile $srcHashFile) {
            Write-Stderr 'WARNING: webclient sources are newer than the staged server copy.'
            Write-Stderr "  Rebuild with: $projectRoot\build.ps1  (build.cmd also works)"
        }
        $gameAssetsStale = $false
        if (Test-StagedEntry -GameEntry './web/static/atheriz_draw/index.html' -Reference (Join-Path $wwwroot 'atheriz_draw/index.html') -Label 'draw') {
            $gameAssetsStale = $true
        }
        if (Test-StagedEntry -GameEntry './web/static/webclient/index.html' -Reference (Join-Path $wwwroot 'webclient/index.html') -Label 'webclient') {
            $gameAssetsStale = $true
        }
        if ($gameAssetsStale) {
            foreach ($d in $dlls) {
                if (Test-Path $d -PathType Leaf) {
                    $cwd = (Get-Location).Path
                    Write-Stderr "  Refresh this game's copy with: dotnet `"$d`" deploy game --web-root `"$cwd/web`""
                    break
                }
            }
        }
    }
    elseif ($verb -in @('new', 'create')) {
        # Scaffolding copies wwwroot into the new game folder: a stale
        # stage would bake old assets into the game, so rebuild first
        # instead of warning. A failed rebuild aborts rather than
        # scaffolding from a stale copy.
        if (Test-WebSrcStale -WebclientDir $webclientDir -Wwwroot $wwwroot -SrcHashFile $srcHashFile) {
            Write-Stderr 'webclient sources are newer than the staged server copy — rebuilding first...'
            $buildPs1 = Join-Path $projectRoot 'build.ps1'
            & $buildPs1
            if ($LASTEXITCODE -ne 0) {
                Fail-Run $LASTEXITCODE
                return
            }
        }
    }

    if ($haveDll) {
        # Built DLL exists: run it directly so its stderr surfaces (no
        # silent fallback). Fall back to dotnet-run only when nothing is
        # built yet.
        foreach ($d in $dlls) {
            if (Test-Path $d -PathType Leaf) {
                if (($null -eq $ServerArgs) -or ($ServerArgs.Count -eq 0)) {
                    & dotnet $d --help
                }
                else {
                    & dotnet $d @ServerArgs
                }
                Fail-Run $LASTEXITCODE
                return
            }
        }
    }

    Write-Stderr 'note: no built Atheriz.Server.dll found — building via dotnet run --project (will be slower)'
    Write-Stderr 'hint: run .\build.ps1 to pre-build webclient + engine'
    if (($null -eq $ServerArgs) -or ($ServerArgs.Count -eq 0)) {
        & dotnet run --project $serverProj -- --help
    }
    else {
        & dotnet run --project $serverProj -- @ServerArgs
    }
    Fail-Run $LASTEXITCODE
    return
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-Atheriz -ServerArgs $Remaining
    exit $script:AtherizExitCode
}
