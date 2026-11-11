using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("What Am I Using? PC Performance Benchmark")]
[assembly: AssemblyProduct("What Am I Using? PC Performance Benchmark")]
[assembly: AssemblyVersion(WaiuBenchmark.Program.Version + ".0")]
[assembly: AssemblyFileVersion(WaiuBenchmark.Program.Version + ".0")]
[assembly: AssemblyInformationalVersion(WaiuBenchmark.Program.Version)]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace WaiuBenchmark
{
    internal static class Program
    {
        internal const string Version = "0.12.0";
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BenchmarkWindow(args.Length==1 && args[0]=="--offline"));
        }
    }

    internal sealed class BenchmarkWindow : Form
    {
        private static readonly Color Blue = Color.FromArgb(33, 89, 216);
        private static readonly Color Ink = Color.FromArgb(23, 40, 56);
        private static readonly Color Muted = Color.FromArgb(67, 86, 106);
        private static readonly string BaselinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhatAmIUsing", "PCBenchmark", "baseline-v4.txt");

        private readonly string baselinePath;
        private readonly Uri referenceEndpoint;
        private readonly Action<string> openFile;
        private readonly Button run = new Button { Text = "Run Benchmark" };
        private readonly Button runAgain = new Button { Text = "Run again", Visible=false };
        private readonly Panel testView = new Panel { Dock=DockStyle.Fill, Visible=false };
        private readonly TableLayoutPanel introView = new LaunchBackground { Dock=DockStyle.Fill, ColumnCount=1, RowCount=1 };
        private readonly Button stop = new Button { Text = "Stop", Enabled = false };
        private readonly Button save = new Button { Text = "Save as baseline", Enabled = false };
        private readonly Button forget = new Button { Text = "Clear baseline", Enabled = false };
        private readonly Button reportButton = new Button { Text = "Reopen results", Enabled = false };
        private readonly StageStrip stages = new StageStrip();
        private readonly Label percentage = LabelFor("0%", 10, true, Blue);
        private readonly Label stageHeadline = LabelFor("", 16, true);
        private readonly PictureBox graphicsPreview = new PictureBox
        {
            Visible=false, Dock=DockStyle.Fill, SizeMode=PictureBoxSizeMode.Zoom,
            BackColor=Color.FromArgb(16,35,61), AccessibleName="Live graphics benchmark preview"
        };
        private double? graphicsFps;
        private readonly StageArtwork stageArtwork=new StageArtwork();
        private readonly System.Windows.Forms.Timer stageMotion=new System.Windows.Forms.Timer { Interval=65 };
        private readonly Label status = LabelFor("Ready to test. Click Run benchmark to begin.", 11, true);
        private readonly Label savedDate = LabelFor("", 9, false);
        private CancellationTokenSource runCancellation;
        private Result latest, baseline;
        private PcReport report;
        private bool closeAfterRun;
        private int reportOpening;

        internal BenchmarkWindow(bool offline) : this(BaselinePath, referenceEndpoint: offline ? null :
            new Uri("https://www.whatamiusing.com/assets/" + ReferenceTargets.FileName)) { }

        internal BenchmarkWindow(string baselinePath,Action<string> openFile=null,Uri referenceEndpoint=null)
        {
            this.baselinePath = baselinePath;
            this.referenceEndpoint = referenceEndpoint;
            this.openFile=openFile ?? (path=>Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }));
            Text = "What Am I Using? PC Performance Benchmark";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle screen=Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize=new Size(Math.Min(1360,Math.Max(590,screen.Width-100)),Math.Min(890,Math.Max(485,screen.Height-85)));
            MinimumSize=new Size(590,485);
            BackColor=Color.FromArgb(246,248,251);
            ForeColor=Ink;
            Font=new Font("Segoe UI",10F);

            var stack=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, Padding=new Padding(20,12,20,9) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            testView.Controls.Add(stack);
            var shell=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, Padding=new Padding(20,12,20,9) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Controls.Add(shell);

            var header=new TableLayoutPanel { AutoSize=true, ColumnCount=2, RowCount=1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(LabelFor("WHAT AM I USING?  /  PREVIEW " + Program.Version,9,true,Blue),0,0);
            var website=WebLink("Open WhatAmIUsing.com","/");
            header.Controls.Add(website,1,0);
            Add(shell,header,4);
            var content=new Panel { Dock=DockStyle.Fill };
            content.Controls.Add(testView);
            content.Controls.Add(introView);
            Add(shell,content,0,true);
            var confidence=new FlowLayoutPanel { AutoSize=true, Padding=new Padding(0,6,0,0),
                AccessibleName="Benchmark privacy and terms" };
            confidence.Controls.Add(LabelFor("Results stay on this PC. Open-source release planned.",9,false,Muted));
            confidence.Controls.Add(WebLink("Privacy policy","/privacy/"));
            confidence.Controls.Add(WebLink("Terms and conditions","/terms/"));
            Add(shell,confidence,0);

            var launch=new TableLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=1, Anchor=AnchorStyles.None, Padding=new Padding(16), BackColor=Color.Transparent };
            launch.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var title=LabelFor("PC Performance Benchmark",26,true);
            title.TextAlign=ContentAlignment.MiddleCenter;
            Add(launch,title,12);
            var launchNote=LabelFor("Save your work first. Allow about a minute and a half.",11,false,Muted);
            launchNote.TextAlign=ContentAlignment.MiddleCenter;
            Add(launch,launchNote,22);
            run.BackColor=Blue;
            run.ForeColor=Color.White;
            run.Font=new Font("Segoe UI",21F,FontStyle.Bold);
            run.FlatStyle=FlatStyle.Flat;
            run.FlatAppearance.BorderSize=0;
            run.MinimumSize=new Size(300,70);
            run.Anchor=AnchorStyles.None;
            run.Margin=new Padding(0,0,0,4);
            launch.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            launch.Controls.Add(run,0,launch.RowCount++);
            introView.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            introView.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            introView.Controls.Add(launch,0,0);
            AcceptButton=run;

            stop.MinimumSize=new Size(86,40);
            Add(stack,ButtonRow(stop,runAgain),5);
            Add(stack,stages,6);
            var stageSummary=new TableLayoutPanel { AutoSize=true, ColumnCount=2, RowCount=1 };
            stageSummary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            stageSummary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            stageSummary.Controls.Add(stageHeadline,0,0);
            percentage.Anchor=AnchorStyles.Right;
            stageSummary.Controls.Add(percentage,1,0);
            Add(stack,stageSummary,2);
            Add(stack,status,7);

            var media=new Panel { Dock=DockStyle.Fill, BackColor=stageArtwork.BackColor };
            stageMotion.Tick+=(sender,args)=>stageArtwork.Advance();
            media.Controls.Add(stageArtwork);
            media.Controls.Add(graphicsPreview);
            graphicsPreview.Paint+=DrawGraphicsFps;
            Add(stack,media,6,true);
            Add(stack,ButtonRow(reportButton,save,forget),1);
            savedDate.Font=new Font("Segoe UI",8.5F);
            Add(stack,savedDate,0);

            try { baseline = Result.Load(baselinePath, Benchmark.WorkerCount); }
            catch (IOException) { status.Text = "The saved baseline could not be read."; }
            catch (UnauthorizedAccessException) { status.Text = "The saved baseline could not be read."; }
            RenderResults();

            run.Click += async (sender, args) => await RunBenchmark();
            runAgain.Click += async (sender, args) => await RunBenchmark();
            stop.Click += (sender, args) => StopBenchmark();
            save.Click += (sender, args) => ChangeBaseline(latest);
            forget.Click += (sender, args) => ChangeBaseline(null);
            reportButton.Click += (sender, args) => OpenReport();
            FormClosed += (sender, args) => {SetFrame(null);stageMotion.Dispose();};
            FormClosing += (sender, args) =>
            {
                if (runCancellation == null) return;
                args.Cancel = true;
                closeAfterRun = true;
                StopBenchmark();
            };
        }

        private LinkLabel WebLink(string label,string path)
        {
            var link=new LinkLabel { Text=label, AutoSize=true, LinkColor=Blue };
            link.LinkClicked+=(sender,args)=>
            {
                try { Process.Start(new ProcessStartInfo("https://www.whatamiusing.com"+path) { UseShellExecute=true }); }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(this,"Windows could not open the default browser.",Text,
                        MessageBoxButtons.OK,MessageBoxIcon.Information);
                }
            };
            return link;
        }

        private void SetProgress(int value)
        {
            stages.SetProgress(value);
            percentage.Text=value+"%";
        }

        private void SetFrame(Bitmap bitmap,double? fps=null)
        {
            var previous=graphicsPreview.Image;
            graphicsFps=fps;
            graphicsPreview.AccessibleDescription=bitmap!=null ? GraphicsFpsText() : null;
            graphicsPreview.Image=bitmap;
            graphicsPreview.Visible=bitmap!=null;
            if (bitmap!=null) stageArtwork.Visible=false;
            if (previous!=null) previous.Dispose();
        }

        private string GraphicsFpsText()
        {
            return graphicsFps.HasValue ? graphicsFps.Value.ToString(graphicsFps.Value<10 ? "0.0" : "0")+" FPS" : "Measuring FPS";
        }

        private void DrawGraphicsFps(object sender,PaintEventArgs args)
        {
            if (graphicsPreview.Image==null) return;
            string value=GraphicsFpsText();
            const string label="Test render rate";
            var display=graphicsPreview.ClientSize;
            double aspect=graphicsPreview.Image.Width/(double)graphicsPreview.Image.Height;
            int width=Math.Min(display.Width,(int)(display.Height*aspect));
            int height=Math.Min(display.Height,(int)(width/aspect));
            int pad=(int)(10*args.Graphics.DpiX/96);
            using (var bold=new Font(Font,FontStyle.Bold))
            using (var shade=new SolidBrush(Color.FromArgb(225,16,35,61)))
            {
                Size main=TextRenderer.MeasureText(value,bold);
                Size small=TextRenderer.MeasureText(label,Font);
                int badgeWidth=Math.Max(main.Width,small.Width)+pad*2;
                int badgeHeight=main.Height+small.Height+pad;
                if (width<badgeWidth+pad*2 || height<badgeHeight+pad*2) return;
                int x=(display.Width+width)/2-badgeWidth-pad, y=(display.Height-height)/2+pad;
                var badge=new Rectangle(x,y,badgeWidth,badgeHeight);
                args.Graphics.FillRectangle(shade,badge);
                TextRenderer.DrawText(args.Graphics,value,bold,new Rectangle(x,y+pad/2,badgeWidth,main.Height),
                    Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(args.Graphics,label,Font,new Rectangle(x,y+pad/2+main.Height,badgeWidth,small.Height),
                    Color.FromArgb(203,220,248),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
        }

        private void ShowStageArt(int stage)
        {
            stageArtwork.Visible=true;
            stageArtwork.ShowStage(stage);
            stageMotion.Enabled=stage>=0 && stage!=2 && SystemInformation.IsMenuAnimationEnabled;
        }

        private static Label LabelFor(string text, float size, bool bold, Color? colour = null)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, ForeColor = colour ?? Ink,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
        }

        private static void Add(TableLayoutPanel stack, Control control, int spaceAfter,bool stretch=false)
        {
            control.Margin = new Padding(0, 0, 0, spaceAfter);
            control.Dock = DockStyle.Fill;
            stack.RowStyles.Add(new RowStyle(stretch ? SizeType.Percent : SizeType.AutoSize,stretch ? 100 : 0));
            stack.Controls.Add(control, 0, stack.RowCount++);
        }

        private static Control ButtonRow(params Button[] buttons)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            foreach (Button button in buttons)
            {
                button.AutoSize = true;
                button.FlatStyle = FlatStyle.Flat;
                button.Padding = new Padding(10, 4, 10, 4);
                button.Margin = new Padding(0, 0, 8, 4);
                row.Controls.Add(button);
            }
            return row;
        }

        private async Task RunBenchmark()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                runCancellation = cancellation;
                reportOpening++;
                introView.Visible=false;
                testView.Visible=true;
                AcceptButton=null;
                runAgain.Visible=false;
                run.Enabled = save.Enabled = forget.Enabled = reportButton.Enabled = false;
                stop.Enabled = true;
                stop.Focus();
                status.Text = "Starting benchmark...";
                stageHeadline.Text = StageStrip.Headlines[0];
                stages.SetStage(0);
                ShowStageArt(0);
                SetProgress(0);
                int shownStage=0;
                var progress = new Progress<Tuple<int,double,string>>(update =>
                {
                    if (runCancellation == cancellation && !cancellation.IsCancellationRequested)
                    {
                        int stage=update.Item1;
                        if (stage!=shownStage)
                        {
                            ShowStageArt(stage);
                            shownStage=stage;
                            stageHeadline.Text=StageStrip.Headlines[stage];
                            stages.SetStage(stage);
                        }
                        status.Text=update.Item3;
                        SetProgress(stage*20 + (int)(20*Math.Max(0,Math.Min(stage==4 ? 0.99 : 1,update.Item2))));
                        if (stage!=2 && graphicsPreview.Image!=null) SetFrame(null);
                    }
                });
                var frames=new Progress<Tuple<Bitmap,double?>>(frame=>
                {
                    if (runCancellation==cancellation && !cancellation.IsCancellationRequested) SetFrame(frame.Item1,frame.Item2);
                    else frame.Item1.Dispose();
                });
                bool completedRun=false;
                try
                {
                    stageArtwork.Prepare();
                    Result completed = null;
                    var previous = baseline;
                    string referenceCache = Path.Combine(Path.GetDirectoryName(baselinePath), ReferenceTargets.FileName);
                    var reference = Task.Run(() => ReferenceTargets.LoadAsync(referenceEndpoint, referenceCache, cancellation.Token));
                    PcReport completedReport = await Task.Run(() =>
                    {
                        var cancel=cancellation.Token;
                        Action<int,double,string> notify=(stage,fraction,message)=>
                            ((IProgress<Tuple<int,double,string>>)progress).Report(Tuple.Create(stage,fraction,message));
                        notify(0,0,"Checking background CPU use and available memory...");
                        double? backgroundCpu=DeepBenchmark.BackgroundCpu(cancel);
                        ulong[] memory=Hardware.ReadMemory();
                        completed = Benchmark.Run(cancel, notify);
                        string graphicsAdapter;
                        double? slowFrame,glassTime;
                        GraphicsLoad heavy;
                        double? gpu=GraphicsBenchmark.Run(cancel,(fraction,message)=>notify(2,fraction,message),
                            (bitmap,fps)=>((IProgress<Tuple<Bitmap,double?>>)frames).Report(Tuple.Create(bitmap,fps)),
                            out graphicsAdapter,out slowFrame,out glassTime,out heavy);
                        notify(3,0,"Preparing the temporary storage test file...");
                        StorageBenchmark storage=StorageBenchmark.Run(cancel,(fraction,message)=>notify(3,fraction,message));
                        notify(4,0,"Identifying your motherboard, BIOS, memory, drives and network adapters...");
                        completed.GraphicsTime=gpu;
                        completed.HeavyGraphics=heavy;
                        ComponentScores.Record(completed,storage,glassTime);
                        return PcReport.Collect(completed,previous,storage,gpu,slowFrame,glassTime,graphicsAdapter,backgroundCpu,memory,cancel,
                            (fraction,message)=>notify(4,fraction,message));
                    });
                    completedReport.Reference = await reference;
                    cancellation.Token.ThrowIfCancellationRequested();
                    latest = completed;
                    report = completedReport;
                    completedRun=true;
                    SetProgress(100);
                    stageHeadline.Text="Benchmark complete";
                    status.Text = "Benchmark complete. Opening your results...";
                }
                catch (OperationCanceledException) { stageHeadline.Text="Benchmark stopped"; status.Text = "Previous results are still available."; }
                catch (Exception) { stageHeadline.Text="Benchmark could not finish"; status.Text = "Previous results are still available."; }
                finally
                {
                    runCancellation = null;
                    stop.Enabled = false;
                    SetFrame(null);
                    ShowStageArt(-1);
                    stages.SetStage(-1, completedRun,
                        report != null && report.Data["graphicsTime"] != null,
                        report != null && report.Data["storageRead"] != null && report.Data["storageWrite"] != null);
                    if (!completedRun) SetProgress(0);
                    RenderResults();
                    if (completedRun && !closeAfterRun && OpenReport())
                        status.Text="Benchmark complete. Your results are open in your browser.";
                    run.Enabled = true;
                    runAgain.Visible=true;
                    AcceptButton=runAgain;
                }
            }
            if (closeAfterRun) Close();
        }

        private void StopBenchmark()
        {
            runCancellation.Cancel();
            stop.Enabled = false;
            status.Text = "Stopping...";
        }

        private void ChangeBaseline(Result value)
        {
            try
            {
                if (value == null) File.Delete(baselinePath);
                else value.Save(baselinePath);
                baseline = value;
                status.Text = value == null ? "Baseline cleared." : "Baseline saved on this PC.";
            }
            catch (IOException) { status.Text = "Could not update the saved baseline."; }
            catch (UnauthorizedAccessException) { status.Text = "Could not update the saved baseline."; }
            RenderResults();
        }

        private bool OpenReport()
        {
            if (report == null) return false;
            try
            {
                string title="PC Performance Report | What Am I Using? · "+DateTime.Now.ToString("HH:mm:ss");
                int opening=++reportOpening;
                openFile(LocalReport.Save(report,Path.GetDirectoryName(baselinePath),title));
                OpenReportInFront(title,opening);
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { status.Text = "Windows could not open the default browser."; }
            catch (IOException) { status.Text = "The report could not be saved on this PC."; }
            catch (UnauthorizedAccessException) { status.Text = "The report could not be saved on this PC."; }
            catch (InvalidOperationException) { status.Text = "The report could not be opened."; }
            return false;
        }

        private async void OpenReportInFront(string title,int opening)
        {
            await ReportWindow.BringForward(Handle,title,()=>!IsDisposed && !Disposing && opening==reportOpening);
        }

        private void RenderResults()
        {
            savedDate.Text = baseline == null ? "No baseline saved." : "Baseline saved from " + baseline.Date.ToString("g");
            save.Enabled = latest != null;
            forget.Enabled = baseline != null;
            reportButton.Enabled = report != null;
            reportButton.Visible = report != null;
            save.Visible=latest!=null;
            forget.Visible=baseline!=null;
        }

    }

    internal sealed class LaunchBackground : TableLayoutPanel
    {
        protected override void OnPaintBackground(PaintEventArgs args)
        {
            if (ClientSize.Width==0 || ClientSize.Height==0) return;
            using (var gradient=new LinearGradientBrush(ClientRectangle,Color.FromArgb(231,240,255),
                Color.FromArgb(248,251,255),LinearGradientMode.ForwardDiagonal))
            {
                args.Graphics.FillRectangle(gradient,args.ClipRectangle);
            }
            var state=args.Graphics.Save();
            args.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            DrawTraces(args.Graphics);
            args.Graphics.TranslateTransform(ClientSize.Width,ClientSize.Height);
            args.Graphics.RotateTransform(180);
            DrawTraces(args.Graphics);
            args.Graphics.Restore(state);
        }

        private static void DrawTraces(Graphics graphics)
        {
            using (var line=new Pen(Color.FromArgb(194,213,244)))
            using (var dot=new SolidBrush(line.Color))
                for (int index=0;index<4;index++)
                {
                    int y=28+index*19, end=145-index*15;
                    graphics.DrawLines(line,new[] { new Point(0,y),new Point(38,y),
                        new Point(54,y+16),new Point(end,y+16) });
                    graphics.FillEllipse(dot,end-3,y+13,6,6);
                }
        }
    }
}
