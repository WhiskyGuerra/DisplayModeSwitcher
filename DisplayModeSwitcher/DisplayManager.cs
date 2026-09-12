using System.Runtime.InteropServices;

namespace DisplayModeSwitcher;

public sealed record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}

public interface IDisplayService
{
    DisplayMode? GetCurrentDisplayMode();
    OperationResult SetDisplayMode(DisplayMode mode);
}

public static class DisplayManager
{
    public enum DispChange { Successful = 0, Restart = 1, Failed = -1, BadMode = -2, NotUpdated = -3 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private const int EnumCurrentSettings = -1;
    private const int CdsTest = 0x00000002;
    private const int DispChangeSuccessful = 0;
    private const int DmPelsWidth = 0x00080000;
    private const int DmPelsHeight = 0x00100000;
    private const int DmDisplayFrequency = 0x00400000;
    private const int DmBitsPerPel = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr window, int flags, IntPtr parameter);

    public static DisplayMode? GetCurrentDisplayMode()
    {
        var mode = CreateDevMode();
        return EnumDisplaySettings(null, EnumCurrentSettings, ref mode) ? ToDisplayMode(mode) : null;
    }

    public static OperationResult SetDisplayMode(uint width, uint height, uint frequency)
    {
        var mode = CreateDevMode();
        var found = false;
        for (var index = 0; EnumDisplaySettings(null, index, ref mode); index++)
        {
            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height && mode.dmDisplayFrequency == frequency)
            {
                found = true;
                break;
            }
        }

        if (!found)
            return OperationResult.Fail($"Modus {width}x{height} @ {frequency} Hz wurde nicht gefunden.");

        mode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency | DmBitsPerPel;
        var testResult = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (testResult != DispChangeSuccessful)
            return OperationResult.Fail($"Der Modustest ist fehlgeschlagen ({(DispChange)testResult}).");

        var result = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        return result == DispChangeSuccessful
            ? OperationResult.Ok()
            : OperationResult.Fail($"Der Modus konnte nicht angewendet werden ({(DispChange)result}).");
    }

    public static List<DisplayMode> GetAvailableDisplayModes()
    {
        var modes = new List<DisplayMode>();
        var nativeMode = CreateDevMode();
        for (var index = 0; EnumDisplaySettings(null, index, ref nativeMode); index++)
        {
            var mode = ToDisplayMode(nativeMode);
            if (!modes.Contains(mode)) modes.Add(mode);
        }

        return modes.OrderByDescending(mode => mode.Width)
            .ThenByDescending(mode => mode.Height)
            .ThenByDescending(mode => mode.Frequency)
            .ToList();
    }

    private static DEVMODE CreateDevMode() => new()
    {
        dmDeviceName = string.Empty,
        dmFormName = string.Empty,
        dmSize = (short)Marshal.SizeOf<DEVMODE>()
    };

    private static DisplayMode ToDisplayMode(DEVMODE mode) => new()
    {
        Width = (uint)mode.dmPelsWidth,
        Height = (uint)mode.dmPelsHeight,
        Frequency = (uint)mode.dmDisplayFrequency,
        Label = $"{mode.dmPelsWidth}x{mode.dmPelsHeight} @ {mode.dmDisplayFrequency}Hz"
    };
}

public sealed class WindowsDisplayService : IDisplayService
{
    public DisplayMode? GetCurrentDisplayMode() => DisplayManager.GetCurrentDisplayMode();
    public OperationResult SetDisplayMode(DisplayMode mode) => DisplayManager.SetDisplayMode(mode.Width, mode.Height, mode.Frequency);
}
