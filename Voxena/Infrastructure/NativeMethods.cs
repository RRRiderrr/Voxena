using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Voxena.Infrastructure
{
    internal static class NativeMethods
    {
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 2;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void ApplyWindowAppearance(Form form, bool dark)
        {
            if (form == null || !form.IsHandleCreated) return;
            try
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
            catch { }
            try
            {
                int value = dark ? 1 : 0;
                int result = DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                if (result != 0) DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));
            }
            catch { }
        }

        public static int HitTestResize(Form form, IntPtr lParam, int grip)
        {
            Point screen = new Point(SignedLowWord(lParam), SignedHighWord(lParam));
            Point p = form.PointToClient(screen);
            bool left = p.X >= 0 && p.X < grip;
            bool right = p.X <= form.ClientSize.Width && p.X > form.ClientSize.Width - grip;
            bool top = p.Y >= 0 && p.Y < grip;
            bool bottom = p.Y <= form.ClientSize.Height && p.Y > form.ClientSize.Height - grip;

            if (left && top) return HTTOPLEFT;
            if (right && top) return HTTOPRIGHT;
            if (left && bottom) return HTBOTTOMLEFT;
            if (right && bottom) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return 0;
        }

        private static int SignedLowWord(IntPtr value) { return unchecked((short)((long)value & 0xFFFF)); }
        private static int SignedHighWord(IntPtr value) { return unchecked((short)(((long)value >> 16) & 0xFFFF)); }
    }
}
