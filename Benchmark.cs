using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WaiuBenchmark
{
    internal sealed class Result
    {
        internal readonly double Single, Parallel, Memory;
        internal readonly int Workers;
        internal readonly DateTime Date;
        internal double Compression;
        internal double AllCoreRate;
        internal int AllCoreWorkers;
        internal double[] Latency;
        internal double? GraphicsTime;
        internal GraphicsLoad HeavyGraphics;
        internal double[] Metrics;
        internal string StorageDrive;

        internal Result(double single, double parallel, double memory, int workers, DateTime date)
        {
            Single = single;
            Parallel = parallel;
            Memory = memory;
            Workers = workers;
            Date = date;
        }

        private const string Format = "WAIU-PC-2";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, new[] { Format, Date.ToString("O", Invariant),
                    Workers.ToString(Invariant), Single.ToString("R", Invariant),
                    Parallel.ToString("R", Invariant), Memory.ToString("R", Invariant),
                    GraphicsTime.HasValue ? GraphicsTime.Value.ToString("R",Invariant) : "",
                    Metrics==null ? "" : string.Join(",",Array.ConvertAll(Metrics,value=>value.ToString("R",Invariant))),
                    StorageDrive ?? "" });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static Result Load(string path, int workers)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 1024) return null;
            string[] lines = File.ReadAllLines(path);
            DateTime date;
            int savedWorkers;
            double single, parallel, memory;
            if ((lines.Length != 6 && lines.Length != 7 && lines.Length != 9) || lines[0] != Format ||
                !DateTime.TryParseExact(lines[1], "O", Invariant, DateTimeStyles.RoundtripKind, out date) ||
                !int.TryParse(lines[2], NumberStyles.None, Invariant, out savedWorkers) || savedWorkers != workers ||
                !ReadRate(lines[3], out single) || !ReadRate(lines[4], out parallel) || !ReadRate(lines[5], out memory))
                return null;
            double gpu;
            var result=new Result(single, parallel, memory, savedWorkers, date)
            { GraphicsTime=lines.Length>=7 && ReadRate(lines[6],out gpu) ? (double?)gpu : null };
            if (lines.Length==9)
            {
                string[] metrics=lines[7].Split(',');
                if (metrics.Length==ComponentScores.MetricCount)
                {
                    var parsed=new double[metrics.Length];
                    for (int i=0;i<parsed.Length;i++)
                        if (!ReadRate(metrics[i],out parsed[i])) parsed[i]=0;
                    result.Metrics=parsed;
                }
                if (lines[8].Length==2 && lines[8][0]>='A' && lines[8][0]<='Z' && lines[8][1]==':')
                    result.StorageDrive=lines[8];
            }
            return result;
        }

        private static bool ReadRate(string text, out double rate)
        {
            return double.TryParse(text, NumberStyles.Float, Invariant, out rate) &&
                rate > 0 && !double.IsInfinity(rate);
        }
    }

    internal static class Benchmark
    {
        private const int SampleMilliseconds = 5000;
        internal const int AllCoreSeconds = 10;
        private const int MiB = 1024 * 1024;
        private const int BufferBytes = 32 * MiB;
        internal static int WorkerCount { get { return Math.Min(8, Environment.ProcessorCount); } }

        internal static Result Run(CancellationToken cancel, Action<int,double,string> progress)
        {
            cancel.ThrowIfCancellationRequested();
            const string singleLabel="Measuring your CPU with one worker...";
            string parallelLabel="Measuring your CPU with " + WorkerCount + " workers...";
            const string memoryLabel="Testing your RAM with repeated memory copies...";
            progress(0,0,singleLabel);
            double single=HashRate(1,cancel,value=>progress(0,0.21*value,singleLabel));
            progress(0,0.21,parallelLabel);
            double parallel=HashRate(WorkerCount,cancel,value=>progress(0,0.21+0.21*value,parallelLabel));
            int allCoreWorkers=Environment.ProcessorCount;
            string allCoreLabel="All-core CPU test · "+allCoreWorkers+" logical workers";
            progress(0,0.42,allCoreLabel);
            double allCore=HashRate(allCoreWorkers,cancel,value=>progress(0,0.42+0.41*value,allCoreLabel),AllCoreSeconds*1000);
            const string compressionLabel="Compressing sample data with your CPU...";
            progress(0,0.83,compressionLabel);
            double compression=DeepBenchmark.Compress(cancel,value=>progress(0,0.83+0.17*value,compressionLabel));
            progress(1,0,memoryLabel);
            double memory=CopyRate(cancel,value=>progress(1,0.55*value,memoryLabel));
            double[] latency=DeepBenchmark.MemoryLatency(cancel,(value,label)=>progress(1,0.55+0.45*value,label));
            cancel.ThrowIfCancellationRequested();
            return new Result(single, parallel, memory, WorkerCount, DateTime.Now)
            { Compression=compression, Latency=latency, AllCoreRate=allCore, AllCoreWorkers=allCoreWorkers };
        }

        private static double HashRate(int workers, CancellationToken cancel, Action<double> progress,
            int sampleMilliseconds=SampleMilliseconds)
        {
            cancel.ThrowIfCancellationRequested();
            // Synthetic input only. Never read user files.
            var input = new byte[MiB];
            new Random(42).NextBytes(input);
            var counters = new int[workers];
            var tasks = new Task[workers];
            using (var ready = new CountdownEvent(workers))
            using (var start = new ManualResetEventSlim())
            {
                var watch = new Stopwatch();
                for (int i = 0; i < workers; i++)
                {
                    int worker = i;
                    tasks[i] = Task.Factory.StartNew(() =>
                    {
                        ready.Signal();
                        start.Wait();
                        using (SHA256 hash = SHA256.Create())
                        {
                            do
                            {
                                if (cancel.IsCancellationRequested) return;
                                hash.ComputeHash(input);
                                counters[worker]++;
                            } while (watch.ElapsedMilliseconds < sampleMilliseconds);
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                ready.Wait();
                watch.Start();
                start.Set();
                while (!Task.WaitAll(tasks,125)) progress(Math.Min(1,watch.ElapsedMilliseconds/(double)sampleMilliseconds));
                watch.Stop();
                cancel.ThrowIfCancellationRequested();
                int total = 0;
                foreach (int count in counters) total += count;
                return total / watch.Elapsed.TotalSeconds;
            }
        }

        private static double CopyRate(CancellationToken cancel, Action<double> progress)
        {
            cancel.ThrowIfCancellationRequested();
            var source = new byte[BufferBytes];
            var destination = new byte[BufferBytes];
            for (int i = 0; i < source.Length; i += 4096) source[i] = (byte)(i / 4096);
            Buffer.BlockCopy(source, 0, destination, 0, BufferBytes);
            var watch = Stopwatch.StartNew();
            int copies = 0;
            long nextUpdate=125;
            do
            {
                cancel.ThrowIfCancellationRequested();
                Buffer.BlockCopy(source, 0, destination, 0, BufferBytes);
                copies++;
                if (watch.ElapsedMilliseconds>=nextUpdate)
                {
                    progress(Fraction(watch));
                    nextUpdate=watch.ElapsedMilliseconds+125;
                }
            } while (watch.ElapsedMilliseconds < SampleMilliseconds);
            watch.Stop();
            return copies * (BufferBytes / (double)MiB) / watch.Elapsed.TotalSeconds;
        }

        private static double Fraction(Stopwatch watch) { return Math.Min(1,watch.ElapsedMilliseconds/(double)SampleMilliseconds); }
    }

    internal static class Hardware
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
                TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        internal static ulong[] ReadMemory()
        {
            var status=new MemoryStatus { Length=(uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            return GlobalMemoryStatusEx(ref status) ? new[] { status.TotalPhysical,status.AvailablePhysical } : new ulong[0];
        }

        internal static string Summary()
        {
            string cpu = "Processor model unavailable";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    string name = key == null ? null : key.GetValue("ProcessorNameString") as string;
                    if (!string.IsNullOrWhiteSpace(name)) cpu = name.Trim();
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }

            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            string ram = GlobalMemoryStatusEx(ref status)
                ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} GiB RAM usable by Windows", status.TotalPhysical / 1073741824.0)
                : "RAM size unavailable";
            return cpu + "\r\n" + Environment.ProcessorCount + " logical processors  |  " + ram;
        }
    }
}
