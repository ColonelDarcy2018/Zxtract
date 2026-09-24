# Zxtract

Windows GUI extraction utility with batch support, 7-Zip split archive support,
drag-and-drop input, password handling, and an optional safe delete-after-success
workflow.

## GUI workflow

The fixed top toolbar provides two workflow tabs, the current status, and run
controls. Tabs stay visible while settings or tasks scroll. Each mode shows
only its relevant input controls; passwords and output settings are shared.
Task counts use a compact status strip and the run log expands on demand.

- To extract one or more individual archives, choose **添加压缩文件...**, then
  choose **开始文件解压** (or press **F5**). The status beside the section title
  shows how many files are waiting.
- To process a whole directory, select a root directory and choose **仅扫描** or
  **扫描并解压**. Recursive nested-archive detection and distributed-volume
  grouping apply to this folder workflow.
- Files and folders can also be dragged into the window. Dropped files join the
  ordinary-file queue; a dropped folder becomes the folder-scan root.
- Select a failed or canceled ordinary-file task and choose **重试选中** to run
  it again from the original path with the current password candidates. Choose
  **复制路径** to copy one source path, or every source-volume path for a grouped
  split archive.
- Password candidates and output/conflict settings are shared by both
  workflows. The current candidate list stays in the common bar at the top of
  the main window; enter one password per line and candidates are tried in
  order.
- Built-in path inference still recognizes `解压密码`, `密码`, `p`, `pass`,
  `password`, and `pwd`. Open **密码助手...** to add custom templates under
  **路径推断规则**, one per line, using `{password}` as the captured value (for
  example `提取码：{password}`). Custom templates are saved locally.
- Open **密码助手...** to manage the local password library. Frequently used
  passwords can be added back to the current candidate list with one click.
  Stored values are protected with Windows DPAPI for the current user and are
  never written to the run log.
- `.7z.001/.7z.002/...` split archives are supported by extracting from the
  first volume. If a later volume is selected or dropped, the app resolves it
  back to `.001` when that first volume exists in the same folder.
- The password box is read when extraction starts, so you can add files first
  and type or change the password afterward.
- Enable **Delete source after success** only when you want source archives
  removed. The GUI deletes only after 7-Zip exits with code `0`; warning results
  keep the source archives.

Password-helper settings are stored at:

```text
%LOCALAPPDATA%\Zxtract\password-settings.json
```

The JSON file contains custom rule text and DPAPI-protected password blobs, not
plain-text password values.

## Recursive one-click workflow

For a reusable recursive workflow, use:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Expand-ArchiveTree.ps1 -Root "D:\archive-root\解压密码：示例密码" -DeleteArchives
```

Or drag a folder onto:

```text
tools\Extract-ArchiveTree.cmd
```

The recursive script:

- scans folders repeatedly, so archives produced by extracting earlier archives
  are processed in later passes;
- reads passwords from path text such as `解压密码：xxx`, `密码: xxx`, `p=0317`,
  `pass=xxx`, `password=xxx`, and `pwd=xxx`;
- supports normal archives, multipart RAR, and `.7z.001/.7z.002/...`;
- can stage split 7z volumes that were placed in sibling folders like
  `name-1`, `name-2` by using hard links under `.extractutil_work\staging`;
- skips empty archive files and logs them as warnings;
- deletes source archives only after a successful 7-Zip extraction;
- writes logs under `.extractutil_work\logs`.

See:
- docs/requirements.md
- docs/technical.md
- docs/modules.md
