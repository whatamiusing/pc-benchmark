using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WaiuBenchmark
{
    internal sealed class StageStrip : TableLayoutPanel
    {
        private static readonly string[] Names = { "CPU", "RAM", "Graphics", "Storage", "Hardware details" };
        internal static readonly string[] Headlines = { "Testing your CPU", "Testing your RAM", "Testing your graphics",
            "Testing your storage", "Reading your hardware details" };
        private readonly Label[] states = new Label[Names.Length];
        private readonly ComponentIcon[] icons = new ComponentIcon[Names.Length];
        private readonly StageProgress[] tracks = new StageProgress[Names.Length];
        private readonly Timer animation = new Timer { Interval=45 };
        private int animated = -1;

        internal StageStrip()
        {
            ColumnCount = Names.Length;
            animation.Tick += (sender,args) =>
            {
                if (animated<0) return;
                icons[animated].Phase+=0.12;
                icons[animated].Invalidate();
            };
            RowCount = 2;
            AutoSize = true;
            for (int index = 0; index < Names.Length; index++)
            {
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
                var tile = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, AutoSize = true,
                    Margin = new Padding(2), Padding = new Padding(2, 7, 2, 7), BackColor = Color.White, Dock = DockStyle.Fill };
                int icon=index==2 ? 3 : index==3 ? 2 : index;
                icons[index] = new ComponentIcon(icon) { Anchor = AnchorStyles.None };
                tile.Controls.Add(icons[index], 0, 0);
                tile.Controls.Add(Caption(Names[index], true), 0, 1);
                states[index] = Caption("Waiting", false);
                tile.Controls.Add(states[index], 0, 2);
                Controls.Add(tile, index, 0);
                tracks[index] = new StageProgress { Dock = DockStyle.Fill, Height = 8, Margin = new Padding(2, 3, 2, 0) };
                Controls.Add(tracks[index], index, 1);
            }
        }

        internal void SetProgress(int percent)
        {
            for (int index=0; index<tracks.Length; index++)
                tracks[index].SetFraction(System.Math.Max(0,System.Math.Min(1,(percent - index * 20) / 20.0)));
        }

        private static Label Caption(string text, bool bold)
        {
            return new Label { Text = text, TextAlign = ContentAlignment.MiddleCenter, AutoSize = true,
                Anchor = AnchorStyles.None, Margin = new Padding(1, 2, 1, 1),
                Font = new Font("Segoe UI", bold ? 9F : 8F, bold ? FontStyle.Bold : FontStyle.Regular) };
        }

        internal void SetStage(int current, bool complete = false, bool graphicsDone = false, bool storageDone = false)
        {
            if (animated!=current)
            {
                animated=current;
                animation.Enabled=current>=0 && SystemInformation.IsMenuAnimationEnabled;
            }
            for (int index = 0; index < Names.Length; index++)
            {
                string value = index == current ? "Running" : index < current || complete ? "Done" : "Waiting";
                if (complete)
                {
                    if (index == 2 && !graphicsDone) value = "Unavailable";
                    if (index == 3 && !storageDone) value = "See report";
                }
                states[index].Text = value;
                states[index].Parent.BackColor = index == current ? Color.FromArgb(232,240,255) : Color.White;
                states[index].ForeColor = index == current ? Color.FromArgb(33, 89, 216) : Color.FromArgb(67, 86, 106);
                icons[index].Active = index == current;
                icons[index].Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animation.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class StageProgress : Control
    {
        private double fraction;

        internal StageProgress()
        {
            DoubleBuffered = true;
            TabStop = false;
        }

        internal void SetFraction(double value)
        {
            if (fraction == value) return;
            fraction = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(Color.FromArgb(219,228,240));
            if (fraction <= 0) return;
            using (var blue=new SolidBrush(Color.FromArgb(33,89,216)))
                args.Graphics.FillRectangle(blue,0,0,(float)(Width*fraction),Height);
        }
    }

    internal sealed class ComponentIcon : Control
    {
        internal int Component;
        internal bool Active;
        internal double Phase;

        internal ComponentIcon(int component)
        {
            Component = component;
            Size = new Size(49, 40);
            SetStyle(ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent;
            DoubleBuffered = true;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics graphics = args.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(Width / 49f, Height / 40f);
            if (Active)
            {
                graphics.TranslateTransform(24.5f,20f);
                graphics.RotateTransform((float)(6*System.Math.Sin(Phase)));
                graphics.TranslateTransform(-24.5f,-20f);
            }
            using (var halo = new SolidBrush(Active ? Color.FromArgb(207, 224, 255) : Color.FromArgb(237, 243, 255)))
            using (var front = new SolidBrush(Color.FromArgb(64, 114, 218)))
            using (var side = new SolidBrush(Color.FromArgb(34, 72, 153)))
            using (var detail = new SolidBrush(Color.FromArgb(193, 218, 255)))
            using (var gold = new SolidBrush(Color.FromArgb(218, 169, 59)))
            using (var line = new Pen(Color.FromArgb(193, 218, 255), 1.6f))
            {
                graphics.FillEllipse(halo, 3, 1, 43, 38);
                if (Component == 0)
                {
                    for (int pin = 13; pin < 37; pin += 5)
                    {
                        graphics.FillRectangle(gold, pin, 7, 2, 28);
                        graphics.FillRectangle(gold, 8, pin - 2, 33, 2);
                    }
                    graphics.FillRectangle(side, 12, 12, 26, 20);
                    graphics.FillRectangle(front, 11, 9, 26, 21);
                    graphics.FillRectangle(detail, 17, 13, 14, 13);
                }
                else if (Component == 1)
                {
                    graphics.FillRectangle(side, 5, 15, 40, 15);
                    graphics.FillRectangle(front, 5, 12, 40, 15);
                    for (int x = 9; x < 40; x += 8) graphics.FillRectangle(detail, x, 15, 5, 7);
                    for (int x = 8; x < 43; x += 3) graphics.FillRectangle(gold, x, 25, 1, 4);
                }
                else if (Component == 2)
                {
                    graphics.FillRectangle(side, 11, 10, 29, 23);
                    graphics.FillRectangle(front, 9, 7, 29, 23);
                    graphics.FillEllipse(detail, 14, 10, 16, 16);
                    graphics.FillEllipse(side, 20, 16, 4, 4);
                    graphics.DrawLine(line, 30, 23, 24, 19);
                }
                else if (Component == 3)
                {
                    graphics.FillRectangle(side, 7, 12, 36, 21);
                    graphics.FillRectangle(front, 8, 10, 33, 19);
                    graphics.FillEllipse(detail, 12, 13, 12, 12);
                    graphics.FillEllipse(detail, 25, 13, 12, 12);
                    graphics.FillEllipse(side, 16, 17, 4, 4);
                    graphics.FillEllipse(side, 29, 17, 4, 4);
                    graphics.FillRectangle(gold, 16, 29, 16, 3);
                }
                else
                {
                    graphics.FillRectangle(side, 9, 11, 32, 23);
                    graphics.FillRectangle(front, 8, 8, 32, 23);
                    graphics.FillRectangle(detail, 14, 13, 20, 12);
                    for (int x = 17; x < 32; x += 4) graphics.FillRectangle(gold, x, 14, 2, 5);
                }
            }
        }
    }
}
