# Zxtract Technical Design

## Stack
- .NET 8, WPF (GUI)
- Core library (pure .NET) for extraction and logging
- 7-Zip CLI (7z.exe) as the extraction engine

## Architecture
- Zxtract: user-facing product and executable name
- ExtractUtil.Core: domain models, extractor interface, 7-Zip integration, job queue, logging
- ExtractUtil.App: WPF UI, view models, shell integration, log export
- tools/7zip: optional local 7z.exe for portable distribution

## Extraction Engine
- Uses 7z.exe with arguments:
  - x (extract), -y (assume yes), -bsp1 (progress)
  - -aou (rename existing), -mmt=on (multi-thread)
  - -pPASSWORD when provided, -o<output>
- Exit codes 0/1 treated as success (1 indicates warnings).
- GUI source deletion is opt-in and runs only after exit code 0. For standard
  split archives it removes the sibling `.7z.001/.7z.002/...` set together.
- Password is captured at start time, so queued jobs use the latest password
  typed before the run begins.

## Recursive Workflow

- `tools/Expand-ArchiveTree.ps1` recursively scans an archive tree and repeats
  passes to process archives created by earlier extractions.
- It supports normal archives, multipart RAR, and `.7z.001/.7z.002/...`.
- Split 7z volumes spread across sibling folders are staged with hard links in
  `.extractutil_work/staging` before extraction.
- It infers passwords from folder/file name tokens and tries candidates in
  nearest-path-first order before the root/default password.
- Empty archive-looking files are skipped and logged as warnings.
- With `-DeleteArchives`, source archives are removed only after successful
  extraction. Logs are written under `.extractutil_work/logs`.

## Concurrency
- JobQueue limits concurrent extractions with SemaphoreSlim.
- Each job uses 7-Zip internal multi-threading.

## Logging
- InMemoryLogSink stores timestamped entries.
- UI subscribes to new log entries for display.
- Export writes log snapshot to a user-selected file.

## Shell Integration
- Uses HKCU registry keys under
  Software\Classes\SystemFileAssociations\.ext\shell\...
- Commands invoke app with --shell-* flags.

## Packaging
- Portable: include tools/7zip/7z.exe alongside app output.
- Installer can be added later; context menu registration is in-app.

## Configuration
- Runtime options are set in the UI (output base, conflict policy, password, parallelism).
