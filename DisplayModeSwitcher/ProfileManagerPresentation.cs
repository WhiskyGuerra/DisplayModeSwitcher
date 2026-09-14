namespace DisplayModeSwitcher;

internal enum PresentationStatusKind
{
    Neutral,
    Loading,
    Ready,
    Warning,
    Error
}

internal enum ProfileEditorStateKind
{
    New,
    Dirty,
    Saved,
    Blocked,
    Launching
}

internal sealed record ProfileEditorState(ProfileEditorStateKind Kind, string Text);

internal static class ProfileManagerPresentation
{
    internal static string TargetStatus(PresentationStatusKind kind, string? message)
    {
        var prefix = kind switch
        {
            PresentationStatusKind.Loading => "Wird geladen:",
            PresentationStatusKind.Ready => "Bereit:",
            PresentationStatusKind.Warning => "Hinweis:",
            PresentationStatusKind.Error => "Fehler:",
            _ => "Status:"
        };

        return $"{prefix} {Normalize(message)}";
    }

    internal static ProfileEditorState EditorState(bool editingExisting, bool dirty, bool launching, bool missingExecutable, bool hasBlockingTarget)
    {
        if (launching)
            return new(ProfileEditorStateKind.Launching, "Start läuft — bitte warten");
        if (missingExecutable)
            return new(ProfileEditorStateKind.Blocked, "Profil geöffnet — Anwendungsdatei fehlt");
        if (hasBlockingTarget)
            return new(ProfileEditorStateKind.Blocked, "Profil geöffnet — Monitorziel muss korrigiert werden");
        if (!editingExisting)
            return new(ProfileEditorStateKind.New, dirty ? "Neues Profil — noch nicht gespeichert" : "Neues Profil — Anwendung und Monitorziel auswählen");
        if (dirty)
            return new(ProfileEditorStateKind.Dirty, "Profil geändert — Änderungen noch nicht gespeichert");
        return new(ProfileEditorStateKind.Saved, "Gespeichertes Profil geöffnet — unverändert");
    }

    internal static string TargetAction(bool editingTarget) => editingTarget
        ? "Änderungen am Ziel übernehmen"
        : "Ziel hinzufügen";

    internal static string ProfileSaveAction(bool editingExisting) => editingExisting
        ? "Änderungen am Profil speichern"
        : "Neues Profil speichern";

    private static string Normalize(string? message) => string.IsNullOrWhiteSpace(message)
        ? "Keine weiteren Angaben."
        : message.Trim();
}
