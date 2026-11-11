using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WaiuBenchmark
{
    internal static class SmokeTest
    {
        private static void Check(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Contains("--report-only"))
            {
                CheckProfiles();
                CheckReferenceUpdates().GetAwaiter().GetResult();
                return;
            }
            var clock = Stopwatch.StartNew();
            var first = new Result(350,900,5000,Benchmark.WorkerCount,DateTime.Now);
            Check(!string.IsNullOrWhiteSpace(Hardware.Summary()), "Hardware summary is available.");

            var folder = Path.Combine(Path.GetTempPath(), "WaiuBenchmarkTest-" + Guid.NewGuid().ToString("N"));
            var file = Path.Combine(folder, "baseline.txt");
            try
            {
                Check(Result.Load(file, first.Workers) == null, "Missing baseline is accepted.");
                first.Save(file);
                var loaded = Result.Load(file, first.Workers);
                Check(loaded != null && loaded.Single == first.Single && loaded.Parallel == first.Parallel &&
                    loaded.Memory == first.Memory && loaded.Date == first.Date, "Baseline round-trip.");
                Check(Result.Load(file, first.Workers + 1) == null, "Incompatible worker count is rejected.");
                first.Save(file);
                Check(Result.Load(file, first.Workers) != null, "Baseline replacement succeeds.");
                File.WriteAllText(file, "broken");
                Check(Result.Load(file, first.Workers) == null, "Invalid baseline is rejected.");
                CheckWindow(file);
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }

            using (var stop = new CancellationTokenSource())
            {
                stop.CancelAfter(200);
                clock.Restart();
                try
                {
                    Benchmark.Run(stop.Token, (_, fraction, message) => { });
                    throw new InvalidOperationException("Run was not cancelled.");
                }
                catch (OperationCanceledException) { }
                Check(clock.Elapsed < TimeSpan.FromSeconds(3), "The workload stopped promptly.");
            }
            Console.WriteLine("Baseline persistence, validation and cancellation passed.");
        }

        private static async Task CheckReferenceUpdates()
        {
            string folder = Path.Combine(Path.GetTempPath(), "WaiuReferenceTest-" + Guid.NewGuid().ToString("N"));
            string cache = Path.Combine(folder, ReferenceTargets.FileName);
            string bundled;
            using (var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("ReferenceV2.json")))
                bundled=reader.ReadToEnd();
            string revised=bundled.Replace("\"revision\": 1","\"revision\": 2").Replace("[30, 60, 90, 120]","[45, 90, 120, 144]");
            try
            {
                var offline = await ReferenceTargets.LoadAsync(null, cache, CancellationToken.None);
                Check(offline.Revision==1 && offline.GraphicsScore(1000.0/60)==60 && offline.GraphicsScore(0)==-1,
                    "The bundled reference handles the exact target and missing measurements.");
                var updated = await FetchReference(cache, revised);
                Check(updated.Revision==2 && updated.Source=="Downloaded" && ReferenceTargets.Grade(updated.GraphicsScore(1000.0/60))==1,
                    "The same app applies revised targets fetched over HTTP.");
                var report = new PcReport { Reference=updated };
                report.Data["graphicsTime"]=12.0;
                report.Data["graphicsSlowFrame"]=1000.0/60;
                var html = LocalReport.Render(report);
                Check(html.Contains("Reference v2") && html.Contains("Above the 90 fps render budget") &&
                    html.Contains("Approaching target</span>"), "The report shows the new grade, target and reference.");
                Check((await ReferenceTargets.LoadAsync(new Uri("http://127.0.0.1:1/"), cache, CancellationToken.None)).Source=="Cached",
                    "A fresh cache is reused without needing the server.");
                File.SetLastWriteTimeUtc(cache,DateTime.UtcNow.AddDays(-2));
                Check((await ReferenceTargets.LoadAsync(new Uri("http://127.0.0.1:1/"),cache,CancellationToken.None)).Revision==2,
                    "If the server is unreachable, the older cached file still works.");
                Check((await FetchReference(cache,revised.Replace("[45, 90, 120, 144]","[90, 45, 120, 144]"))).Source=="Cached" &&
                    (await FetchReference(cache,revised.Replace(ReferenceTargets.Workload,"incompatible-workload"))).Source=="Cached" &&
                    (await FetchReference(cache,revised.Replace("\"revision\": 2","\"revision\": 1"))).Source=="Cached",
                    "Malformed targets, incompatible workloads and older revisions cannot replace the cache.");
                File.WriteAllText(cache,"invalid cache");
                Check((await FetchReference(cache,"{}")).Source=="Bundled","Invalid local and remote data fall back to the bundled reference.");
                Console.WriteLine("Reference download, cache, offline fallback, validation and report grading passed.");
            }
            finally
            {
                if (File.Exists(cache)) File.Delete(cache);
                if (Directory.Exists(folder)) Directory.Delete(folder);
            }
        }

        private static async Task<ReferenceTargets> FetchReference(string cache,string json)
        {
            var listener = new TcpListener(IPAddress.Loopback,0);
            listener.Start();
            try
            {
                var request = ReferenceTargets.LoadAsync(new Uri("http://127.0.0.1:"+
                    ((IPEndPoint)listener.LocalEndpoint).Port+"/assets/"+ReferenceTargets.FileName),cache,CancellationToken.None);
                var accept = listener.AcceptTcpClientAsync();
                Check(await Task.WhenAny(accept,Task.Delay(4500))==accept,"The app requests the test reference.");
                using (var connection = await accept)
                using (var stream = connection.GetStream())
                {
                    var reader = new StreamReader(stream,Encoding.ASCII);
                    Check((await reader.ReadLineAsync()).StartsWith("GET /assets/"+ReferenceTargets.FileName+" ",StringComparison.Ordinal),
                        "The request fetches the fixed asset with no hardware details in the URL.");
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nConnection: close\r\nContent-Length: "+
                        Encoding.UTF8.GetByteCount(json)+"\r\n\r\n"+json);
                    await stream.WriteAsync(response,0,response.Length);
                }
                return await request;
            }
            finally { listener.Stop(); }
        }

        private static void CheckProfiles()
        {
            var current=new Result(80,240,5000,Benchmark.WorkerCount,DateTime.Now) { GraphicsTime=10 };
            var before=new Result(100,200,4500,Benchmark.WorkerCount,DateTime.Now.AddHours(-1)) { GraphicsTime=12 };
            current.Metrics=new double[] { 80,240,100,5000,100,10,10,300,200,600 };
            before.Metrics=new double[] { 100,200,100,4500,100,12,12,300,200,600 };
            current.StorageDrive=before.StorageDrive="C:";
            var report=PcReport.Collect(current,before,new StorageBenchmark(),10,12,10,"Example graphics",0,
                new[] { 8589934592UL,536870912UL },CancellationToken.None);
            string drive=report.Data.ContainsKey("windowsDrive") ? report.Data["windowsDrive"] as string : null;
            Console.WriteLine("Windows edition detected: {0}; Windows volume detected: {1}; physical model matched: {2}.",
                report.Data["os"]!=null,drive!=null,report.Data.ContainsKey("windowsDisk")&&report.Data["windowsDisk"]!=null);

            report.Data["os"]="Windows test edition";
            report.Data["osBuild"]="26100";
            report.Data["windowsDrive"]="C:";
            report.Data["windowsDisk"]="Example storage";
            report.Data["cpu"]="13th Gen Intel(R) Core(TM) i9-13900K";
            report.Data["graphicsTestAdapter"]="NVIDIA GeForce RTX 5070 Ti";
            report.Data["storageDisk"]="Example NVMe";
            report.Data["modules"]=new[] { new { size=34359738368L } };
            report.Data["storageRandomRead"]=9000;
            report.Data["storageWrite"]=1000;
            report.Data["allCoreRate"]=19305.0;
            report.Data["allCoreWorkers"]=32;
            report.Data["graphicsHeavyFps"]=72.5;
            report.Data["graphicsHeavyFrameTime"]=12.0;
            report.Data["bios"]="F12";
            report.Data["boardMaker"]="Example & Co";
            report.Data["boardModel"]="Board A+B";
            string rendered=LocalReport.Render(report);
            Check(!rendered.Contains(">Intel XMP profile</dt>") &&
                rendered.Contains("https://www.google.com/search?q=BIOS%20update%20for%20Example%20%26%20Co%20Board%20A%2BB\" target=\"_blank\" rel=\"noopener noreferrer\""),
                "The BIOS search encodes the board name, opens a new tab and the XMP line is absent.");
            Check(rendered.Contains("across 32 logical workers for 10 seconds") &&
                rendered.Contains(GraphicsBenchmark.HeavyTitle) && rendered.Contains("FPS at 2560") &&
                rendered.Contains("four samples per pixel"),
                "Both new workloads have separate report results.");
            var footer=rendered.Substring(rendered.IndexOf("<footer",StringComparison.Ordinal));
            Check(footer.Contains("href=\"https://www.whatamiusing.com/\"") &&
                footer.Contains("href=\"https://www.whatamiusing.com/terms/\"") &&
                footer.Contains("href=\"https://www.whatamiusing.com/privacy/\""),"The report footer links to the website, terms and privacy.");
            Check(Regex.Matches(rendered,@"class=""reference-grade grade-[0-4]"">[0-9]+/100").Count==8 &&
                rendered.Contains("Whole-PC score") && rendered.Contains("How the scores work"),
                "All three uses, four components and the whole PC are scored when measurements are present.");
            Check(Regex.Matches(rendered,">Hardware on paper</strong>").Count==4 &&
                Regex.Matches(rendered,">Measured in this run</strong>").Count==4 &&
                rendered.Contains("24 cores and 32 threads") && rendered.Contains("16 GB of graphics memory") &&
                rendered.Contains("model name identifies an NVMe drive") &&
                ReferenceTargets.Bundled.GpuNote("NVIDIA GeForce RTX 5070 Ti Laptop GPU")==null &&
                ReferenceTargets.Bundled.CpuNote("Intel Core i9-13900KF")==null,
                "Each component separates specs from results, and similar model names do not borrow desktop specs.");
            Check(rendered.Contains("<summary>Compare with a saved run</summary>") && rendered.Contains("120% of your saved run"),
                "Saved-run scores are available in their own section.");
            Check(rendered.Contains("Within the 60 fps render budget for this scene") &&
                !rendered.Substring(0,rendered.IndexOf("<details class=\"overview component-scores\"",StringComparison.Ordinal)).Contains("% of your saved run"),
                "Main cards show a measured workload target and keep comparison gauges separate.");
            Check(rendered.Contains("Below your saved run"),"The CPU status identifies a sizeable local change.");
            Check(rendered.Contains("Windows test edition") && rendered.Contains("Example storage"),"The report renders OS and Windows drive details.");
            Check(rendered.IndexOf(">Everyday use</h3>",StringComparison.Ordinal)<rendered.IndexOf(">Gaming</h3>",StringComparison.Ordinal) &&
                rendered.Contains("Word processing, email, YouTube and social media.") && rendered.Contains("large photo batches"),
                "Everyday use comes first and the cards explain their use cases.");
            var totals=ComponentScores.Compare(current,before);
            Check(totals.All(value=>value.HasValue) && Math.Abs(totals[3].Value-1.2)<0.001,"Whole PC and component scores account for lower graphics times.");
            before.StorageDrive="D:";
            var mismatched=ComponentScores.Compare(current,before);
            Check(!mismatched[0].HasValue && !mismatched[4].HasValue,"Mismatched test volumes do not produce a storage or whole-PC score.");
            report.Data.Remove("baseline");
            report.Data.Remove("baselineGraphicsTime");
            report.Data.Remove("componentScores");
            report.Data.Remove("graphicsSlowFrame");
            report.Data.Remove("boardModel");
            var partial=LocalReport.Render(report);
            Check(Regex.Matches(partial,"Score unavailable").Count==3 &&
                partial.Contains("A whole-PC score needs measurements from all four components."),
                "Missing graphics leaves only the Gaming, graphics and whole-PC scores unavailable.");
            Check(!partial.Contains("Check for BIOS updates"),"The BIOS link needs a motherboard model.");
            Check(!LocalReport.Render(report).Contains("% of your saved run"),"The first run does not invent reference percentages.");
            string output=Environment.GetEnvironmentVariable("WAIU_SMOKE_REPORT");
            if (!string.IsNullOrEmpty(output))
            {
                var sample=new PcReport();
                foreach (string key in new[] { "scores", "workers", "date", "os", "osBuild", "windowsDrive", "windowsDisk", "graphicsTime", "graphicsGlassTime" })
                    sample.Data[key]=report.Data[key];
                sample.Data["baseline"]=PcReport.Scores(before);
                sample.Data["baselineGraphicsTime"]=before.GraphicsTime;
                sample.Data["componentScores"]=totals;
                sample.Data["wifi"]=new[] { "Example wireless adapter · Wi-Fi 6 family (802.11ax)" };
                sample.Data["bluetooth"]=new[] { "Example Bluetooth radio" };
                sample.Data["slots"]=4;
                sample.Data["memoryMaximum"]=137438953472L;
                sample.Data["modules"]=new[] { new { type="DDR5",form="DIMM",size=34359738368L } };
                LocalReport.Save(sample,output);
            }
            Console.WriteLine("Inventory: Wi-Fi adapters={0}; Bluetooth radios={1}; firmware RAM maximum={2}.",
                ((Array)report.Data["wifi"]).Length,((Array)report.Data["bluetooth"]).Length,report.Data.ContainsKey("memoryMaximum"));
            Console.WriteLine("Use-case percentages, CPU status, and first-run behaviour passed.");
        }

        private static void CheckWindow(string path)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string autoOpened=null;
            int openCount=0;
            using (var form = new BenchmarkWindow(path,file=>{autoOpened=file;openCount++;}))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                var controls = Descendants(form).ToList();
                var run = controls.OfType<Button>().Single(button => button.Text == "Run Benchmark");
                var again = controls.OfType<Button>().Single(button => button.Text == "Run again");
                var save = controls.OfType<Button>().Single(button => button.Text == "Save as baseline");
                var stop = controls.OfType<Button>().Single(button => button.Text == "Stop");
                var clear = controls.OfType<Button>().Single(button => button.Text == "Clear baseline");
                var open = controls.OfType<Button>().Single(button => button.Text == "Reopen results");
                var strip = controls.OfType<StageStrip>().Single();
                var progressTracks = controls.OfType<StageProgress>().ToArray();
                var labels = controls.OfType<Label>().ToArray();
                var stageHeadline=labels.Single(label=>label.Font.Size==16);
                var percentage=labels.Single(label=>label.Text=="0%");
                var seenStages=new List<string>();
                stageHeadline.TextChanged+=(sender,args)=>seenStages.Add(stageHeadline.Text);
                var preview = controls.OfType<PictureBox>().Single();
                var artwork = controls.OfType<StageArtwork>().Single();
                var privacy=controls.OfType<LinkLabel>().Single(link=>link.Text=="Privacy policy");
                var terms=controls.OfType<LinkLabel>().Single(link=>link.Text=="Terms and conditions");
                Exception failure = null;
                form.Shown += async (sender, args) =>
                {
                    try
                    {
                        var timer=Stopwatch.StartNew();
                        form.ClientSize=new Size(640,500);
                        form.PerformLayout();
                        var client=form.RectangleToScreen(form.ClientRectangle);
                        var start=run.RectangleToScreen(run.ClientRectangle);
                        Check(client.Contains(start) && Math.Abs(start.Left+start.Width/2-(client.Left+client.Width/2))<10,
                            "The start button is centred and fits a compact window.");
                        Check(!strip.Visible && !stop.Visible && !artwork.Visible,"The launch screen hides test controls and artwork.");
                        Check(privacy.Visible && terms.Visible && client.Contains(terms.RectangleToScreen(terms.ClientRectangle)),
                            "The footer links are visible on the compact launch screen.");
                        string ready=Environment.GetEnvironmentVariable("WAIU_SMOKE_READY");
                        if (!string.IsNullOrEmpty(ready))
                            using (var readyImage=new Bitmap(form.Width,form.Height))
                            {
                                form.DrawToBitmap(readyImage,new Rectangle(Point.Empty,readyImage.Size));
                                readyImage.Save(ready,System.Drawing.Imaging.ImageFormat.Png);
                            }
                        form.ClientSize=new Size(1280,760);
                        run.PerformClick();
                        Check(!run.Visible && stop.Enabled && artwork.Visible && strip.Visible && progressTracks.Length==5,
                            "Starting reveals controls, progress and stage artwork.");
                        Check(privacy.Visible && terms.Visible,"The footer links stay visible during testing.");
                        Check(progressTracks.All(track=>track.Height>0 &&
                            track.PointToScreen(Point.Empty).Y+track.Height<=artwork.PointToScreen(Point.Empty).Y),
                            "Each segment sits below its tile and above the animation.");
                        Check(form.Icon!=null,"The Windows form has an application icon.");
                        int frame=artwork.FrameIndex;
                        if (SystemInformation.IsMenuAnimationEnabled) await WaitUntil(()=>artwork.FrameIndex!=frame);
                        string componentArt=Environment.GetEnvironmentVariable("WAIU_SMOKE_COMPONENT");
                        if (!string.IsNullOrEmpty(componentArt))
                            using (var image=new Bitmap(form.Width,form.Height))
                            {
                                form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                                image.Save(componentArt,System.Drawing.Imaging.ImageFormat.Png);
                            }
                        bool sawFrame=false,sawGlass=false,sawHeavy=false;
                        while (!run.Enabled && timer.Elapsed.TotalSeconds<125)
                        {
                            bool hasFps=preview.Image!=null && Regex.IsMatch(preview.AccessibleDescription ?? "",@"^\d+(?:[.,]\d+)? FPS$");
                            if (hasFps && !sawFrame)
                            {
                                sawFrame=true;
                                string capture=Environment.GetEnvironmentVariable("WAIU_SMOKE_FRAME");
                                if (!string.IsNullOrEmpty(capture)) preview.Image.Save(capture,System.Drawing.Imaging.ImageFormat.Png);
                                string window=Environment.GetEnvironmentVariable("WAIU_SMOKE_WINDOW");
                                if (!string.IsNullOrEmpty(window))
                                    using (var image=new Bitmap(form.Width,form.Height))
                                    {
                                        form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                                        image.Save(window,System.Drawing.Imaging.ImageFormat.Png);
                                    }
                            }
                            if (hasFps && !sawGlass && controls.OfType<Label>().Any(label=>
                                label.Text.Contains("Glassworks at 1080p") &&
                                (label.Text.Contains("· 1.7") || label.Text.Contains("· 1.8") ||
                                 label.Text.Contains("· 1.9") || label.Text.Contains("· 2."))))
                            {
                                sawGlass=true;
                                string glass=Environment.GetEnvironmentVariable("WAIU_SMOKE_GLASS");
                                if (!string.IsNullOrEmpty(glass)) preview.Image.Save(glass,System.Drawing.Imaging.ImageFormat.Png);
                            }
                            if (hasFps && preview.Image.Width==GraphicsBenchmark.HeavyWidth) sawHeavy=true;
                            await Task.Delay(50);
                        }
                        await WaitUntil(() => run.Enabled);
                        Check(seenStages.SequenceEqual(StageStrip.Headlines.Concat(new[] { "Benchmark complete" })) &&
                            percentage.Text=="100%","Each stage has a distinct headline and the strip reaches 100%.");
                        Check(save.Enabled && open.Enabled, "A completed run can be saved as a baseline or reopened.");
                        Check(openCount==1 && autoOpened!=null && File.Exists(autoOpened),"The completed benchmark saves and opens the report automatically.");
                        var reportField = typeof(BenchmarkWindow).GetField("report", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var report = (PcReport)reportField.GetValue(form);
                        foreach (string key in new[] { "scores", "modules", "drives", "graphics", "network" })
                            Check(report.Data.ContainsKey(key), "The report includes " + key + ".");
                        Check(((double[])report.Data["scores"]).All(rate=>rate>0), "CPU and RAM produced positive rates.");
                        Check((double)report.Data["compression"]>0,"The CPU compression workload returned a rate.");
                        Check((int)report.Data["allCoreWorkers"]==Environment.ProcessorCount &&
                            (double)report.Data["allCoreRate"]>0,"The all-core run returns throughput with one worker per reported logical processor.");
                        Check(((double[])report.Data["latency"]).Length==3 &&
                            ((double[])report.Data["latency"]).All(rate=>rate>0),"All three latency samples returned results.");
                        Check(report.Data["graphicsTime"]!=null && report.Data["graphicsGlassTime"]!=null,
                            "Both revised graphics scenes compiled and returned a hardware timing on this test machine.");
                        if (report.Data["graphicsTime"]!=null) Check(sawFrame,"Clean Energy Circuit showed a live frame and FPS.");
                        if (report.Data["graphicsTime"]!=null) Check(report.Data["graphicsSlowFrame"]!=null,"The graphics run recorded frame consistency.");
                        if (report.Data["graphicsGlassTime"]!=null) Check(sawGlass,"Glassworks showed a live frame and FPS.");
                        Check(sawHeavy && report.Data["graphicsHeavyFps"]!=null && report.Data["graphicsHeavyFrameTime"]!=null,
                            "The queued heavy pass returned a GPU time, throughput and a live 1440p preview.");
                        Check(!preview.Visible && preview.AccessibleDescription==null,"The FPS readout clears after the graphics stage.");
                        Check(open.Enabled && controls.OfType<LinkLabel>().Any(link=>link.Text=="Open WhatAmIUsing.com"),
                            "The report and official site links are present.");
                        Console.WriteLine("Graphics scenes: energy={0}; glass={1}.",report.Data["graphicsTime"]!=null,report.Data["graphicsGlassTime"]!=null);
                        Console.WriteLine("All-core: {0} workers, {1:F0} MiB/s. Heavy graphics: {2:F1} FPS, {3:F2} ms GPU time per frame.",
                            report.Data["allCoreWorkers"],report.Data["allCoreRate"],report.Data["graphicsHeavyFps"],report.Data["graphicsHeavyFrameTime"]);
                        Console.WriteLine("Full run: {0:F1} seconds. GPU timed: {1}. Storage read: {2}. Storage write: {3}. Small reads: {4}.",
                            timer.Elapsed.TotalSeconds,report.Data["graphicsTime"]!=null,report.Data["storageRead"]!=null,
                            report.Data["storageWrite"]!=null,report.Data["storageRandomRead"]!=null);
                        string htmlFile=LocalReport.Save(report,Path.GetDirectoryName(path));
                        Check(File.ReadAllText(htmlFile).Contains("Clean Energy Circuit"),"Local HTML includes the graphics result.");
                        string output=Environment.GetEnvironmentVariable("WAIU_SMOKE_REPORT");
                        if (!string.IsNullOrEmpty(output))
                        {
                            var sample=new PcReport();
                            foreach (string key in new[] { "scores", "workers", "date", "compression", "latency",
                                "graphicsTime", "graphicsSlowFrame", "storageRead", "storageWrite", "storageRandomRead" })
                                sample.Data[key]=report.Data[key];
                            sample.Data["cpu"]="<img src=x onerror=alert(1)>";
                            sample.Data["backgroundCpu"]=24;
                            sample.Data["preflightMemory"]=new[] { 8589934592L,500000000L };
                            sample.Data["storageVolumeBytes"]=500000000000L;
                            sample.Data["storageFreeBytes"]=20000000000L;
                            LocalReport.Save(sample,output);
                        }
                        File.Delete(htmlFile);
                        Console.WriteLine("Inventory counts: {0} memory modules, {1} drives, {2} graphics adapters.",
                            ((Array)report.Data["modules"]).Length, ((Array)report.Data["drives"]).Length,
                            ((Array)report.Data["graphics"]).Length);
                        save.PerformClick();
                        Check(clear.Enabled && Result.Load(path, Benchmark.WorkerCount) != null, "The UI saves a local baseline.");
                        Check(again.Visible,"A finished run offers a clear repeat action.");
                        again.PerformClick();
                        stop.PerformClick();
                        await WaitUntil(() => run.Enabled);
                        Check(save.Enabled && clear.Enabled, "Stopping preserves the completed results.");
                        Check(openCount==1,"Stopping does not open a report.");
                        clear.PerformClick();
                        Check(!clear.Enabled && !File.Exists(path), "The UI clears the local baseline.");
                    }
                    catch (Exception error) { failure = error; }
                    finally { form.Close(); }
                };
                Application.Run(form);
                if (failure != null) throw new InvalidOperationException("Windows UI smoke test failed.", failure);
            }
            Console.WriteLine("Windows UI run, save, stop and clear passed.");
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(15))
                await Task.Delay(20);
            Check(condition(), "The UI finishes within the smoke test time limit.");
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
