namespace DisplayModeSwitcher;

/// <summary>
/// Führt Änderungen an einem Profil so aus, dass ein fehlgeschlagenes Speichern
/// den vorherigen Speicherzustand wiederherstellt.
/// </summary>
public static class ProfileEditWorkflow
{
    public static OperationResult Save(ProfileStore store, string? originalProcessKey, string processKey, DisplayMode mode, ProfileRetentionPolicy policy)
    {
        if (WouldReplaceUnsupportedProfile(store, originalProcessKey) ||
            WouldReplaceUnsupportedProfile(store, processKey))
            return OperationResult.Fail(DisplayProfileCompatibility.UnsupportedTargetsMessage);

        // Expliziter Übergang: Die aktuelle Oberfläche legt bis zur Monitorwahl
        // ausschließlich ein einzelnes Primärmonitor-Ziel an.
        return Save(store, originalProcessKey, processKey, new DisplayProfile(policy,
            [new DisplayProfileTarget(new PrimaryMonitor(), mode)]));
    }

    public static OperationResult Save(ProfileStore store, string? originalProcessKey, string processKey, DisplayProfile profile)
    {
        if (string.IsNullOrWhiteSpace(processKey))
            return OperationResult.Fail("Bitte eine Anwendung auswählen.");

        ArgumentNullException.ThrowIfNull(profile);

        var isRename = !string.IsNullOrWhiteSpace(originalProcessKey) &&
            !string.Equals(originalProcessKey, processKey, StringComparison.OrdinalIgnoreCase);
        var hadTarget = store.Profiles.TryGetValue(processKey, out var previousTargetReference);
        var previousTarget = previousTargetReference?.DeepCopy();
        DisplayProfile? previousOriginal = null;
        var hadOriginal = false;
        if (isRename && store.Profiles.TryGetValue(originalProcessKey!, out var previousOriginalReference))
        {
            hadOriginal = true;
            previousOriginal = previousOriginalReference.DeepCopy();
        }

        store.Profiles[processKey] = profile.DeepCopy();
        if (isRename)
            store.Profiles.TryRemove(originalProcessKey!, out _);

        var result = store.Save();
        if (result.Success)
            return result;

        if (hadTarget && previousTarget is not null)
            store.Profiles[processKey] = previousTarget;
        else
            store.Profiles.TryRemove(processKey, out _);

        if (isRename && hadOriginal && previousOriginal is not null)
            store.Profiles[originalProcessKey!] = previousOriginal;

        return result;
    }

    public static OperationResult Remove(ProfileStore store, string processKey)
    {
        if (!store.Profiles.TryRemove(processKey, out var removed))
            return OperationResult.Fail("Das Profil ist nicht mehr vorhanden.");

        var rollbackCopy = removed.DeepCopy();

        var result = store.Save();
        if (!result.Success)
            store.Profiles[processKey] = rollbackCopy;

        return result;
    }

    private static bool WouldReplaceUnsupportedProfile(ProfileStore store, string? processKey) =>
        !string.IsNullOrWhiteSpace(processKey) &&
        store.Profiles.TryGetValue(processKey, out var existing) &&
        !DisplayProfileCompatibility.TryGetCurrentPrimaryTarget(existing, out _);
}
