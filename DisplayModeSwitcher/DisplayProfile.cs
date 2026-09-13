namespace DisplayModeSwitcher;

public enum ProfileRetentionPolicy
{
    Once,
    Startup,
    Continuous
}

/// <summary>
/// Persistente Monitor-Auswahl. Friendly Name und EDID-Daten sind bewusst nicht
/// Teil der Identität: Sie dienen ausschließlich einer späteren manuellen
/// Neuverknüpfung und dürfen nie als automatischer Matching-Fallback dienen.
/// </summary>
public abstract record MonitorSelector;

public sealed record PrimaryMonitor : MonitorSelector;

public sealed record SpecificMonitor(
    string MonitorDevicePath,
    string FriendlyNameSnapshot,
    string? EdidManufacturerId = null,
    string? EdidProductCodeId = null) : MonitorSelector;

public sealed record DisplayProfileTarget(MonitorSelector MonitorSelector, DisplayMode Mode);

/// <summary>
/// Ein Profil enthält eine Richtlinie und mindestens ein explizites Monitorziel.
/// Die Zielliste wird beim Erstellen kopiert und ist danach nicht mehr veränderbar.
/// </summary>
public sealed class DisplayProfile : IEquatable<DisplayProfile>
{
    private readonly IReadOnlyList<DisplayProfileTarget> _targets;

    public ProfileRetentionPolicy Policy { get; }
    public IReadOnlyList<DisplayProfileTarget> Targets => _targets;

    public DisplayProfile(ProfileRetentionPolicy policy, IEnumerable<DisplayProfileTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Policy = policy;
        _targets = Array.AsReadOnly(targets.Select(CloneTarget).ToArray());
    }

    // Übergangskonstruktor für bestehenden primärmonitorbezogenen Anwendungscode.
    // Neue Aufrufer sollen den Selector immer explizit angeben.
    public DisplayProfile(DisplayMode mode, ProfileRetentionPolicy policy = ProfileRetentionPolicy.Startup)
        : this(policy, [new DisplayProfileTarget(new PrimaryMonitor(), mode)])
    {
    }

    public DisplayProfile DeepCopy() => new(Policy, _targets);

    public bool Equals(DisplayProfile? other) => other is not null &&
        Policy == other.Policy &&
        _targets.SequenceEqual(other._targets);

    public override bool Equals(object? obj) => obj is DisplayProfile other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Policy);
        foreach (var target in _targets)
            hash.Add(target);
        return hash.ToHashCode();
    }

    private static DisplayProfileTarget CloneTarget(DisplayProfileTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.MonitorSelector);
        ArgumentNullException.ThrowIfNull(target.Mode);
        MonitorSelector selector = target.MonitorSelector switch
        {
            PrimaryMonitor => new PrimaryMonitor(),
            SpecificMonitor specific => new SpecificMonitor(
                specific.MonitorDevicePath,
                specific.FriendlyNameSnapshot,
                specific.EdidManufacturerId,
                specific.EdidProductCodeId),
            _ => throw new ArgumentException("Die Monitorwahl wird nicht unterstützt.", nameof(target))
        };
        return new DisplayProfileTarget(selector, new DisplayMode
        {
            Width = target.Mode.Width,
            Height = target.Mode.Height,
            Frequency = target.Mode.Frequency,
            Label = target.Mode.Label
        });
    }
}

public static class DisplayProfileCompatibility
{
    public const string UnsupportedTargetsMessage = "Dieses Profil verwendet eine Monitorwahl, die in diesem Zwischenstand noch nicht ausgeführt werden kann. Unterstützt wird derzeit ausschließlich genau ein Ziel für den Primärmonitor.";

    public static bool TryGetCurrentPrimaryTarget(DisplayProfile profile, out DisplayProfileTarget? target)
    {
        target = null;
        if (profile.Targets.Count != 1 || profile.Targets[0].MonitorSelector is not PrimaryMonitor)
            return false;

        target = profile.Targets[0];
        return true;
    }
}

public static class DisplayProfilePresentation
{
    public static string DescribeTargets(DisplayProfile profile)
    {
        if (profile.Targets.Count != 1)
            return $"{profile.Targets.Count} Monitorziele";

        var target = profile.Targets[0];
        var monitor = target.MonitorSelector switch
        {
            PrimaryMonitor => "Primärmonitor",
            SpecificMonitor specific => specific.FriendlyNameSnapshot,
            _ => "Unbekannter Monitor"
        };
        return $"{monitor} → {target.Mode.Width}x{target.Mode.Height}@{target.Mode.Frequency}Hz";
    }
}

public static class MonitorSelectorIdentity
{
    public static bool Equals(MonitorSelector first, MonitorSelector second) => (first, second) switch
    {
        (PrimaryMonitor, PrimaryMonitor) => true,
        (SpecificMonitor left, SpecificMonitor right) => string.Equals(
            left.MonitorDevicePath,
            right.MonitorDevicePath,
            StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

public static class ProfileRetentionPolicyText
{
    public static string ToDisplayName(ProfileRetentionPolicy policy) => policy switch
    {
        ProfileRetentionPolicy.Once => "Einmalig",
        ProfileRetentionPolicy.Startup => "Während Startphase stabilisieren",
        ProfileRetentionPolicy.Continuous => "Dauerhaft erzwingen",
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    public static string ToShortDisplayName(ProfileRetentionPolicy policy) => policy switch
    {
        ProfileRetentionPolicy.Once => "Einmalig",
        ProfileRetentionPolicy.Startup => "Startphase",
        ProfileRetentionPolicy.Continuous => "Dauerhaft",
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };
}
