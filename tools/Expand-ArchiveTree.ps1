[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [string]$Password,

    [string]$SevenZipPath,

    [switch]$DeleteArchives,

    [switch]$AnalyzeOnly,

    [int]$MaxPasses = 10
)

$ErrorActionPreference = 'Stop'

function Resolve-SevenZipPath {
    param([string]$ConfiguredPath)

    if ($ConfiguredPath -and (Test-Path -LiteralPath $ConfiguredPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $ConfiguredPath).ProviderPath
    }

    $local = Join-Path $PSScriptRoot '7zip\7z.exe'
    if (Test-Path -LiteralPath $local -PathType Leaf) {
        return (Resolve-Path -LiteralPath $local).ProviderPath
    }

    $envPath = [Environment]::GetEnvironmentVariable('EXTRACTUTIL_7Z_PATH')
    if ($envPath -and (Test-Path -LiteralPath $envPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $envPath).ProviderPath
    }

    $installed = @(
        (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
        (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe')
    )

    foreach ($candidate in $installed) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }

    throw '7z.exe was not found. Pass -SevenZipPath or set EXTRACTUTIL_7Z_PATH.'
}

function Get-PasswordFromPath {
    param([string]$Path)

    foreach ($part in ($Path -split '[\\/]')) {
        if ($part -match '(?:\u89e3\u538b\u5bc6\u7801|\u5bc6\u7801)\s*[:\uFF1A]\s*(.+)$') {
            return $Matches[1].Trim()
        }
    }

    return $null
}

function Add-PasswordCandidate {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $clean = $Value.Trim()
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return
    }

    if (-not $Candidates.Contains($clean)) {
        [void]$Candidates.Add($clean)
    }
}

function Get-PasswordCandidatesFromText {
    param([string]$Text)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $valuePattern = '([^\s\\/\)\]\}\uFF09\u3011\u300B,\uFF0C;\uFF1B]+)'
    $patterns = @(
        "(?:\u89e3\u538b\u5bc6\u7801|\u5bc6\u7801)\s*[:\uFF1A=]\s*$valuePattern",
        "(?i)(?:^|[\(\[\{\uFF08\u3010\s_-])p\s*[:\uFF1A=]\s*$valuePattern",
        "(?i)(?:pass|password|pwd)\s*[:\uFF1A=]\s*$valuePattern"
    )

    foreach ($pattern in $patterns) {
        foreach ($match in [regex]::Matches($Text, $pattern)) {
            Add-PasswordCandidate -Candidates $candidates -Value $match.Groups[1].Value
        }
    }

    return @($candidates)
}

function Get-PasswordCandidatesForTask {
    param($Task)

    $candidates = [System.Collections.Generic.List[string]]::new()
    $parts = @($Task.InputPath -split '[\\/]')

    for ($i = $parts.Count - 1; $i -ge 0; $i--) {
        foreach ($candidate in (Get-PasswordCandidatesFromText -Text $parts[$i])) {
            Add-PasswordCandidate -Candidates $candidates -Value $candidate
        }
    }

    if ($script:Password) {
        Add-PasswordCandidate -Candidates $candidates -Value $script:Password
    }

    if ($candidates.Count -eq 0) {
        return @($null)
    }

    return @($candidates)
}

function Write-RunLog {
    param(
        [string]$Level,
        [string]$Message
    )

    $line = '{0:O} [{1}] {2}' -f (Get-Date), $Level, $Message
    $line | Add-Content -LiteralPath $script:LogPath -Encoding UTF8
    Write-Host $line
}

function Test-InWorkRoot {
    param([string]$Path)

    return $Path.StartsWith($script:WorkRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Test-SevenZipSplitVolumeName {
    param(
        [string]$Name,
        [ref]$VolumeNumber
    )

    if ($Name -notmatch '(?i)^(.+\.7z)\.(\d{3})$') {
        return $false
    }

    $VolumeNumber.Value = [int]$Matches[2]
    return $true
}

function Get-ArchiveBaseName {
    param([string]$Name)

    if ($Name -match '(?i)^(.+)\.7z\.\d{3}$') {
        return $Matches[1]
    }

    if ($Name -match '(?i)^(.+)\.part\d+\.rar$') {
        return $Matches[1]
    }

    $suffixes = @(
        '.tar.gz',
        '.tar.bz2',
        '.tar.xz',
        '.zip',
        '.7z',
        '.rar',
        '.tar',
        '.tgz',
        '.gz',
        '.bz2',
        '.xz'
    )

    foreach ($suffix in $suffixes) {
        if ($Name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            return $Name.Substring(0, $Name.Length - $suffix.Length)
        }
    }

    return [IO.Path]::GetFileNameWithoutExtension($Name)
}

function Test-RegularArchiveName {
    param([string]$Name)

    $ignoredVolume = 0
    if (Test-SevenZipSplitVolumeName -Name $Name -VolumeNumber ([ref]$ignoredVolume)) {
        return $false
    }

    if ($Name -match '(?i)\.r\d{2}$') {
        return $false
    }

    if ($Name -match '(?i)\.part(\d+)\.rar$') {
        return ([int]$Matches[1]) -eq 1
    }

    return $Name -match '(?i)(\.zip|\.7z|\.rar|\.tar|\.tar\.gz|\.tgz|\.gz|\.tar\.bz2|\.bz2|\.tar\.xz|\.xz)$'
}

function Get-LogicalSplitKey {
    param([IO.FileInfo]$File)

    $baseName = $File.Name -replace '(?i)\.\d{3}$', ''
    $directory = $File.DirectoryName
    $leaf = Split-Path -Leaf $directory

    if ($leaf -match '^(.+)-\d+$') {
        $directory = Split-Path -Parent $directory
    }

    return "$directory|$baseName"
}

function Get-ArchiveFiles {
    Get-ChildItem -LiteralPath $script:RootPath -Recurse -File -Force |
        Where-Object { -not (Test-InWorkRoot -Path $_.FullName) }
}

function New-SplitTasks {
    param([IO.FileInfo[]]$Files)

    $items = foreach ($file in $Files) {
        $volumeNumber = 0
        if (Test-SevenZipSplitVolumeName -Name $file.Name -VolumeNumber ([ref]$volumeNumber)) {
            [pscustomobject]@{
                File = $file
                VolumeNumber = $volumeNumber
                Key = Get-LogicalSplitKey -File $file
                BaseFileName = ($file.Name -replace '(?i)\.\d{3}$', '')
            }
        }
    }

    foreach ($group in ($items | Group-Object Key)) {
        $byVolume = $group.Group | Group-Object VolumeNumber
        $duplicates = $byVolume | Where-Object { $_.Count -gt 1 }
        if ($duplicates) {
            Write-RunLog 'WARN' "Skipped split archive with duplicate volume numbers: $($group.Name)"
            continue
        }

        $first = $group.Group | Where-Object { $_.VolumeNumber -eq 1 } | Select-Object -First 1
        if (-not $first) {
            Write-RunLog 'WARN' "Skipped split archive without .001: $($group.Name)"
            continue
        }

        $numbers = @($group.Group | Sort-Object VolumeNumber | ForEach-Object { $_.VolumeNumber })
        $max = ($numbers | Measure-Object -Maximum).Maximum
        $missing = @(1..$max | Where-Object { $numbers -notcontains $_ })
        if ($missing.Count -gt 0) {
            Write-RunLog 'WARN' "Skipped split archive with missing volume(s) $($missing -join ','): $($group.Name)"
            continue
        }

        $ordered = @($group.Group | Sort-Object VolumeNumber)
        $firstFile = $first.File
        $sourceDirectories = @($ordered | ForEach-Object { $_.File.DirectoryName } | Sort-Object -Unique)
        $baseName = Get-ArchiveBaseName -Name $firstFile.Name

        [pscustomobject]@{
            Kind = 'Split7z'
            DisplayName = $first.BaseFileName
            InputPath = $firstFile.FullName
            OutputDirectory = Join-Path $firstFile.DirectoryName $baseName
            DeletePaths = @($ordered | ForEach-Object { $_.File.FullName })
            StageNeeded = $sourceDirectories.Count -gt 1
            VolumeFiles = @($ordered | ForEach-Object { $_.File })
        }
    }
}

function New-RegularTasks {
    param([IO.FileInfo[]]$Files)

    foreach ($file in $Files) {
        $isRegularArchive = Test-RegularArchiveName -Name $file.Name
        if (-not $isRegularArchive) {
            continue
        }

        if ($file.Length -eq 0) {
            Write-RunLog 'WARN' "Skipped empty archive file: $($file.FullName)"
            continue
        }

        $deletePaths = @($file.FullName)

        if ($file.Name -match '(?i)^(.+)\.part1\.rar$') {
            $prefix = $Matches[1]
            $deletePaths = @(Get-ChildItem -LiteralPath $file.DirectoryName -File -Force |
                Where-Object { $_.Name -match ('(?i)^{0}\.part\d+\.rar$' -f [regex]::Escape($prefix)) } |
                Sort-Object Name |
                ForEach-Object { $_.FullName })
        }
        elseif ($file.Extension.Equals('.rar', [StringComparison]::OrdinalIgnoreCase)) {
            $prefix = [IO.Path]::GetFileNameWithoutExtension($file.Name)
            $rarParts = @(Get-ChildItem -LiteralPath $file.DirectoryName -File -Force |
                Where-Object { $_.Name -match ('(?i)^{0}\.r\d{{2}}$' -f [regex]::Escape($prefix)) } |
                Sort-Object Name |
                ForEach-Object { $_.FullName })

            if ($rarParts.Count -gt 0) {
                $deletePaths += $rarParts
            }
        }

        $baseName = Get-ArchiveBaseName -Name $file.Name

        [pscustomobject]@{
            Kind = 'Archive'
            DisplayName = $file.Name
            InputPath = $file.FullName
            OutputDirectory = Join-Path $file.DirectoryName $baseName
            DeletePaths = $deletePaths
            StageNeeded = $false
            VolumeFiles = @()
        }
    }
}

function Get-ArchiveTasks {
    $files = @(Get-ArchiveFiles)
    $splitTasks = @(New-SplitTasks -Files $files)
    $regularTasks = @(New-RegularTasks -Files $files)
    return @($splitTasks + $regularTasks | Sort-Object Kind, InputPath)
}

function New-TaskKey {
    param($Task)

    return (($Task.DeletePaths | Sort-Object) -join '|')
}

function Invoke-ArchiveTask {
    param($Task)

    $stageDir = $null
    $inputPath = $Task.InputPath

    try {
        if ($Task.StageNeeded) {
            $stageDir = Join-Path $script:StageRoot ([guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

            foreach ($volume in $Task.VolumeFiles) {
                $linkPath = Join-Path $stageDir $volume.Name
                New-Item -ItemType HardLink -Path $linkPath -Target $volume.FullName | Out-Null
            }

            $inputPath = Join-Path $stageDir (Split-Path -Leaf $Task.InputPath)
            Write-RunLog 'INFO' "Staged split volumes with hard links: $($Task.DisplayName)"
        }

        New-Item -ItemType Directory -Path $Task.OutputDirectory -Force | Out-Null

        Write-RunLog 'INFO' "Extracting [$($Task.Kind)] $($Task.DisplayName) -> $($Task.OutputDirectory)"

        $passwordCandidates = @(Get-PasswordCandidatesForTask -Task $Task)
        for ($attemptIndex = 0; $attemptIndex -lt $passwordCandidates.Count; $attemptIndex++) {
            $candidatePassword = $passwordCandidates[$attemptIndex]
            $args = @(
                'x',
                '-y',
                '-aou',
                '-mmt=on',
                '-bb1',
                '-bsp1'
            )

            if ($candidatePassword) {
                $args += ('-p' + $candidatePassword)
            }

            $args += ('-o' + $Task.OutputDirectory)
            $args += $inputPath

            $safeArgs = @($args | ForEach-Object {
                if ($_.StartsWith('-p', [StringComparison]::OrdinalIgnoreCase)) { '-p******' } else { $_ }
            })

            if ($passwordCandidates.Count -gt 1) {
                Write-RunLog 'INFO' "Password attempt $($attemptIndex + 1) of $($passwordCandidates.Count): $($Task.DisplayName)"
            }
            Write-RunLog 'INFO' "Run: `"$script:SevenZip`" $($safeArgs -join ' ')"

            $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
            $writer = [System.IO.StreamWriter]::new($script:SevenZipLogPath, $true, $utf8NoBom)
            $previousErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                & $script:SevenZip @args 2>&1 | ForEach-Object {
                    if ($_ -is [System.Management.Automation.ErrorRecord]) {
                        $writer.WriteLine($_.ToString())
                    }
                    else {
                        $writer.WriteLine([string]$_)
                    }
                }
            }
            catch {
                $writer.WriteLine($_.Exception.Message)
                Write-RunLog 'ERROR' "7-Zip invocation failed: $($Task.DisplayName); $($_.Exception.Message)"
                continue
            }
            finally {
                $ErrorActionPreference = $previousErrorActionPreference
                $writer.Dispose()
            }

            $exitCode = $LASTEXITCODE
            if ($exitCode -eq 0) {
                Write-RunLog 'INFO' "Extracted successfully: $($Task.DisplayName)"

                if ($DeleteArchives) {
                    foreach ($path in ($Task.DeletePaths | Sort-Object -Unique)) {
                        if (Test-Path -LiteralPath $path -PathType Leaf) {
                            Remove-Item -LiteralPath $path -Force
                            Write-RunLog 'INFO' "Deleted source archive: $path"
                        }
                    }
                }

                return $true
            }

            Write-RunLog 'ERROR' "7-Zip failed with exit code ${exitCode}: $($Task.DisplayName)"
        }

        return $false
    }
    finally {
        if ($stageDir -and (Test-Path -LiteralPath $stageDir)) {
            Remove-Item -LiteralPath $stageDir -Recurse -Force
        }
    }
}

$script:RootPath = (Resolve-Path -LiteralPath $Root).ProviderPath
$script:SevenZip = Resolve-SevenZipPath -ConfiguredPath $SevenZipPath

if (-not $Password) {
    $Password = Get-PasswordFromPath -Path $script:RootPath
}

$script:Password = if ([string]::IsNullOrWhiteSpace($Password)) { $null } else { $Password }
$script:WorkRoot = Join-Path $script:RootPath '.extractutil_work'
$script:StageRoot = Join-Path $script:WorkRoot 'staging'
$logRoot = Join-Path $script:WorkRoot 'logs'

New-Item -ItemType Directory -Path $script:StageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:LogPath = Join-Path $logRoot "extract-tree-$timestamp.log"
$script:SevenZipLogPath = Join-Path $logRoot "7zip-$timestamp.log"

Write-RunLog 'INFO' "Root: $script:RootPath"
Write-RunLog 'INFO' "7-Zip: $script:SevenZip"
Write-RunLog 'INFO' ("Password: " + $(if ($script:Password) { 'provided' } else { 'none' }))
Write-RunLog 'INFO' ("Mode: " + $(if ($AnalyzeOnly) { 'analyze only' } elseif ($DeleteArchives) { 'extract and delete archives after success' } else { 'extract and keep archives' }))

$attempted = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$totalSucceeded = 0
$totalFailed = 0

for ($pass = 1; $pass -le $MaxPasses; $pass++) {
    $tasks = @(Get-ArchiveTasks | Where-Object { -not $attempted.Contains((New-TaskKey -Task $_)) })

    if ($tasks.Count -eq 0) {
        Write-RunLog 'INFO' "No more archive tasks found on pass $pass."
        break
    }

    Write-RunLog 'INFO' "Pass $pass found $($tasks.Count) archive task(s)."

    foreach ($task in $tasks) {
        $key = New-TaskKey -Task $task
        [void]$attempted.Add($key)

        if ($AnalyzeOnly) {
            Write-RunLog 'INFO' "ANALYZE [$($task.Kind)] $($task.DisplayName) -> $($task.OutputDirectory)"
            foreach ($path in $task.DeletePaths) {
                Write-RunLog 'INFO' "  source: $path"
            }
            continue
        }

        $succeeded = $false
        try {
            $succeeded = Invoke-ArchiveTask -Task $task
        }
        catch {
            Write-RunLog 'ERROR' "Task failed unexpectedly and will be skipped: $($task.DisplayName); $($_.Exception.Message)"
            $succeeded = $false
        }

        if ($succeeded) {
            $totalSucceeded++
        }
        else {
            $totalFailed++
        }
    }

    if ($AnalyzeOnly) {
        break
    }
}

Write-RunLog 'INFO' "Completed. Succeeded: $totalSucceeded; Failed: $totalFailed; Log: $script:LogPath"

if ($totalFailed -gt 0) {
    exit 2
}

exit 0
