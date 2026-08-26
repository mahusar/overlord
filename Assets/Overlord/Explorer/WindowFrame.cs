using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Overlord.Explorer
{
    public static class WindowFrame
    {
        public const float BarHeight = 28f;

        private static bool applied;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int GWL_STYLE = -16;

        private const uint WS_POPUP = 0x80000000u;
        private const uint WS_VISIBLE = 0x10000000u;
        private const uint WS_CLIPSIBLINGS = 0x04000000u;
        private const uint WS_CLIPCHILDREN = 0x02000000u;
        private const uint WS_THICKFRAME = 0x00040000u;
        private const uint WS_MINIMIZEBOX = 0x00020000u;
        private const uint WS_MAXIMIZEBOX = 0x00010000u;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int SW_MINIMIZE = 6;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowLong(IntPtr window, int index, uint value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after,
            int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        private static IntPtr Window()
        {
            IntPtr active = GetActiveWindow();
            if (active != IntPtr.Zero)
            {
                return active;
            }

            return FindWindow(null, Application.productName);
        }
#endif

        public static void Apply()
        {
            if (applied)
            {
                return;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr window = Window();
            if (window == IntPtr.Zero)
            {
                return;
            }

            uint style = WS_POPUP | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN |
                WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

            SetWindowLong(window, GWL_STYLE, style);
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
#endif

            applied = true;
        }

        public static void BeginDrag()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr window = Window();
            if (window == IntPtr.Zero)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(window, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
#endif
        }

        public static void Minimize()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr window = Window();
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SW_MINIMIZE);
            }
#endif
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
