using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WaiuBenchmark
{
    internal static class WifiInventory
    {
        internal static string[] Read()
        {
            var adapters=new List<string>();
            uint version;
            IntPtr handle;
            if (WlanOpenHandle(2,IntPtr.Zero,out version,out handle)!=0) return adapters.ToArray();
            try
            {
                IntPtr list;
                if (WlanEnumInterfaces(handle,IntPtr.Zero,out list)!=0) return adapters.ToArray();
                try
                {
                    int count=Math.Min(16,Marshal.ReadInt32(list));
                    for (int i=0;i<count;i++)
                    {
                        // WLAN_INTERFACE_INFO: GUID, 256 UTF-16 characters, DWORD state.
                        IntPtr item=IntPtr.Add(list,8+i*532);
                        var id=(Guid)Marshal.PtrToStructure(item,typeof(Guid));
                        string name=Marshal.PtrToStringUni(IntPtr.Add(item,16),256).TrimEnd('\0').Trim();
                        int terminator=name.IndexOf('\0');
                        if (terminator>=0) name=name.Substring(0,terminator);
                        string generation=Capability(handle,ref id);
                        adapters.Add((name.Length>0 ? name : "Wireless adapter")+
                            (generation.Length>0 ? " · "+generation : ""));
                    }
                }
                finally { WlanFreeMemory(list); }
            }
            finally { WlanCloseHandle(handle,IntPtr.Zero); }
            return adapters.ToArray();
        }

        private static string Capability(IntPtr handle,ref Guid id)
        {
            IntPtr value;
            if (WlanGetInterfaceCapability(handle,ref id,IntPtr.Zero,out value)!=0) return "";
            try
            {
                int count=Math.Min(64,Marshal.ReadInt32(value,16)), best=0;
                for (int i=0;i<count;i++)
                {
                    int phy=Marshal.ReadInt32(value,20+4*i);
                    if (phy==7 || phy==8 || phy==10 || phy==11) best=Math.Max(best,phy);
                }
                switch (best)
                {
                    case 11:return "Wi-Fi 7 (802.11be)";
                    case 10:return "Wi-Fi 6 family (802.11ax)";
                    case 8:return "Wi-Fi 5 (802.11ac)";
                    case 7:return "Wi-Fi 4 (802.11n)";
                    default:return "";
                }
            }
            finally { WlanFreeMemory(value); }
        }

        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint actual,out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle,IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle,IntPtr reserved,out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetInterfaceCapability(IntPtr handle,ref Guid id,IntPtr reserved,out IntPtr capabilities);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr value);
    }
}
