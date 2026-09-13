namespace DisplayModeSwitcher;

/// <summary>
/// Führt Änderungen an einem Profil so aus, dass ein fehlgeschlagenes Speichern
/// den vorherigen Speicherzustand wiederherstellt.
/// </summary>
public static class ProfileEditWorkflow
{
    public static OperationResult Save(ProfileStore store, string? originalProcessKey, string processKey, DisplayMode mode)
    {
        if (string.IsNullOrWhiteSpace(processKey))
            return OperationResult.Fail("Bitte eine Anwendung auswählen.");

        var isRename = !string.IsNullOrWhiteSpace(originalProcessKey) &&
            !string.Equals(originalProcessKey, processKey, StringComparison.OrdinalIgnoreCase);
        var hadTarget = store.Profiles.TryGetValue(processKey, out var previousTarget);
        DisplayMode? previousOriginal = null;
        var hadOriginal = isRename && store.Profiles.TryGetValue(originalProcessKey!, out previousOriginal);

        store.Profiles[processKey] = mode;
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
}
