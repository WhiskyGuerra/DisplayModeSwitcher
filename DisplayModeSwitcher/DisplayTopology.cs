using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DisplayModeSwitcher;

public readonly record struct DisplayAdapterId(long Value);

public readonly record struct DisplaySourceIdentity(DisplayAdapterId AdapterId, uint SourceId);

public readonly record struct DisplayTargetIdentity(DisplayAdapterId AdapterId, uint TargetId);

public readonly record struct DisplayPoint(int X, int Y);

public enum DisplayOutputTechnology : int
{
    Other = -1,
    Hd15 = 0,
    SVideo = 1,
    CompositeVideo = 2,
    ComponentVideo = 3,
    Dvi = 4,
    Hdmi = 5,
    Lvds = 6,
    Djpn = 8,
    Sdi = 9,
    DisplayPortExternal = 10,
    DisplayPortEmbedded = 11,
    UdiExternal = 12,
    UdiEmbedded = 13,
    SdtvDongle = 14,
    Miracast = 15,
    IndirectWired = 16,
    IndirectVirtual = 17,
    Internal = unchecked((int)0x80000000)
}

public enum DisplayOrientation : uint
{
    Default = 0,
    Rotate90 = 1,
    Rotate180 = 2,
    Rotate270 = 3
}

/// <summary>
/// Vollständige lesende Modusidentität. Diese Daten werden noch nicht zum
/// Umschalten verwendet, bewahren aber die für eine spätere exakte
/// Wiederherstellung relevanten Eigenschaften.
/// </summary>
public sealed record EndpointDisplayMode(
    uint Width,
    uint Height,
    uint Frequency,
    uint BitsPerPixel,
    uint DisplayFlags,
    DisplayOrientation Orientation,
    uint FixedOutput)
{
    public string Label => $"{Width}x{Height} @ {Frequency}Hz";
}

/// <summary>
/// Ein physisches aktives Display-Target. MonitorDevicePath ist die einzige
/// persistierbare Identität; GDI-Name, Beschriftung und Position beschreiben
/// ausschließlich das aktuelle Routing.
/// </summary>
public sealed record DisplayEndpoint(
    DisplayTargetIdentity TargetIdentity,
    DisplaySourceIdentity SourceIdentity,
    string MonitorDevicePath,
    string FriendlyName,
    ushort EdidManufacturerId,
    ushort EdidProductCodeId,
    DisplayOutputTechnology OutputTechnology,
    uint ConnectorInstance,
    string GdiSourceName,
    bool IsPrimary,
    DisplayPoint? Position,
    EndpointDisplayMode? CurrentMode,
    bool IsCloneSource)
{
    public bool IsPersistable => !string.IsNullOrWhiteSpace(MonitorDevicePath);
}

public sealed class DisplayTopologySnapshot
{
    private readonly ReadOnlyCollection<DisplayEndpoint> _endpoints;
    private readonly ReadOnlyCollection<DisplayReadError> _warnings;

    public DisplayTopologySnapshot(
        IEnumerable<DisplayEndpoint> endpoints,
        IEnumerable<DisplayReadError>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        _endpoints = Array.AsReadOnly(endpoints.ToArray());
        _warnings = Array.AsReadOnly((warnings ?? Array.Empty<DisplayReadError>()).ToArray());
    }

    public IReadOnlyList<DisplayEndpoint> Endpoints => _endpoints;
    public IReadOnlyList<DisplayReadError> Warnings => _warnings;
}

public sealed record DisplayReadError(string Operation, int NativeErrorCode, string Message);

public sealed class DisplayReadResult<T>
{
    private DisplayReadResult(T? value, DisplayReadError? error)
    {
        Value = value;
        Error = error;
    }

    public bool Success => Error is null;
    public T? Value { get; }
    public DisplayReadError? Error { get; }

    public static DisplayReadResult<T> Ok(T value) => new(value, null);
    public static DisplayReadResult<T> Fail(string operation, int nativeErrorCode, string message) =>
        new(default, new DisplayReadError(operation, nativeErrorCode, message));
}

public interface IDisplayTopologyService
{
    DisplayReadResult<DisplayTopologySnapshot> GetSnapshot();
    DisplayReadResult<EndpointDisplayMode> GetCurrentMode(DisplayEndpoint endpoint);
    DisplayReadResult<IReadOnlyList<EndpointDisplayMode>> GetAvailableModes(DisplayEndpoint endpoint);
}

public enum MonitorMatchStatus
{
    Exact,
    Missing,
    Ambiguous,
    Unpersistable,
    CloneGroupUnsupported
}

public sealed record MonitorMatchResult(
    MonitorMatchStatus Status,
    DisplayEndpoint? Endpoint,
    string Message)
{
    public bool Success => Status == MonitorMatchStatus.Exact;
}

/// <summary>
/// Löst ausschließlich explizite, stabile Monitorwahlen auf. Anzeigename,
/// EDID und GDI-Routing sind bewusst keine automatischen Fallbacks.
/// </summary>
public static class MonitorSelectorMatcher
{
    public static MonitorMatchResult Resolve(DisplayTopologySnapshot snapshot, MonitorSelector selector)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selector);

        return selector switch
        {
            PrimaryMonitor => ResolvePrimary(snapshot.Endpoints),
            SpecificMonitor specific => ResolveSpecific(snapshot.Endpoints, specific),
            _ => new MonitorMatchResult(MonitorMatchStatus.Missing, null, "Die Monitorwahl wird nicht unterstützt.")
        };
    }

    public static MonitorMatchResult CreateSpecificSelection(DisplayEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsPersistable)
        {
            return new MonitorMatchResult(
                MonitorMatchStatus.Unpersistable,
                endpoint,
                "Windows liefert für diesen Monitor keinen persistierbaren Gerätepfad.");
        }

        return new MonitorMatchResult(MonitorMatchStatus.Exact, endpoint, "Der Monitor ist eindeutig persistierbar.");
    }

    private static MonitorMatchResult ResolvePrimary(IReadOnlyList<DisplayEndpoint> endpoints)
    {
        var matches = endpoints.Where(endpoint => endpoint.IsPrimary).ToArray();
        if (matches.Length == 0)
            return new MonitorMatchResult(MonitorMatchStatus.Missing, null, "Windows meldet keinen Primärmonitor.");

        if (matches.Any(endpoint => endpoint.IsCloneSource))
        {
            return new MonitorMatchResult(
                MonitorMatchStatus.CloneGroupUnsupported,
                null,
                "Der Primärmonitor gehört zu einer derzeit nicht unterstützten Klon-Gruppe.");
        }

        if (matches.Length != 1)
            return new MonitorMatchResult(MonitorMatchStatus.Ambiguous, null, "Windows meldet mehrere Primärmonitore.");

        return new MonitorMatchResult(MonitorMatchStatus.Exact, matches[0], "Primärmonitor eindeutig gefunden.");
    }

    private static MonitorMatchResult ResolveSpecific(
        IReadOnlyList<DisplayEndpoint> endpoints,
        SpecificMonitor selector)
    {
        if (string.IsNullOrWhiteSpace(selector.MonitorDevicePath))
        {
            return new MonitorMatchResult(
                MonitorMatchStatus.Unpersistable,
                null,
                "Die gespeicherte Monitorwahl enthält keinen Gerätepfad.");
        }

        var matches = endpoints
            .Where(endpoint => endpoint.IsPersistable && string.Equals(
                endpoint.MonitorDevicePath,
                selector.MonitorDevicePath,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return new MonitorMatchResult(MonitorMatchStatus.Missing, null, "Der gespeicherte Monitor ist derzeit nicht aktiv.");
        if (matches.Length > 1)
            return new MonitorMatchResult(MonitorMatchStatus.Ambiguous, null, "Der Monitor-Gerätepfad ist in der aktuellen Topologie nicht eindeutig.");
        if (matches[0].IsCloneSource)
        {
            return new MonitorMatchResult(
                MonitorMatchStatus.CloneGroupUnsupported,
                null,
                "Der Monitor gehört zu einer derzeit nicht unterstützten Klon-Gruppe.");
        }

        return new MonitorMatchResult(MonitorMatchStatus.Exact, matches[0], "Monitor-Gerätepfad eindeutig gefunden.");
    }
}

public static partial class DisplayEndpointLabel
{
    public static string Format(DisplayEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var name = endpoint.FriendlyName.Trim();
        if (name.Length == 0 && (endpoint.EdidManufacturerId != 0 || endpoint.EdidProductCodeId != 0))
        {
            name = string.Create(
                CultureInfo.InvariantCulture,
                $"Monitor 0x{endpoint.EdidManufacturerId:X4}/0x{endpoint.EdidProductCodeId:X4}");
        }
        if (name.Length == 0)
            name = "Unbekannter Monitor";

        var details = new List<string>();
        if (endpoint.IsPrimary)
            details.Add("Primär");

        var connector = ConnectorName(endpoint.OutputTechnology);
        if (connector is not null)
            details.Add(connector);

        var displayNumber = DisplayNumberRegex().Match(endpoint.GdiSourceName);
        details.Add(displayNumber.Success ? $"Anzeige {displayNumber.Groups[1].Value}" : "Anzeige unbekannt");

        if (endpoint.CurrentMode is not null)
            details.Add($"{endpoint.CurrentMode.Width}x{endpoint.CurrentMode.Height}");
        if (endpoint.Position is { } position)
            details.Add($"Position {position.X}/{position.Y}");

        return $"{name} ({string.Join(", ", details)})";
    }

    private static string? ConnectorName(DisplayOutputTechnology technology) => technology switch
    {
        DisplayOutputTechnology.Hd15 => "VGA",
        DisplayOutputTechnology.SVideo => "S-Video",
        DisplayOutputTechnology.CompositeVideo => "Composite",
        DisplayOutputTechnology.ComponentVideo => "Component",
        DisplayOutputTechnology.Dvi => "DVI",
        DisplayOutputTechnology.Hdmi => "HDMI",
        DisplayOutputTechnology.Lvds => "LVDS",
        DisplayOutputTechnology.DisplayPortExternal => "DisplayPort",
        DisplayOutputTechnology.DisplayPortEmbedded => "interner DisplayPort",
        DisplayOutputTechnology.UdiExternal => "UDI",
        DisplayOutputTechnology.UdiEmbedded => "interner UDI",
        DisplayOutputTechnology.Miracast => "Miracast",
        DisplayOutputTechnology.IndirectWired => "indirekt kabelgebunden",
        DisplayOutputTechnology.IndirectVirtual => "virtuell",
        DisplayOutputTechnology.Internal => "intern",
        _ => null
    };

    [GeneratedRegex(@"^\\\\\.\\DISPLAY(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplayNumberRegex();
}
