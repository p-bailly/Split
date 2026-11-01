using System;
using System.Runtime.InteropServices;

namespace Split
{
    internal sealed partial class Engine
    {
        // Point d'entrée : regarde l'état réel de la fenêtre et décide quoi faire.
        void Evaluate(IntPtr hwnd)
        {
            if (!cfg.Enabled || layout == null) return;
            if (!Native.IsWindow(hwnd)) { wins.Remove(hwnd); return; }
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return;
            WinInfo w = Get(hwnd);
            if (!Touchable(w))
            {
                if (Tracing(w)) Log.Write("trace " + w + " ignorée");
                return;
            }

            RECT wr;
            if (!Native.GetWindowRect(hwnd, out wr)) return;
            bool onMonitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) == layout.Monitor;
            if (!onMonitor)
            {
                if (w.Mode == Mode.Full) LeaveFull(w, wr);
                w.Mode = Mode.None;
                w.HasLast = w.HasSaved = w.HasMax = false;
                return;
            }

            long style = Native.GetStyle(hwnd);
            bool zoomed = Native.IsZoomed(hwnd);
            bool framed = (style & Native.WS_CAPTION) == Native.WS_CAPTION || (style & Native.WS_THICKFRAME) != 0;
            int cover = (zoomed && framed) ? 0 : Coverage(hwnd, layout.Bounds);
            if (Tracing(w))
                Log.Write("trace " + w + " " + wr + " mode=" + w.Mode + (zoomed ? " agrandie" : "") + (framed ? " cadre" : " sans-cadre")
                    + (cover == 2 ? " couvre-tout" : cover == 1 ? " couvre-tout-moins-1px" : "") + (Native.IsArranged(hwnd) ? " ancrée" : ""));

            if (cover > 0) { OnFull(w, cover == 1); return; }
            w.FullSince = 0;
            if (zoomed) OnMax(w);
            else if (!framed && TryAdopt(w, wr)) return;
            else OnNormal(w, wr);
        }

        // Option « trace=nom_du_processus » : détaille dans le journal ce que Split voit de ses fenêtres.
        bool Tracing(WinInfo w)
        {
            return w != null && cfg.Trace.Length > 0 && string.Equals(cfg.Trace, w.Process, StringComparison.OrdinalIgnoreCase);
        }

        void OnNormal(WinInfo w, RECT wr)
        {
            switch (w.Mode)
            {
                case Mode.Bypass:
                    w.Mode = Mode.None;
                    break;
                case Mode.Max:
                    if (!wr.Near(w.Expected, 2))
                    {
                        if (IsDragging(w.Hwnd)) { Schedule(w.Hwnd, 130); return; }
                        LeaveMax(w, wr);
                        Native.GetWindowRect(w.Hwnd, out wr);
                    }
                    else if (w.Refit)
                    {
                        w.Refit = false;
                        FitVisible(w.Hwnd, layout.ZoneWork[w.Zone]);
                        Native.GetWindowRect(w.Hwnd, out wr);
                        w.Expected = wr;
                    }
                    break;
                case Mode.Full:
                    if (!wr.Near(w.Expected, 2))
                    {
                        if (w.Forced && !w.GivenUp && Fight(w)) { Place(w, w.Expected, false); return; }
                        if (StaysFull(w, wr)) return;
                        LeaveFull(w, wr);
                        Schedule(w.Hwnd, 0);   // sans bordure et à la taille d'une moitié, elle y sera rangée (TryAdopt)
                    }
                    break;
            }
            if (w.Mode != Mode.Full) KeepSnappedInZone(w, ref wr);
            w.Last = wr;
            w.HasLast = true;
        }

        // Une fenêtre ancrée par Windows (Win+Gauche / Droite, glissée contre un bord de l'écran) ne déborde pas sur
        // l'autre moitié. Windows ne connaît que l'écran entier : il lui donne sa moitié à lui (qui n'est pas la nôtre
        // hors 50/50), ou la place laissée par une autre fenêtre ancrée (« remplir l'espace disponible »), même cachée
        // derrière un jeu. On recoupe au moment où elle est ancrée, ou si elle change de taille toute seule ; un
        // redimensionnement fait à la souris est respecté.
        void KeepSnappedInZone(WinInfo w, ref RECT wr)
        {
            if (!Arranged(w.Hwnd)) { w.WasArranged = w.NoTrim = false; return; }
            if (inMoveSize)
            {
                if (w.Hwnd != moveHwnd) w.WasArranged = true;   // voisine redimensionnée avec celle qu'on tire
                return;
            }
            bool fresh = !w.WasArranged && w.HasLast;
            bool changed = w.HasLast && !wr.Near(w.Last, 0);
            w.WasArranged = true;
            if (!cfg.InterceptMaximize || w.NoTrim) return;
            if (fresh)
            {
                // Même position et même largeur qu'avant : étirée en hauteur (Win+Maj+Haut), pas ancrée sur un côté.
                if (wr.Left == w.Last.Left && wr.Width == w.Last.Width) return;
            }
            else if (!changed || Environment.TickCount - moveEndTick < 600) return;

            RECT fb;
            if (Native.DwmGetWindowAttribute(w.Hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fb, 16) != 0) fb = wr;
            int l = fb.Left - wr.Left, r = wr.Right - fb.Right;
            if (l < 0 || l > 40 || r < 0 || r > 40) { l = r = 0; fb = wr; }
            RECT b = layout.Bounds;
            bool atLeft = fb.Left <= b.Left + 2, atRight = fb.Right >= b.Right - 2;
            if (atLeft == atRight) return;   // pas contre un bord, ou sur toute la largeur
            RECT z = layout.Zones[atLeft ? 0 : layout.Zones.Length - 1];
            if ((atLeft ? fb.Right - z.Right : z.Left - fb.Left) <= 2) return;

            if (!Fight(w))
            {
                w.NoTrim = true;
                Log.Write("Fenêtre ancrée laissée telle quelle (elle reprend sa largeur) : " + w);
                return;
            }
            RECT t = atLeft ? new RECT(wr.Left, wr.Top, z.Right + r, wr.Bottom) : new RECT(z.Left - l, wr.Top, wr.Right, wr.Bottom);
            Log.Write("Fenêtre ancrée recoupée à sa moitié (" + (fresh ? "vient d'être ancrée" : "a changé de taille") + ") : " + w + " " + wr + " -> " + t);
            if (!Place(w, t, false)) return;
            w.LastApply = Environment.TickCount;
            Native.GetWindowRect(w.Hwnd, out wr);
        }

        // La fenêtre vient d'être agrandie par Windows : on la ramène dans sa moitié.
        void OnMax(WinInfo w)
        {
            IntPtr hwnd = w.Hwnd;
            if (!cfg.InterceptMaximize || w.Mode == Mode.Bypass) return;
            if (Native.IsHungAppWindow(hwnd)) return;
            if (w.Mode == Mode.Full) { RECT cur; Native.GetWindowRect(hwnd, out cur); LeaveFull(w, cur); }

            // Maj enfoncée : vraie maximisation sur tout l'écran.
            if (Native.GetAsyncKeyState(Native.VK_SHIFT) < 0)
            {
                w.Mode = Mode.Bypass;
                w.HasSaved = w.HasMax = false;
                Log.Write("Maximisation complète (Maj) : " + w);
                return;
            }

            WINDOWPLACEMENT wp = NewPlacement();
            Native.GetWindowPlacement(hwnd, ref wp);
            RECT normal = layout.FromWorkspace(wp.rcNormalPosition);
            bool dropped = hwnd == moveHwnd && (inMoveSize || Environment.TickCount - moveEndTick < 700);

            // Deuxième clic sur « Agrandir » d'une fenêtre déjà agrandie dans sa moitié : on restaure.
            if (w.Mode == Mode.Max && !dropped)
            {
                RestoreSaved(w, normal);
                return;
            }

            int z = dropped ? layout.ZoneAtCursor() : layout.ZoneOf(w.HasLast ? w.Last : normal);
            if (z < 0) z = layout.ZoneOf(normal);
            if (z < 0) z = Math.Max(0, layout.ZoneAtCursor());
            RECT saved = (w.Mode == Mode.Max && w.HasSaved) ? w.Saved : normal;
            ApplyMax(w, z, saved);
        }

        void ApplyMax(WinInfo w, int zone, RECT saved)
        {
            IntPtr hwnd = w.Hwnd;
            RECT visible = layout.ZoneWork[zone];
            int b = InvisibleBorder(hwnd);
            SetNormal(hwnd, new RECT(visible.Left - b, visible.Top, visible.Right + b, visible.Bottom + b));
            if (Native.IsZoomed(hwnd))
            {
                // Refus (fenêtre lancée en administrateur) : on n'insiste pas.
                w.Mode = Mode.Bypass;
                WarnElevated(w);
                return;
            }
            FitVisible(hwnd, visible);
            RECT now;
            Native.GetWindowRect(hwnd, out now);
            w.Mode = Mode.Max;
            w.Zone = w.LastZone = zone;
            w.Expected = now;
            w.MaxRect = now;
            w.HasMax = true;
            w.Saved = saved;
            w.HasSaved = true;
            w.Last = now;
            w.HasLast = true;
            w.Refit = true;
            Schedule(hwnd, 350);
            Log.Write("Agrandie dans la zone " + (zone + 1) + " : " + w + " -> " + now);
        }

        // La fenêtre agrandie a été déplacée ou redimensionnée : elle redevient une fenêtre normale.
        void LeaveMax(WinInfo w, RECT wr)
        {
            bool moved = w.Hwnd == moveHwnd && Environment.TickCount - moveEndTick < 1500
                && wr.Width == w.Expected.Width && wr.Height == w.Expected.Height;
            if (moved && w.HasSaved && (w.Saved.Width < wr.Width || w.Saved.Height < wr.Height))
            {
                // Comme Windows : en la décrochant par la barre de titre, elle reprend sa taille d'avant.
                POINT c;
                Native.GetCursorPos(out c);
                double fx = wr.Width > 0 ? (double)(c.X - wr.Left) / wr.Width : 0.5;
                fx = Math.Max(0.05, Math.Min(0.95, fx));
                int nw = Math.Min(w.Saved.Width, wr.Width), nh = Math.Min(w.Saved.Height, wr.Height);
                Place(w, new RECT(c.X - (int)(nw * fx), wr.Top, c.X - (int)(nw * fx) + nw, wr.Top + nh), false);
            }
            w.Mode = Mode.None;
            w.HasSaved = w.HasMax = false;
            w.Refit = false;
        }

        static WINDOWPLACEMENT NewPlacement()
        {
            WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
            wp.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            return wp;
        }

        // Sort la fenêtre de l'état « agrandi » et lui donne ce rectangle (coordonnées écran), sans l'activer.
        void SetNormal(IntPtr hwnd, RECT r)
        {
            WINDOWPLACEMENT wp = NewPlacement();
            Native.GetWindowPlacement(hwnd, ref wp);
            wp.flags = 0;
            wp.showCmd = Native.SW_SHOWNOACTIVATE;
            wp.rcNormalPosition = layout.ToWorkspace(r);
            if (!Native.SetWindowPlacement(hwnd, ref wp))
                Log.Write("SetWindowPlacement a échoué (" + Marshal.GetLastWin32Error() + ")");

            // Certaines applications (WinForms) reprennent d'elles-mêmes leur ancienne position en quittant
            // l'état agrandi : une fois redevenues normales, un simple déplacement suffit.
            RECT now;
            if (!Native.IsZoomed(hwnd) && Native.GetWindowRect(hwnd, out now) && !now.Near(r, 1))
                Native.SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        // Bordure de redimensionnement invisible de Windows 10 (7 px à 100 %).
        static int InvisibleBorder(IntPtr hwnd)
        {
            if ((Native.GetStyle(hwnd) & Native.WS_THICKFRAME) == 0) return 0;
            try
            {
                uint dpi = Native.GetDpiForWindow(hwnd);
                if (dpi == 0) dpi = 96;
                return Math.Max(0, Native.GetSystemMetricsForDpi(Native.SM_CXSIZEFRAME, dpi)
                    + Native.GetSystemMetricsForDpi(Native.SM_CXPADDEDBORDER, dpi) - 1);
            }
            catch (EntryPointNotFoundException)
            {
                return Math.Max(0, Native.GetSystemMetrics(Native.SM_CXSIZEFRAME) + Native.GetSystemMetrics(Native.SM_CXPADDEDBORDER) - 1);
            }
        }

        // Ajuste pour que la partie VISIBLE de la fenêtre (selon DWM) remplisse exactement le rectangle.
        static void FitVisible(IntPtr hwnd, RECT visible)
        {
            RECT wr, fb;
            if (!Native.GetWindowRect(hwnd, out wr)) return;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fb, 16) != 0) return;
            int l = fb.Left - wr.Left, t = fb.Top - wr.Top, r = wr.Right - fb.Right, b = wr.Bottom - fb.Bottom;
            if (l < 0 || t < 0 || r < 0 || b < 0 || l > 40 || t > 40 || r > 40 || b > 40) return;
            RECT want = new RECT(visible.Left - l, visible.Top - t, visible.Right + r, visible.Bottom + b);
            if (want.Near(wr, 0)) return;
            Native.SetWindowPos(hwnd, IntPtr.Zero, want.Left, want.Top, want.Width, want.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        static RECT Shrink(RECT zone)
        {
            int w = (int)(zone.Width * 0.7), h = (int)(zone.Height * 0.75);
            int x = zone.Left + (zone.Width - w) / 2, y = zone.Top + (zone.Height - h) / 2;
            return new RECT(x, y, x + w, y + h);
        }
    }
}
