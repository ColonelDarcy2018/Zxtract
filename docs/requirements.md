# Zxtract Requirements

## Goals
- Provide a Windows 10/11 GUI extraction tool with batch support.
- Support zip, 7z, rar, and common archive types via 7-Zip.
- Provide Explorer context menu actions.

## In Scope
- GUI (WPF) for selecting archives, options, and progress.
- Multi-threaded extraction via 7-Zip (-mmt).
- Queue with configurable parallelism.
- Conflict policy default to rename existing files.
- Log collection and export to file.
- Context menu registration for current user.
- Optional source archive deletion after verified successful extraction.
- Reusable recursive PowerShell workflow for archive trees.

## Out of Scope (for now)
- Archive creation/compression.
- Remote downloads or cloud integration.
- Automatic updates.

## Functional Requirements
1. Add one or multiple archive files from the file dialog or by drag-and-drop.
2. Add all supported archives from a folder (recursive).
3. Default output: same-named subfolder next to the archive.
4. Optional custom output base directory.
5. Conflict policy: rename existing files (default).
6. Password support for encrypted archives.
7. Progress reporting per job.
8. Logs visible in UI and exportable to a file.
9. Explorer context menu:
   - Extract Here
   - Extract To Folder
   - 用 Zxtract 打开
10. Support 7z split archives by using the first volume (`.7z.001`) as the extraction entry.
11. Allow users to add files/folders by drag-and-drop.
12. Password input is evaluated when extraction starts, not only when jobs are added.
13. When enabled, delete source archives only after 7-Zip exits successfully.
14. Recursive workflow infers passwords from path tokens such as `解压密码：xxx`, `密码:xxx`, and `p=xxx`.

## Non-Functional Requirements
- Windows 10/11 compatible.
- Handle large archives (>10GB).
- Speed-first with multi-threading enabled.
- No admin required for normal usage (context menu uses HKCU).
- Portable-friendly layout (7z.exe can be bundled locally).

## Acceptance Criteria
- Can extract zip/7z/rar and `.7z.001` split archives using 7z.exe bundled or installed.
- Can add archives by drag-and-drop and can type the password after adding jobs.
- Context menu actions launch extraction successfully.
- Batch extraction runs without stopping on individual failures.
- Logs can be exported to a user-selected file.
- Recursive workflow can scan, extract, and safely delete completed archive sources.
