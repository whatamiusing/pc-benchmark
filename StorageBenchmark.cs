using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace WaiuBenchmark
{
    internal sealed class StorageBenchmark
    {
        internal const int SampleMiB = 512;
        private const int MiB = 1024 * 1024;
        internal string Drive;
        internal double? Write, Read, RandomRead;
        internal long FreeBytes, VolumeBytes;

        internal static StorageBenchmark Run(CancellationToken cancel, Action<double,string> progress)
        {
            var result = new StorageBenchmark();
            IntPtr memory=IntPtr.Zero;
            try
            {
                string directory=Path.GetTempPath(), root=Path.GetPathRoot(directory);
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return result;
                var drive = new DriveInfo(root);
                if (drive.DriveType!=DriveType.Fixed) return result;
                result.Drive=drive.Name.Substring(0,2);
                result.FreeBytes=drive.AvailableFreeSpace;
                result.VolumeBytes=drive.TotalSize;
                if (result.FreeBytes<1024L*MiB) return result;
                string path=Path.Combine(directory,"WAIU-"+Guid.NewGuid().ToString("N")+".tmp");
                memory=Marshal.AllocHGlobal(MiB+65535);
                var aligned=new IntPtr((memory.ToInt64()+65535L)&~65535L);
                byte[] data=new byte[MiB];
                new Random(31).NextBytes(data);
                Marshal.Copy(data,0,aligned,MiB);
                cancel.ThrowIfCancellationRequested();
                // WRITE_THROUGH, NO_BUFFERING and DELETE_ON_CLOSE, in that order.
                using (var file=CreateFile(path,0xc0010000,0,IntPtr.Zero,1,0xa4000080,IntPtr.Zero))
                {
                    if (file.IsInvalid) return result;
                    double? write=Transfer(file,aligned,WriteFile,0,"Writing the storage test file...",cancel,progress);
                    long position;
                    if (!write.HasValue || !SetFilePointerEx(file,0,out position,0)) return result;
                    result.Write=write;
                    result.Read=Transfer(file,aligned,ReadFile,0.4,"Reading the storage test file...",cancel,progress);
                    if (result.Read.HasValue) result.RandomRead=ReadSmallBlocks(file,aligned,cancel,progress);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            finally { Marshal.FreeHGlobal(memory); }
            return result;
        }

        private static double? Transfer(SafeFileHandle file,IntPtr buffer,TransferBlock transfer,double start,string label,
            CancellationToken cancel,Action<double,string> progress)
        {
            var watch=Stopwatch.StartNew();
            progress(start,label);
            for (int index=0; index<SampleMiB; index++)
            {
                cancel.ThrowIfCancellationRequested();
                uint count;
                if (watch.Elapsed.TotalSeconds>12 || !transfer(file,buffer,MiB,out count,IntPtr.Zero) || count!=MiB)
                    return null;
                if ((index+1)%32==0 || index+1==SampleMiB)
                    progress(start+0.4*(index+1)/SampleMiB,label+" "+(index+1)+" / "+SampleMiB+" MiB");
            }
            if (start==0 && !FlushFileBuffers(file)) return null;
            watch.Stop();
            cancel.ThrowIfCancellationRequested();
            return watch.Elapsed.TotalSeconds<=12 ? (double?)(SampleMiB/watch.Elapsed.TotalSeconds) : null;
        }

        private static double? ReadSmallBlocks(SafeFileHandle file,IntPtr buffer,CancellationToken cancel,Action<double,string> progress)
        {
            const uint blockSize=4096;
            const int maxRequests=4096;
            var random=new Random(73);
            var watch=Stopwatch.StartNew();
            int reads=0;
            progress(0.8,"Measuring small random reads (4 KiB)...");
            while (reads<maxRequests && watch.Elapsed.TotalSeconds<4)
            {
                cancel.ThrowIfCancellationRequested();
                long position=random.Next((int)(SampleMiB*MiB/blockSize))*(long)blockSize;
                long actual;
                uint count;
                if (!SetFilePointerEx(file,position,out actual,0) ||
                    !ReadFile(file,buffer,blockSize,out count,IntPtr.Zero) || count!=blockSize) return null;
                reads++;
                if (reads%64==0)
                    progress(0.8+0.2*Math.Min(1,Math.Max(reads/(double)maxRequests,watch.Elapsed.TotalSeconds/4)),
                        "Measuring small random reads (4 KiB)...");
            }
            return reads>0 ? (double?)(reads/watch.Elapsed.TotalSeconds) : null;
        }

        private delegate bool TransferBlock(SafeFileHandle file,IntPtr buffer,uint count,out uint done,IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode,SetLastError=true)]
        private static extern SafeFileHandle CreateFile(string path,uint access,uint sharing,IntPtr attributes,
            uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(SafeFileHandle file,IntPtr buffer,uint count,out uint done,IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(SafeFileHandle file,IntPtr buffer,uint count,out uint done,IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle file);
        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFilePointerEx(SafeFileHandle file,long distance,out long position,uint method);
    }
}
