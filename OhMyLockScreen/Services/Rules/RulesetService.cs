// Adapted from ClassIsland RulesetService, GPL-3.0.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services;

using OhMyLockScreen.Shared.Models.Ruleset;

namespace OhMyLockScreen.Services.Rules;

public class RulesetService : IRulesetService
{
    public ILogger<RulesetService> Logger { get; }

    public IReadOnlyDictionary<string, RuleRegistryInfo> Rules { get; }

    public RulesetService(ILogger<RulesetService> logger, IEnumerable<RuleRegistryInfo> registrations)
    {
        Logger = logger;
        Rules = registrations.ToDictionary(info => info.Id, StringComparer.Ordinal);
    }

    public event EventHandler? StatusUpdated;

    private int BoolToRuleObjectState(bool? v) => v switch
    {
        true => 2,
        false => 1,
        null => 0
    };

    private bool? IsRuleSatisfied(Rule i)
    {
        if (i.Id == string.Empty)
            return null;

        if (!Rules.TryGetValue(i.Id, out var rule))
        {
            Logger.LogWarning("找不到规则 {Id} 的注册信息，已默认其结果为 false.", i.Id);
            return false;
        }

        if (rule.Handle != null)
        {
            try
            {
                return rule.Handle(rule.ResolveSettings(i.Settings));
            }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "规则 {Id} 判定失败。", i.Id);
                return false;
            }
        }
        Logger.LogWarning("规则 {Id} 的处理程序没有注册，已默认其结果为 false.", rule.Id);
        return false;
    }

    private bool? IsRulesetGroupSatisfied(RuleGroup ruleset)
    {
        var rulesetSatisfied = ruleset.Mode == RulesetLogicalMode.And;
        if (!ruleset.Rules.Any(r => r.Id != ""))
        {
            return null;
        }

        foreach (var i in ruleset.Rules)
        {
            bool? res = IsRuleSatisfied(i);
            if (res == null)
            {
                i.State = BoolToRuleObjectState(res);
                continue;
            }

            bool result = (bool)res;
            result ^= i.IsReversed;
            i.State = BoolToRuleObjectState(result);
            if (!result && ruleset.Mode == RulesetLogicalMode.And)
            {
                rulesetSatisfied = false;
                break;
            }
            if (result && ruleset.Mode == RulesetLogicalMode.Or)
            {
                rulesetSatisfied = true;
                break;
            }
        }
        rulesetSatisfied ^= ruleset.IsReversed;
        return rulesetSatisfied;
    }

    public bool IsRulesetSatisfied(Ruleset ruleset)
    {
        var isSatisfied = ruleset.Mode == RulesetLogicalMode.And;
        if (ruleset.Groups.Count <= 0)
        {
            ruleset.State = BoolToRuleObjectState(false);
            return false;
        }
        foreach (var i in ruleset.Groups)
        {
            i.State = 0;
            foreach (var j in i.Rules)
            {
                j.State = 0;
            }
        }
        foreach (var group in ruleset.Groups.Where(x => x.IsEnabled))
        {
            bool? res = IsRulesetGroupSatisfied(group);
            group.State = BoolToRuleObjectState(res);
            if (res == null)
                continue;

            bool result = (bool)res;
            if (!result && ruleset.Mode == RulesetLogicalMode.And)
            {
                isSatisfied = false;
                break;
            }
            if (result && ruleset.Mode == RulesetLogicalMode.Or)
            {
                isSatisfied = true;
                break;
            }
        }
        isSatisfied ^= ruleset.IsReversed;
        ruleset.State = BoolToRuleObjectState(isSatisfied);
        return isSatisfied;
    }

    public void RegisterRuleHandler(string id, RuleRegistryInfo.HandleDelegate handler)
    {
        if (!Rules.TryGetValue(id, out var ruleRegistryInfo))
        {
            throw new KeyNotFoundException($"找不到规则 {id}。");
        }

        ruleRegistryInfo.Handle += handler;
    }

    public void NotifyStatusChanged()
    {
        StatusUpdated?.Invoke(this, EventArgs.Empty);
    }
}
