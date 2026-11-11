using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WaiuBenchmark
{
    internal static class LocalReport
    {
        internal static string Save(PcReport report,string directory,string title=null)
        {
            Directory.CreateDirectory(directory);
            string destination=Path.Combine(directory,"WAIU-PC-Performance-Report.html");
            string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,Render(report,title),new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temporary,destination,null);
                else File.Move(temporary,destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return destination;
        }

        internal static string Render(PcReport report,string title=null)
        {
            // Use the existing report's JSON data model without emitting its data into executable JavaScript.
            var serializer=new JavaScriptSerializer();
            var data=(IDictionary<string,object>)serializer.DeserializeObject(serializer.Serialize(report.Data));
            var output=new StringBuilder();
            const string printScript="document.getElementById('print').addEventListener('click',function(){window.print();});";
            string printHash;
            using (var sha=SHA256.Create()) printHash=Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(printScript)));
            output.Append("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"referrer\" content=\"no-referrer\">")
                .Append("<meta name=\"robots\" content=\"noindex, nofollow\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'sha256-")
                .Append(printHash).Append("'; base-uri 'none'; form-action 'none'\"><title>")
                .Append(Escape(title ?? "PC Performance Report | What Am I Using?")).Append("</title><style>")
                .Append(Resource("Report.css")).Append("</style></head><body>")
                .Append(Resource("ReportIcons.svg"))
                .Append("<header class=\"header\"><div class=\"wrap brand\"><span class=\"brand-icon\" aria-hidden=\"true\">")
                .Append(Resource("BenchmarkIcon.svg")).Append("</span>What Am I Using?</div></header>")
                .Append("<main class=\"wrap\"><div class=\"intro\"><p class=\"eyebrow\">PC PERFORMANCE BENCHMARK</p><h1>Your PC performance report</h1><p id=\"report-date\">");
            DateTime date;
            if (DateTime.TryParse(Text(data,"date"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out date))
                output.Append("Run on ").Append(Escape(date.ToLocalTime().ToString("f")));
            output.Append("</p></div><div class=\"actions\"><button id=\"print\" type=\"button\">Print or save as PDF</button>")
                .Append("<a class=\"button\" href=\"https://www.whatamiusing.com/\">Back to What Am I Using?</a><span>Saved on this PC</span></div>");

            object[] scores=Items(data,"scores");
            object[] baseline=Items(data,"baseline");
            var grades=new ReportGrades(data,scores,report.Reference);
            ProfileCards(output,data,scores,report.Reference,grades);
            PcStatus(output,data,scores,baseline,grades);
            Overview(output,data,baseline.Length==3);
            Open(output,"cpu","Processor (CPU)",grades.Cpu,CpuOnPaper(data,report.Reference),"the single- and multi-worker CPU tests");
            Row(output,"Processor",Text(data,"cpu"));
            Row(output,"Logical processors",Text(data,"logicalProcessors"));
            Row(output,"One worker",Compare(scores,baseline,0));
            Row(output,Text(data,"workers")+" workers",Compare(scores,baseline,1));
            double allCore=Number(Get(data,"allCoreRate"));
            int allWorkers=(int)Number(Get(data,"allCoreWorkers"));
            Row(output,"All-core pass",allCore>0 && allWorkers>0 ?
                Rate(allCore)+" across "+allWorkers+" logical workers for "+Benchmark.AllCoreSeconds+" seconds" : "Measurement unavailable");
            Row(output,"Compressing sample data",Rate(Number(Get(data,"compression"))));
            if (Get(data,"backgroundCpu")!=null) Row(output,"CPU use before the run",Number(Get(data,"backgroundCpu")).ToString("0.#")+"%");
            Close(output,"The processor runs your apps. One worker tests a single task. The all-core pass uses every logical processor available to this app. Compression is a different kind of CPU work.","speed-up-slow-windows-pc","Check what is slowing down Windows");

            Open(output,"ram","Memory (RAM)",grades.Ram,MemoryOnPaper(grades.MemoryGiB,report.Reference),"the RAM buffer-copy test");
            var modules=Rows(data,"modules").ToArray();
            if (modules.Length==0) Row(output,"Module details","Unavailable from Windows");
            else Row(output,"Reported memory",Size(modules.Sum(module=>Number(Get(module,"size"))),1073741824.0,"GiB"));
            for (int i=0;i<modules.Length;i++)
            {
                var module=modules[i];
                string speed=Text(module,"speed");
                Row(output,"Module "+(i+1),Join(Size(Number(Get(module,"size")),1073741824.0,"GiB"),
                    speed=="0" ? "" : speed+" MT/s",Text(module,"location"),Text(module,"part")));
            }
            int slots=(int)Number(Get(data,"slots"));
            if (modules.Length>0 && slots>=modules.Length)
            {
                Row(output,"Memory positions reported",modules.Length+" in use of "+slots);
                Row(output,"Estimated free positions",(slots-modules.Length).ToString());
            }
            else Row(output,"Free memory positions","Unable to estimate");
            Row(output,"Memory buffer copy",Compare(scores,baseline,2));
            var available=Items(data,"preflightMemory");
            if (available.Length==2)
                Row(output,"Available before the run",Size(Number(available[1]),1073741824.0,"GiB")+" of "+Size(Number(available[0]),1073741824.0,"GiB"));
            var latency=Items(data,"latency");
            if (latency.Length==3)
            {
                Row(output,"Small working set (256 KiB)",Number(latency[0]).ToString("0.0")+" ns per access");
                Row(output,"Medium working set (8 MiB)",Number(latency[1]).ToString("0.0")+" ns per access");
                Row(output,"Large working set (64 MiB)",Number(latency[2]).ToString("0.0")+" ns per access");
            }
            Close(output,"RAM holds what your apps are using now. Smaller working sets may fit in the CPU cache. Lower access times are quicker. Firmware-reported slots can include soldered memory.","upgrade-ram","Check whether more RAM would help");

            Open(output,"gpu","Graphics (GPU)",grades.Graphics,GraphicsOnPaper(data,report.Reference),"the 1080p Clean Energy Circuit");
            var adapters=Rows(data,"graphics").ToArray();
            for (int i=0;i<adapters.Length;i++)
                Row(output,"Graphics adapter "+(i+1),Join(Text(adapters[i],"model"),Text(adapters[i],"driver")));
            if (adapters.Length==0) Row(output,"Graphics adapter","Unavailable from Windows");
            double graphics=Number(Get(data,"graphicsTime"));
            Row(output,"Clean Energy Circuit",graphics>0 ? graphics.ToString("0.00")+" ms per frame at 1920 × 1080" : "Graphics measurement unavailable");
            if (graphics>0)
            {
                Row(output,"Adapter tested",Text(data,"graphicsTestAdapter"));
                double slow=Number(Get(data,"graphicsSlowFrame"));
                if (slow>0) Row(output,"95% of timed frames rendered within",slow.ToString("0.00")+" ms");
            }
            double glass=Number(Get(data,"graphicsGlassTime"));
            Row(output,"Glassworks",glass>0 ? glass.ToString("0.00")+" ms per frame at 1920 × 1080" : "Graphics measurement unavailable");
            double heavyFps=Number(Get(data,"graphicsHeavyFps")), heavyTime=Number(Get(data,"graphicsHeavyFrameTime"));
            Row(output,GraphicsBenchmark.HeavyTitle,heavyFps>0 ?
                heavyFps.ToString("N1")+" FPS at 2560 × 1440, four samples per pixel" : "Graphics measurement unavailable");
            if (heavyTime>0) Row(output,"Heavy pass GPU time",heavyTime.ToString("0.00")+" ms per frame (average across batches)");
            Close(output,"The GPU draws games, video effects and other visuals. The heavy pass queues frames to measure sustained rendering in this scene. Its FPS includes the work of submitting frames and capturing the preview.","upgrade-gpu","Work out whether a graphics upgrade is worthwhile");

            Open(output,"disk","Storage (SSD or hard drive)",grades.Storage,StorageOnPaper(data),"the small-read and sequential-write tests");
            var drives=Rows(data,"drives").ToArray();
            for (int i=0;i<drives.Length;i++)
                Row(output,"Drive "+(i+1),Join(Text(drives[i],"model"),Size(Number(Get(drives[i],"size")),1e9,"GB")));
            if (drives.Length==0) Row(output,"Drive models","Unavailable from Windows");
            Row(output,"Test volume",Text(data,"storageDrive"));
            Row(output,"Sequential write",Rate(Number(Get(data,"storageWrite"))));
            Row(output,"Sequential read",Rate(Number(Get(data,"storageRead"))));
            double randomReads=Number(Get(data,"storageRandomRead"));
            Row(output,"Small random reads (4 KiB)",randomReads>0 ? randomReads.ToString("N0")+" reads/s" : "Skipped or unavailable");
            double volume=Number(Get(data,"storageVolumeBytes")), free=Number(Get(data,"storageFreeBytes"));
            if (volume>0) Row(output,"Free space on the test volume",(free/1e9).ToString("0.#")+" GB ("+(free/volume).ToString("P0")+")");
            Close(output,"Sequential tests write and read a temporary 512 MiB file. Small reads jump around that file and give a different view of responsiveness. These tests cover the volume that holds your temporary folder.","upgrade-to-ssd","Check your options for upgrading to an SSD");

            Open(output,"network","Network connection");
            Row(output,"Active connection types",string.Join(", ",Items(data,"network").OfType<string>()));
            Row(output,"Wi-Fi adapters",Join(Items(data,"wifi").OfType<string>().ToArray()));
            var bluetooth=Items(data,"bluetooth").OfType<string>().ToArray();
            Row(output,"Bluetooth radio",Join(bluetooth));
            if (bluetooth.Length>0) Row(output,"Bluetooth version","Check the radio’s LMP version in Device Manager");
            Close(output,"Installed adapters can appear even while Ethernet is in use. Wi-Fi 6 family means the driver reports 802.11ax. A 6 GHz band and the Bluetooth version need separate confirmation.");

            Open(output,"board","Motherboard and BIOS");
            Row(output,"PC model",Text(data,"systemModel"));
            Row(output,"Motherboard",Join(Text(data,"boardMaker"),Text(data,"boardModel")));
            BiosVersion(output,data);
            Row(output,"BIOS release date",Text(data,"biosDate"));
            string[] types=modules.Select(module=>Join(Text(module,"type"),Text(module,"form"))).Where(type=>type.Length>0).Distinct().ToArray();
            Row(output,"Installed memory type",Join(types));
            if (modules.Length>0 && slots>=modules.Length)
            {
                Row(output,"Memory positions",modules.Length+" occupied · "+(slots-modules.Length)+" estimated free");
            }
            else Row(output,"Free memory positions","Unable to estimate");
            Row(output,"Firmware-reported maximum RAM",Size(Number(Get(data,"memoryMaximum")),1073741824.0,"GiB"));
            output.Append("</dl>");
            MemoryDiagram(output,modules,slots);
            FinishSection(output,"Use the motherboard or PC manufacturer's specifications to confirm the maximum RAM and supported modules before buying. Firmware values can be incomplete.","upgrade-ram","How to check RAM compatibility");
            ComponentSummary(output,Items(data,"componentScores"));
            return output.Append("<footer class=\"footer-note report-footer\"><p>What Am I Using? · PC Performance Benchmark ")
                .Append(Program.Version).Append(" preview</p><nav aria-label=\"Report footer\">")
                .Append("<a href=\"https://www.whatamiusing.com/\">whatamiusing.com</a>")
                .Append("<a href=\"https://www.whatamiusing.com/terms/\">Terms and conditions</a>")
                .Append("<a href=\"https://www.whatamiusing.com/privacy/\">Privacy policy</a></nav></footer>")
                .Append("</main><script>").Append(printScript).Append("</script></body></html>").ToString();
        }

        private static void BiosVersion(StringBuilder output,IDictionary<string,object> data)
        {
            string version=Text(data,"bios"), model=Text(data,"boardModel");
            output.Append("<div class=\"detail\"><dt>BIOS version</dt><dd>").Append(Escape(version.Length>0 ? version : "Unavailable"));
            if (model.Length>0)
            {
                string board=(Text(data,"boardMaker")+" "+model).Trim();
                output.Append(" <a class=\"bios-update\" href=\"https://www.google.com/search?q=")
                    .Append(Escape(Uri.EscapeDataString("BIOS update for "+board)))
                    .Append("\" target=\"_blank\" rel=\"noopener noreferrer\">Check for BIOS updates</a>");
            }
            output.Append("</dd></div>");
        }

        private static void MemoryDiagram(StringBuilder output,IDictionary<string,object>[] modules,int slots)
        {
            output.Append("<div class=\"memory-map\" role=\"group\" aria-labelledby=\"memory-map-heading\">")
                .Append("<h3 id=\"memory-map-heading\">Memory positions</h3>");
            if (modules.Length==0)
            {
                output.Append("<p>Windows did not report which memory positions are occupied.</p></div>");
                return;
            }
            output.Append("<ul class=\"memory-map-grid\">");
            for (int index=0;index<modules.Length;index++)
            {
                var module=modules[index];
                string location=Text(module,"location");
                string capacity=Size(Number(Get(module,"size")),1073741824.0,"GiB");
                MemoryPosition(output,"occupied",location.Length>0 ? location : "Module "+(index+1),
                    capacity.Length>0 ? "Occupied · "+capacity : "Occupied");
            }
            int free=slots>=modules.Length ? slots-modules.Length : -1;
            if (free>0 && free<=16)
                for (int i=0;i<free;i++) MemoryPosition(output,"free","Free position","Estimated");
            else if (free>16) MemoryPosition(output,"free",free+" free positions","Estimated");
            output.Append("</ul><p class=\"section-note\">");
            if (free<0) output.Append("The total position count is unavailable.");
            else if (free==0) output.Append("All reported positions are occupied.");
            else output.Append("Free positions are estimated from firmware. Their exact location on the board is unknown.");
            output.Append("</p></div>");
        }

        private static void MemoryPosition(StringBuilder output,string state,string label,string description)
        {
            output.Append("<li class=\"memory-position ").Append(state).Append("\"><span class=\"slot-shape\" aria-hidden=\"true\">")
                .Append("<i></i><i></i><i></i><i></i></span><strong>").Append(Escape(label))
                .Append("</strong><span>").Append(Escape(description)).Append("</span></li>");
        }

        private static void ComponentSummary(StringBuilder output,object[] scores)
        {
            output.Append("<details class=\"overview component-scores\"><summary>Compare with a saved run</summary><p>In this section, 100% means the same result as your saved run.</p><div class=\"profile-grid\">");
            string[] names={ "Whole PC", "Processor", "Memory", "Graphics", "Storage" };
            for (int i=0;i<names.Length;i++)
            {
                double score=At(scores,i);
                output.Append("<article class=\"profile-card\"><h3>").Append(names[i]).Append("</h3>");
                Gauge(output,score);
                output.Append("<p>").Append(Escape(score>0 ? score.ToString("P0")+" of your saved run" : "Needs comparable runs")).Append("</p></article>");
            }
            output.Append("</div><p class=\"section-note\">Red is lower, amber is within 5%, and green is higher than your saved run. The combined result needs all four components. Storage compares runs on the same drive letter. Save a baseline in this version, then run again to compare.</p></details>");
        }

        private sealed class ReportGrades
        {
            internal readonly int Cpu, Ram, Graphics, Storage, Everyday, Workstation, Overall;
            internal readonly double MemoryGiB;
            internal readonly string LimitingComponent;

            internal ReportGrades(IDictionary<string,object> data,object[] scores,ReferenceTargets reference)
            {
                int single=reference.Score("cpuSingle",At(scores,0));
                int parallel=reference.Score("cpuParallel",At(scores,1));
                int random=reference.Score("randomReads",Number(Get(data,"storageRandomRead")));
                int write=reference.Score("storageWrite",Number(Get(data,"storageWrite")));
                double installed=Rows(data,"modules").Sum(module=>Number(Get(module,"size")));
                MemoryGiB=(installed>0 ? installed : At(Items(data,"preflightMemory"),0))/1073741824.0;
                Cpu=Math.Min(single,parallel);
                Ram=reference.Score("ramCopy",At(scores,2));
                Graphics=reference.GraphicsScore(Number(Get(data,"graphicsSlowFrame")));
                Storage=Math.Min(random,write);
                Everyday=Math.Min(Math.Min(single,random),reference.Score("ramEverydayGiB",MemoryGiB));
                Workstation=Math.Min(Math.Min(parallel,write),reference.Score("ramWorkstationGiB",MemoryGiB));
                int[] components={ Cpu,Ram,Graphics,Storage };
                string[] names={ "Processor","Memory","Graphics","Storage" };
                Overall=components.Min();
                LimitingComponent=string.Join(" and ",names.Where((name,index)=>components[index]==Overall));
            }
        }

        private static string CpuOnPaper(IDictionary<string,object> data,ReferenceTargets reference)
        {
            string model=Text(data,"cpu");
            string note=reference.CpuNote(model);
            if (note!=null) return note;
            double cores=Number(Get(data,"cpuCores"));
            if (cores>0) return cores.ToString("0")+" physical cores. Windows reports the core count. A capability assessment for this model is unavailable.";
            return model.Length>0 ? model+". A capability assessment for this model is unavailable." :
                "Windows could not identify the processor.";
        }

        private static string MemoryOnPaper(double capacity,ReferenceTargets reference)
        {
            if (capacity<=0) return "Windows could not report the memory capacity.";
            string amount=capacity.ToString("0.#")+" GiB of reported capacity. ";
            if (reference.Score("ramWorkstationGiB",capacity)>=80)
                return amount+"Plenty of room for everyday use and the workstation profile.";
            if (reference.Score("ramWorkstationGiB",capacity)>=60)
                return amount+"Meets the capacity target for the workstation profile.";
            if (reference.Score("ramEverydayGiB",capacity)>=60)
                return amount+"Meets the everyday capacity target. Larger workstation jobs may benefit from more memory.";
            return amount+"Below the everyday capacity target.";
        }

        private static string GraphicsOnPaper(IDictionary<string,object> data,ReferenceTargets reference)
        {
            string model=Text(data,"graphicsTestAdapter");
            if (model.Length==0)
            {
                var adapters=Rows(data,"graphics").ToArray();
                if (adapters.Length==1) model=Text(adapters[0],"model");
            }
            if (model.Length==0) return "See the listed adapters below. Windows could not identify one adapter for this assessment.";
            return reference.GpuNote(model) ?? (model+". A capability assessment for this model is unavailable.");
        }

        private static string StorageOnPaper(IDictionary<string,object> data)
        {
            string model=Text(data,"storageDisk");
            string capacity=Size(Number(Get(data,"storageVolumeBytes")),1e9,"GB");
            string volume=capacity.Length>0 ? "The tested volume has "+capacity+" of capacity." : "";
            if (model.Length==0)
                return Join(volume,"Windows could not match the test volume to a single physical drive.");
            if (model.IndexOf("NVMe",StringComparison.OrdinalIgnoreCase)>=0)
                return Join(model,"The model name identifies an NVMe drive, a fast storage class.",volume);
            if (model.IndexOf("SSD",StringComparison.OrdinalIgnoreCase)>=0)
                return Join(model,"The model name identifies solid-state storage, which is typically responsive for small reads.",volume);
            return Join(model,volume,"A capability assessment for this drive model is unavailable.");
        }

        private static string MeasuredComment(int score,string workload)
        {
            switch (ReferenceTargets.Grade(score))
            {
                case 0: return "Below the preview target in "+workload+".";
                case 1: return "Approached the preview target in "+workload+".";
                case 2: return "Met the preview target in "+workload+".";
                case 3: return "Comfortably exceeded the preview target in "+workload+".";
                case 4: return "Well above the preview target in "+workload+".";
                default: return "A measurement needed for this score was unavailable.";
            }
        }

        private static void ProfileCards(StringBuilder output,IDictionary<string,object> data,object[] scores,ReferenceTargets reference,ReportGrades grades)
        {
            double gpu=Number(Get(data,"graphicsTime")), slow=Number(Get(data,"graphicsSlowFrame"));
            output.Append("<section class=\"profiles\" aria-labelledby=\"profile-heading\"><h2 id=\"profile-heading\">Performance by use</h2><div class=\"profile-grid\">");
            Profile(output,"Everyday use","Word processing, email, YouTube and social media.",
                Rate(At(scores,0)),"Single-worker CPU, memory capacity and small storage reads.","Processor details","cpu",grades.Everyday);
            Profile(output,"Gaming","Playing PC games, where resolution and graphics settings matter. Grade based on the graphics test.",
                gpu>0 ? gpu.ToString("0.00")+" ms per frame" : "Unavailable",
                grades.Graphics>=0 ? (slow<=1000.0/reference.GraphicsGoal ? "Within the " : "Above the ")+reference.GraphicsGoal+" fps render budget for this scene" :
                    "1080p graphics timing",
                "1080p Clean Energy Circuit","gpu",grades.Graphics);
            Profile(output,"Workstation","Demanding jobs such as video editing and exports, large photo batches and virtual machines.",
                Rate(At(scores,1)),"Multi-worker CPU, memory capacity and sequential storage writes.","Processor details","cpu",grades.Workstation);
            output.Append("</div><p class=\"section-note\">Preview targets · Reference v").Append(reference.Revision).Append(" · ")
                .Append(Escape(reference.Date)).Append(" · ").Append(Escape(reference.Source)).Append("</p>")
                .Append("<details class=\"reference-notes\"><summary>How the scores work</summary>")
                .Append("<p>These scores use provisional targets for the tests in this benchmark. They are points out of 100. A score of 60 meets the target. ")
                .Append("Each combined score uses its lowest input score so a fast component cannot hide a slow one.</p>")
                .Append("<p>The all-core and heavy graphics passes are shown separately below. The existing grades use the one-worker, up-to-eight-worker and 1080p reference tests.</p>")
                .Append("<p>Thresholds for Approaching, Meets, Strong and Exceptional, in that order.</p><ul>");
            Threshold(output,reference,"One CPU worker (MiB/s)","cpuSingle");
            Threshold(output,reference,"Multiple CPU workers, up to eight (MiB/s)","cpuParallel");
            Threshold(output,reference,"RAM buffer copy (MiB/s)","ramCopy");
            Threshold(output,reference,"Memory capacity for Everyday use (GiB)","ramEverydayGiB");
            Threshold(output,reference,"Memory capacity for Workstation (GiB)","ramWorkstationGiB");
            Threshold(output,reference,"Graphics frame-time equivalent at the 95th percentile (fps)","graphicsFps");
            Threshold(output,reference,"Small random storage reads (reads/s)","randomReads");
            Threshold(output,reference,"Sequential storage writes (MiB/s)","storageWrite");
            output.Append("</ul></details></section>");
        }

        private static void Threshold(StringBuilder output,ReferenceTargets reference,string label,string metric)
        {
            output.Append("<li>").Append(Escape(label)).Append(" · ").Append(Escape(reference.Scale(metric))).Append("</li>");
        }

        private static void Profile(StringBuilder output,string name,string uses,string value,string context,string detail,string anchor,int score)
        {
            output.Append("<article class=\"profile-card\"><h3>").Append(Escape(name)).Append("</h3><p class=\"profile-uses\">")
                .Append(Escape(uses)).Append("</p>");
            GradeBadge(output,score);
            output.Append("<strong>")
                .Append(Escape(value)).Append("</strong><p>").Append(Escape(context)).Append("</p><a href=\"#")
                .Append(anchor).Append("\">").Append(Escape(detail)).Append("</a></article>");
        }

        private static void GradeBadge(StringBuilder output,int score)
        {
            int grade=ReferenceTargets.Grade(score);
            string[] labels={ "Below target", "Approaching target", "Meets target", "Strong", "Exceptional" };
            output.Append("<span class=\"reference-grade");
            if (grade>=0)
                output.Append(" grade-").Append(grade).Append("\">").Append(score).Append("/100 · ").Append(labels[grade]);
            else output.Append("\">Score unavailable");
            output.Append("</span>");
        }

        private static void Gauge(StringBuilder output,double ratio)
        {
            const string arc="M9 48a41 41 0 0 1 82 0";
            output.Append("<svg class=\"score-gauge\" viewBox=\"0 0 100 55\" aria-hidden=\"true\"><path class=\"track\" d=\"").Append(arc).Append("\"/>");
            if (ratio>0 && !double.IsInfinity(ratio))
            {
                output.Append("<path class=\"lower\" pathLength=\"100\" d=\"").Append(arc).Append("\"/>")
                    .Append("<path class=\"similar\" pathLength=\"100\" d=\"").Append(arc).Append("\"/>")
                    .Append("<path class=\"higher\" pathLength=\"100\" d=\"").Append(arc).Append("\"/>");
                double angle=Math.PI*(1-Math.Max(0,Math.Min(1,(ratio-0.70)/0.60)));
                string x=(50+41*Math.Cos(angle)).ToString("0.0",CultureInfo.InvariantCulture);
                string y=(48-41*Math.Sin(angle)).ToString("0.0",CultureInfo.InvariantCulture);
                output.Append("<circle cx=\"").Append(x).Append("\" cy=\"").Append(y).Append("\" r=\"4.3\"/>");
            }
            output.Append("</svg>");
        }

        private static void PcStatus(StringBuilder output,IDictionary<string,object> data,object[] scores,object[] baseline,ReportGrades grades)
        {
            output.Append("<section class=\"report-section pc-status\"><h2>PC status</h2><div class=\"overall-grade\"><strong>Whole-PC score</strong>");
            GradeBadge(output,grades.Overall);
            output.Append("<p class=\"section-note\">");
            if (grades.Overall>=0) output.Append("Lowest component score in this run · ").Append(Escape(grades.LimitingComponent));
            else output.Append("A whole-PC score needs measurements from all four components.");
            output.Append("</p></div><dl>");
            string cpuStatus="Benchmark recorded";
            if (At(baseline,0)>0 && At(baseline,1)>0)
            {
                double one=At(scores,0)/At(baseline,0), parallel=At(scores,1)/At(baseline,1);
                cpuStatus=one<0.85 || parallel<0.85 ?
                    "Below your saved run in at least one CPU test. Repeat under the same conditions." :
                    "Single- and multi-worker results shown against your saved run below.";
            }
            Row(output,"Processor",Join(Text(data,"cpu"),cpuStatus));
            string gpu=Text(data,"graphicsTestAdapter");
            if (gpu.Length==0) gpu=string.Join(", ",Rows(data,"graphics").Select(adapter=>Text(adapter,"model")).Where(name=>name.Length>0));
            Row(output,"Graphics",Join(gpu,Get(data,"graphicsTime")!=null ? "1080p measurement recorded" : "Measurement unavailable"));
            Row(output,"Windows drive",Join(Text(data,"windowsDrive"),Text(data,"windowsDisk")));
            var memory=Items(data,"preflightMemory");
            Row(output,"Memory",memory.Length==2 ? Size(Number(memory[0]),1073741824.0,"GiB usable") : "");
            string build=Text(data,"osBuild");
            Row(output,"Operating system",Join(Text(data,"os"),build.Length>0 ? "build "+build : Text(data,"osVersion")));
            Close(output,"The Windows drive is where Windows is installed. The storage test volume is listed separately in the storage section.");
        }

        private static double At(object[] values,int index) { return values.Length>index ? Number(values[index]) : 0; }


        private static void Overview(StringBuilder output,IDictionary<string,object> data,bool hasBaseline)
        {
            var findings=new List<string>();
            double cpu=Number(Get(data,"backgroundCpu"));
            if (cpu>=15)
                findings.Add("Windows was using "+cpu.ToString("0")+"% of the CPU before the test. Close busy apps and repeat the run to see whether it affects the result.");
            var memory=Items(data,"preflightMemory");
            if (memory.Length==2)
            {
                double total=Number(memory[0]),free=Number(memory[1]);
                if (total>0 && free<2*1073741824.0 && free/total<0.15)
                    findings.Add("Little memory was available at the start. Try closing apps. If memory repeatedly runs low during your normal work, check your RAM upgrade options.");
            }
            double volume=Number(Get(data,"storageVolumeBytes")),storageFree=Number(Get(data,"storageFreeBytes"));
            if (volume>0 && storageFree<50e9 && storageFree/volume<0.15)
                findings.Add("Your test drive has limited free space. Free up some room before considering a replacement.");
            if (Get(data,"graphicsTime")==null || Get(data,"storageRandomRead")==null)
                findings.Add("One or more tests could not return a measurement. Check the component sections for what was recorded.");
            if (findings.Count==0)
                findings.Add("This run did not flag heavy background CPU use, low available memory or low space on the tested volume. Check the sections below for the actual measurements.");
            output.Append("<section class=\"overview\"><h2>What to check first</h2><ul>");
            foreach (string finding in findings) output.Append("<li>").Append(Escape(finding)).Append("</li>");
            output.Append("</ul><p>").Append(hasBaseline ?
                "Compare the changes against your own saved baseline. Repeat under the same conditions before acting on a small difference." :
                "Save a baseline in the Windows app, make one change, then run the same test again. These measurements give you a starting point.")
                .Append("</p><nav aria-label=\"Jump to a component\">")
                .Append("<a href=\"#cpu\">CPU</a><a href=\"#ram\">RAM</a><a href=\"#gpu\">Graphics</a><a href=\"#disk\">Storage</a>")
                .Append("<a href=\"#network\">Network</a><a href=\"#board\">Motherboard</a></nav></section>");
        }

        private static string Resource(string name)
        {
            using (var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var reader=new StreamReader(stream)) return reader.ReadToEnd();
        }
        private static void Open(StringBuilder html,string icon,string label,int? score=null,string onPaper=null,string workload=null)
        {
            html.Append("<section class=\"report-section\" id=\"").Append(icon).Append("\"><div class=\"section-title\"><span class=\"component-icon\" aria-hidden=\"true\"><svg viewBox=\"0 0 40 40\"><use href=\"#icon-")
                .Append(icon).Append("\"></use></svg></span><h2>").Append(Escape(label)).Append("</h2>");
            if (score.HasValue) GradeBadge(html,score.Value);
            html.Append("</div>");
            if (score.HasValue)
                html.Append("<div class=\"hardware-verdicts\"><p><strong>Hardware on paper</strong> ")
                    .Append(Escape(onPaper)).Append("</p><p><strong>Measured in this run</strong> ")
                    .Append(Escape(MeasuredComment(score.Value,workload))).Append("</p></div>");
            html.Append("<dl>");
        }
        private static void Close(StringBuilder html,string note,string slug=null,string label=null)
        {
            html.Append("</dl>");
            FinishSection(html,note,slug,label);
        }

        private static void FinishSection(StringBuilder html,string note,string slug=null,string label=null)
        {
            if (note.Length>0) html.Append("<p class=\"section-note\">").Append(Escape(note)).Append("</p>");
            if (slug!=null) html.Append("<p class=\"guide-link\"><a href=\"https://www.whatamiusing.com/guides/")
                .Append(slug).Append("/\">").Append(Escape(label)).Append("</a></p>");
            html.Append("</section>");
        }
        private static void Row(StringBuilder html,string title,string value)
        {
            html.Append("<div class=\"detail\"><dt>").Append(Escape(title)).Append("</dt><dd>")
                .Append(Escape(string.IsNullOrWhiteSpace(value) ? "Unavailable" : value)).Append("</dd></div>");
        }
        private static string Escape(string text) { return WebUtility.HtmlEncode(text); }
        private static object Get(IDictionary<string,object> data,string key)
        {
            object value;
            return data.TryGetValue(key,out value) ? value : null;
        }
        private static object[] Items(IDictionary<string,object> data,string key) { return Get(data,key) as object[] ?? new object[0]; }
        private static IEnumerable<IDictionary<string,object>> Rows(IDictionary<string,object> data,string key)
        { return Items(data,key).OfType<IDictionary<string,object>>(); }
        private static double Number(object value) { return value==null ? 0 : Convert.ToDouble(value,CultureInfo.InvariantCulture); }
        private static string Text(IDictionary<string,object> data,string key) { return Convert.ToString(Get(data,key),CultureInfo.CurrentCulture) ?? ""; }
        private static string Join(params string[] values) { return string.Join(" · ",values.Where(value=>!string.IsNullOrWhiteSpace(value))); }
        private static string Size(double bytes,double divisor,string unit) { return bytes>0 ? (bytes/divisor).ToString("0.#")+" "+unit : ""; }
        private static string Rate(double value) { return value>0 ? value.ToString("N0")+" MiB/s" : "Skipped or unavailable"; }
        private static string Compare(object[] scores,object[] baseline,int index)
        {
            double now=scores.Length>index ? Number(scores[index]) : 0;
            double before=baseline.Length>index ? Number(baseline[index]) : 0;
            return before>0 && now>0 ? Rate(now)+" · "+(now/before-1).ToString("+0%;-0%;0%")+" vs baseline" : Rate(now);
        }
    }
}
