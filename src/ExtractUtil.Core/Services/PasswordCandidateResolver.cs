using System.Text.RegularExpressions;

namespace ExtractUtil.Core.Services;

public static partial class PasswordCandidateResolver
{
    public static IReadOnlyList<string> Resolve(
        string archivePath,
        IEnumerable<string>? explicitCandidates,
        bool inferFromPath,
        IEnumerable<string>? successfulCandidates = null,
        IEnumerable<string>? customInferenceRules = null)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddRange(successfulCandidates, result, seen);

        if (inferFromPath)
        {
            var parts = Path.GetFullPath(archivePath)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

            for (var index = parts.Length - 1; index >= 0; index--)
            {
                AddCustomRuleMatches(parts[index], customInferenceRules, result, seen);
                foreach (Match match in PasswordTokenRegex().Matches(parts[index]))
                {
                    Add(match.Groups[1].Value, result, seen);
                }
            }
        }

        AddRange(explicitCandidates, result, seen);
        return result;
    }

    public static IReadOnlyList<string> ParseRuleTemplates(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && IsValidRuleTemplate(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsValidRuleTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var first = template.IndexOf("{password}", StringComparison.OrdinalIgnoreCase);
        return first >= 0 &&
               template.IndexOf("{password}", first + "{password}".Length, StringComparison.OrdinalIgnoreCase) < 0;
    }

    public static IReadOnlyList<string> ParseMultiline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            Add(line, result, seen);
        }

        return result;
    }

    private static void AddRange(IEnumerable<string>? source, ICollection<string> result, ISet<string> seen)
    {
        if (source is null)
        {
            return;
        }

        foreach (var value in source)
        {
            Add(value, result, seen);
        }
    }

    private static void AddCustomRuleMatches(
        string pathPart,
        IEnumerable<string>? templates,
        ICollection<string> result,
        ISet<string> seen)
    {
        if (templates is null)
        {
            return;
        }

        foreach (var template in templates)
        {
            if (!TryBuildTemplatePattern(template, out var pattern))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(
                         pathPart,
                         pattern,
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                         TimeSpan.FromMilliseconds(100)))
            {
                Add(match.Groups["password"].Value, result, seen);
            }
        }
    }

    private static bool TryBuildTemplatePattern(string? template, out string pattern)
    {
        pattern = string.Empty;
        if (!IsValidRuleTemplate(template))
        {
            return false;
        }

        var markerIndex = template!.IndexOf("{password}", StringComparison.OrdinalIgnoreCase);
        var prefix = template[..markerIndex];
        var suffix = template[(markerIndex + "{password}".Length)..];
        var capture = suffix.Length == 0
            ? @"(?<password>[^\s\\/\)\]\}）,，;；]+)"
            : @"(?<password>.+?)";
        pattern = Regex.Escape(prefix) + capture + Regex.Escape(suffix);
        return true;
    }

    private static void Add(string? value, ICollection<string> result, ISet<string> seen)
    {
        var clean = value?.Trim();
        if (!string.IsNullOrWhiteSpace(clean) && seen.Add(clean))
        {
            result.Add(clean);
        }
    }

    [GeneratedRegex(
        @"(?:(?:解压密码|密码)|(?:^|[\(\[\{（【\s_-])(?:p|pass|password|pwd))\s*[:：=]\s*([^\s\\/\)\]\}）,，;；]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordTokenRegex();
}
