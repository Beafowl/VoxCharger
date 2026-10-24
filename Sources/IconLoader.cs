using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoxCharger
{
    // Three icons, all in Resources/:
    //   * icon.ico      — the white logo (16 to 256 px), for the app's windows, which are always dark.
    //   * icon_dark.ico — the same logo in black, for the taskbar in Windows' light mode.
    //   * icon_app.ico  — the white logo with a thin dark outline: the exe's icon (Explorer, shortcuts,
    //                     pinned taskbar buttons), which can't follow the theme, so it has to read on both.
    //
    // icon.ico and icon_dark.ico are embedded as assembly resources (see csproj) and loaded by name.
    internal static class IconLoader
    {
        // Resource name pattern matches MSBuild's default for EmbeddedResource:
        //   <RootNamespace>.<RelativePath with '/' replaced by '.'>
        private const string DarkResource  = "VoxCharger.Resources.icon_dark.ico";
        private const string LightResource = "VoxCharger.Resources.icon.ico";

        public static Icon Dark()  => LoadEmbedded(DarkResource);
        public static Icon Light() => LoadEmbedded(LightResource);

        // The windows use a dark theme regardless of the Windows setting
        public static Icon ForDarkUi() => Light();

        // True when the taskbar (and Alt+Tab) are light: the "Windows mode" setting. Light when the
        // registry value is missing, that is the Windows default.
        public static bool IsTaskbarLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key?.GetValue("SystemUsesLightTheme") is int value)
                        return value != 0;
                }
            }
            catch
            {
                // fall through to the default
            }

            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETICON = 0x0080;
        private const int ICON_BIG   = 1;

        // White logo in the (dark) title bar; the taskbar and Alt+Tab icon follows Windows' light or dark
        // mode, also when it changes while the window is open
        public static void ApplyTo(Form form)
        {
            var white = Light();
            var black = Dark();
            if (white == null)
                return;

            form.Icon = white;

            void UpdateBig()
            {
                if (!form.IsHandleCreated)
                    return;

                var big = IsTaskbarLight() && black != null ? black : white;
                SendMessage(form.Handle, WM_SETICON, (IntPtr)ICON_BIG, big.Handle);
            }

            UserPreferenceChangedEventHandler changed = (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && form.IsHandleCreated)
                    form.BeginInvoke(new Action(UpdateBig));
            };

            // SystemEvents is static: unsubscribe with the window, or it keeps the form alive
            form.HandleCreated += (s, e) => UpdateBig();
            SystemEvents.UserPreferenceChanged += changed;
            form.FormClosed += (s, e) =>
            {
                SystemEvents.UserPreferenceChanged -= changed;
                black?.Dispose();
            };

            UpdateBig();
        }

        private static Icon LoadEmbedded(string resourceName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    return new Icon(stream);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
