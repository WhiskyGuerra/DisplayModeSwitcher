namespace DisplayModeSwitcher;

/// <summary>
/// Führt Änderungen an einem Profil so aus, dass ein fehlgeschlagenes Speichern
/// den vorherigen Speicherzustand wiederherstellt.
/// </summary>
public static class ProfileEditWorkflow
{
    public static OperationResult Save(ProfileStore store, string? originalProcessKey, string processKey, DisplayMode mode, ProfileRetentionPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(processKey))
            return OperationResult.Fail("Bitte eine Anwendung auswählen.");

        var isRename = !string.IsNullOrWhiteSpace(originalProcessKey) &&
            !string.Equals(originalProcessKey, processKey, StringComparison.OrdinalIgnoreCase);
        var hadTarget = store.Profiles.TryGetValue(processKey, out var previousTarget);
        DisplayProfile? previousOriginal = null;
        var hadOriginal = isRename && store.Profiles.TryGetValue(originalProcessKey!, out previousOriginal);

        store.Profiles[processKey] = new DisplayProfile(mode, policy);
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

        var result = store.Save();
        if (!result.Success)
            store.Profiles[processKey] = removed;

        return result;
    }
}
