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

## UI Regression Check

The Windows-only smoke check loads the application's actual task-row XAML and
checks progress binding during queued, running, and completed file/folder tasks:

```powershell
dotnet run --project .\tools\ExtractUtil.UiSmoke\ExtractUtil.UiSmoke.csproj -c Release
```

An optional directory argument also scans that directory and lays out its tasks
in the real DataGrid. This check does not extract archives or open a window:

```powershell
dotnet run --project .\tools\ExtractUtil.UiSmoke\ExtractUtil.UiSmoke.csproj -c Release -- "D:\archive-root"
```

Progress is display-only: the progress bar must use `Mode=OneWay` because
`ProgressPercent` has no public setter and WPF's default range-value binding is
two-way. Omitting the mode causes an unhandled binding exception when a task row
is created.

To render the current WPF UI with synthetic demonstration tasks and check status
hierarchy, selection stability, filtering, pause feedback, and panel switching:

```powershell
dotnet run --project .\tools\ExtractUtil.UiSmoke\ExtractUtil.UiSmoke.csproj -c Release -- --preview .\.build-check\ui-preview
```

Preview images are generated directly from the WPF visual tree at two window
widths; no user archives are extracted. Ordinary progress updates no longer reset
the collection view; it is refreshed only when a task enters or leaves a status
filter, or when the user changes the filter, search, or workflow.
