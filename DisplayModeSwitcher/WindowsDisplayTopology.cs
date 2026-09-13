using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace DisplayModeSwitcher;

public sealed record WindowsDisplayConfigPath(
    DisplaySourceIdentity SourceIdentity,
    DisplayTargetIdentity TargetIdentity,
    uint? SourceModeInfoIndex,
    uint? CloneGroupId,
    DisplayOutputTechnology OutputTechnology);

public sealed record WindowsDisplayConfigMode(
    bool IsSourceMode,
    DisplaySourceIdentity SourceIdentity,
    uint Width,
    uint Height,
    DisplayPoint Position);

public sealed record WindowsTargetDeviceName(
    string MonitorDevicePath,
    string FriendlyName,
    ushort EdidManufacturerId,
    ushort EdidProductCodeId,
    DisplayOutputTechnology OutputTechnology,
    uint ConnectorInstance);

/// <summary>
/// Kleine, ausschließlich lesende Fassade über die verwendeten Win32-APIs.
/// Sie enthält absichtlich keinen Set-/Change-Aufruf.
/// </summary>
public interface IWindowsDisplayApi
{
    int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    int QueryDisplayConfig(
        uint flags,
        uint pathCapacity,
        uint modeCapacity,
        out IReadOnlyList<WindowsDisplayConfigPath> paths,
        out IReadOnlyList<WindowsDisplayConfigMode> modes);

    int GetTargetDeviceName(DisplayTargetIdentity target, out WindowsTargetDeviceName deviceName);
    int GetSourceDeviceName(DisplaySourceIdentity source, out string gdiSourceName);
    bool IsPrimarySource(string gdiSourceName);
    int ReadDisplaySettings(string gdiSourceName, int modeNumber, out EndpointDisplayMode? mode);
}

public sealed class WindowsDisplayTopologyService : IDisplayTopologyService
{
    public const uint QueryOnlyActivePaths = 0x00000002;
    public const uint QueryVirtualModeAware = 0x00000010;
    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorNoMoreItems = 259;
    public const int EnumCurrentSettings = -1;

    private const int MaximumTopologyRetries = 16;
    private readonly IWindowsDisplayApi _api;

    public WindowsDisplayTopologyService(IWindowsDisplayApi? api = null)
    {
        _api = api ?? new WindowsDisplayApi();
    }

    public DisplayReadResult<DisplayTopologySnapshot> GetSnapshot()
    {
        var flags = QueryOnlyActivePaths | QueryVirtualModeAware;
        for (var attempt = 1; attempt <= MaximumTopologyRetries; attempt++)
        {
            var sizeResult = _api.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (sizeResult == ErrorInsufficientBuffer)
                continue;
            if (sizeResult != ErrorSuccess)
            {
                return DisplayReadResult<DisplayTopologySnapshot>.Fail(
                    nameof(IWindowsDisplayApi.GetDisplayConfigBufferSizes),
                    sizeResult,
                    "Die Größe der aktiven Windows-Anzeigetopologie konnte nicht gelesen werden.");
            }

            if (pathCount > int.MaxValue || modeCount > int.MaxValue)
            {
                return DisplayReadResult<DisplayTopologySnapshot>.Fail(
                    nameof(IWindowsDisplayApi.GetDisplayConfigBufferSizes),
                    unchecked((int)0x80070057),
                    "Windows meldet eine ungültig große Anzeigetopologie.");
            }

            var queryResult = _api.QueryDisplayConfig(
                flags,
                pathCount,
                modeCount,
                out var paths,
                out var modes);
            if (queryResult == ErrorInsufficientBuffer)
                continue;
            if (queryResult != ErrorSuccess)
            {
                return DisplayReadResult<DisplayTopologySnapshot>.Fail(
                    nameof(IWindowsDisplayApi.QueryDisplayConfig),
                    queryResult,
                    "Die aktive Windows-Anzeigetopologie konnte nicht gelesen werden.");
            }

            return BuildSnapshot(paths, modes);
        }

        return DisplayReadResult<DisplayTopologySnapshot>.Fail(
            nameof(IWindowsDisplayApi.QueryDisplayConfig),
            ErrorInsufficientBuffer,
            "Die Anzeigetopologie änderte sich während der Abfrage wiederholt.");
    }

    public DisplayReadResult<EndpointDisplayMode> GetCurrentMode(DisplayEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(endpoint.GdiSourceName))
        {
            return DisplayReadResult<EndpointDisplayMode>.Fail(
                nameof(IWindowsDisplayApi.ReadDisplaySettings),
                ErrorNoMoreItems,
                "Der Monitor hat derzeit keine lesbare GDI-Anzeigequelle.");
        }

        var result = _api.ReadDisplaySettings(endpoint.GdiSourceName, EnumCurrentSettings, out var mode);
        return result == ErrorSuccess && mode is not null
            ? DisplayReadResult<EndpointDisplayMode>.Ok(mode)
            : DisplayReadResult<EndpointDisplayMode>.Fail(
                nameof(IWindowsDisplayApi.ReadDisplaySettings),
                result,
                $"Der aktuelle Modus von {endpoint.GdiSourceName} konnte nicht gelesen werden.");
    }

    public DisplayReadResult<IReadOnlyList<EndpointDisplayMode>> GetAvailableModes(DisplayEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(endpoint.GdiSourceName))
        {
            return DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Fail(
                nameof(IWindowsDisplayApi.ReadDisplaySettings),
                ErrorNoMoreItems,
                "Der Monitor hat derzeit keine lesbare GDI-Anzeigequelle.");
        }

        var modes = new HashSet<EndpointDisplayMode>();
        for (var index = 0; ; index++)
        {
            var result = _api.ReadDisplaySettings(endpoint.GdiSourceName, index, out var mode);
            if (result == ErrorNoMoreItems)
                break;
            if (result != ErrorSuccess || mode is null)
            {
                return DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Fail(
                    nameof(IWindowsDisplayApi.ReadDisplaySettings),
                    result,
                    $"Die verfügbaren Modi von {endpoint.GdiSourceName} konnten nicht vollständig gelesen werden.");
            }

            modes.Add(mode);
        }

        IReadOnlyList<EndpointDisplayMode> ordered = new ReadOnlyCollection<EndpointDisplayMode>(modes
            .OrderByDescending(mode => mode.Width)
            .ThenByDescending(mode => mode.Height)
            .ThenByDescending(mode => mode.Frequency)
            .ThenByDescending(mode => mode.BitsPerPixel)
            .ThenBy(mode => mode.Orientation)
            .ThenBy(mode => mode.DisplayFlags)
            .ThenBy(mode => mode.FixedOutput)
            .ToArray());
        return DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Ok(ordered);
    }

    private DisplayReadResult<DisplayTopologySnapshot> BuildSnapshot(
        IReadOnlyList<WindowsDisplayConfigPath> paths,
        IReadOnlyList<WindowsDisplayConfigMode> modes)
    {
        var endpoints = new List<DisplayEndpoint>(paths.Count);
        var warnings = new List<DisplayReadError>();
        foreach (var path in paths)
        {
            var targetResult = _api.GetTargetDeviceName(path.TargetIdentity, out var targetName);
            if (targetResult != ErrorSuccess)
            {
                return DisplayReadResult<DisplayTopologySnapshot>.Fail(
                    nameof(IWindowsDisplayApi.GetTargetDeviceName),
                    targetResult,
                    "Der physische Monitorname konnte nicht gelesen werden.");
            }

            var sourceResult = _api.GetSourceDeviceName(path.SourceIdentity, out var sourceName);
            if (sourceResult != ErrorSuccess)
            {
                return DisplayReadResult<DisplayTopologySnapshot>.Fail(
                    nameof(IWindowsDisplayApi.GetSourceDeviceName),
                    sourceResult,
                    "Die aktuelle GDI-Anzeigezuordnung konnte nicht gelesen werden.");
            }

            var sourceMode = FindSourceMode(path, modes);
            EndpointDisplayMode? currentMode = null;
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                var modeResult = _api.ReadDisplaySettings(sourceName, EnumCurrentSettings, out currentMode);
                if (modeResult != ErrorSuccess || currentMode is null)
                {
                    warnings.Add(new DisplayReadError(
                        nameof(IWindowsDisplayApi.ReadDisplaySettings),
                        modeResult,
                        $"Der aktuelle Modus von {sourceName} konnte nicht gelesen werden."));
                }
            }

            endpoints.Add(new DisplayEndpoint(
                path.TargetIdentity,
                path.SourceIdentity,
                targetName.MonitorDevicePath ?? string.Empty,
                (targetName.FriendlyName ?? string.Empty).Trim(),
                targetName.EdidManufacturerId,
                targetName.EdidProductCodeId,
                path.OutputTechnology,
                targetName.ConnectorInstance,
                sourceName ?? string.Empty,
                !string.IsNullOrWhiteSpace(sourceName) && _api.IsPrimarySource(sourceName),
                sourceMode?.Position,
                currentMode,
                IsCloneSource: false));
        }

        var sourceCounts = endpoints
            .GroupBy(endpoint => endpoint.SourceIdentity)
            .ToDictionary(group => group.Key, group => group.Count());
        var gdiCounts = endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.GdiSourceName))
            .GroupBy(endpoint => endpoint.GdiSourceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var cloneGroupCounts = paths
            .Where(path => path.SourceModeInfoIndex is null && path.CloneGroupId is not null)
            .GroupBy(path => (path.SourceIdentity.AdapterId, CloneGroupId: path.CloneGroupId!.Value))
            .ToDictionary(group => group.Key, group => group.Count());

        var finalEndpoints = endpoints.Zip(paths, (endpoint, path) => endpoint with
        {
            IsCloneSource = sourceCounts[endpoint.SourceIdentity] > 1 ||
                (!string.IsNullOrWhiteSpace(endpoint.GdiSourceName) && gdiCounts[endpoint.GdiSourceName] > 1) ||
                (path.SourceModeInfoIndex is null &&
                    path.CloneGroupId is { } cloneGroupId &&
                    cloneGroupCounts[(path.SourceIdentity.AdapterId, cloneGroupId)] > 1)
        });

        return DisplayReadResult<DisplayTopologySnapshot>.Ok(new DisplayTopologySnapshot(finalEndpoints, warnings));
    }

    private static WindowsDisplayConfigMode? FindSourceMode(
        WindowsDisplayConfigPath path,
        IReadOnlyList<WindowsDisplayConfigMode> modes)
    {
        if (path.SourceModeInfoIndex is not { } index || index >= modes.Count)
            return null;

        var mode = modes[(int)index];
        return mode.IsSourceMode && mode.SourceIdentity == path.SourceIdentity ? mode : null;
    }
}

public sealed class WindowsDisplayApi : IWindowsDisplayApi
{
    private const uint DisplayConfigPathSupportVirtualMode = 0x00000008;
    private const uint DisplayDevicePrimaryDevice = 0x00000004;
    private const uint DisplayConfigModeInfoTypeSource = 1;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;
    private const uint InvalidModeIndex = 0xffff;

    public int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount) =>
        NativeMethods.GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount);

    public int QueryDisplayConfig(
        uint flags,
        uint pathCapacity,
        uint modeCapacity,
        out IReadOnlyList<WindowsDisplayConfigPath> paths,
        out IReadOnlyList<WindowsDisplayConfigMode> modes)
    {
        var nativePathCount = pathCapacity;
        var nativeModeCount = modeCapacity;
        var nativePaths = new DisplayConfigPathInfo[checked((int)pathCapacity)];
        var nativeModes = new DisplayConfigModeInfo[checked((int)modeCapacity)];
        var result = NativeMethods.QueryDisplayConfig(
            flags,
            ref nativePathCount,
            nativePaths,
            ref nativeModeCount,
            nativeModes,
            IntPtr.Zero);
        if (result != WindowsDisplayTopologyService.ErrorSuccess)
        {
            paths = Array.Empty<WindowsDisplayConfigPath>();
            modes = Array.Empty<WindowsDisplayConfigMode>();
            return result;
        }

        paths = Array.AsReadOnly(nativePaths.Take(checked((int)nativePathCount)).Select(ToPath).ToArray());
        modes = Array.AsReadOnly(nativeModes.Take(checked((int)nativeModeCount))
            .Select(ToMode)
            .ToArray());
        return result;
    }

    public int GetTargetDeviceName(DisplayTargetIdentity target, out WindowsTargetDeviceName deviceName)
    {
        var nativeName = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoGetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = new Luid(target.AdapterId.Value),
                Id = target.TargetId
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };
        var result = NativeMethods.DisplayConfigGetDeviceInfo(ref nativeName);
        deviceName = result == WindowsDisplayTopologyService.ErrorSuccess
            ? new WindowsTargetDeviceName(
                nativeName.MonitorDevicePath ?? string.Empty,
                (nativeName.MonitorFriendlyDeviceName ?? string.Empty).Trim(),
                nativeName.EdidManufactureId,
                nativeName.EdidProductCodeId,
                (DisplayOutputTechnology)nativeName.OutputTechnology,
                nativeName.ConnectorInstance)
            : new WindowsTargetDeviceName(string.Empty, string.Empty, 0, 0, DisplayOutputTechnology.Other, 0);
        return result;
    }

    public int GetSourceDeviceName(DisplaySourceIdentity source, out string gdiSourceName)
    {
        var nativeName = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoGetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = new Luid(source.AdapterId.Value),
                Id = source.SourceId
            },
            ViewGdiDeviceName = string.Empty
        };
        var result = NativeMethods.DisplayConfigGetDeviceInfo(ref nativeName);
        gdiSourceName = result == WindowsDisplayTopologyService.ErrorSuccess
            ? nativeName.ViewGdiDeviceName ?? string.Empty
            : string.Empty;
        return result;
    }

    public bool IsPrimarySource(string gdiSourceName)
    {
        for (uint index = 0; ; index++)
        {
            var device = DisplayDevice.Create();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0))
                return false;
            if (string.Equals(device.DeviceName, gdiSourceName, StringComparison.OrdinalIgnoreCase))
                return (device.StateFlags & DisplayDevicePrimaryDevice) != 0;
        }
    }

    public int ReadDisplaySettings(string gdiSourceName, int modeNumber, out EndpointDisplayMode? mode)
    {
        var nativeMode = DevMode.Create();
        if (!NativeMethods.EnumDisplaySettingsEx(gdiSourceName, modeNumber, ref nativeMode, 0))
        {
            mode = null;
            if (modeNumber >= 0)
                return WindowsDisplayTopologyService.ErrorNoMoreItems;

            var error = Marshal.GetLastWin32Error();
            return error == WindowsDisplayTopologyService.ErrorSuccess ? 31 : error;
        }

        mode = new EndpointDisplayMode(
            nativeMode.PelsWidth,
            nativeMode.PelsHeight,
            nativeMode.DisplayFrequency,
            nativeMode.BitsPerPel,
            nativeMode.DisplayFlags,
            (DisplayOrientation)nativeMode.DisplayInfo.DisplayOrientation,
            nativeMode.DisplayInfo.DisplayFixedOutput);
        return WindowsDisplayTopologyService.ErrorSuccess;
    }

    private static WindowsDisplayConfigPath ToPath(DisplayConfigPathInfo path)
    {
        var virtualMode = (path.Flags & DisplayConfigPathSupportVirtualMode) != 0;
        var rawSourceModeIndex = virtualMode ? path.SourceInfo.ModeInfoIndex >> 16 : path.SourceInfo.ModeInfoIndex;
        var sourceModeIndexIsInvalid = virtualMode
            ? rawSourceModeIndex == InvalidModeIndex
            : rawSourceModeIndex == uint.MaxValue;
        uint? sourceModeIndex = sourceModeIndexIsInvalid ? null : rawSourceModeIndex;
        uint? cloneGroupId = virtualMode && sourceModeIndexIsInvalid
            ? path.SourceInfo.ModeInfoIndex & 0xffff
            : null;
        return new WindowsDisplayConfigPath(
            new DisplaySourceIdentity(new DisplayAdapterId(path.SourceInfo.AdapterId.Value), path.SourceInfo.Id),
            new DisplayTargetIdentity(new DisplayAdapterId(path.TargetInfo.AdapterId.Value), path.TargetInfo.Id),
            sourceModeIndex,
            cloneGroupId == InvalidModeIndex ? null : cloneGroupId,
            (DisplayOutputTechnology)path.TargetInfo.OutputTechnology);
    }

    private static WindowsDisplayConfigMode ToMode(DisplayConfigModeInfo mode)
    {
        var isSource = mode.InfoType == DisplayConfigModeInfoTypeSource;
        return new WindowsDisplayConfigMode(
            isSource,
            new DisplaySourceIdentity(new DisplayAdapterId(mode.AdapterId.Value), mode.Id),
            isSource ? mode.ModeInfo.SourceMode.Width : 0,
            isSource ? mode.ModeInfo.SourceMode.Height : 0,
            isSource
                ? new DisplayPoint(mode.ModeInfo.SourceMode.Position.X, mode.ModeInfo.SourceMode.Position.Y)
                : default);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;

        public Luid(long value)
        {
            LowPart = unchecked((uint)value);
            HighPart = unchecked((int)(value >> 32));
        }

        public readonly long Value => unchecked((long)((ulong)(uint)HighPart << 32 | LowPart));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public int OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectL
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDesktopImageInfo
    {
        public PointL PathSourceSize;
        public RectL DesktopImageRegion;
        public RectL DesktopImageClip;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
        [FieldOffset(0)] public DisplayConfigDesktopImageInfo DesktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;

        public static DisplayDevice Create() => new()
        {
            Cb = (uint)Marshal.SizeOf<DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)] public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public DevModeDisplayUnion DisplayInfo;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;

        public static DevMode Create() => new()
        {
            DeviceName = string.Empty,
            FormName = string.Empty,
            Size = checked((ushort)Marshal.SizeOf<DevMode>())
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DevModeDisplayUnion
    {
        [FieldOffset(0)] public PointL Position;
        [FieldOffset(8)] public uint DisplayOrientation;
        [FieldOffset(12)] public uint DisplayFixedOutput;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DisplayConfigPathInfo[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] DisplayConfigModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
        public static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
        public static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayDevices(
            string? lpDevice,
            uint iDevNum,
            ref DisplayDevice lpDisplayDevice,
            uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplaySettingsEx(
            string lpszDeviceName,
            int iModeNum,
            ref DevMode lpDevMode,
            uint dwFlags);
    }
}
