using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ProfessionalPowerCopyCatalogModern
{
    /// <summary>Small Win32 helpers used to keep the product glued to CATIA.</summary>
    internal static class NativeWindows
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        /// <summary>Main window of the interactive CATIA V5 process (CNEXT.exe).</summary>
        public static IntPtr FindCatia()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName("CNEXT"))
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        /// <summary>Brings CATIA to the front (restoring it when minimised), so the
        /// user lands on the CATIA-side dashboard right after a command.</summary>
        public static void FocusCatia()
        {
            try
            {
                IntPtr handle = FindCatia();
                if (handle == IntPtr.Zero) return;
                if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
                SetForegroundWindow(handle);
            }
            catch { }
        }
    }
}
