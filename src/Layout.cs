using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Split
{
    internal struct MonitorDesc
    {
        public IntPtr Handle;
        public string Device;
        public RECT Bounds, Work;
        public bool Primary;
    }

    // Géométrie des « écrans virtuels » (deux moitiés, ou trois zones) sur l'écran physique choisi, de gauche à droite.
    internal sealed class Layout
    {
        public IntPtr Monitor;
        public string Device = "";
        public RECT Bounds, Work;
        public RECT[] Zones = new RECT[2];      // zone complète (plein écran)
        public RECT[] ZoneWork = new RECT[2];   // zone sans la barre des tâches (agrandissement)
        public List<IntPtr> Taskbars = new List<IntPtr>();
        public int VirtualLeft;                 // bord gauche de l'ensemble des écrans

        public static List<MonitorDesc> Monitors()
        {
            List<MonitorDesc> list = new List<MonitorDesc>();
            Native.MonitorEnumProc cb = delegate(IntPtr h, IntPtr hdc, IntPtr rc, IntPtr data)
            {
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (Native.GetMonitorInfo(h, ref mi))
                {
                    MonitorDesc m = new MonitorDesc();
                    m.Handle = h;
                    m.Device = mi.szDevice ?? "";
                    m.Bounds = mi.rcMonitor;
                    m.Work = mi.rcWork;
                    m.Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                    list.Add(m);
                }
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return list;
        }

        public static Layout Compute(Config cfg)
        {
            List<MonitorDesc> mons = Monitors();
            if (mons.Count == 0) return null;

            MonitorDesc pick = mons[0];
            bool found = false;
            foreach (MonitorDesc m in mons)
                if (string.Equals(m.Device, cfg.Monitor, StringComparison.OrdinalIgnoreCase)) { pick = m; found = true; }
            if (!found)
            {
                // auto : l'écran au format le plus large (le 32:9), l'écran principal en cas d'égalité
                double best = -1;
                foreach (MonitorDesc m in mons)
                {
                    double aspect = (double)m.Bounds.Width / Math.Max(1, m.Bounds.Height) + (m.Primary ? 0.001 : 0);
                    if (aspect > best) { best = aspect; pick = m; }
                }
            }

            Layout l = new Layout();
            l.Monitor = pick.Handle;
            l.Device = pick.Device;
            l.Bounds = pick.Bounds;
            l.Work = pick.Work;
            l.VirtualLeft = pick.Bounds.Left;
            foreach (MonitorDesc m in mons) l.VirtualLeft = Math.Min(l.VirtualLeft, m.Bounds.Left);
            int n = cfg.Parts.Count, total = 0, sum = 0, left = pick.Bounds.Left;
            foreach (int p in cfg.Parts) total += p;
            l.Zones = new RECT[n];
            l.ZoneWork = new RECT[n];
            for (int i = 0; i < n; i++)
            {
                sum += cfg.Parts[i];
                int right = i == n - 1 ? pick.Bounds.Right : pick.Bounds.Left + (int)Math.Round(pick.Bounds.Width * (double)sum / total);
                l.Zones[i] = new RECT(left, pick.Bounds.Top, right, pick.Bounds.Bottom);
                l.ZoneWork[i] = RECT.Intersect(l.Zones[i], l.Work);
                left = right;
            }
            l.FindTaskbars();
            return l;
        }

        public string Describe()
        {
            string s = Device + " " + Bounds + " zones";
            foreach (RECT z in Zones) s += " " + z;
            return s;
        }

        void FindTaskbars()
        {
            string[] classes = { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
            foreach (string cls in classes)
            {
                IntPtr h = IntPtr.Zero;
                for (int guard = 0; guard < 16; guard++)
                {
                    h = Native.FindWindowEx(IntPtr.Zero, h, cls, null);
                    if (h == IntPtr.Zero) break;
                    RECT r;
                    if (Native.GetWindowRect(h, out r) && r.IntersectArea(Bounds) > 0) Taskbars.Add(h);
                }
            }
        }

        public bool Same(Layout o)
        {
            if (o == null || o.Monitor != Monitor || !o.Bounds.Near(Bounds, 0) || !o.Work.Near(Work, 0)) return false;
            if (o.Zones.Length != Zones.Length) return false;
            for (int i = 0; i < Zones.Length; i++)
                if (!o.Zones[i].Near(Zones[i], 0)) return false;
            return true;
        }

        // Zone de même taille que le rectangle qui en contient la plus grande partie (-1 s'il n'y en a pas) :
        // celle où un jeu réglé sur cette résolution tient sans être déformé.
        public int MatchingZone(RECT r, int tolerance)
        {
            int best = -1;
            long bestArea = -1;
            for (int i = 0; i < Zones.Length; i++)
            {
                if (Math.Abs(r.Width - Zones[i].Width) > tolerance || Math.Abs(r.Height - Zones[i].Height) > tolerance) continue;
                long a = Zones[i].IntersectArea(r);
                if (a > bestArea) { bestArea = a; best = i; }
            }
            return best;
        }

        public int ZoneAt(int x, int y)
        {
            for (int i = 0; i < Zones.Length; i++)
                if (Zones[i].Contains(x, y)) return i;
            return -1;
        }

        // Zone qui contient la plus grande partie du rectangle (-1 s'il est hors de l'écran divisé).
        public int ZoneOf(RECT r)
        {
            int best = -1;
            long bestArea = 0;
            for (int i = 0; i < Zones.Length; i++)
            {
                long a = Zones[i].IntersectArea(r);
                if (a > bestArea) { bestArea = a; best = i; }
            }
            return best;
        }

        public int ZoneAtCursor()
        {
            POINT p;
            return Native.GetCursorPos(out p) ? ZoneAt(p.X, p.Y) : -1;
        }

        // GetWindowPlacement travaille en coordonnées « espace de travail » (décalées par la barre des tâches).
        public RECT ToWorkspace(RECT s)
        {
            int dx = Bounds.Left - Work.Left, dy = Bounds.Top - Work.Top;
            return new RECT(s.Left + dx, s.Top + dy, s.Right + dx, s.Bottom + dy);
        }

        public RECT FromWorkspace(RECT w)
        {
            int dx = Work.Left - Bounds.Left, dy = Work.Top - Bounds.Top;
            return new RECT(w.Left + dx, w.Top + dy, w.Right + dx, w.Bottom + dy);
        }
    }
}
