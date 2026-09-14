namespace DisplayModeSwitcher;

public static class ExistingTargetSelection
{
    public static TargetDraftState? Resolve(IEnumerable<TargetDraftState> targets, MonitorSelector selector)
    {
        TargetDraftState? match = null;
        foreach (var target in targets)
        {
            if (!MonitorSelectorIdentity.Equals(target.Target.MonitorSelector, selector))
                continue;
            if (match is not null)
                return null;
            match = target;
        }
        return match;
    }
}
