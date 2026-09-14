using DisplayModeSwitcher;

internal static class ProfileManagerPresentationTests
{
    internal static void EditorStatesAreExplicit()
    {
        Equal(ProfileEditorStateKind.New, ProfileManagerPresentation.EditorState(false, false, false, false, false).Kind);
        Contains("noch nicht gespeichert", ProfileManagerPresentation.EditorState(false, true, false, false, false).Text);
        Equal(ProfileEditorStateKind.Saved, ProfileManagerPresentation.EditorState(true, false, false, false, false).Kind);
        Equal(ProfileEditorStateKind.Dirty, ProfileManagerPresentation.EditorState(true, true, false, false, false).Kind);
        Equal(ProfileEditorStateKind.Blocked, ProfileManagerPresentation.EditorState(true, false, false, true, false).Kind);
        Equal(ProfileEditorStateKind.Blocked, ProfileManagerPresentation.EditorState(true, false, false, false, true).Kind);
        Equal(ProfileEditorStateKind.Launching, ProfileManagerPresentation.EditorState(true, false, true, false, false).Kind);
    }

    internal static void ActionNamesDistinguishTheirScope()
    {
        Equal("Ziel hinzufügen", ProfileManagerPresentation.TargetAction(false));
        Equal("Änderungen am Ziel übernehmen", ProfileManagerPresentation.TargetAction(true));
        Equal("Neues Profil speichern", ProfileManagerPresentation.ProfileSaveAction(false));
        Equal("Änderungen am Profil speichern", ProfileManagerPresentation.ProfileSaveAction(true));
    }

    internal static void StatusTextsExposeTheirMeaning()
    {
        Equal("Wird geladen: Monitore", ProfileManagerPresentation.TargetStatus(PresentationStatusKind.Loading, " Monitore "));
        Equal("Bereit: Auswahl möglich", ProfileManagerPresentation.TargetStatus(PresentationStatusKind.Ready, "Auswahl möglich"));
        Equal("Fehler: Kaputt", ProfileManagerPresentation.TargetStatus(PresentationStatusKind.Error, "Kaputt"));
        Equal("Status: Keine weiteren Angaben.", ProfileManagerPresentation.TargetStatus(PresentationStatusKind.Neutral, null));
    }

    internal static void ExistingTargetSelectionIsExactAndUnambiguous()
    {
        var primary = Target(0, new PrimaryMonitor());
        var firstSpecific = Target(1, new SpecificMonitor("DISPLAY#ONE", "Eins", null, null));
        var sameSpecificIdentity = Target(2, new SpecificMonitor("display#one", "Anderer Name", null, null));
        var otherSpecific = Target(3, new SpecificMonitor("DISPLAY#TWO", "Zwei", null, null));

        Equal(primary, ExistingTargetSelection.Resolve([primary, firstSpecific], new PrimaryMonitor()));
        Equal(firstSpecific, ExistingTargetSelection.Resolve([primary, firstSpecific], new SpecificMonitor("display#ONE", "Neu", null, null)));
        Equal<TargetDraftState?>(null, ExistingTargetSelection.Resolve([primary, firstSpecific], new SpecificMonitor("DISPLAY#MISSING", "Fehlt", null, null)));
        Equal<TargetDraftState?>(null, ExistingTargetSelection.Resolve([primary, firstSpecific, sameSpecificIdentity], new SpecificMonitor("DISPLAY#ONE", "Eins", null, null)));
        Equal<TargetDraftState?>(null, ExistingTargetSelection.Resolve([primary, Target(4, new PrimaryMonitor()), otherSpecific], new PrimaryMonitor()));
        Equal<TargetDraftState?>(null, ExistingTargetSelection.Resolve([firstSpecific, otherSpecific], new PrimaryMonitor()));
    }

    private static TargetDraftState Target(int index, MonitorSelector selector) => new(
        index,
        new DisplayProfileTarget(selector, new DisplayMode { Width = 1920, Height = 1080, Frequency = 60 }),
        MonitorMatchStatus.Exact,
        $"Ziel {index}",
        "Testziel");

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal)) throw new InvalidOperationException($"'{actual}' enthält nicht '{expected}'.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Erwartet: {expected}; tatsächlich: {actual}");
    }
}
