using System.Diagnostics;
using System.Text.RegularExpressions;
using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

// 通过 7z.exe 执行解压，负责拼接参数、解析进度与错误信息。
public sealed class SevenZipExtractor : IArchiveExtractor
{
    // 7-Zip 的标准输出中通常包含类似 " 23% file.txt" 的进度行。
    private static readonly Regex ProgressRegex = new(@"^\s*(\d+)%\s*(.*)$", RegexOptions.Compiled);
    private const int MaxDiagnosticLines = 500;

    private readonly ProcessRunner _processRunner;
    private readonly string? _explicitPath;

    public SevenZipExtractor(ProcessRunner processRunner, string? explicitPath = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _explicitPath = explicitPath;
    }

    // 执行解压：校验输入、定位 7z.exe、运行进程，并将进度/日志回传给上层。
    public async Task<ExtractResult> ExtractAsync(
        ExtractJob job,
        IProgress<ExtractProgress> progress,
        ILogSink log,
        CancellationToken cancellationToken)
    {
        // 先校验输出目录，避免 7z.exe 运行失败。
        if (string.IsNullOrWhiteSpace(job.Options.OutputDirectory))
        {
            return new ExtractResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Output directory is required.",
                FailureKind = ExtractFailureKind.Unknown
            };
        }

        // 依次尝试显式路径、环境变量、应用目录、系统安装路径。
        var sevenZipPath = ResolveSevenZipPath(log);
        if (string.IsNullOrWhiteSpace(sevenZipPath))
        {
            return new ExtractResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "7z.exe not found. Place it in tools\\7zip\\7z.exe or install 7-Zip.",
                FailureKind = ExtractFailureKind.ToolNotFound
            };
        }

        Directory.CreateDirectory(job.Options.OutputDirectory);

        // 组合 7-Zip CLI 参数。
        var arguments = BuildArguments(job, job.Options);
        log.Append(LogLevel.Info, BuildSafeCommandLine(sevenZipPath, arguments));

        var startInfo = new ProcessStartInfo
        {
            FileName = sevenZipPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var diagnosticLines = new List<string>();

        try
        {
            var exitCode = await _processRunner.RunAsync(
                startInfo,
                line =>
                {
                    AddDiagnosticLine(diagnosticLines, line);
                    HandleStdOut(line, progress, log);
                },
                line =>
                {
                    AddDiagnosticLine(diagnosticLines, line);
                    log.Append(LogLevel.Error, line);
                },
                cancellationToken).ConfigureAwait(false);

            // 7-Zip 约定：0 为成功，1 为成功但有警告。
            var success = exitCode == 0 || exitCode == 1;
            var diagnostics = string.Join(Environment.NewLine, diagnosticLines);

            return new ExtractResult
            {
                Success = success,
                ExitCode = exitCode,
                ErrorMessage = success ? null : diagnostics,
                WarningMessage = exitCode == 1 ? diagnostics : null,
                FailureKind = success ? ExtractFailureKind.None : ClassifyFailure(diagnostics)
            };
        }
        catch (OperationCanceledException)
        {
            log.Append(LogLevel.Warning, "Extraction canceled.");
            return new ExtractResult
            {
                Success = false,
                ExitCode = -2,
                ErrorMessage = "Canceled",
                FailureKind = ExtractFailureKind.Canceled
            };
        }
    }

    private static void AddDiagnosticLine(ICollection<string> lines, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (lines)
        {
            if (lines.Count < MaxDiagnosticLines)
            {
                lines.Add(line);
            }
        }
    }

    private static ExtractFailureKind ClassifyFailure(string diagnostics)
    {
        if (ContainsAny(diagnostics,
                "wrong password",
                "incorrect password",
                "can not open encrypted archive",
                "data error in encrypted file"))
        {
            return ExtractFailureKind.WrongPassword;
        }

        if (ContainsAny(diagnostics,
                "missing volume",
                "unexpected end of archive",
                "cannot find the file specified"))
        {
            return ExtractFailureKind.MissingVolume;
        }

        if (ContainsAny(diagnostics, "access is denied", "permission denied"))
        {
            return ExtractFailureKind.AccessDenied;
        }

        if (ContainsAny(diagnostics,
                "can not open the file as archive",
                "cannot open the file as archive",
                "is not archive"))
        {
            return ExtractFailureKind.InvalidArchive;
        }

        if (ContainsAny(diagnostics, "crc failed", "data error", "archive is corrupted"))
        {
            return ExtractFailureKind.CorruptArchive;
        }

        return ExtractFailureKind.Unknown;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    // 根据用户选项生成 7-Zip 命令行参数。
    private static List<string> BuildArguments(ExtractJob job, ExtractOptions options)
    {
        var args = new List<string>
        {
            "x",
            "-y",
            "-bsp1",
            "-bb1",
            GetConflictSwitch(options.ConflictPolicy)
        };

        if (options.EnableMultiThread)
        {
            args.Add("-mmt=on");
        }

        if (!string.IsNullOrWhiteSpace(options.Password))
        {
            // 7-Zip 要求密码与 -p 在同一个参数内（不能有空格）。
            args.Add("-p" + options.Password);
        }

        args.Add("-o" + options.OutputDirectory);
        args.Add(job.ArchivePath);

        return args;
    }

    private static string GetConflictSwitch(ConflictPolicy policy)
    {
        return policy switch
        {
            ConflictPolicy.RenameExisting => "-aou",
            ConflictPolicy.Overwrite => "-aot",
            ConflictPolicy.Skip => "-aos",
            _ => "-aou"
        };
    }

    // 标准输出既可能是进度行，也可能是普通信息行。
    private static void HandleStdOut(string line, IProgress<ExtractProgress> progress, ILogSink log)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var match = ProgressRegex.Match(line);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
        {
            var current = match.Groups[2].Value.Trim();
            progress.Report(new ExtractProgress
            {
                Percentage = percent,
                CurrentFile = string.IsNullOrWhiteSpace(current) ? null : current,
                RawLine = line
            });
            return;
        }

        log.Append(LogLevel.Info, line);
    }

    // 尝试多种位置定位 7z.exe，便于便携版和系统安装版共存。
    private string? ResolveSevenZipPath(ILogSink log)
    {
        if (!string.IsNullOrWhiteSpace(_explicitPath) && File.Exists(_explicitPath))
        {
            return _explicitPath;
        }

        var envPath = Environment.GetEnvironmentVariable("EXTRACTUTIL_7Z_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var baseDir = AppContext.BaseDirectory;
        var localCandidates = new[]
        {
            Path.Combine(baseDir, "7z.exe"),
            Path.Combine(baseDir, "tools", "7zip", "7z.exe")
        };

        foreach (var candidate in localCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var installedCandidates = new[]
        {
            Path.Combine(programFiles, "7-Zip", "7z.exe"),
            Path.Combine(programFilesX86, "7-Zip", "7z.exe")
        };

        foreach (var candidate in installedCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        log.Append(LogLevel.Error, "7z.exe not found. Configure EXTRACTUTIL_7Z_PATH or install 7-Zip.");
        return null;
    }

    // 记录命令行时隐藏密码，避免泄露。
    private static string BuildSafeCommandLine(string exePath, IReadOnlyList<string> arguments)
    {
        var sanitized = arguments.Select(arg =>
            arg.StartsWith("-p", StringComparison.OrdinalIgnoreCase) ? "-p******" : arg);

        return $"Run: \"{exePath}\" {string.Join(' ', sanitized.Select(QuoteIfNeeded))}";
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
