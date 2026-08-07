using LiveAuthCore.Data.Entities;

namespace LiveAuthCore.Services.Meter;

public sealed record MeterRouteDecision(
    MeterRouteRule? Rule,
    string NormalizedRoute,
    long PriceSats,
    bool IsBlocked)
{
    public bool IsFree => !IsBlocked && PriceSats == 0;
}

public interface IMeterRouteMatcher
{
    MeterRouteDecision Match(MeterProjectSettings settings, IEnumerable<MeterRouteRule> rules, string method, string path);
    string? ValidatePattern(string pattern);
}

public sealed class MeterRouteMatcher : IMeterRouteMatcher
{
    public MeterRouteDecision Match(MeterProjectSettings settings, IEnumerable<MeterRouteRule> rules, string method, string path)
    {
        var normalizedMethod = method.Trim().ToUpperInvariant();
        var normalizedPath = NormalizePath(path);
        var match = rules
            .Where(x => x.Enabled && (x.HttpMethod == "*" ||
                string.Equals(x.HttpMethod, normalizedMethod, StringComparison.OrdinalIgnoreCase)))
            .Where(x => IsMatch(x.PathPattern, normalizedPath))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => Specificity(x.PathPattern))
            .ThenBy(x => x.Id)
            .FirstOrDefault();

        if (match != null)
            return new(match, match.PathPattern, Math.Max(0, match.PriceSats), false);

        return settings.UnmatchedRouteBehavior.ToUpperInvariant() switch
        {
            MeterUnmatchedRouteBehaviors.Free => new(null, normalizedPath, 0, false),
            MeterUnmatchedRouteBehaviors.DefaultPrice => new(null, normalizedPath, Math.Max(0, settings.DefaultPriceSats), false),
            _ => new(null, normalizedPath, 0, true)
        };
    }

    public string? ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !pattern.StartsWith('/') || pattern.Length > 1024)
            return "Path pattern must begin with '/' and be at most 1024 characters.";
        if (pattern.Contains('\\') || pattern.Contains("..", StringComparison.Ordinal) || pattern.Contains('\0'))
            return "Path pattern contains an unsafe segment.";
        var segments = Split(pattern);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment == "*" && i != segments.Length - 1)
                return "The '*' wildcard is supported only as the final segment.";
            if (segment.Contains('*') && segment != "*")
                return "The '*' wildcard must occupy an entire segment.";
            if (segment.StartsWith(':') && (segment.Length == 1 || segment.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
                return "Template parameters use ':name' with letters, numbers, or underscores.";
        }
        return null;
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        if (!path.StartsWith('/')) path = "/" + path;
        if (path.Contains('\\') || path.Contains('\0'))
            throw new MeterSecurityException("invalid_path", "Request path is not valid.");
        var segments = Split(path);
        if (segments.Any(x => x is "." or ".."))
            throw new MeterSecurityException("invalid_path", "Relative path segments are not allowed.");
        return segments.Length == 0 ? "/" : "/" + string.Join('/', segments);
    }

    private static bool IsMatch(string pattern, string path)
    {
        var expected = Split(pattern);
        var actual = Split(path);
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] == "*") return true;
            if (i >= actual.Length) return false;
            if (expected[i].StartsWith(':')) continue;
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal)) return false;
        }
        return actual.Length == expected.Length;
    }

    private static int Specificity(string pattern) => Split(pattern).Sum(x => x switch
    {
        "*" => 0,
        _ when x.StartsWith(':') => 10,
        _ => 100
    });

    private static string[] Split(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
