using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;
using System.Drawing;
using System.Globalization;

namespace DxgiLatencyShare
{
    public static class SystemHelper
    {
        public static IEnumerable<HWND> FindWindows(string windowClass, string windowTitle, bool partialMatch = false)
        {
            var windows = new List<HWND>();

            User32.EnumWindows(delegate (HWND hwnd, IntPtr lParam)
            {
                GetWindowClassAndTitle(hwnd, out string currentWindowClass, out string CurrentWindowTitle);

                if (IsWindowClassOrTitleMatched(windowClass, currentWindowClass, partialMatch)
                    && IsWindowClassOrTitleMatched(windowTitle, CurrentWindowTitle, partialMatch))
                {
                    windows.Add(hwnd);
                }
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        public static IEnumerable<(HWND hwndMain, HWND hwndChild)> FindWindow(string windowClass, string windowTitle, string childWindowClass, string childWindowTitle, bool partialMatch = false)
        {
            foreach (var hwnd in FindWindows(windowClass, windowTitle, partialMatch))
            {
                foreach (var hwndChild in FindChildWindows(hwnd, childWindowClass, childWindowTitle, partialMatch))
                {
                    yield return (hwnd, hwndChild);
                }
            }
        }

        public static IEnumerable<HWND> FindChildWindows(HWND hwndParent, string childWindowClass, string childWindowTitle, bool partialMatch = false)
        {
            var childWindows = new List<HWND>();

            // the return value of win32 API EnumChildWindows should be ignored according to 
            // https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumchildwindows
            // but there is a bug in the implementation of User32.EnumChildWindows, which will throw
            // exception if return value is FALSE (always).
            // so we check if there is any child window firstly
            if (User32.GetChildWindow(hwndParent) == HWND.NULL)
            {
                return childWindows;
            }

            var allChildWindows = User32.EnumChildWindows(hwndParent);

            foreach (var childWindow in allChildWindows)
            {
                GetWindowClassAndTitle((IntPtr)childWindow, out string currentWindowClass, out string CurrentWindowTitle);

                if (IsWindowClassOrTitleMatched(childWindowClass, currentWindowClass, partialMatch)
                    && IsWindowClassOrTitleMatched(childWindowTitle, CurrentWindowTitle, partialMatch))
                {
                    childWindows.Add(childWindow);
                }
            }

            return childWindows;
        }

        private static bool IsWindowClassOrTitleMatched(string expected, string actual, bool partialMatch)
        {
            if (string.IsNullOrEmpty(expected))
            {
                return true;
            }

            if (partialMatch)
            {
                return actual.ToLowerInvariant().Contains(expected.ToLowerInvariant());
            }
            else
            {
                return string.Compare(expected, actual, true, CultureInfo.InvariantCulture) == 0;
            }
        }

        public static void GetWindowClassAndTitle(HWND hwnd, out string windowClass, out string windowTitle)
        {
            windowClass = null;
            windowTitle = null;

            var classNameBuilder = new StringBuilder(256);

            User32.GetClassName(hwnd, classNameBuilder, 256);

            windowClass = classNameBuilder.ToString();


            var titleBuilder = new StringBuilder(256);

            User32.GetWindowText(hwnd, titleBuilder, 256);

            windowTitle = titleBuilder.ToString();

        }

        public static void ActivateWindow(HWND hwnd)
        {
            // restore window
            User32.ShowWindow(hwnd, ShowWindowCommand.SW_RESTORE);

            User32.SetForegroundWindow(hwnd);
        }

        public static void CloseWindow(HWND hwnd)
        {
            const UInt32 WM_CLOSE = 0x0010;
            User32.SendMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static Rectangle ClientToScreen(HWND hwnd, Rectangle clientArea)
        {
            var point = new Point(clientArea.Left, clientArea.Top);

            User32.ClientToScreen(hwnd, ref point);

            var screenArea = new Rectangle(point.X, point.Y, clientArea.Width, clientArea.Height);

            return screenArea;
        }

        public static Rectangle ScreenToClient(HWND hwnd, Rectangle screenArea)
        {
            var point = new Point(screenArea.Left, screenArea.Top);

            User32.ScreenToClient(hwnd, ref point);

            var clientArea = new Rectangle(point.X, point.Y, screenArea.Width, screenArea.Height);

            return clientArea;
        }


        public static Bitmap CaptureScreen(HWND hwnd, Rectangle clientArea)
        {
            var bounds = ClientToScreen(hwnd, clientArea);

            var bitmap = new Bitmap(bounds.Width, bounds.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(new Point(bounds.Left, bounds.Top), Point.Empty, bounds.Size);
            }

            return bitmap;
        }

        public static Rectangle GetWindowRect(HWND hwnd)
        {
            if (hwnd.IsNull)
            {
                throw new ArgumentNullException(nameof(hwnd));
            }

            if (!User32.GetWindowRect(hwnd, out var rect))
            {
                throw new ArgumentException($"Failed to get window rect for window {hwnd.DangerousGetHandle():x}");
            }

            return new Rectangle(rect.left, rect.top, rect.Width, rect.Height);
        }

        public static Rectangle GetClientRect(HWND hwnd)
        {
            if (hwnd.IsNull)
            {
                throw new ArgumentNullException(nameof(hwnd));
            }

            if (!User32.GetClientRect(hwnd, out var rect))
            {
                throw new ArgumentException($"Failed to get window rect for window {hwnd.DangerousGetHandle():x}");
            }

            return new Rectangle(rect.left, rect.top, rect.Width, rect.Height);
        }
    }
}
