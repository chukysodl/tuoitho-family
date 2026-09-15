using System.Security.Cryptography;
using System.Text;

namespace TuoiTho.Core.Policy;

public enum AppRuleDecision { Allow, Block }
public enum DefaultAppPolicy { BlockUnknown, AllowUnknown }
public enum AppClassification { UserApplication, BackgroundHelper, SystemProtected }
public enum AppSimulationDecision { WouldAllow, WouldBlock, BlockUnknown, BackgroundAllowed, SystemAllowed }

public sealed record AppIdentity(string NormalizedExecutablePath, string FileName, string? DisplayName = null, string? FileSha256 = null, string? Publisher = null, string? ProductName = null)
{
    public static AppIdentity FromExecutablePath(string executablePath, string? displayName = null, string? publisher = null, string? productName = null, string? fileSha256 = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("An executable path is required.", nameof(executablePath));
        var normalized = Path.GetFullPath(executablePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return new(normalized, Path.GetFileName(normalized), displayName, fileSha256, publisher, productName);
    }

    public string RuleKey => NormalizedExecutablePath;
    public string DisplayLabel => FirstUsable(DisplayName, ProductName, FileName, NormalizedExecutablePath);
    public static string StableId(string profileId, string normalizedPath) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{profileId}\n{normalizedPath}"))).ToLowerInvariant();

    private static string FirstUsable(params string?[] values) => values.First(value => !string.IsNullOrWhiteSpace(value))!;
}

public sealed record AppRule(string ProfileId, AppIdentity Identity, AppRuleDecision Decision, bool Enabled = true, int? DailyQuotaMinutes = null, string? Schedule = null);
public sealed record ObservedApp(string ProfileId, int SessionId, AppIdentity Identity, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, AppClassification Classification = AppClassification.BackgroundHelper);
public sealed record AppPolicyEvaluation(AppSimulationDecision Decision, string Reason)
{
    public bool WouldBlock => Decision is AppSimulationDecision.WouldBlock or AppSimulationDecision.BlockUnknown;
}

public sealed class AppPolicyEngine
{
    public static AppPolicyEvaluation Evaluate(AppIdentity identity, IEnumerable<AppRule> rules, DefaultAppPolicy defaultPolicy, AppClassification classification = AppClassification.UserApplication)
    {
        if (classification == AppClassification.SystemProtected) return new(AppSimulationDecision.SystemAllowed, "SYSTEM_ALLOWED");
        if (classification == AppClassification.BackgroundHelper) return new(AppSimulationDecision.BackgroundAllowed, "BACKGROUND_ALLOWED");
        var matching = rules.Where(r => r.Enabled && string.Equals(r.Identity.NormalizedExecutablePath, identity.NormalizedExecutablePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Any(r => r.Decision == AppRuleDecision.Block)) return new(AppSimulationDecision.WouldBlock, "EXPLICIT_BLOCK");
        if (matching.Any(r => r.Decision == AppRuleDecision.Allow)) return new(AppSimulationDecision.WouldAllow, "EXPLICIT_ALLOW");
        return defaultPolicy == DefaultAppPolicy.BlockUnknown ? new(AppSimulationDecision.BlockUnknown, "BLOCK_UNKNOWN") : new(AppSimulationDecision.WouldAllow, "ALLOW_UNKNOWN");
    }
}

public interface IAppPolicyStore
{
    Task<DefaultAppPolicy> GetDefaultPolicyAsync(string profileId, CancellationToken cancellationToken = default);
    Task SetDefaultPolicyAsync(string profileId, DefaultAppPolicy policy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppRule>> GetRulesAsync(string profileId, CancellationToken cancellationToken = default);
    Task SaveRuleAsync(AppRule rule, CancellationToken cancellationToken = default);
    Task RemoveRuleAsync(string profileId, AppIdentity identity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ObservedApp>> GetObservedAppsAsync(string profileId, int sessionId, CancellationToken cancellationToken = default);
    Task RecordObservationAsync(ObservedApp observation, CancellationToken cancellationToken = default);
}