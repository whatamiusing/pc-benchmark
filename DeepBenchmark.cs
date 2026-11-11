using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;

namespace WaiuBenchmark
{
    internal static class DeepBenchmark
    {
        private const int MiB=1024*1024;
        internal static readonly int[] LatencyKiB={ 256,8192,65536 };

        internal static double Compress(CancellationToken cancel,Action<double> progress)
        {
            var input=new byte[MiB];
            var noise=new byte[512];
            var random=new Random(71);
            for (int block=0;block<input.Length;block+=4096)
            {
                cancel.ThrowIfCancellationRequested();
                for (int i=0;i<4096;i++) input[block+i]=(byte)((i/17+block/4096)%71);
                random.NextBytes(noise);
                Buffer.BlockCopy(noise,0,input,block,noise.Length);
            }
            using (var output=new MemoryStream(input.Length+65536))
            {
                var watch=Stopwatch.StartNew();
                long bytes=0;
                long nextUpdate=125;
                do
                {
                    output.SetLength(0);
                    using (var compressed=new DeflateStream(output,CompressionLevel.Fastest,true))
                    {
                        for (int offset=0;offset<input.Length;offset+=65536)
                        {
                            cancel.ThrowIfCancellationRequested();
                            compressed.Write(input,offset,65536);
                        }
                    }
                    bytes+=input.Length;
                    if (watch.ElapsedMilliseconds>=nextUpdate)
                    {
                        progress(Math.Min(1,watch.Elapsed.TotalSeconds/4));
                        nextUpdate=watch.ElapsedMilliseconds+125;
                    }
                } while(watch.ElapsedMilliseconds<4000);
                return bytes/(double)MiB/watch.Elapsed.TotalSeconds;
            }
        }

        internal static double[] MemoryLatency(CancellationToken cancel,Action<double,string> progress)
        {
            var rates=new double[LatencyKiB.Length];
            // A single large backing array is reused across all three workloads.
            int[] links=new int[LatencyKiB[LatencyKiB.Length-1]*1024/sizeof(int)];
            int[] order=new int[links.Length/16];
            for (int sample=0;sample<rates.Length;sample++)
            {
                cancel.ThrowIfCancellationRequested();
                int count=LatencyKiB[sample]*1024/64;
                string label="Measuring memory access time · "+(LatencyKiB[sample]<1024 ?
                    LatencyKiB[sample]+" KiB" : LatencyKiB[sample]/1024+" MiB")+" working set";
                progress(sample/(double)rates.Length,label);
                for (int i=0;i<count;i++) order[i]=i*16;
                var random=new Random(39);
                for (int i=count-1;i>0;i--)
                {
                    if ((i&8191)==0) cancel.ThrowIfCancellationRequested();
                    int other=random.Next(i+1);
                    int value=order[i];
                    order[i]=order[other];
                    order[other]=value;
                }
                for (int i=0;i<count;i++) links[order[i]]=order[(i+1)%count];
                int index=order[0];
                for (int warmup=0;warmup<16384;warmup++) index=links[index];
                var watch=Stopwatch.StartNew();
                long reads=0,nextUpdate=125;
                do
                {
                    cancel.ThrowIfCancellationRequested();
                    for (int i=0;i<8192;i++) index=links[index];
                    reads+=8192;
                    if (watch.ElapsedMilliseconds>=nextUpdate)
                    {
                        progress((sample+Math.Min(1,watch.Elapsed.TotalSeconds/1.25))/rates.Length,label);
                        nextUpdate=watch.ElapsedMilliseconds+125;
                    }
                } while(watch.ElapsedMilliseconds<1250);
                rates[sample]=watch.Elapsed.TotalSeconds*1e9/reads;
                GC.KeepAlive(index);
            }
            return rates;
        }

        internal static double? BackgroundCpu(CancellationToken cancel)
        {
            long idleBefore,kernelBefore,userBefore;
            if (!GetSystemTimes(out idleBefore,out kernelBefore,out userBefore)) return null;
            if (cancel.WaitHandle.WaitOne(900)) cancel.ThrowIfCancellationRequested();
            long idleAfter,kernelAfter,userAfter;
            if (!GetSystemTimes(out idleAfter,out kernelAfter,out userAfter)) return null;
            long capacity=kernelAfter-kernelBefore+userAfter-userBefore;
            if (capacity<=0) return null;
            return Math.Max(0,Math.Min(100,100*(1-(idleAfter-idleBefore)/(double)capacity)));
        }

        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    }
}
