[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Desktop', 'Apk')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $Path,

    [ValidateSet('win-x64', 'linux-x64')]
    [string] $Platform = 'win-x64',

    [string[]] $ExpectedAbis = @('arm64-v8a', 'armeabi-v7a', 'x86_64')
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
if (-not (Test-Path -LiteralPath $resolved)) {
    throw "Release target not found: $resolved"
}

if ($Mode -eq 'Desktop') {
    $tempExtract = $null
    $inspectDir = $resolved
    try {
        if ((Get-Item -LiteralPath $resolved) -is [IO.FileInfo]) {
            if (-not $resolved.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Desktop file mode supports .zip archives only: $resolved"
            }

            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $tempExtract = Join-Path ([IO.Path]::GetTempPath()) ("p2pm-verify-" + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempExtract -Force | Out-Null
            [IO.Compression.ZipFile]::ExtractToDirectory($resolved, $tempExtract)
            $inspectDir = $tempExtract
        }

        $exeName = if ($Platform -eq 'win-x64') { 'P2PMessenger.Desktop.exe' } else { 'P2PMessenger.Desktop' }
        $torName = if ($Platform -eq 'win-x64') { 'tor.exe' } else { 'tor' }

        $required = @(
            $exeName,
            "tor/$torName",
            'THIRD-PARTY-NOTICES.txt',
            'licenses/tor/tor.txt',
            'licenses/tor/openssl.txt',
            'licenses/tor/libevent.txt',
            'licenses/tor/zlib.txt'
        )

        foreach ($rel in $required) {
            $candidate = Join-Path $inspectDir $rel
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                throw "Missing required desktop release file: $rel"
            }
        }

        $allFiles = @(Get-ChildItem -LiteralPath $inspectDir -Recurse -File)
        foreach ($file in $allFiles) {
            $rel = $file.FullName.Substring($inspectDir.Length).TrimStart('\', '/').Replace('\', '/')
            if ($rel -match '(^|/)pluggable_transports(/|$)' -or
                $rel -match '(^|/)tor-gencert' -or
                $rel -match '(^|/)lyrebird' -or
                $rel -match '(^|/)conjure') {
                throw "Prohibited pluggable transport or tool binary in desktop release: $rel"
            }
            if ($rel.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Debug symbol file found in desktop release: $rel"
            }
        }

        $totalBytes = ($allFiles | Measure-Object Length -Sum).Sum
        $totalMb = [Math]::Round($totalBytes / 1MB, 2)
        Write-Host "Desktop release verified ($Platform): $($allFiles.Count) files, $totalMb MB"
    }
    finally {
        if ($tempExtract -and (Test-Path -LiteralPath $tempExtract)) {
            Remove-Item -LiteralPath $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$apkInfo = Get-Item -LiteralPath $resolved
$zip = [IO.Compression.ZipFile]::OpenRead($resolved)
try {
    $entryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $zip.Entries) {
        [void]$entryNames.Add($entry.FullName)
        if ($entry.FullName -match '\.pdb(\.so)?$') {
            throw "Debug symbol entry found in APK: $($entry.FullName)"
        }
    }

    foreach ($abi in $ExpectedAbis) {
        foreach ($lib in @('libTor.so', 'libsodium.so', 'libe_sqlite3.so')) {
            $expected = "lib/$abi/$lib"
            if (-not $entryNames.Contains($expected)) {
                throw "Missing required APK native library: $expected"
            }
        }
    }

    $checked64 = 0
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -notmatch '^lib/(arm64-v8a|x86_64)/.*\.so$') {
            continue
        }

        $stream = $entry.Open()
        $ms = New-Object IO.MemoryStream
        try {
            $stream.CopyTo($ms)
            $bytes = $ms.ToArray()
        }
        finally {
            $ms.Dispose()
            $stream.Dispose()
        }

        if ($bytes.Length -lt 64 -or
            $bytes[0] -ne 0x7F -or
            $bytes[1] -ne 0x45 -or
            $bytes[2] -ne 0x4C -or
            $bytes[3] -ne 0x46 -or
            $bytes[4] -ne 2 -or
            $bytes[5] -ne 1) {
            throw "Invalid 64-bit little-endian ELF binary in APK: $($entry.FullName)"
        }

        $phoff = [BitConverter]::ToUInt64($bytes, 32)
        $phentsize = [BitConverter]::ToUInt16($bytes, 54)
        $phnum = [BitConverter]::ToUInt16($bytes, 56)
        if ($phentsize -lt 56 -or ($phoff + [uint64]$phentsize * [uint64]$phnum) -gt [uint64]$bytes.LongLength) {
            throw "Corrupt ELF64 program header table in APK: $($entry.FullName)"
        }

        $loadCount = 0
        for ($i = 0; $i -lt $phnum; $i++) {
            $off = [int]($phoff + [uint64]$i * [uint64]$phentsize)
            $ptype = [BitConverter]::ToUInt32($bytes, $off)
            if ($ptype -eq 1) {
                $loadCount++
                $align = [BitConverter]::ToUInt64($bytes, $off + 48)
                if ($align -lt 16384 -or ($align % 16384) -ne 0) {
                    throw "ELF64 PT_LOAD segment not 16 KB aligned in $($entry.FullName): p_align=$align"
                }
            }
        }

        if ($loadCount -eq 0) {
            throw "ELF64 binary has no PT_LOAD segments in APK: $($entry.FullName)"
        }

        $checked64++
    }

    $apkMb = [Math]::Round($apkInfo.Length / 1MB, 2)
    Write-Host "APK verified: $apkMb MB, ABIs=[$($ExpectedAbis -join ', ')], $checked64 64-bit .so files 16 KB-aligned"
}
finally {
    $zip.Dispose()
}
