[CmdletBinding()]
param(
    [string[]] $Platform,
    [string] $Destination,
    [string] $BaseUri = 'https://archive.torproject.org/tor-package-archive/torbrowser',
    [string] $KeyFile,
    [switch] $Force
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$BundleVersion = '16.0'
$TorVersion = '0.4.9.15'
$SigningKeyFingerprint = 'EF6E286DDA85EA2A4BA7DE684E2C6E8793298290'
$KeyUris = @(
    'https://openpgpkey.torproject.org/.well-known/openpgpkey/torproject.org/hu/kounek7zrdx745qydx6p59t9mqjpuhdf?l=torbrowser',
    'https://torproject.org/.well-known/openpgpkey/hu/kounek7zrdx745qydx6p59t9mqjpuhdf?l=torbrowser',
    "https://keys.openpgp.org/vks/v1/by-fingerprint/$SigningKeyFingerprint"
)
$Executables = @{
    'windows-x86_64'  = 'tor/tor.exe'
    'linux-x86_64'    = 'tor/tor'
    'linux-aarch64'   = 'tor/tor'
    'android-aarch64' = 'tor/libTor.so'
    'android-armv7'   = 'tor/libTor.so'
    'android-x86_64'  = 'tor/libTor.so'
}

$OnWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows

if ($PSVersionTable.PSEdition -eq 'Desktop') {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}

function Invoke-Native {
    param([string] $FilePath, [string[]] $ArgumentList)
    $ErrorActionPreference = 'Continue'
    $output = @(& $FilePath @ArgumentList 2>&1 | Where-Object { $null -ne $_ } | ForEach-Object { "$_" })
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Test-PosixGpg {
    param([string] $Gpg)
    if (-not $OnWindows) {
        return $false
    }
    $version = Invoke-Native $Gpg @('--version')
    $homeLine = $version.Output | Where-Object { $_.StartsWith('Home:') } | Select-Object -First 1
    return [bool]($homeLine -and $homeLine.Substring('Home:'.Length).Trim().StartsWith('/'))
}

function ConvertTo-GpgPath {
    param([string] $Path)
    if (-not $OnWindows) {
        return $Path
    }
    if (-not $GpgIsPosix) {
        return $Path -replace '\\', '/'
    }
    if ($Cygpath) {
        $converted = Invoke-Native $Cygpath @('-u', $Path)
        if ($converted.ExitCode -eq 0 -and $converted.Output.Count -gt 0) {
            return $converted.Output[0]
        }
    }
    if ($Path -match '^([A-Za-z]):[\\/](.*)$') {
        return '/' + $Matches[1].ToLowerInvariant() + '/' + ($Matches[2] -replace '\\', '/')
    }
    return $Path -replace '\\', '/'
}

function Find-Gpg {
    $command = Get-Command gpg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        return $command.Source
    }
    if ($OnWindows) {
        $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }
        foreach ($root in $roots) {
            foreach ($relative in @('Git\usr\bin\gpg.exe', 'GnuPG\bin\gpg.exe')) {
                $candidate = Join-Path $root $relative
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
    }
    throw 'gpg was not found. Install Git for Windows (it includes gpg) or GnuPG on Windows, or the gnupg package on Linux, then run this script again.'
}

function Find-Tar {
    if ($OnWindows) {
        $systemTar = Join-Path $env:SystemRoot 'System32\tar.exe'
        if (Test-Path -LiteralPath $systemTar) {
            return $systemTar
        }
        throw "tar.exe was not found in $env:SystemRoot\System32; it ships with Windows 10 1803 and later."
    }
    return 'tar'
}

function Get-RemoteFile {
    param([string] $Uri, [string] $OutFile)
    Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing
}

function Get-DefaultPlatform {
    if ($OnWindows) {
        return 'windows-x86_64'
    }
    if ($IsLinux) {
        $machine = (Invoke-Native 'uname' @('-m')).Output | Select-Object -First 1
        if ($machine -eq 'aarch64') {
            return 'linux-aarch64'
        }
        return 'linux-x86_64'
    }
    throw 'This system has no default platform; pass -Platform.'
}

function Import-SigningKey {
    param([string] $Gpg, [string] $GpgHome, [string] $WorkDirectory)
    $keyPath = Join-Path $WorkDirectory 'signing-key'
    if ($KeyFile) {
        Copy-Item -LiteralPath $KeyFile -Destination $keyPath
    }
    else {
        $fetched = $false
        foreach ($uri in $KeyUris) {
            try {
                Get-RemoteFile -Uri $uri -OutFile $keyPath
                $fetched = $true
                break
            }
            catch {
                Write-Warning "Could not fetch the signing key from $uri ($($_.Exception.Message))."
            }
        }
        if (-not $fetched) {
            throw 'Could not fetch the Tor Browser Developers signing key from any source. Download it by hand and pass it with -KeyFile; the script still checks its fingerprint.'
        }
    }
    $import = Invoke-Native $Gpg @('--homedir', (ConvertTo-GpgPath $GpgHome), '--batch', '--no-autostart', '--import', (ConvertTo-GpgPath $keyPath))
    if ($import.ExitCode -ne 0) {
        throw "gpg could not import the signing key:`n$($import.Output -join "`n")"
    }
}

function Assert-Signature {
    param([string] $Gpg, [string] $GpgHome, [string] $Archive, [string] $Signature)
    $verify = Invoke-Native $Gpg @('--homedir', (ConvertTo-GpgPath $GpgHome), '--batch', '--no-autostart', '--status-fd', '1', '--verify', (ConvertTo-GpgPath $Signature), (ConvertTo-GpgPath $Archive))
    $validSignatures = @($verify.Output | Where-Object { $_.StartsWith('[GNUPG:] VALIDSIG ') })
    $trusted = @($validSignatures | Where-Object { (($_ -split ' ') | Select-Object -Last 1) -eq $SigningKeyFingerprint })
    if ($verify.ExitCode -ne 0 -or $trusted.Count -eq 0) {
        throw "The signature of $(Split-Path -Leaf $Archive) is not a valid signature by the key $SigningKeyFingerprint. gpg said:`n$($verify.Output -join "`n")"
    }
}

function Install-Bundle {
    param([string] $Name, [string] $Gpg, [string] $Tar, [string] $GpgHome, [string] $WorkDirectory)
    $target = Join-Path (Join-Path $Destination $BundleVersion) $Name
    $executable = Join-Path $target $Executables[$Name]

    if ((Test-Path -LiteralPath $target) -and -not $Force) {
        Write-Host "$Name is already installed: $executable"
        return
    }

    $fileName = "tor-expert-bundle-$Name-$BundleVersion.tar.gz"
    $archive = Join-Path $WorkDirectory $fileName
    $signature = "$archive.asc"
    $uri = "$($BaseUri.TrimEnd('/'))/$BundleVersion/$fileName"

    Write-Host "Downloading $uri"
    Get-RemoteFile -Uri $uri -OutFile $archive
    Get-RemoteFile -Uri "$uri.asc" -OutFile $signature
    Assert-Signature -Gpg $Gpg -GpgHome $GpgHome -Archive $archive -Signature $signature
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Signature checked (key $SigningKeyFingerprint); SHA-256 $hash"

    $staging = "$target.partial"
    foreach ($path in @($staging, $target)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $extract = Invoke-Native $Tar @('-xzf', $archive, '-C', $staging)
    if ($extract.ExitCode -ne 0) {
        throw "tar could not unpack $fileName`:`n$($extract.Output -join "`n")"
    }
    Move-Item -LiteralPath $staging -Destination $target

    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The bundle has no $($Executables[$Name]); its layout has changed."
    }
    Write-Host "$Name installed: $executable"
}

$scriptFolder = $PSScriptRoot
if (-not $scriptFolder) {
    $scriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Definition
}
if (-not $Destination) {
    $Destination = Join-Path $scriptFolder 'tor-bundles'
}
$Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)

$Platform = @($Platform | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Platform.Count -eq 0) {
    $Platform = @(Get-DefaultPlatform)
}
foreach ($name in $Platform) {
    if (-not $Executables.ContainsKey($name)) {
        throw "Unknown platform '$name'. Use one or more of: $(($Executables.Keys | Sort-Object) -join ', ')."
    }
}

$gpg = Find-Gpg
$GpgIsPosix = Test-PosixGpg $gpg
$Cygpath = $null
if ($GpgIsPosix) {
    $cygpathCandidate = Join-Path (Split-Path -Parent $gpg) 'cygpath.exe'
    if (Test-Path -LiteralPath $cygpathCandidate) {
        $Cygpath = $cygpathCandidate
    }
}
$tar = Find-Tar
$work = Join-Path ([IO.Path]::GetTempPath()) ("p2pm-get-tor-" + [Guid]::NewGuid().ToString('N'))
$gpgHome = Join-Path $work 'gnupg'
New-Item -ItemType Directory -Path $gpgHome -Force | Out-Null
if (-not $OnWindows) {
    [void](Invoke-Native 'chmod' @('700', $gpgHome))
}

try {
    Write-Host "Tor Expert Bundle $BundleVersion (tor $TorVersion) for $($Platform -join ', ')"
    Import-SigningKey -Gpg $gpg -GpgHome $gpgHome -WorkDirectory $work
    foreach ($name in $Platform) {
        Install-Bundle -Name $name -Gpg $gpg -Tar $tar -GpgHome $gpgHome -WorkDirectory $work
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
