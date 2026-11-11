using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using Microsoft.Win32;

namespace WaiuBenchmark
{
    internal sealed class PcReport
    {
        internal readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        internal ReferenceTargets Reference = ReferenceTargets.Bundled;

        internal static PcReport Collect(Result result, Result baseline, StorageBenchmark storage, double? graphics,
            double? slowFrame, double? glassTime, string graphicsAdapter, double? backgroundCpu, ulong[] preflightMemory, CancellationToken cancel,
            Action<double,string> progress=null)
        {
            var report = new PcReport();
            report.Data["version"] = 1;
            report.Data["date"] = result.Date.ToUniversalTime().ToString("O");
            report.Data["scores"] = Scores(result);
            report.Data["compression"]=result.Compression;
            report.Data["allCoreRate"]=result.AllCoreRate;
            report.Data["allCoreWorkers"]=result.AllCoreWorkers;
            report.Data["latency"]=result.Latency;
            report.Data["backgroundCpu"]=backgroundCpu;
            report.Data["preflightMemory"]=preflightMemory;
            report.Data["componentScores"]=ComponentScores.Compare(result,baseline);
            if (baseline != null)
            {
                report.Data["baseline"] = Scores(baseline);
                report.Data["baselineGraphicsTime"] = baseline.GraphicsTime;
            }
            report.Data["workers"] = result.Workers;
            report.Data["logicalProcessors"] = Environment.ProcessorCount;

            var cpu = Query("Win32_Processor", "Name, NumberOfCores", cancel).FirstOrDefault();
            report.Data["cpu"] = Value(cpu, "Name");
            report.Data["cpuCores"] = cpu==null ? 0 : Number(cpu,"NumberOfCores");
            var modules = Query("Win32_PhysicalMemory", "DeviceLocator, Capacity, ConfiguredClockSpeed, PartNumber, SMBIOSMemoryType, FormFactor", cancel);
            report.Data["modules"] = modules.Select(module => new
            {
                location = Value(module, "DeviceLocator"),
                size = Number(module, "Capacity"),
                speed = Number(module, "ConfiguredClockSpeed"),
                part = Value(module, "PartNumber"),
                type = MemoryType(Number(module,"SMBIOSMemoryType")),
                form = MemoryForm(Number(module,"FormFactor"))
            }).ToArray();
            var arrays = Query("Win32_PhysicalMemoryArray", "Use, MemoryDevices, MaxCapacityEx", cancel)
                .Where(array => Number(array, "Use") == 3).ToArray();
            long slots = arrays.Sum(array => Number(array, "MemoryDevices"));
            // Firmware reports memory positions. A soldered chip can occupy one.
            if (modules.Count > 0 && modules.Count < 16 && slots >= modules.Count && slots <= 128)
                report.Data["slots"] = slots;
            if (arrays.Length==1)
            {
                long maximum=Number(arrays[0],"MaxCapacityEx");
                long installed=modules.Sum(module=>Number(module,"Capacity"));
                if (maximum>0 && maximum<=17179869184L && maximum*1024>=installed)
                    report.Data["memoryMaximum"]=maximum*1024;
            }
            ReadBoard(report.Data);
            if (progress!=null) progress(0.35,"Reading Windows and drive details...");

            var drives=Query("Win32_DiskDrive", "Index, Model, Size, InterfaceType", cancel);
            report.Data["drives"] = drives
                .Select(disk => new { model = Value(disk, "Model"), size = Number(disk, "Size"), bus = Value(disk, "InterfaceType") }).ToArray();
            ReadWindows(report.Data,drives,cancel);
            report.Data["storageDrive"] = storage.Drive;
            object windowsDrive;
            report.Data["storageDisk"] = report.Data.TryGetValue("windowsDrive",out windowsDrive) &&
                string.Equals(storage.Drive,windowsDrive as string,StringComparison.OrdinalIgnoreCase) ?
                report.Data["windowsDisk"] : DiskModel(storage.Drive,drives,cancel);
            report.Data["storageWrite"] = storage.Write;
            report.Data["storageRead"] = storage.Read;
            report.Data["storageRandomRead"] = storage.RandomRead;
            report.Data["storageFreeBytes"] = storage.FreeBytes;
            report.Data["storageVolumeBytes"] = storage.VolumeBytes;
            report.Data["graphicsTime"] = graphics;
            report.Data["graphicsSlowFrame"] = slowFrame;
            report.Data["graphicsGlassTime"] = glassTime;
            report.Data["graphicsHeavyFps"] = result.HeavyGraphics==null ? (double?)null : result.HeavyGraphics.FramesPerSecond;
            report.Data["graphicsHeavyFrameTime"] = result.HeavyGraphics==null ? null : result.HeavyGraphics.FrameMilliseconds;
            report.Data["graphicsTestAdapter"] = Clean(graphicsAdapter);

            report.Data["graphics"] = Query("Win32_VideoController", "Name, DriverVersion", cancel)
                .Select(gpu => new { model = Value(gpu, "Name"), driver = Value(gpu, "DriverVersion") }).ToArray();

            cancel.ThrowIfCancellationRequested();
            if (progress!=null) progress(0.7,"Identifying network, Wi-Fi and Bluetooth adapters...");
            try
            {
                report.Data["network"] = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                    .Select(adapter => ConnectionType(adapter.NetworkInterfaceType)).Where(type => type != null).Distinct().ToArray();
            }
            catch (NetworkInformationException) { report.Data["network"] = new string[0]; }
            string[] wifi;
            try { wifi=WifiInventory.Read(); }
            catch (DllNotFoundException) { wifi=new string[0]; }
            if (wifi.Length==0)
                wifi=QueryFields("SELECT Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE AND AdapterTypeID=9",
                    "Name",cancel).Select(row=>Value(row,"Name")).Where(name=>name!=null).ToArray();
            report.Data["wifi"]=wifi;
            report.Data["bluetooth"]=QueryFields(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass='Bluetooth' AND (Service='BTHUSB' OR Service='BTHUART')",
                "Name",cancel).Select(row=>Value(row,"Name")).Where(name=>name!=null).Distinct().ToArray();
            cancel.ThrowIfCancellationRequested();
            return report;
        }

        private static string MemoryType(long type)
        {
            switch (type)
            {
                case 18:return "DDR";
                case 19:return "DDR2";
                case 24:return "DDR3";
                case 26:return "DDR4";
                case 30:return "LPDDR4";
                case 34:return "DDR5";
                case 35:return "LPDDR5";
                default:return "";
            }
        }

        private static string MemoryForm(long type)
        {
            switch(type)
            {
                case 8:return "DIMM";
                case 12:return "SO-DIMM";
                default:return "";
            }
        }

        internal static double[] Scores(Result result)
        {
            return new[] { result.Single, result.Parallel, result.Memory };
        }

        private static void ReadWindows(Dictionary<string,object> data,List<Dictionary<string,object>> drives,CancellationToken cancel)
        {
            var os=Query("Win32_OperatingSystem","Caption, Version, BuildNumber, SystemDrive",cancel).FirstOrDefault();
            data["os"]=Value(os,"Caption");
            data["osVersion"]=Value(os,"Version");
            data["osBuild"]=Value(os,"BuildNumber");
            string drive=Value(os,"SystemDrive");
            if (!DriveLetter(drive)) return;
            data["windowsDrive"]=drive;
            data["windowsDisk"]=DiskModel(drive,drives,cancel);
        }

        private static bool DriveLetter(string drive)
        {
            return drive!=null && drive.Length==2 && drive[1]==':' && drive[0]>='A' && drive[0]<='Z';
        }

        private static string DiskModel(string drive,List<Dictionary<string,object>> drives,CancellationToken cancel)
        {
            if (!DriveLetter(drive)) return null;
            var partitions=QueryFields("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='"+drive+"'} WHERE AssocClass=Win32_LogicalDiskToPartition",
                "DiskIndex",cancel);
            var indices=partitions.Select(partition=>partition["DiskIndex"]).Where(index=>index!=null).Select(Convert.ToUInt32).Distinct().ToArray();
            if (indices.Length!=1) return null;
            var disk=drives.FirstOrDefault(item=>item["Index"]!=null && Convert.ToUInt32(item["Index"])==indices[0]);
            return Value(disk,"Model");
        }

        private static void ReadBoard(Dictionary<string, object> data)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (key == null) return;
                    data["boardMaker"] = Clean(key.GetValue("BaseBoardManufacturer"));
                    data["boardModel"] = Clean(key.GetValue("BaseBoardProduct"));
                    data["systemModel"] = Clean(key.GetValue("SystemProductName"));
                    data["bios"] = Clean(key.GetValue("BIOSVersion"));
                    data["biosDate"] = Clean(key.GetValue("BIOSReleaseDate"));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        private static string ConnectionType(NetworkInterfaceType type)
        {
            switch (type)
            {
                case NetworkInterfaceType.Wireless80211: return "Wi-Fi";
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetFx:
                case NetworkInterfaceType.FastEthernetT: return "Ethernet";
                case NetworkInterfaceType.Wwanpp:
                case NetworkInterfaceType.Wwanpp2: return "Mobile broadband";
                default: return null;
            }
        }

        private static List<Dictionary<string, object>> Query(string source, string columns, CancellationToken cancel)
        {
            return QueryFields("SELECT " + columns + " FROM " + source,columns,cancel);
        }

        private static List<Dictionary<string, object>> QueryFields(string query,string columns,CancellationToken cancel)
        {
            var output = new List<Dictionary<string, object>>();
            cancel.ThrowIfCancellationRequested();
            try
            {
                var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = true, Rewindable = false };
                using (var search = new ManagementObjectSearcher("root\\CIMV2", query, options))
                using (var results = search.Get())
                {
                    foreach (ManagementBaseObject result in results)
                    {
                        using (result)
                        {
                            cancel.ThrowIfCancellationRequested();
                            var fields = new Dictionary<string, object>();
                            foreach (string key in columns.Split(',')) fields[key.Trim()] = result[key.Trim()];
                            output.Add(fields);
                        }
                        if (output.Count == 16) break;
                    }
                }
            }
            catch (ManagementException) { output.Clear(); }
            catch (UnauthorizedAccessException) { output.Clear(); }
            catch (System.Runtime.InteropServices.COMException) { output.Clear(); }
            return output;
        }

        private static string Value(Dictionary<string, object> row, string key)
        {
            return row == null ? null : Clean(row[key]);
        }

        private static string Clean(object value)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = new string(text.Where(character => !char.IsControl(character)).Take(96).ToArray()).Trim();
            if (text.Length == 0 || text == "Default string" || text == "To Be Filled By O.E.M.") return null;
            return text;
        }

        private static long Number(Dictionary<string, object> row, string key)
        {
            long number;
            return long.TryParse(Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture), out number) &&
                number >= 0 ? number : 0;
        }
    }
}
