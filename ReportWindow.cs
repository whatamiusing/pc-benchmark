using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WaiuBenchmark
{
    internal static class ReportWindow
    {
        internal static async Task BringForward(IntPtr owner,string title,Func<bool> current)
        {
            for (int attempt=0;attempt<24;attempt++)
            {
                await Task.Delay(125);
                // Leave focus alone if the browser already activated or the user moved elsewhere.
                if (!current() || GetForegroundWindow()!=owner) return;
                IntPtr target=IntPtr.Zero;
                EnumWindows((window,unused)=>
                {
                    if (window==owner || !IsWindowVisible(window)) return true;
                    var caption=new StringBuilder(320);
                    if (GetWindowText(window,caption,caption.Capacity)>0 &&
                        caption.ToString().IndexOf(title,StringComparison.Ordinal)>=0)
                    {
                        target=window;
                        return false;
                    }
                    return true;
                },IntPtr.Zero);
                if (target==IntPtr.Zero) continue;
                if (GetForegroundWindow()!=owner || !current()) return;
                if (IsIconic(target)) ShowWindowAsync(target,9);
                if (!SetForegroundWindow(target))
                {
                    var flash=new FlashInfo { Size=(uint)Marshal.SizeOf(typeof(FlashInfo)), Window=target, Flags=2, Count=3 };
                    FlashWindowEx(ref flash);
                }
                return;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags,Count,Timeout;
        }
        private delegate bool WindowCallback(IntPtr window,IntPtr value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback,IntPtr value);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
    }
}
