using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace WaiuBenchmark
{
    internal sealed class StageArtwork : Control
    {
        internal const int FrameWidth=640, FrameHeight=360, Columns=5, Rows=4;
        internal static readonly string[] Names={ "processor", "memory", "storage", "inventory" };
        private readonly Bitmap[] sheets=new Bitmap[Names.Length];
        private int frame, art;
        internal int FrameIndex { get { return frame; } }

        internal StageArtwork()
        {
            Dock=DockStyle.Fill;
            DoubleBuffered=true;
            AccessibleName="Illustration of data moving through the component being tested";
            BackColor=Color.FromArgb(16,35,61);
        }

        internal void Prepare()
        {
            for (int i=0;i<sheets.Length;i++)
            {
                if (sheets[i]!=null) continue;
                using (Stream source=Assembly.GetExecutingAssembly().GetManifestResourceStream(Names[i]+".jpg"))
                using (var image=new Bitmap(source)) sheets[i]=new Bitmap(image);
            }
        }

        internal void ShowStage(int stage)
        {
            art=stage==3 ? 2 : stage==4 ? 3 : Math.Max(0,Math.Min(stage,1));
            frame=0;
            Invalidate();
        }

        internal void Advance()
        {
            frame=(frame+1)%(Columns*Rows);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            Bitmap sheet=sheets[art];
            if (sheet==null) return;
            int width=Math.Min(Width,Height*16/9), height=width*9/16;
            if (width==0 || height==0) return;
            var destination=new Rectangle((Width-width)/2,(Height-height)/2,width,height);
            var source=new Rectangle((frame%Columns)*FrameWidth,(frame/Columns)*FrameHeight,FrameWidth,FrameHeight);
            args.Graphics.InterpolationMode=InterpolationMode.Bilinear;
            args.Graphics.DrawImage(sheet,destination,source,GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Bitmap sheet in sheets) if (sheet!=null) sheet.Dispose();
            base.Dispose(disposing);
        }
    }
}
