using System;

namespace Split
{
    // Un plein écran d'une moitié doit recouvrir la barre des tâches de sa moitié, comme sur un vrai deuxième écran,
    // sans jamais passer devant la fenêtre dans laquelle on travaille.
    internal sealed partial class Engine
    {
        public IntPtr ForegroundOverride;   // tests : un bureau caché n'a pas de fenêtre active au sens de Windows

        // Tests (mode debug) : désigne la fenêtre à considérer comme active, et refait le point sur l'empilement.
        public void TestForeground(IntPtr hwnd)
        {
            ForegroundOverride = hwnd;
            if (cfg.Enabled) { UpdateTopmost(); Recheck(); }
        }

        void UpdateTopmost()
        {
            if (layout == null) return;
            IntPtr fg = ForegroundOverride != IntPtr.Zero ? ForegroundOverride : Native.GetForegroundWindow();
            WinInfo fgInfo = fg == IntPtr.Zero ? null : Get(fg);
            bool fgShell = fg != IntPtr.Zero && (fgInfo == null || fgInfo.Shell);
            RECT fr = new RECT();
            bool fgRect = fg != IntPtr.Zero && !fgShell && Native.GetWindowRect(fg, out fr);

            WinInfo active = null;
            foreach (WinInfo w in wins.Values)
            {
                if (w.Mode != Mode.Full || w.GivenUp) continue;
                if (!Native.IsWindow(w.Hwnd) || !Native.IsWindowVisible(w.Hwnd) || Native.IsIconic(w.Hwnd)) continue;
                bool own = fg == w.Hwnd || IsOwnedBy(fg, w.Hwnd);
                if (own) active = w;
                if (!cfg.CoverTaskbar || (!own && fgShell)) continue;
                if (!own && fgRect && Overlaps(fr, w.Expected)) Lower(w, fg);
                else if (own || w.MadeTopmost || !Covered(w)) Raise(w);
                if (w.Chromium && !w.Forced) YieldTaskbar(w, own);
            }
            AutoLock(active);
        }

        // N'intervient que si une barre des tâches est passée devant la fenêtre : on la glisse juste au-dessus.
        void Raise(WinInfo w)
        {
            IntPtr bar = IntPtr.Zero;
            foreach (IntPtr b in layout.Taskbars)
                if (IsAbove(b, w.Hwnd) && (bar == IntPtr.Zero || IsAbove(b, bar))) bar = b;
            if (bar == IntPtr.Zero) return;

            IntPtr prev = Native.GetWindow(bar, Native.GW_HWNDPREV);
            if (prev != IntPtr.Zero) Native.SetWindowPos(w.Hwnd, prev, 0, 0, 0, 0, ZFlags);
            if (prev == IntPtr.Zero || IsAbove(bar, w.Hwnd))
                Native.SetWindowPos(w.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, ZFlags);
            if (!w.WasTopmost) w.MadeTopmost = true;
            bool stuck = IsAbove(bar, w.Hwnd);   // Windows n'a pas (encore) suivi : on réessaiera au prochain passage
            if (cfg.Debug && (!w.Raised || stuck != w.RaiseStuck))
                Log.Write("Empilement : passée devant la barre des tâches" + (stuck ? " (sans effet pour l'instant)" : "") + " : " + w);
            w.RaiseStuck = stuck;
            w.Raised = true;
        }

        void Lower(WinInfo w, IntPtr fg)
        {
            if (w.WasTopmost || !IsTopmost(w.Hwnd)) { w.MadeTopmost = false; return; }
            Native.SetWindowPos(w.Hwnd, fg, 0, 0, 0, 0, ZFlags);
            w.MadeTopmost = IsTopmost(w.Hwnd);
            if (cfg.Debug) Log.Write("Empilement : passée derrière la fenêtre active : " + w + (w.MadeTopmost ? " (toujours visible : la fenêtre active l'est aussi)" : ""));
        }

        // Filet de sécurité : une fenêtre que Split a passée devant la barre des tâches ne doit plus être « toujours
        // visible » une fois sortie du plein écran. On le vérifie sur son état réel, jusqu'à l'avoir constaté.
        void CheckRaised()
        {
            foreach (WinInfo w in wins.Values)
            {
                if (!w.Raised || (w.Mode == Mode.Full && !w.GivenUp) || !Native.IsWindow(w.Hwnd)) continue;
                if (!w.WasTopmost && IsTopmost(w.Hwnd))
                {
                    Native.SetWindowPos(w.Hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, ZFlags);
                    Log.Write("Empilement : « toujours visible » retiré après coup : " + w);
                }
                else w.Raised = false;
            }
        }

        // Une autre fenêtre d'application est-elle posée par-dessus ? (Alors on ne la fait pas remonter.)
        bool Covered(WinInfo w)
        {
            const long skip = Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
            IntPtr h = w.Hwnd;
            for (int i = 0; i < 600; i++)
            {
                h = Native.GetWindow(h, Native.GW_HWNDPREV);
                if (h == IntPtr.Zero) return false;
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) continue;
                if ((Native.GetExStyle(h) & skip) != 0) continue;
                RECT r;
                if (!Native.GetWindowRect(h, out r) || !Overlaps(r, w.Expected)) continue;
                int cloaked;
                if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) continue;
                if (IsOwnedBy(h, w.Hwnd) || WinInfo.IsShellClass(h)) continue;
                return true;
            }
            return false;
        }

        static bool Overlaps(RECT a, RECT b)
        {
            return Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > 32
                && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > 32;
        }

        // a est-elle au-dessus de b dans l'ordre d'empilement ?
        static bool IsAbove(IntPtr a, IntPtr b)
        {
            IntPtr h = b;
            for (int i = 0; i < 4000; i++)
            {
                h = Native.GetWindow(h, Native.GW_HWNDPREV);
                if (h == IntPtr.Zero) return false;
                if (h == a) return true;
            }
            return false;
        }

        static bool IsOwnedBy(IntPtr hwnd, IntPtr owner)
        {
            IntPtr h = hwnd;
            for (int i = 0; i < 8 && h != IntPtr.Zero; i++)
            {
                h = Native.GetWindow(h, Native.GW_OWNER);
                if (h == owner) return true;
            }
            return false;
        }
    }
}
