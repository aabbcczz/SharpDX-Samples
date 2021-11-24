using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;
using System.Drawing;

namespace SimpleHelloWorld
{
    static class SystemHelper
    {
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
