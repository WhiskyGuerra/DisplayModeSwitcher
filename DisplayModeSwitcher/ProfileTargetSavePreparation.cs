namespace DisplayModeSwitcher;

/// <summary>
/// Bereitet ausschließlich eine noch offene Modusänderung am bereits
/// ausgewählten Monitorziel für das Speichern des Profils vor.
/// </summary>
public static class ProfileTargetSavePreparation
{
    public static bool CanPrepare(
        TargetDraftState? selectedTarget,
        MonitorChoice? selectedMonitor,
        DisplayMode? selectedMode) =>
        Validate(selectedTarget, selectedMonitor, selectedMode).Success;

    public static ProfileTargetSavePreparationResult Apply(
        ProfileTargetEditor editor,
        TargetDraftState? selectedTarget,
        MonitorChoice? selectedMonitor,
        DisplayMode? selectedMode)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var validation = Validate(selectedTarget, selectedMonitor, selectedMode);
        if (!validation.Success)
            return ProfileTargetSavePreparationResult.Fail(validation.Error!);
        if (selectedMonitor is null)
            return ProfileTargetSavePreparationResult.Unchanged();

        var changed = !selectedMode!.Equals(selectedTarget!.Target.Mode);
        if (!changed)
            return ProfileTargetSavePreparationResult.Unchanged();

        var update = editor.Update(selectedTarget.Index, selectedMonitor, selectedMode);
        return update.Success
            ? ProfileTargetSavePreparationResult.Updated()
            : ProfileTargetSavePreparationResult.Fail(update.Error!);
    }

    private static EditorResult Validate(
        TargetDraftState? selectedTarget,
        MonitorChoice? selectedMonitor,
        DisplayMode? selectedMode)
    {
        if (selectedMonitor is null)
            return EditorResult.Ok();
        if (selectedTarget is null)
            return EditorResult.Fail("Bitte das neue Monitorziel zuerst mit ‚Monitor hinzufügen‘ übernehmen.");
        if (!MonitorSelectorIdentity.Equals(selectedTarget.Target.MonitorSelector, selectedMonitor.Selector))
            return EditorResult.Fail("Ein Monitorwechsel muss weiterhin ausdrücklich über ‚Ziel neu zuordnen‘ bestätigt werden.");
        if (selectedMode is null)
            return EditorResult.Fail("Bitte warten, bis die Anzeigemodi geladen sind, und einen gültigen Modus auswählen.");
        return EditorResult.Ok();
    }
}

public sealed record ProfileTargetSavePreparationResult(bool Success, bool TargetUpdated, string? Error = null)
{
    public static ProfileTargetSavePreparationResult Unchanged() => new(true, false);
    public static ProfileTargetSavePreparationResult Updated() => new(true, true);
    public static ProfileTargetSavePreparationResult Fail(string error) => new(false, false, error);
}
