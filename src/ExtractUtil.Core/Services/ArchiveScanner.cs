using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using ExtractUtil.Core.Enums;
using ExtractUtil.Core.Models;

namespace ExtractUtil.Core.Services;

/// <summary>
/// Discovers physical archive files and converts multipart layouts into logical work items.
/// Enumeration is performed on a worker thread so callers can safely use it from a UI thread.
/// </summary>
public sealed partial class ArchiveScanner : IArchiveScanner
{
    private const string WorkDirectoryName = ".extractutil_work";

    private static readonly string[] RegularArchiveSuffixes =
    {
        ".tar.bz2",
        ".tar.gz",
        ".tar.xz",
        ".tgz",
        ".zip",
        ".7z",
        ".rar",
        ".tar",
        ".gz",
        ".bz2",
        ".xz"
    };

    private static readonly EnumerationOptions DirectoryEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    public Task<ArchiveScanResult> ScanAsync(
        ArchiveScanOptions options,
        IProgress<ArchiveScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(
            () => ScanCore(options, progress, cancellationToken),
            cancellationToken);
    }

    private static ArchiveScanResult ScanCore(
        ArchiveScanOptions options,
        IProgress<ArchiveScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rootDirectory = ValidateAndNormalizeRoot(options);
        var customOutputRoot = ValidateAndNormalizeCustomOutput(options);
        var startedAt = DateTimeOffset.UtcNow;
        var candidates = new List<ArchiveCandidate>();
        var warnings = new List<string>();
        var directories = new Stack<string>();
        directories.Push(rootDirectory);

        var filesScanned = 0;
        var directoriesScanned = 0;
        var progressInterval = Math.Max(1, options.ProgressReportInterval);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            directoriesScanned++;

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(
                             directory,
                             "*",
                             DirectoryEnumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (Exception ex) when (IsRecoverableFileSystemException(ex))
                    {
                        warnings.Add($"Skipped inaccessible entry '{entry}': {ex.Message}");
                        continue;
                    }

                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (IsWorkDirectory(entry))
                        {
                            continue;
                        }

                        if (options.RecurseSubdirectories)
                        {
                            directories.Push(entry);
                        }

                        continue;
                    }

                    filesScanned++;
                    try
                    {
                        var fileInfo = new FileInfo(entry);
                        if (TryCreateCandidate(fileInfo, out var candidate))
                        {
                            candidates.Add(candidate);
                        }
                    }
                    catch (Exception ex) when (IsRecoverableFileSystemException(ex))
                    {
                        warnings.Add($"Skipped inaccessible file '{entry}': {ex.Message}");
                    }

                    if (filesScanned % progressInterval == 0)
                    {
                        ReportProgress(
                            progress,
                            entry,
                            filesScanned,
                            directoriesScanned,
                            candidates.Count,
                            isGrouping: false);
                    }
                }
            }
            catch (Exception ex) when (IsRecoverableFileSystemException(ex))
            {
                warnings.Add($"Could not fully scan directory '{directory}': {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(
            progress,
            rootDirectory,
            filesScanned,
            directoriesScanned,
            candidates.Count,
            isGrouping: true);

        var context = new ScanContext(
            rootDirectory,
            customOutputRoot,
            options.OutputMode,
            options.EnableSiblingVolumeGrouping,
            options.PreserveRelativeDirectories);
        var items = BuildWorkItems(candidates, context, cancellationToken);

        ReportProgress(
            progress,
            null,
            filesScanned,
            directoriesScanned,
            candidates.Count,
            isGrouping: false);

        return new ArchiveScanResult
        {
            RootDirectory = rootDirectory,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Items = items,
            FilesScanned = filesScanned,
            DirectoriesScanned = directoriesScanned,
            Warnings = warnings.ToArray()
        };
    }

    private static IReadOnlyList<ArchiveWorkItem> BuildWorkItems(
        IReadOnlyList<ArchiveCandidate> candidates,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        var items = new List<ArchiveWorkItem>();
        var consumed = new HashSet<ArchiveCandidate>();

        BuildNumericSplitItems(candidates, consumed, items, context, cancellationToken);
        BuildPartRarItems(candidates, consumed, items, context, cancellationToken);
        BuildLegacyRarItems(candidates, consumed, items, context, cancellationToken);

        foreach (var candidate in candidates
                     .Where(candidate => candidate.Type == CandidateType.Regular && !consumed.Contains(candidate))
                     .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(CreateRegularWorkItem(candidate, context));
        }

        return items
            .OrderBy(item => item.EntryPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind)
            .ToArray();
    }

    private static void BuildNumericSplitItems(
        IReadOnlyList<ArchiveCandidate> candidates,
        ISet<ArchiveCandidate> consumed,
        ICollection<ArchiveWorkItem> items,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        var splitCandidates = candidates.Where(candidate =>
            candidate.Type is CandidateType.SevenZipVolume or CandidateType.ZipVolume);

        var groups = splitCandidates.GroupBy(
            candidate => GetNumericSplitGroupingKey(candidate, context.EnableSiblingVolumeGrouping),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = group
                .OrderBy(candidate => candidate.VolumeNumber)
                .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var part in parts)
            {
                consumed.Add(part);
            }

            var first = parts.First();
            var kind = first.Type == CandidateType.SevenZipVolume
                ? ArchiveKind.SevenZipSplit
                : ArchiveKind.ZipSplit;

            items.Add(CreateMultipartWorkItem(
                kind,
                group.Key,
                first.FamilyName,
                first.OutputBaseName,
                parts,
                context,
                expectedFirstVolume: 1,
                firstVolumeDescription: ".001"));
        }
    }

    private static void BuildPartRarItems(
        IReadOnlyList<ArchiveCandidate> candidates,
        ISet<ArchiveCandidate> consumed,
        ICollection<ArchiveWorkItem> items,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        var groups = candidates
            .Where(candidate => candidate.Type == CandidateType.PartRarVolume)
            .GroupBy(
                candidate => $"{NormalizeKeyPath(candidate.DirectoryPath)}|{candidate.FamilyName}",
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = group
                .OrderBy(candidate => candidate.VolumeNumber)
                .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var part in parts)
            {
                consumed.Add(part);
            }

            var first = parts.First();
            items.Add(CreateMultipartWorkItem(
                ArchiveKind.PartRar,
                $"part-rar|{group.Key}",
                $"{first.OutputBaseName}.part1.rar",
                first.OutputBaseName,
                parts,
                context,
                expectedFirstVolume: 1,
                firstVolumeDescription: ".part1.rar"));
        }
    }

    private static void BuildLegacyRarItems(
        IReadOnlyList<ArchiveCandidate> candidates,
        ISet<ArchiveCandidate> consumed,
        ICollection<ArchiveWorkItem> items,
        ScanContext context,
        CancellationToken cancellationToken)
    {
        var regularRars = candidates
            .Where(candidate => candidate.Type == CandidateType.Regular && candidate.IsPlainRar)
            .ToLookup(
                candidate => $"{NormalizeKeyPath(candidate.DirectoryPath)}|{candidate.OutputBaseName}",
                StringComparer.OrdinalIgnoreCase);

        var legacyGroups = candidates
            .Where(candidate => candidate.Type == CandidateType.LegacyRarVolume)
            .GroupBy(
                candidate => $"{NormalizeKeyPath(candidate.DirectoryPath)}|{candidate.OutputBaseName}",
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in legacyGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var physicalParts = new List<ArchiveCandidate>();

            foreach (var mainArchive in regularRars[group.Key])
            {
                mainArchive.VolumeNumber = 1;
                physicalParts.Add(mainArchive);
                consumed.Add(mainArchive);
            }

            physicalParts.AddRange(group);
            foreach (var part in group)
            {
                consumed.Add(part);
            }

            var parts = physicalParts
                .OrderBy(candidate => candidate.VolumeNumber)
                .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var first = parts.First();

            items.Add(CreateMultipartWorkItem(
                ArchiveKind.LegacyRar,
                $"legacy-rar|{group.Key}",
                $"{first.OutputBaseName}.rar",
                first.OutputBaseName,
                parts,
                context,
                expectedFirstVolume: 1,
                firstVolumeDescription: ".rar"));
        }
    }

    private static ArchiveWorkItem CreateMultipartWorkItem(
        ArchiveKind kind,
        string logicalKey,
        string displayName,
        string outputBaseName,
        IReadOnlyList<ArchiveCandidate> candidates,
        ScanContext context,
        int expectedFirstVolume,
        string firstVolumeDescription)
    {
        var blockingReasons = new List<string>();
        var byVolume = candidates
            .GroupBy(candidate => candidate.VolumeNumber ?? 0)
            .OrderBy(group => group.Key)
            .ToArray();

        var duplicateVolumes = byVolume
            .Where(group => group.Count() > 1)
            .Select(group => FormatVolumeNumber(group.Key, kind))
            .ToArray();
        if (duplicateVolumes.Length > 0)
        {
            blockingReasons.Add($"Duplicate or ambiguous volume(s): {string.Join(", ", duplicateVolumes)}.");
        }

        var availableNumbers = byVolume.Select(group => group.Key).Where(number => number > 0).ToArray();
        if (!availableNumbers.Contains(expectedFirstVolume))
        {
            blockingReasons.Add($"Required first volume {firstVolumeDescription} is missing.");
        }

        if (availableNumbers.Length > 0)
        {
            var maximum = availableNumbers.Max();
            var availableSet = availableNumbers.ToHashSet();
            var missing = Enumerable.Range(expectedFirstVolume, maximum - expectedFirstVolume + 1)
                .Where(number => !availableSet.Contains(number))
                .Select(number => FormatVolumeNumber(number, kind))
                .ToArray();

            if (missing.Length > 0)
            {
                blockingReasons.Add($"Missing volume(s): {string.Join(", ", missing)}.");
            }
        }

        var emptyParts = candidates
            .Where(candidate => candidate.Length == 0)
            .Select(candidate => candidate.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (emptyParts.Length > 0)
        {
            blockingReasons.Add($"Empty source file(s): {string.Join(", ", emptyParts)}.");
        }

        var entry = candidates
            .Where(candidate => candidate.VolumeNumber == expectedFirstVolume)
            .OrderBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? candidates.OrderBy(candidate => candidate.VolumeNumber)
                .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .First();

        var sourceParts = candidates
            .OrderBy(candidate => candidate.VolumeNumber)
            .ThenBy(candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new ArchiveSourcePart
            {
                Path = candidate.FullPath,
                VolumeNumber = candidate.VolumeNumber,
                Length = candidate.Length
            })
            .ToArray();

        return new ArchiveWorkItem
        {
            Kind = kind,
            LogicalKey = logicalKey,
            DisplayName = displayName,
            EntryPath = entry.FullPath,
            OutputDirectory = ResolveOutputDirectory(entry.DirectoryPath, outputBaseName, context),
            RelativeDirectory = GetRelativeDirectory(context.RootDirectory, entry.DirectoryPath),
            SourceParts = sourceParts,
            TotalSourceBytes = SumLengths(sourceParts),
            IsCrossDirectoryVolumeSet = candidates
                .Select(candidate => candidate.DirectoryPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any(),
            CanExtract = blockingReasons.Count == 0,
            BlockingReasons = blockingReasons.ToArray()
        };
    }

    private static ArchiveWorkItem CreateRegularWorkItem(ArchiveCandidate candidate, ScanContext context)
    {
        var blockingReasons = candidate.Length == 0
            ? new[] { $"Empty source file: {candidate.FileName}." }
            : Array.Empty<string>();
        var sourcePart = new ArchiveSourcePart
        {
            Path = candidate.FullPath,
            VolumeNumber = null,
            Length = candidate.Length
        };

        return new ArchiveWorkItem
        {
            Kind = ArchiveKind.Regular,
            LogicalKey = $"regular|{NormalizeKeyPath(candidate.FullPath)}",
            DisplayName = candidate.FileName,
            EntryPath = candidate.FullPath,
            OutputDirectory = ResolveOutputDirectory(candidate.DirectoryPath, candidate.OutputBaseName, context),
            RelativeDirectory = GetRelativeDirectory(context.RootDirectory, candidate.DirectoryPath),
            SourceParts = new[] { sourcePart },
            TotalSourceBytes = candidate.Length,
            IsCrossDirectoryVolumeSet = false,
            CanExtract = blockingReasons.Length == 0,
            BlockingReasons = blockingReasons
        };
    }

    private static string GetNumericSplitGroupingKey(ArchiveCandidate candidate, bool enableSiblingGrouping)
    {
        var directoryKey = NormalizeKeyPath(candidate.DirectoryPath);
        if (!enableSiblingGrouping)
        {
            return $"split|directory|{directoryKey}|{candidate.FamilyName}";
        }

        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(candidate.DirectoryPath));
        var match = SiblingPartDirectoryRegex().Match(directoryName);
        if (!match.Success ||
            !match.Groups["stem"].Value.Equals(candidate.OutputBaseName, StringComparison.OrdinalIgnoreCase))
        {
            return $"split|directory|{directoryKey}|{candidate.FamilyName}";
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(candidate.DirectoryPath));
        if (string.IsNullOrWhiteSpace(parent))
        {
            return $"split|directory|{directoryKey}|{candidate.FamilyName}";
        }

        return $"split|siblings|{NormalizeKeyPath(parent)}|{match.Groups["stem"].Value}|{candidate.FamilyName}";
    }

    private static string ResolveOutputDirectory(
        string archiveDirectory,
        string outputBaseName,
        ScanContext context)
    {
        return context.OutputMode switch
        {
            ArchiveOutputMode.ArchiveDirectory => archiveDirectory,
            ArchiveOutputMode.CustomRoot when context.PreserveRelativeDirectories => Path.GetFullPath(Path.Combine(
                context.CustomOutputRoot!,
                GetRelativeDirectory(context.RootDirectory, archiveDirectory),
                outputBaseName)),
            ArchiveOutputMode.CustomRoot => Path.GetFullPath(Path.Combine(context.CustomOutputRoot!, outputBaseName)),
            _ => Path.GetFullPath(Path.Combine(archiveDirectory, outputBaseName))
        };
    }

    private static string GetRelativeDirectory(string rootDirectory, string directory)
    {
        var relative = Path.GetRelativePath(rootDirectory, directory);
        return relative == "." ? string.Empty : relative;
    }

    private static long SumLengths(IEnumerable<ArchiveSourcePart> parts)
    {
        long total = 0;
        foreach (var part in parts)
        {
            try
            {
                total = checked(total + part.Length);
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }

        return total;
    }

    private static string FormatVolumeNumber(int number, ArchiveKind kind)
    {
        return kind switch
        {
            ArchiveKind.SevenZipSplit or ArchiveKind.ZipSplit => number.ToString("D3", CultureInfo.InvariantCulture),
            ArchiveKind.PartRar => $"part{number}",
            ArchiveKind.LegacyRar when number == 1 => ".rar",
            ArchiveKind.LegacyRar => $".r{number - 2:D2}",
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static bool TryCreateCandidate(FileInfo file, out ArchiveCandidate candidate)
    {
        var fileName = file.Name;

        var splitMatch = NumericSplitRegex().Match(fileName);
        if (splitMatch.Success)
        {
            var extension = splitMatch.Groups["extension"].Value;
            var stem = splitMatch.Groups["stem"].Value;
            var familyName = $"{stem}.{extension}";
            candidate = new ArchiveCandidate(
                file.FullName,
                file.DirectoryName ?? string.Empty,
                fileName,
                file.Length,
                extension.Equals("7z", StringComparison.OrdinalIgnoreCase)
                    ? CandidateType.SevenZipVolume
                    : CandidateType.ZipVolume,
                familyName,
                stem,
                int.Parse(splitMatch.Groups["volume"].Value, CultureInfo.InvariantCulture),
                isPlainRar: false);
            return true;
        }

        var partRarMatch = PartRarRegex().Match(fileName);
        if (partRarMatch.Success &&
            int.TryParse(partRarMatch.Groups["volume"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var partNumber))
        {
            var stem = partRarMatch.Groups["stem"].Value;
            candidate = new ArchiveCandidate(
                file.FullName,
                file.DirectoryName ?? string.Empty,
                fileName,
                file.Length,
                CandidateType.PartRarVolume,
                $"{stem}.rar",
                stem,
                partNumber,
                isPlainRar: false);
            return true;
        }

        var legacyRarMatch = LegacyRarRegex().Match(fileName);
        if (legacyRarMatch.Success &&
            int.TryParse(legacyRarMatch.Groups["volume"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var legacyNumber))
        {
            var stem = legacyRarMatch.Groups["stem"].Value;
            candidate = new ArchiveCandidate(
                file.FullName,
                file.DirectoryName ?? string.Empty,
                fileName,
                file.Length,
                CandidateType.LegacyRarVolume,
                $"{stem}.rar",
                stem,
                legacyNumber + 2,
                isPlainRar: false);
            return true;
        }

        foreach (var suffix in RegularArchiveSuffixes)
        {
            if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var outputBaseName = fileName[..^suffix.Length];
            candidate = new ArchiveCandidate(
                file.FullName,
                file.DirectoryName ?? string.Empty,
                fileName,
                file.Length,
                CandidateType.Regular,
                fileName,
                outputBaseName,
                volumeNumber: null,
                isPlainRar: suffix.Equals(".rar", StringComparison.OrdinalIgnoreCase));
            return true;
        }

        candidate = null!;
        return false;
    }

    private static string ValidateAndNormalizeRoot(ArchiveScanOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new ArgumentException("A scan root directory is required.", nameof(options));
        }

        var root = Path.GetFullPath(options.RootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Scan root directory was not found: {root}");
        }

        if (IsWorkDirectory(root))
        {
            throw new ArgumentException("The .extractutil_work directory cannot be used as a scan root.", nameof(options));
        }

        var attributes = File.GetAttributes(root);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("A reparse point cannot be used as a scan root.");
        }

        return Path.TrimEndingDirectorySeparator(root);
    }

    private static string? ValidateAndNormalizeCustomOutput(ArchiveScanOptions options)
    {
        if (options.OutputMode != ArchiveOutputMode.CustomRoot)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.CustomOutputRoot))
        {
            throw new ArgumentException("CustomOutputRoot is required when OutputMode is CustomRoot.", nameof(options));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.CustomOutputRoot));
    }

    private static bool IsWorkDirectory(string path)
    {
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(path))
            .Equals(WorkDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoverableFileSystemException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException;
    }

    private static string NormalizeKeyPath(string path)
    {
        return Path.GetFullPath(path).ToUpperInvariant();
    }

    private static void ReportProgress(
        IProgress<ArchiveScanProgress>? progress,
        string? currentPath,
        int filesScanned,
        int directoriesScanned,
        int candidatesFound,
        bool isGrouping)
    {
        progress?.Report(new ArchiveScanProgress
        {
            CurrentPath = currentPath,
            FilesScanned = filesScanned,
            DirectoriesScanned = directoriesScanned,
            ArchiveCandidatesFound = candidatesFound,
            IsGrouping = isGrouping
        });
    }

    [GeneratedRegex(@"^(?<stem>.+)\.(?<extension>7z|zip)\.(?<volume>\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericSplitRegex();

    [GeneratedRegex(@"^(?<stem>.+)\.part(?<volume>\d{1,6})\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartRarRegex();

    [GeneratedRegex(@"^(?<stem>.+)\.r(?<volume>\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyRarRegex();

    [GeneratedRegex(@"^(?<stem>.+)-(?<part>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiblingPartDirectoryRegex();

    private enum CandidateType
    {
        Regular,
        SevenZipVolume,
        ZipVolume,
        PartRarVolume,
        LegacyRarVolume
    }

    private sealed class ArchiveCandidate
    {
        public ArchiveCandidate(
            string fullPath,
            string directoryPath,
            string fileName,
            long length,
            CandidateType type,
            string familyName,
            string outputBaseName,
            int? volumeNumber,
            bool isPlainRar)
        {
            FullPath = fullPath;
            DirectoryPath = directoryPath;
            FileName = fileName;
            Length = length;
            Type = type;
            FamilyName = familyName;
            OutputBaseName = outputBaseName;
            VolumeNumber = volumeNumber;
            IsPlainRar = isPlainRar;
        }

        public string FullPath { get; }

        public string DirectoryPath { get; }

        public string FileName { get; }

        public long Length { get; }

        public CandidateType Type { get; }

        public string FamilyName { get; }

        public string OutputBaseName { get; }

        public int? VolumeNumber { get; set; }

        public bool IsPlainRar { get; }
    }

    private sealed record ScanContext(
        string RootDirectory,
        string? CustomOutputRoot,
        ArchiveOutputMode OutputMode,
        bool EnableSiblingVolumeGrouping,
        bool PreserveRelativeDirectories);
}
