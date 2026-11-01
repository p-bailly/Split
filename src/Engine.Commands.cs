using System;
using System.Collections.Generic;

namespace Split
{
    // Actions déclenchées par les raccourcis clavier et le menu.
    internal sealed partial class Engine
    {
        int lockZone = -1;
        bool lockAuto;

        public int LockedZone { get { return lockZone; } }
        public IntPtr TargetOverride;   // tests : fenêtre visée à la place de la fenêtre active

        WinInfo Target(out RECT wr)
        {
            wr = new RECT();
            if (!cfg.Enabled || layout == null) return null;
            IntPtr hwnd = TargetOverride != IntPtr.Zero ? TargetOverride : Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return null;
            WinInfo w = Get(hwnd);
            if (w == null || w.Shell || !Native.GetWindowRect(hwnd, out wr)) return null;
            return w;
        }

        int ZoneFor(IntPtr hwnd, RECT wr)
        {
            int z = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) == layout.Monitor ? layout.ZoneOf(wr) : -1;
            return z >= 0 ? z : Math.Max(0, layout.ZoneAtCursor());
        }

        void Remember(WinInfo w)
        {
            RECT r;
            if (Native.GetWindowRect(w.Hwnd, out r)) { w.Last = r; w.HasLast = true; }
        }

        // Même critère que la détection automatique : la zone cliente recouvre tout l'écran divisé.
        bool IsFullscreen(IntPtr hwnd)
        {
            long style = Native.GetStyle(hwnd);
            bool framed = (style & Native.WS_CAPTION) == Native.WS_CAPTION || (style & Native.WS_THICKFRAME) != 0;
            return !(Native.IsZoomed(hwnd) && framed) && Coverage(hwnd, layout.Bounds) > 0;
        }

        // Retient la moitié choisie pour cette application : son prochain plein écran s'y ouvrira.
        void RememberZone(WinInfo w)
        {
            if (string.IsNullOrEmpty(w.Process) || w.Mode != Mode.Full || w.GivenUp) return;
            int old;
            if (cfg.Remember.TryGetValue(w.Process, out old) && old == w.Zone) return;
            cfg.Remember[w.Process] = w.Zone;
            if (Save != null) Save();
        }

        // Envoie la fenêtre active dans l'écran voisin (-1 : celui de gauche, +1 : celui de droite). Au bord, elle reste
        // dans son écran et y est simplement recadrée ; venue d'un autre moniteur, elle entre par le côté demandé.
        public void MoveBy(int delta)
        {
            RECT wr;
            WinInfo w = Target(out wr);
            if (w == null) return;
            int n = layout.Zones.Length, target;
            if (Native.MonitorFromWindow(w.Hwnd, Native.MONITOR_DEFAULTTONEAREST) != layout.Monitor)
                target = delta < 0 ? 0 : n - 1;
            else
            {
                bool placed = (w.Mode == Mode.Max || (w.Mode == Mode.Full && !w.GivenUp)) && w.Zone >= 0 && w.Zone < n && wr.Near(w.Expected, 2);
                int cur = placed ? w.Zone : layout.ZoneOf(wr);
                if (cur < 0) cur = Math.Max(0, layout.ZoneAtCursor());
                target = Math.Max(0, Math.Min(n - 1, cur + delta));
            }
            MoveToZone(target);
        }

        // Envoie la fenêtre active dans cette zone en gardant son état (normale, agrandie, plein écran).
        public void MoveToZone(int zone)
        {
            RECT wr;
            WinInfo w = Target(out wr);
            if (w == null || zone < 0 || zone >= layout.Zones.Length) return;
            IntPtr hwnd = w.Hwnd;
            w.Stubborn = false;   // demande explicite : on réessaie même si la fenêtre s'est déjà dérobée
            w.StrayTries = 0;

            // Plein écran (d'une moitié, ou de tout l'écran comme un jeu sans bordure) : plein écran de la moitié choisie.
            if (w.Mode == Mode.Full || IsFullscreen(hwnd))
            {
                if (w.Mode != Mode.Full || w.GivenUp || w.Zone != zone)
                {
                    bool wholeScreen = w.Mode != Mode.Full || w.GivenUp;
                    w.GivenUp = false;
                    w.Fights = 0;
                    if (w.Forced) w.ForcedRect = Carry(w.ForcedRect, zone, 0);   // la taille d'origine suit la fenêtre
                    ApplyFull(w, zone, false);
                    if (wholeScreen && !w.GivenUp) Introduce(w);                 // rappel : régler la résolution du jeu
                }
                RememberZone(w);
                return;
            }
            int border = InvisibleBorder(hwnd);
            if (Native.IsZoomed(hwnd))
            {
                WINDOWPLACEMENT wp = NewPlacement();
                Native.GetWindowPlacement(hwnd, ref wp);
                ApplyMax(w, zone, Carry(layout.FromWorkspace(wp.rcNormalPosition), zone, border));
                return;
            }
            if (w.Mode == Mode.Max && wr.Near(w.Expected, 2))
            {
                // La taille « restaurée » suit la fenêtre dans l'autre moitié.
                if (w.Zone != zone) ApplyMax(w, zone, Carry(w.Saved, zone, border));
                return;
            }

            // Fenêtre sans bordure à la taille d'une moitié (jeu réglé sur cette résolution) : plein écran de la moitié.
            long style = Native.GetStyle(hwnd);
            bool framed = (style & Native.WS_CAPTION) == Native.WS_CAPTION || (style & Native.WS_THICKFRAME) != 0;
            if (!framed && SameSize(wr, layout.Zones[zone]))
            {
                ApplyFull(w, zone, false);
                RememberZone(w);
                return;
            }

            w.Mode = Mode.None;
            w.HasSaved = w.HasMax = false;
            Place(w, Carry(wr, zone, border), false);
            Remember(w);
        }

        // Transpose un rectangle dans une moitié : même position relative, réduit et recadré pour y tenir.
        RECT Carry(RECT r, int zone, int border)
        {
            int cur = layout.Bounds.IntersectArea(r) > 0 ? layout.ZoneOf(r) : -1;
            RECT dst = layout.ZoneWork[zone];
            int nw = Math.Min(r.Width, dst.Width + 2 * border), nh = Math.Min(r.Height, dst.Height + border);
            int nx = r.Left, ny = r.Top;
            if (cur < 0)
            {
                nx = dst.Left + (dst.Width - nw) / 2;
                ny = dst.Top + (dst.Height - nh) / 2;
            }
            else if (cur != zone)
            {
                RECT src = layout.ZoneWork[cur];
                nx = dst.Left + (int)((long)(r.Left - src.Left) * dst.Width / Math.Max(1, src.Width));
            }
            nx = Math.Max(dst.Left - border, Math.Min(nx, dst.Right + border - nw));
            ny = Math.Max(dst.Top, Math.Min(ny, dst.Bottom + border - nh));
            return new RECT(nx, ny, nx + nw, ny + nh);
        }

        // Agrandit la fenêtre active dans sa moitié, ou la restaure si elle l'est déjà.
        public void ToggleMaximize()
        {
            RECT wr;
            WinInfo w = Target(out wr);
            if (w == null || w.Mode == Mode.Full) return;
            if (w.Mode == Mode.Max && wr.Near(w.Expected, 2)) { RestoreSaved(w, wr); return; }
            RECT saved = wr;
            if (Native.IsZoomed(w.Hwnd))
            {
                WINDOWPLACEMENT wp = NewPlacement();
                Native.GetWindowPlacement(w.Hwnd, ref wp);
                saved = layout.FromWorkspace(wp.rcNormalPosition);
            }
            ApplyMax(w, ZoneFor(w.Hwnd, wr), saved);
        }

        public void Restore()
        {
            RECT wr;
            WinInfo w = Target(out wr);
            if (w == null) return;
            if (w.Mode == Mode.Full && w.Forced) ExitForced(w);
            else if (w.Mode == Mode.Max && wr.Near(w.Expected, 2)) RestoreSaved(w, wr);
            else if (Native.IsZoomed(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
        }

        void RestoreSaved(WinInfo w, RECT fallback)
        {
            RECT back = w.HasSaved ? w.Saved : fallback;
            if (back.Near(w.Expected, 24) || back.Width < 80 || back.Height < 60) back = Shrink(layout.ZoneWork[w.Zone]);
            w.Mode = Mode.None;
            w.HasSaved = w.HasMax = false;
            w.Refit = false;
            SetNormal(w.Hwnd, back);
            Remember(w);
            Log.Write("Restaurée : " + w + " -> " + back);
        }

        // Plein écran sans bordure imposé dans la moitié : pour les jeux en mode fenêtré.
        public void ToggleFullscreen()
        {
            RECT wr;
            WinInfo w = Target(out wr);
            if (w == null) return;
            IntPtr hwnd = w.Hwnd;

            if (w.Mode == Mode.Full && !w.GivenUp)
            {
                if (w.Forced) ExitForced(w);
                else Say("Plein écran", "Cette fenêtre s'est mise en plein écran elle-même : quittez-le depuis l'application (Échap, F11...).");
                return;
            }
            if (IsFullscreen(hwnd))
            {
                // Déjà en plein écran sur tout l'écran : on la ramène simplement dans la moitié du curseur.
                w.GivenUp = false;
                w.Fights = 0;
                ApplyFull(w, Math.Max(0, layout.ZoneAtCursor()), false);
                RememberZone(w);
                return;
            }

            int zone = (w.Mode == Mode.Max) ? w.Zone : ZoneFor(hwnd, wr);
            if (Native.IsZoomed(hwnd))
            {
                WINDOWPLACEMENT wp = NewPlacement();
                Native.GetWindowPlacement(hwnd, ref wp);
                SetNormal(hwnd, layout.FromWorkspace(wp.rcNormalPosition));
                if (Native.IsZoomed(hwnd) || !Native.GetWindowRect(hwnd, out wr)) { WarnElevated(w); return; }
            }

            w.SavedStyle = Native.GetStyle(hwnd);
            w.SavedExStyle = Native.GetExStyle(hwnd);
            w.ForcedRect = wr;
            Native.SetLong(hwnd, Native.GWL_STYLE, w.SavedStyle & ~(Native.WS_CAPTION | Native.WS_THICKFRAME));
            Native.SetLong(hwnd, Native.GWL_EXSTYLE, w.SavedExStyle & ~(Native.WS_EX_DLGMODALFRAME
                | Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE | Native.WS_EX_STATICEDGE));
            w.Forced = true;
            ApplyFull(w, zone, true);
            if (w.GivenUp) LeaveFull(w, wr);
        }

        void ExitForced(WinInfo w)
        {
            RECT back = w.ForcedRect;
            LeaveFull(w, back);
            Place(w, back, true);
            Remember(w);
        }

        // ---------- souris ----------

        public void ToggleCursorLock()
        {
            if (lockZone >= 0) LockCursorTo(-1);
            else if (layout != null && layout.ZoneAtCursor() >= 0) LockCursorTo(layout.ZoneAtCursor());
        }

        // zone < 0 : libère la souris.
        public void LockCursorTo(int zone)
        {
            if (zone < 0 || layout == null || !cfg.Enabled) Unlock();
            else
            {
                lockZone = zone;
                lockAuto = false;
                ApplyCursorLock();
                lockTimer.Start();
            }
            if (Changed != null) Changed();
        }

        void Unlock()
        {
            lockZone = -1;
            lockAuto = false;
            lockTimer.Stop();
            Native.ClipCursor(IntPtr.Zero);
        }

        // Windows annule ClipCursor à chaque changement de fenêtre : on le réapplique régulièrement.
        void ApplyCursorLock()
        {
            if (lockZone < 0 || layout == null) return;
            if (lockZone >= layout.Zones.Length)   // la répartition a changé : cette zone n'existe plus
            {
                Unlock();
                if (Changed != null) Changed();
                return;
            }
            RECT r = layout.Zones[lockZone];
            Native.ClipCursor(ref r);
        }

        void AutoLock(WinInfo active)
        {
            bool before = lockZone >= 0;
            if (!cfg.AutoLockCursor || active == null)
            {
                if (lockAuto) Unlock();
            }
            else if (lockZone < 0 || (lockAuto && lockZone != active.Zone))
            {
                lockZone = active.Zone;
                lockAuto = true;
                ApplyCursorLock();
                lockTimer.Start();
            }
            if (before != (lockZone >= 0) && Changed != null) Changed();
        }

        // ---------- configuration ----------

        // Après un changement de répartition ou d'écran : recalcule les moitiés et y replace les fenêtres suivies.
        public void Reconfigure()
        {
            Layout fresh = Layout.Compute(cfg);
            if (fresh == null) return;
            layout = fresh;
            if (cfg.Enabled) { Refit(); UpdateTopmost(); }
        }

        void Refit()
        {
            foreach (WinInfo w in new List<WinInfo>(wins.Values))
            {
                if (!Native.IsWindow(w.Hwnd) || Native.IsIconic(w.Hwnd)) continue;
                if (w.Mode != Mode.Max && !(w.Mode == Mode.Full && !w.GivenUp)) continue;

                // Les zones ont pu changer de nombre ou de place : chaque fenêtre garde celle où elle se trouve, et un
                // plein écran prend de préférence une zone à sa taille (un jeu n'y est pas déformé).
                RECT cur;
                if (Native.GetWindowRect(w.Hwnd, out cur))
                {
                    int z = w.Mode == Mode.Full ? layout.MatchingZone(cur, 8) : -1;
                    if (z < 0) z = layout.ZoneOf(cur);
                    if (z >= 0) w.Zone = z;
                }
                if (w.Zone < 0 || w.Zone >= layout.Zones.Length) continue;
                if (w.Mode == Mode.Max) ApplyMax(w, w.Zone, w.Saved);
                else ApplyFull(w, w.Zone, false);
            }
        }

        public void SetEnabled(bool on)
        {
            cfg.Enabled = on;
            if (on) SnapshotExisting();
            else ReleaseAll();
        }

        // Une option de fond a changé (ce que Split intercepte) : on rend tout et on repart de l'état actuel.
        public void Reset()
        {
            ReleaseAll();
            if (cfg.Enabled) SnapshotExisting();
        }

        // Rend la main : bordures des jeux, premier plan et souris reviennent à la normale.
        void ReleaseAll()
        {
            Unlock();
            foreach (WinInfo w in new List<WinInfo>(wins.Values))
            {
                if (!Native.IsWindow(w.Hwnd)) continue;
                if (w.Mode == Mode.Full && w.Forced) ExitForced(w);
                else if (w.Mode == Mode.Full || w.Raised) DropTopmost(w);
                w.Mode = Mode.None;
                w.HasSaved = w.HasMax = false;
            }
            pending.Clear();
        }
    }
}
