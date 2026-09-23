# ExtractUtil Modules

## ExtractUtil.Core
- Models: ExtractJob, ExtractOptions, ExtractProgress, ExtractResult, LogEntry
- Enums: ConflictPolicy, ExtractJobStatus, LogLevel
- Services:
  - IArchiveExtractor
  - SevenZipExtractor (7z CLI integration)
  - ProcessRunner (process execution)
  - JobQueue (concurrency)
  - InMemoryLogSink (logging)

## ExtractUtil.App
- UI: MainWindow.xaml
- ViewModels:
  - MainViewModel (commands, queue orchestration)
  - JobItemViewModel (per-job state)
  - RelayCommand (ICommand utility)
- Services:
  - ContextMenuRegistration (Explorer integration)
  - ShellArgumentParser (CLI args)

## tools/7zip
- Optional 7z.exe for portable distribution

## tools
- Expand-ArchiveTree.ps1: recursive archive-tree workflow
- Extract-ArchiveTree.cmd: drag/drop wrapper for the recursive workflow

## docs
- requirements.md, technical.md, modules.md
