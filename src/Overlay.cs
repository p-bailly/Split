using System;
using System.Drawing;
using System.Windows.Forms;

namespace Split
{
    // Affiche brièvement les zones par-dessus le bureau (sans prendre le focus ni bloquer la souris).
    internal sealed class Overlay : Form
    {
        public static readonly Color[] Colors = { Color.FromArgb(47, 128, 237), Color.FromArgb(242, 153, 74), Color.FromArgb(39, 174, 96), Color.FromArgb(155, 81, 224) };
        readonly string title, detail;

        Overlay(RECT r, int index)
        {
            title = (index + 1).ToString();
            detail = "Écran " + (index + 1) + "  ·  " + r.Width + " × " + r.Height;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Colors[index % Colors.Length];
            Opacity = 0.6;
            Bounds = new Rectangle(r.Left + 10, r.Top + 10, Math.Max(40, r.Width - 20), Math.Max(40, r.Height - 20));
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x20 | 0x08000000 | 0x08;   // TOOLWINDOW | TRANSPARENT | NOACTIVATE | TOPMOST
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle area = ClientRectangle;
            using (StringFormat center = new StringFormat())
            using (Font big = new Font("Segoe UI", Math.Max(24f, area.Height / 4f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font small = new Font("Segoe UI", Math.Max(14f, area.Height / 28f), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                e.Graphics.DrawString(title, big, Brushes.White, area, center);
                Rectangle below = new Rectangle(area.Left, area.Top + area.Height / 2 + (int)(big.Size * 0.6f), area.Width, (int)(small.Size * 2));
                e.Graphics.DrawString(detail, small, Brushes.White, below, center);
            }
        }

        public static void Flash(Layout layout)
        {
            if (layout == null) return;
            try
            {
                Overlay[] forms = new Overlay[layout.Zones.Length];
                for (int i = 0; i < forms.Length; i++)
                {
                    forms[i] = new Overlay(layout.Zones[i], i);
                    forms[i].Show();
                }
                Timer t = new Timer();
                t.Interval = 1400;
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    foreach (Overlay f in forms) f.Close();
                };
                t.Start();
            }
            catch (Exception ex) { Log.Write("Overlay : " + ex); }   // purement décoratif : ne doit jamais bloquer Split
        }
    }
}
