using OhMyLockScreen.Shared.Models.Ruleset;

namespace OhMyLockScreen.Shared.Abstractions.Services;

public interface IRulesetService
{
    IReadOnlyDictionary<string, RuleRegistryInfo> Rules { get; }
    event EventHandler? StatusUpdated;
    bool IsRulesetSatisfied(Ruleset ruleset);
    void RegisterRuleHandler(string id, RuleRegistryInfo.HandleDelegate handler);
    void NotifyStatusChanged();
}
