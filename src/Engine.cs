using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Split
{
    // Surveille les fenêtres (sans rien injecter dans les autres programmes) et les replace dans les moitiés.
    internal sealed partial class Engine : IDisposable
    {
        readonly Config cfg;
        readonly Dictionary<IntPtr, WinInfo> wins = new Dictionary<IntPtr, WinInfo>();
        readonly Dictionary<IntPtr, int> pending = new Dictionary<IntPtr, int>();
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly Native.WinEventProc proc;
        readonly Timer tick = new Timer();
        readonly Timer house = new Timer();
        readonly Timer lockTimer = new Timer();
        readonly Timer soon = new Timer();      // seconde vérification de l'empilement, après la réaction de Windows
        readonly uint myPid;
        Layout layout;
        bool inMoveSize;
        IntPtr moveHwnd;
        int moveEndTick;
        int soonLeft;

        public Action<string, string> Notify;   // titre, texte
        public Action Changed;
        public Action Save;                     // la configuration a changé (moitié mémorisée)

        public Layout Layout { get { return layout; } }

        public Engine(Config cfg)
        {
            this.cfg = cfg;
            myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            moveEndTick = Environment.TickCount - 60000;   // aucun déplacement récent
            proc = OnWinEvent;
            tick.Interval = 15;
            tick.Tick += delegate { Drain(); };
            house.Interval = 1000;
            house.Tick += delegate { Housekeeping(); };
            lockTimer.Interval = 100;
            lockTimer.Tick += delegate { ApplyCursorLock(); };
            soon.Interval = 60;
            soon.Tick += delegate
            {
                if (--soonLeft <= 0) soon.Stop();
                if (this.cfg.Enabled) { UpdateTopmost(); CheckRaised(); }
            };
        }

        // Windows déplace la barre des tâches un peu après nous (elle revient devant, ou s'efface) : on revérifie
        // l'empilement plusieurs fois dans la demi-seconde qui suit.
        void Recheck()
        {
            soonLeft = 8;
            soon.Stop();
            soon.Start();
        }

        public void Start()
        {
            layout = Layout.Compute(cfg);
            SnapshotExisting();
            uint[] events = {
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_MOVESIZESTART, Native.EVENT_SYSTEM_MOVESIZEEND,
                Native.EVENT_SYSTEM_MINIMIZEEND, Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_LOCATIONCHANGE
            };
            foreach (uint e in events)
            {
                IntPtr h = Native.SetWinEventHook(e, e, IntPtr.Zero, proc, 0, 0,
                    Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
                if (h != IntPtr.Zero) hooks.Add(h);
            }
            house.Start();
            Log.Write("Start: " + (layout == null ? "aucun écran" : layout.Describe()));
        }

        public void Dispose()
        {
            foreach (IntPtr h in hooks) Native.UnhookWinEvent(h);
            hooks.Clear();
            tick.Stop(); house.Stop(); lockTimer.Stop(); soon.Stop();
            ReleaseAll();
            tick.Dispose(); house.Dispose(); lockTimer.Dispose(); soon.Dispose();
        }

        // Les fenêtres déjà agrandies ou en plein écran au lancement sont laissées telles quelles.
        void SnapshotExisting()
        {
            wins.Clear();
            if (layout == null) return;
            Native.EnumWindowsProc cb = delegate(IntPtr hwnd, IntPtr lParam)
            {
                if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
                WinInfo w = Get(hwnd);
                if (w == null || w.Shell) return true;
                RECT wr;
                if (!Native.GetWindowRect(hwnd, out wr)) return true;
                if (Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) != layout.Monitor) return true;
                w.WasArranged = Arranged(hwnd);
                if (Coverage(hwnd, layout.Bounds) > 0) { w.Mode = Mode.Full; w.GivenUp = true; w.Expected = wr; }
                else if (Native.IsZoomed(hwnd)) w.Mode = Mode.Bypass;
                else { w.Last = wr; w.HasLast = true; }
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
        }

        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0) return;
            try
            {
                switch (ev)
                {
                    case Native.EVENT_OBJECT_LOCATIONCHANGE:
                        // Un plein écran suivi (ou un navigateur Chromium, qui se replace tout seul) est traité
                        // sur-le-champ : attendre le prochain tic laisserait voir le débordement.
                        WinInfo known;
                        if (wins.TryGetValue(hwnd, out known) && (known.Mode == Mode.Full || known.Chromium) && !known.GivenUp)
                            Evaluate(hwnd);
                        else if (IsTopLevel(hwnd)) Schedule(hwnd, 0);
                        break;
                    case Native.EVENT_OBJECT_SHOW:
                        if (IsTopLevel(hwnd)) Schedule(hwnd, 80);
                        break;
                    case Native.EVENT_SYSTEM_MINIMIZEEND:
                        Schedule(hwnd, 60);
                        break;
                    case Native.EVENT_SYSTEM_FOREGROUND:
                        Schedule(hwnd, 0);
                        if (cfg.Enabled) { UpdateTopmost(); Recheck(); }
                        break;
                    case Native.EVENT_SYSTEM_MOVESIZESTART:
                        inMoveSize = true;
                        moveHwnd = hwnd;
                        break;
                    case Native.EVENT_SYSTEM_MOVESIZEEND:
                        inMoveSize = false;
                        moveHwnd = hwnd;
                        moveEndTick = Environment.TickCount;
                        Schedule(hwnd, 130);
                        break;
                }
            }
            catch (Exception ex) { Log.Write("OnWinEvent: " + ex); }
        }

        void Schedule(IntPtr hwnd, int delayMs)
        {
            int due = Environment.TickCount + delayMs;
            int old;
            if (!pending.TryGetValue(hwnd, out old) || due - old < 0) pending[hwnd] = due;
            if (!tick.Enabled) tick.Start();
        }

        void Drain()
        {
            int now = Environment.TickCount;
            List<IntPtr> ready = null;
            foreach (KeyValuePair<IntPtr, int> kv in pending)
                if (now - kv.Value >= 0) (ready ?? (ready = new List<IntPtr>())).Add(kv.Key);
            if (ready != null)
            {
                foreach (IntPtr h in ready) pending.Remove(h);
                foreach (IntPtr h in ready)
                {
                    try { Evaluate(h); }
                    catch (Exception ex) { Log.Write("Evaluate: " + ex); }
                }
            }
            if (pending.Count == 0) tick.Stop();
        }

        void Housekeeping()
        {
            try
            {
                List<IntPtr> dead = null;
                foreach (KeyValuePair<IntPtr, WinInfo> kv in wins)
                    if (!Native.IsWindow(kv.Key)) (dead ?? (dead = new List<IntPtr>())).Add(kv.Key);
                if (dead != null) foreach (IntPtr h in dead) wins.Remove(h);

                Layout fresh = Layout.Compute(cfg);
                bool moved = fresh != null && !fresh.Same(layout);
                if (fresh != null) layout = fresh;
                if (!cfg.Enabled) return;
                if (moved) { Log.Write("Écran modifié : " + layout.Bounds); Refit(); if (Changed != null) Changed(); }
                UpdateTopmost();
                CheckRaised();
            }
            catch (Exception ex) { Log.Write("Housekeeping: " + ex); }
        }

        // ---------- utilitaires ----------

        static bool IsTopLevel(IntPtr hwnd)
        {
            return (Native.GetStyle(hwnd) & Native.WS_CHILD) == 0 && Native.GetAncestor(hwnd, Native.GA_ROOT) == hwnd;
        }

        WinInfo Get(IntPtr hwnd)
        {
            WinInfo w;
            if (wins.TryGetValue(hwnd, out w)) return w;
            w = WinInfo.Describe(hwnd);
            if (w.Pid == myPid) return null;
            wins[hwnd] = w;
            return w;
        }

        // Fenêtre d'application que Split a le droit de déplacer ?
        bool Touchable(WinInfo w)
        {
            if (w == null || w.Shell || cfg.IsExcluded(w.Process)) return false;
            long ex = Native.GetExStyle(w.Hwnd);
            if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0) return false;
            if ((ex & Native.WS_EX_LAYERED) != 0 && (ex & Native.WS_EX_TRANSPARENT) != 0) return false;
            int cloaked;
            if (Native.DwmGetWindowAttribute(w.Hwnd, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return false;
            return true;
        }

        bool IsDragging(IntPtr hwnd)
        {
            return hwnd == moveHwnd && (inMoveSize || Environment.TickCount - moveEndTick < 120);
        }

        // Mot à employer dans les messages : « moitié » à deux écrans, « zone » au-delà.
        string Part { get { return layout != null && layout.Zones.Length > 2 ? "zone" : "moitié"; } }

        // Fenêtre que Windows tient pour ancrée. En mode debug, une propriété de fenêtre permet aux tests de le simuler
        // (l'ancrage réel demande une vraie souris ou un vrai clavier).
        bool Arranged(IntPtr hwnd)
        {
            return Native.IsArranged(hwnd) || (cfg.Debug && Native.GetProp(hwnd, "Split.Arranged") != IntPtr.Zero);
        }

        // La fenêtre vient-elle d'être lâchée après un déplacement à la souris ?
        bool Dropped(IntPtr hwnd)
        {
            return hwnd == moveHwnd && !inMoveSize && Environment.TickCount - moveEndTick < 700;
        }

        static bool ClientCovers(IntPtr hwnd, RECT area)
        {
            RECT c;
            if (!Native.GetClientRect(hwnd, out c)) return false;
            POINT p = new POINT();
            if (!Native.ClientToScreen(hwnd, ref p)) return false;
            RECT s = new RECT(p.X, p.Y, p.X + c.Width, p.Y + c.Height);
            return s.Contains(area);
        }

        bool Place(WinInfo w, RECT r, bool frameChanged)
        {
            uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            if (frameChanged) flags |= Native.SWP_FRAMECHANGED;
            if (Native.SetWindowPos(w.Hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags)) return true;
            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.Write("SetWindowPos a échoué (" + err + ") pour " + w);
            if (err == 5) WarnElevated(w);
            return false;
        }

        void WarnElevated(WinInfo w)
        {
            if (w.Warned) return;
            w.Warned = true;
            Say("Fenêtre protégée", "« " + w.Process + " » tourne en administrateur : Split ne peut pas la déplacer. "
                + "Clic droit sur l'icône Split > Redémarrer en administrateur.");
        }

        void Say(string title, string text)
        {
            Log.Write("Notification: " + title + " - " + text);
            if (Notify != null) Notify(title, text);
        }
    }
}
