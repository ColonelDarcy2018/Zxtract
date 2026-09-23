using ExtractUtil.Core.Enums;

namespace ExtractUtil.Core.Services;

// 将一次密码尝试的隔离输出合并到最终目录，避免失败尝试污染用户文件。
public sealed class OutputCommitter
{
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false
    };

    public async Task CommitAsync(
        string attemptDirectory,
        string destinationDirectory,
        ConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var sourceRoot = EnsureDirectoryPath(attemptDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SafeEnumeration)
                     .OrderBy(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            var target = ResolveContainedPath(destinationRoot, relativePath);
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SafeEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var target = ResolveContainedPath(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            switch (conflictPolicy)
            {
                case ConflictPolicy.Skip when File.Exists(target):
                    File.Delete(file);
                    break;

                case ConflictPolicy.Overwrite:
                    await MoveOrCopyAsync(file, target, overwrite: true, cancellationToken).ConfigureAwait(false);
                    break;

                case ConflictPolicy.RenameExisting when File.Exists(target):
                    target = FindAvailablePath(target);
                    await MoveOrCopyAsync(file, target, overwrite: false, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    await MoveOrCopyAsync(file, target, overwrite: false, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static async Task MoveOrCopyAsync(
        string source,
        string destination,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        try
        {
            File.Move(source, destination, overwrite);
        }
        catch (IOException) when (!PathsShareRoot(source, destination))
        {
            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                destination,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            File.Delete(source);
        }
    }

    private static string FindAvailablePath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);

        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Unable to find an available name for {path}.");
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Unsafe output path escaped destination: {relativePath}");
        }

        return candidate;
    }

    private static string EnsureDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        return fullPath;
    }

    private static bool PathsShareRoot(string first, string second)
    {
        return string.Equals(Path.GetPathRoot(first), Path.GetPathRoot(second), StringComparison.OrdinalIgnoreCase);
    }
}
