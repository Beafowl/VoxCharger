using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace VoxCharger
{
    // Two-icon scheme:
    //   * icon.ico      — the white logo (16 to 256 px). The app's windows are
    //                     always dark, so their title bar and taskbar icon is
    //                     this one; it is also the exe's icon.
    //   * icon_dark.ico — the black logo, for light backgrounds.
    //
    // Both .ico files live in Resources/ and are embedded as assembly
    // resources (see csproj). This loader pulls them out by name and adds a
    // tiny registry probe for the current Windows app theme so the form's
    // titlebar / taskbar icon stays legible regardless of theme. The .exe's
    // Win32 ApplicationIcon (Explorer / alt-tab when not running) is still a
    // single static icon — that's a build-time concept and can't be theme-
    // switched dynamically.
    internal static class IconLoader
    {
        // Resource name pattern matches MSBuild's default for EmbeddedResource:
        //   <RootNamespace>.<RelativePath with '/' replaced by '.'>
        // RootNamespace is "VoxCharger" per the csproj.
        private const string DarkResource  = "VoxCharger.Resources.icon_dark.ico";
        private const string LightResource = "VoxCharger.Resources.icon.ico";

        public static Icon Dark()  => LoadEmbedded(DarkResource);
        public static Icon Light() => LoadEmbedded(LightResource);

        // The windows use a dark theme regardless of the Windows setting
        public static Icon ForDarkUi() => Light();

        // True when Windows is configured to render apps with a light theme
        // (taskbar, titlebar, app chrome). False = dark theme. Defaults to
        // light if the registry key is missing or unreadable, since light is
        // the Windows default and matches what most cabinets/dev machines
        // use.
        public static bool IsAppsLightTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return true;
                    var v = key.GetValue("AppsUseLightTheme");
                    if (v is int iv) return iv != 0;
                }
            }
            catch { /* fall through to default */ }
            return true;
        }

        // Pick the icon that matches the current Windows app theme. Use this
        // for Form.Icon so the icon stays legible against the OS chrome.
        // icon.ico is the variant authored for light theme (dark-coloured
        // glyph on a light surface); icon_dark.ico is the variant authored
        // for dark theme (light-coloured glyph on a dark surface). The
        // earlier mapping was inverted — fixed below.
        public static Icon ForCurrentTheme()
        {
            return IsAppsLightTheme() ? Light() : Dark();
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
