using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DisplayModeSwitcher
{
    public static class DisplayManager
    {
        public enum DispChange
        {
            Successful = 0,
            Restart = 1,
            Failed = -1,
            BadMode = -2,
            NotUpdated = -3
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
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
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
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

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_TEST = 0x00000002;
        private const int CDS_FULLSCREEN = 0x00000004;
        private const int CDS_UPDATEREGISTRY = 0x00000001;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DM_PELSWIDTH = 0x00080000;
        private const int DM_PELSHEIGHT = 0x00100000;
        private const int DM_DISPLAYFREQUENCY = 0x00400000;
        private const int DM_BITSPERPEL = 0x00040000;

        public static DisplayMode? GetCurrentDisplayMode()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                return null;

            return new DisplayMode
            {
                Width = (uint)dm.dmPelsWidth,
                Height = (uint)dm.dmPelsHeight,
                Frequency = (uint)dm.dmDisplayFrequency,
                Label = $"{dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz"
            };
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

        public static bool SetDisplayMode(uint width, uint height, uint frequency)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            // Suche nach passendem Modus
            bool found = false;
            for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
            {
                if (dm.dmPelsWidth == width &&
                    dm.dmPelsHeight == height &&
                    dm.dmDisplayFrequency == frequency)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                MessageBox.Show($"Modus {width}x{height}@{frequency}Hz nicht gefunden.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Felder zum Ändern angeben
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;

            // Testen
            int test = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (test != DISP_CHANGE_SUCCESSFUL)
            {
                MessageBox.Show($"Test fehlgeschlagen → {(DispChange)test}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Anwenden (RAM-only)
            int result = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
            if (result != DISP_CHANGE_SUCCESSFUL)
            {
                MessageBox.Show($"Änderung fehlgeschlagen → {(DispChange)result}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        public static List<DisplayMode> GetAvailableDisplayModes()
        {
            List<DisplayMode> modes = new List<DisplayMode>();
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            int i = 0;
            while (EnumDisplaySettings(null, i++, ref dm))
            {
                var mode = new DisplayMode
                {
                    Width = (uint)dm.dmPelsWidth,
                    Height = (uint)dm.dmPelsHeight,
                    Frequency = (uint)dm.dmDisplayFrequency,
                    Label = $"{dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz"
                };

                if (!modes.Contains(mode))
                    modes.Add(mode);
            }

            return modes.OrderByDescending(m => m.Width)
                        .ThenByDescending(m => m.Height)
                        .ThenByDescending(m => m.Frequency)
                        .ToList();
        }

        public static DisplayMode? GetCurrentDisplayMode()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                return null;

            return new DisplayMode
            {
                Width = (uint)dm.dmPelsWidth,
                Height = (uint)dm.dmPelsHeight,
                Frequency = (uint)dm.dmDisplayFrequency,
                Label = $"{dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz"
            };
        }

    }
}
