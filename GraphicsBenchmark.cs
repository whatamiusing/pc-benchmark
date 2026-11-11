using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace WaiuBenchmark
{
    internal sealed class GraphicsLoad
    {
        internal readonly double FramesPerSecond;
        internal readonly double? FrameMilliseconds;

        internal GraphicsLoad(double framesPerSecond,double? frameMilliseconds)
        {
            FramesPerSecond=framesPerSecond;
            FrameMilliseconds=frameMilliseconds;
        }
    }

    internal sealed class GraphicsBenchmark : IDisposable
    {
        internal const int Width = 1920, Height = 1080, SceneSeconds = 16;
        internal const int HeavyWidth=2560, HeavyHeight=1440, HeavySeconds=16;
        private const int FramesPerBatch=4;
        internal const string HeavyTitle="Glassworks heavy pass";
        internal static readonly string[] Scenes = { "Clean Energy Circuit", "Glassworks" };
        private readonly List<IntPtr> resources = new List<IntPtr>();
        private IntPtr device, context, target, staging, constants, before, after, clock;
        private IntPtr queryData, frameData;
        private int width,height;

        internal static double? Run(CancellationToken cancel, Action<double, string> progress, Action<Bitmap,double?> preview,
            out string adapterName, out double? slowFrame, out double? glassTime, out GraphicsLoad heavy)
        {
            adapterName=null;
            slowFrame=null;
            glassTime=null;
            heavy=null;
            double? first=null;
            try
            {
                using (var test = new GraphicsBenchmark())
                {
                    progress(0, "Preparing Clean Energy Circuit at 1080p...");
                    test.Initialise();
                    adapterName=test.AdapterName();
                    first=test.MeasureScene(0,cancel,progress,preview,out slowFrame);
                    double? glassSlow;
                    glassTime=test.MeasureScene(1,cancel,progress,preview,out glassSlow);
                }
                cancel.ThrowIfCancellationRequested();
                progress(2.0/3, "Preparing "+HeavyTitle+" at 1440p...");
                using (var test=new GraphicsBenchmark())
                {
                    test.Initialise(width:HeavyWidth,height:HeavyHeight,pixelEntry:"psHeavy");
                    heavy=test.MeasureHeavy(cancel,progress,preview);
                }
                progress(1, "Graphics measurement complete.");
            }
            catch (COMException) { }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (TimeoutException) { }
            catch (InvalidDataException) { }
            return first;
        }

        private double? MeasureScene(int scene,CancellationToken cancel,Action<double,string> progress,
            Action<Bitmap,double?> preview,out double? slow)
        {
            slow=null;
            string title="Rendering "+Scenes[scene]+" at 1080p";
            progress(scene/3.0,title+"...");
            DrawFrame(0,scene,cancel);
            var watch=Stopwatch.StartNew();
            double nextPreview=0,total=0,sampleStart=0;
            double? fps=null;
            int sampleFrames=0;
            var frames=new List<double>();
            while (watch.Elapsed.TotalSeconds<SceneSeconds)
            {
                cancel.ThrowIfCancellationRequested();
                double elapsed=watch.Elapsed.TotalSeconds;
                double? measured=DrawFrame(elapsed,scene,cancel);
                if (measured.HasValue) { frames.Add(measured.Value); total+=measured.Value; }
                sampleFrames++;
                if (elapsed>=nextPreview)
                {
                    Bitmap bitmap=Snapshot();
                    double now=watch.Elapsed.TotalSeconds;
                    double interval=now-sampleStart;
                    if (interval>=0.5)
                    {
                        fps=sampleFrames/interval;
                        sampleStart=now;
                        sampleFrames=0;
                    }
                    preview(bitmap,fps);
                    nextPreview=watch.Elapsed.TotalSeconds+0.065;
                    progress((scene+Math.Min(1,elapsed/SceneSeconds))/3,title+" · "+elapsed.ToString("0.0")+" s");
                }
            }
            if (frames.Count==0) return null;
            frames.Sort();
            slow=frames[(int)Math.Ceiling(0.95*frames.Count)-1];
            return total/frames.Count;
        }

        private struct BatchQueries
        {
            internal IntPtr Before, After, Clock;
        }

        private GraphicsLoad MeasureHeavy(CancellationToken cancel,Action<double,string> progress,Action<Bitmap,double?> preview)
        {
            var slots=new[] { new BatchQueries { Before=before,After=after,Clock=clock },
                new BatchQueries { Before=Query(2),After=Query(2),Clock=Query(3) } };
            const string title=HeavyTitle+" · 1440p";
            QueueBatch(slots[0],0,cancel);
            ReadTiming(slots[0],cancel,true);
            preview(Snapshot(),null);

            var watch=Stopwatch.StartNew();
            int submitted=0, completed=0, timedFrames=0, lastFrames=0;
            double totalMilliseconds=0, nextPreview=0.2, sampleStart=0;
            double? fps=null;
            bool previewPending=false;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                while (submitted-completed<slots.Length && watch.Elapsed.TotalSeconds<HeavySeconds)
                {
                    QueueBatch(slots[submitted%slots.Length],watch.Elapsed.TotalSeconds,cancel);
                    submitted++;
                }
                if (completed==submitted) break;
                double? elapsed=ReadTiming(slots[completed%slots.Length],cancel,true);
                completed++;
                if (elapsed.HasValue) { totalMilliseconds+=elapsed.Value; timedFrames+=FramesPerBatch; }
                double now=watch.Elapsed.TotalSeconds;
                if (now-sampleStart>=0.5)
                {
                    fps=(completed-lastFrames)*FramesPerBatch/(now-sampleStart);
                    lastFrames=completed;
                    sampleStart=now;
                }
                if (previewPending)
                {
                    var bitmap=ReadSnapshot(false);
                    if (bitmap!=null)
                    {
                        preview(bitmap,fps);
                        previewPending=false;
                    }
                }
                if (now>=nextPreview)
                {
                    if (!previewPending)
                    {
                        QueueSnapshot();
                        previewPending=true;
                    }
                    progress((2+Math.Min(1,now/HeavySeconds))/3,title+" · "+now.ToString("0.0")+" s");
                    nextPreview=now+0.2;
                }
            }
            return completed>0 ? new GraphicsLoad(completed*FramesPerBatch/watch.Elapsed.TotalSeconds,
                timedFrames>0 ? (double?)(totalMilliseconds/timedFrames) : null) : null;
        }

        private void QueueBatch(BatchQueries queries,double elapsed,CancellationToken cancel)
        {
            var end=Slot<QueryCommand>(context,28);
            Slot<QueryCommand>(context,27)(context,queries.Clock);
            end(context,queries.Before);
            for (int i=0;i<FramesPerBatch;i++)
            {
                cancel.ThrowIfCancellationRequested();
                SubmitFrame(elapsed+i/60.0,1);
            }
            end(context,queries.After);
            end(context,queries.Clock);
        }

        internal static void RenderArtwork(int scene,int count,int width,int height,Action<int,Bitmap> consume)
        {
            using (var renderer=new GraphicsBenchmark())
            {
                renderer.Initialise("StageArt.hlsl",width,height);
                for (int index=0;index<count;index++)
                {
                    renderer.DrawFrame(index/(double)count,scene,CancellationToken.None);
                    using (var frame=renderer.Snapshot()) consume(index,frame);
                }
            }
        }

        private void Initialise(string shader="CleanEnergy.hlsl",int width=Width,int height=Height,string pixelEntry="psMain")
        {
            this.width=width;
            this.height=height;
            // Hardware only. A software-rendered result is not a graphics-card benchmark.
            int feature;
            Check(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0, IntPtr.Zero, 0, 7, out device, out feature, out context));
            Keep(device);
            Keep(context);
            var texture = new TextureDesc { Width=width, Height=height, MipLevels=1, ArraySize=1, Format=87,
                SampleCount=1, BindFlags=0x20 };
            IntPtr created;
            Check(Slot<CreateTexture>(device, 5)(device, ref texture, IntPtr.Zero, out created));
            target = Keep(created);
            texture.BindFlags=0; texture.Usage=3; texture.CpuAccess=0x20000;
            Check(Slot<CreateTexture>(device, 5)(device, ref texture, IntPtr.Zero, out created));
            staging = Keep(created);
            Check(Slot<CreateView>(device, 9)(device, target, IntPtr.Zero, out created));
            IntPtr view = Keep(created);
            var buffer = new BufferDesc { ByteWidth=16, BindFlags=4 };
            Check(Slot<CreateBuffer>(device, 3)(device, ref buffer, IntPtr.Zero, out created));
            constants = Keep(created);
            IntPtr vertex = Shader(shader,"vsMain", "vs_5_0", 12);
            IntPtr pixel = Shader(shader,pixelEntry, "ps_5_0", 15);
            before = Query(2); after = Query(2); clock = Query(3);
            var viewport = new Viewport { Width=width, Height=height, MaxDepth=1 };
            Slot<SetViewports>(context, 44)(context, 1, ref viewport);
            Slot<SetTargets>(context, 33)(context, 1, ref view, IntPtr.Zero);
            Slot<SetTopology>(context, 24)(context, 4);
            Slot<SetShader>(context, 11)(context, vertex, IntPtr.Zero, 0);
            Slot<SetShader>(context, 9)(context, pixel, IntPtr.Zero, 0);
            Slot<SetBuffers>(context, 16)(context, 0, 1, ref constants);
            queryData=Marshal.AllocHGlobal(16);
            frameData=Marshal.AllocHGlobal(16);
            Marshal.WriteInt64(frameData,0,0);
            Marshal.WriteInt64(frameData,8,0);
        }

        private double? DrawFrame(double elapsed,int scene,CancellationToken cancel)
        {
            var queries=new BatchQueries { Before=before,After=after,Clock=clock };
            SetFrameConstants(elapsed,scene);
            var end=Slot<QueryCommand>(context,28);
            Slot<QueryCommand>(context,27)(context,clock);
            end(context,before);
            Slot<Draw>(context,13)(context,3,0);
            end(context,after);
            end(context,clock);
            return ReadTiming(queries,cancel);
        }

        private void SubmitFrame(double elapsed,int scene)
        {
            SetFrameConstants(elapsed,scene);
            Slot<Draw>(context,13)(context,3,0);
        }

        private void SetFrameConstants(double elapsed,int scene)
        {
            Marshal.WriteInt32(frameData, BitConverter.ToInt32(BitConverter.GetBytes((float)elapsed), 0));
            Marshal.WriteInt32(frameData,4,BitConverter.ToInt32(BitConverter.GetBytes((float)scene),0));
            Slot<UpdateResource>(context, 48)(context, constants, 0, IntPtr.Zero, frameData, 0, 0);
        }

        private double? ReadTiming(BatchQueries queries,CancellationToken cancel,bool keepQueueFed=false)
        {
            WaitFor(queries.Clock,16,cancel,keepQueueFed);
            long frequency = Marshal.ReadInt64(queryData);
            if (frequency <= 0 || Marshal.ReadInt32(queryData, 8) != 0) return null;
            WaitFor(queries.Before,8,cancel,keepQueueFed);
            long start = Marshal.ReadInt64(queryData);
            WaitFor(queries.After,8,cancel,keepQueueFed);
            long finish = Marshal.ReadInt64(queryData);
            return finish > start ? (double?)(1000.0 * (finish-start)/frequency) : null;
        }

        private void WaitFor(IntPtr query,uint size,CancellationToken cancel,bool keepQueueFed)
        {
            var watch=Stopwatch.StartNew();
            var getData=Slot<GetData>(context, 29);
            uint flags=0;
            int result;
            while ((result=getData(context,query,queryData,size,flags))==1)
            {
                cancel.ThrowIfCancellationRequested();
                if (watch.ElapsedMilliseconds > 3500) throw new TimeoutException("The graphics device stopped responding.");
                if (keepQueueFed)
                {
                    // The first poll can submit buffered work. Later polls only check completion.
                    flags=1;
                    Thread.Yield();
                }
                else Thread.Sleep(1);
            }
            Check(result);
        }

        private Bitmap Snapshot()
        {
            QueueSnapshot();
            return ReadSnapshot(true);
        }

        private void QueueSnapshot()
        {
            Slot<CopyResource>(context, 47)(context, staging, target);
        }

        private Bitmap ReadSnapshot(bool wait)
        {
            Mapped mapped;
            const uint doNotWait=0x100000;
            int result=Slot<MapResource>(context,14)(context,staging,0,1,wait ? 0 : doNotWait,out mapped);
            if (!wait && result==unchecked((int)0x887A000A)) return null;
            Check(result);
            var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                var bits=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try
                {
                    for (int y=0; y<height; y++)
                        MoveMemory(IntPtr.Add(bits.Scan0,y*bits.Stride),IntPtr.Add(mapped.Data,y*mapped.RowPitch),(UIntPtr)(width*4));
                }
                finally { image.UnlockBits(bits); }
                return image;
            }
            catch { image.Dispose(); throw; }
            finally { Slot<UnmapResource>(context, 15)(context, staging, 0); }
        }

        private IntPtr Query(int type)
        {
            var desc = new QueryDesc { Query=type };
            IntPtr query;
            Check(Slot<CreateQuery>(device, 24)(device, ref desc, out query));
            return Keep(query);
        }

        private string AdapterName()
        {
            var iid=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            IntPtr dxgi;
            if (Marshal.QueryInterface(device,ref iid,out dxgi)<0) return null;
            try
            {
                IntPtr adapter;
                if (Slot<GetAdapter>(dxgi,7)(dxgi,out adapter)<0) return null;
                try
                {
                    AdapterDesc description;
                    return Slot<GetAdapterDesc>(adapter,8)(adapter,out description)>=0 ? description.Description : null;
                }
                finally { Marshal.Release(adapter); }
            }
            finally { Marshal.Release(dxgi); }
        }

        private IntPtr Shader(string resource,string entry, string profile, int slot)
        {
            byte[] source;
            using (var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (var buffer=new MemoryStream()) { stream.CopyTo(buffer); source=buffer.ToArray(); }
            IntPtr code, errors;
            int result=D3DCompile(source,(UIntPtr)source.Length,null,IntPtr.Zero,IntPtr.Zero,entry,profile,0x8000,0,out code,out errors);
            try
            {
                if (result<0)
                {
                    string reason=errors==IntPtr.Zero ? "Unknown compiler error" :
                        Marshal.PtrToStringAnsi(Slot<BlobPointer>(errors,3)(errors));
                    throw new InvalidDataException(resource+" shader: "+reason);
                }
                IntPtr shader;
                Check(Slot<CreateShader>(device,slot)(device,Slot<BlobPointer>(code,3)(code),
                    Slot<BlobSize>(code,4)(code),IntPtr.Zero,out shader));
                return Keep(shader);
            }
            finally
            {
                if (errors!=IntPtr.Zero) Marshal.Release(errors);
                if (code!=IntPtr.Zero) Marshal.Release(code);
            }
        }

        private IntPtr Keep(IntPtr resource) { resources.Add(resource); return resource; }
        private static void Check(int result) { if (result<0) Marshal.ThrowExceptionForHR(result); }
        private static T Slot<T>(IntPtr instance, int offset) where T : class
        {
            IntPtr pointer=Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),offset*IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer,typeof(T));
        }

        public void Dispose()
        {
            for (int i=resources.Count-1; i>=0; i--) Marshal.Release(resources[i]);
            Marshal.FreeHGlobal(queryData);
            Marshal.FreeHGlobal(frameData);
        }

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,
            uint version,out IntPtr device,out int feature,out IntPtr context);
        [DllImport("d3dcompiler_47.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
        private static extern int D3DCompile(byte[] source,UIntPtr size,string name,IntPtr defines,IntPtr include,string entry,
            string profile,uint flags,uint effectFlags,out IntPtr code,out IntPtr errors);
        [DllImport("kernel32.dll", EntryPoint="RtlMoveMemory")]
        private static extern void MoveMemory(IntPtr destination,IntPtr source,UIntPtr bytes);

        [StructLayout(LayoutKind.Sequential)] private struct TextureDesc
        { public int Width,Height,MipLevels,ArraySize,Format,SampleCount,SampleQuality,Usage,BindFlags,CpuAccess,MiscFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct BufferDesc
        { public int ByteWidth,Usage,BindFlags,CpuAccess,MiscFlags,StructureByteStride; }
        [StructLayout(LayoutKind.Sequential)] private struct QueryDesc { public int Query,MiscFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct Viewport { public float X,Y,Width,Height,MinDepth,MaxDepth; }
        [StructLayout(LayoutKind.Sequential)] private struct Mapped { public IntPtr Data; public int RowPitch,DepthPitch; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct AdapterDesc
        {
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
            public uint Vendor,Device,Subsystem,Revision;
            public UIntPtr DedicatedVideo,DedicatedSystem,SharedSystem;
            public uint LuidLow;
            public int LuidHigh;
        }

        private delegate IntPtr BlobPointer(IntPtr self);
        private delegate UIntPtr BlobSize(IntPtr self);
        private delegate int CreateTexture(IntPtr self,ref TextureDesc desc,IntPtr data,out IntPtr output);
        private delegate int CreateBuffer(IntPtr self,ref BufferDesc desc,IntPtr data,out IntPtr output);
        private delegate int CreateView(IntPtr self,IntPtr resource,IntPtr desc,out IntPtr view);
        private delegate int CreateShader(IntPtr self,IntPtr code,UIntPtr bytes,IntPtr linkage,out IntPtr shader);
        private delegate int CreateQuery(IntPtr self,ref QueryDesc desc,out IntPtr query);
        private delegate void SetViewports(IntPtr self,uint count,ref Viewport viewport);
        private delegate void SetTargets(IntPtr self,uint count,ref IntPtr view,IntPtr depth);
        private delegate void SetTopology(IntPtr self,int topology);
        private delegate void SetShader(IntPtr self,IntPtr shader,IntPtr instances,uint count);
        private delegate void SetBuffers(IntPtr self,uint slot,uint count,ref IntPtr buffer);
        private delegate void UpdateResource(IntPtr self,IntPtr resource,uint index,IntPtr box,IntPtr data,uint row,uint depth);
        private delegate void QueryCommand(IntPtr self,IntPtr query);
        private delegate int GetData(IntPtr self,IntPtr query,IntPtr data,uint size,uint flags);
        private delegate void Draw(IntPtr self,uint count,uint first);
        private delegate void CopyResource(IntPtr self,IntPtr destination,IntPtr source);
        private delegate int MapResource(IntPtr self,IntPtr resource,uint index,int map,uint flags,out Mapped data);
        private delegate void UnmapResource(IntPtr self,IntPtr resource,uint index);
        private delegate int GetAdapter(IntPtr self,out IntPtr adapter);
        private delegate int GetAdapterDesc(IntPtr self,out AdapterDesc description);
    }
}
