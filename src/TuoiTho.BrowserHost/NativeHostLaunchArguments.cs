using System.Globalization;
using TuoiTho.Core.Policy;

namespace TuoiTho.BrowserHost;

/// <summary>Validates only the Chrome/Edge Native Messaging launch contract before protocol I/O starts.</summary>
public static class NativeHostLaunchArguments
{
    public static bool TryValidate(string[] args, BrowserControlConfiguration configuration, out string error)
    {
        error = string.Empty;
        if (args.Length is < 1 or > 2) { error = "NATIVE_ORIGIN_REQUIRED"; return false; }
        var originAllowed = configuration.AllowedExtensionIds
            .Select(id => "chrome-extension://" + id + "/")
            .Any(origin => string.Equals(args[0], origin, StringComparison.Ordinal));
        if (!originAllowed) { error = "UNAUTHORIZED_NATIVE_ORIGIN"; return false; }
        if (args.Length == 1) return true;
        const string prefix = "--parent-window=";
        if (!args[1].StartsWith(prefix, StringComparison.Ordinal) || !long.TryParse(args[1].AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var handle) || handle < 0)
        {
            error = "INVALID_PARENT_WINDOW";
            return false;
        }
        return true;
    }
}
