using System;
using System.Collections.Generic;

namespace Split
{
    internal sealed partial class Engine
    {
        // Les changements d'empilement ne passent jamais par le veto de la fenêtre (voir Engine.Chromium.cs).
        const uint ZFlags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOSENDCHANGING;
        const uint PFlags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
        readonly HashSet<string> introduced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // La zone cliente recouvre-t-elle l'écran ? 0 = non, 2 = oui, 1 = oui à 1 ou 2 px près en bas
        // (taille que se donne un navigateur Chromium en plein écran quand il n'est plus au premier plan).
        static int Coverage(IntPtr hwnd, RECT area)
        {
            RECT c;
            if (!Native.GetClientRect(hwnd, out c)) return 0;
            POINT p = new POINT();
            if (!Native.ClientToScreen(hwnd, ref p)) return 0;
            if (p.X > area.Left || p.Y > area.Top || p.X + c.Width < area.Right) return 0;
            int bottom = p.Y + c.Height;
            return bottom >= area.Bottom ? 2 : (bottom >= area.Bottom - 2 ? 1 : 0);
        }

        // La fenêtre recouvre tout l'écran (F11, vidéo, jeu sans bordure) : on la limite à sa moitié.
        void OnFull(WinInfo w, bool almost)
        {
            if (w.Mode == Mode.Full && w.GivenUp) return;
            if (!cfg.InterceptFullscreen) return;
            IntPtr hwnd = w.Hwnd;
            int now = Environment.TickCount;
            bool tracked = w.Mode == Mode.Full;
            if (w.Chromium) { w.Hack = almost; w.HackKnown = true; }

            // Une application inconnue doit finir sa transition avant qu'on intervienne. Une fenêtre déjà suivie
            // (ou Chromium, qui a terminé quand l'événement arrive) est reprise tout de suite, sans temps mort.
            if (!tracked && !w.Chromium)
            {
                if (w.FullSince == 0) { w.FullSince = now | 1; Schedule(hwnd, 120); return; }
                if (now - w.FullSince < 100) { Schedule(hwnd, 60); return; }
                if (cfg.SkipExclusive && IsExclusiveFullscreen())
                {
                    Log.Write("Plein écran exclusif, ignoré : " + w);
                    return;
                }
            }
            if (Native.IsHungAppWindow(hwnd)) return;

            bool fresh = w.Mode == Mode.None && !w.HasLast;   // apparue directement en plein écran : sans doute un jeu
            int zone;
            if (tracked)
            {
                if (!Fight(w)) { GiveUp(w); return; }
                zone = w.Zone;
            }
            else if (w.Mode == Mode.Max) zone = w.Zone;
            else zone = PickZone(w);
            ApplyFull(w, zone, false);
            if (fresh && !w.GivenUp) Introduce(w);
        }

        // Moitié d'origine de la fenêtre si elle est nette, sinon la moitié mémorisée pour l'application, sinon le curseur.
        int PickZone(WinInfo w)
        {
            int z = w.HasLast ? layout.ZoneOf(w.Last) : -1;
            if (z >= 0 && Mostly(w.Last, layout.Zones[z], 90)) return z;
            int pref;
            if (cfg.Remember.TryGetValue(w.Process, out pref) && pref >= 0 && pref < layout.Zones.Length) return pref;
            if (z >= 0) return z;
            return DefaultZone();
        }

        // Moitié où ranger une fenêtre à la taille d'une moitié mais posée à cheval sur les deux : celle où elle se
        // trouvait juste avant, sinon celle que Split lui avait déjà donnée, sinon celle mémorisée pour l'application,
        // sinon celle qu'elle recouvre nettement le plus, sinon le réglage (curseur par défaut).
        int StrayZone(WinInfo w, RECT wr)
        {
            int z = w.HasLast ? layout.ZoneOf(w.Last) : -1;
            if (z >= 0 && Mostly(w.Last, layout.Zones[z], 90)) return z;
            if (w.LastZone >= 0 && w.LastZone < layout.Zones.Length) return w.LastZone;
            int pref;
            if (cfg.Remember.TryGetValue(w.Process, out pref) && pref >= 0 && pref < layout.Zones.Length) return pref;
            z = layout.ZoneOf(wr);
            if (z >= 0 && Mostly(wr, layout.Zones[z], 60)) return z;
            return DefaultZone();
        }

        int DefaultZone()
        {
            int n = layout.Zones.Length;
            if (cfg.NewFullscreenZone == "left") return 0;
            if (cfg.NewFullscreenZone == "right") return n - 1;
            if ((cfg.NewFullscreenZone == "center" || cfg.NewFullscreenZone == "middle") && n > 2) return n / 2;
            return Math.Max(0, layout.ZoneAtCursor());
        }

        // Le rectangle se trouve-t-il dans la zone pour au moins « percent » % de sa surface ?
        static bool Mostly(RECT r, RECT zone, int percent)
        {
            return zone.IntersectArea(r) * 100 >= (long)r.Width * r.Height * percent;
        }

        // squeezed : la fenêtre occupait tout l'écran et vient d'être réduite (l'image d'un jeu est alors écrasée).
        void Introduce(WinInfo w, bool squeezed = true)
        {
            if (!introduced.Add(w.Process)) return;
            RECT z = layout.Zones[w.Zone];
            string text = squeezed ? "Image déformée ? Réglez la résolution du jeu sur " + z.Width + " × " + z.Height + ". " : "";
            if (cfg.HotkeyLeft.Length > 0 && cfg.HotkeyRight.Length > 0)
                text += cfg.HotkeyLeft + " / " + cfg.HotkeyRight + " : changer d'écran.";
            if (text.Length == 0) text = "Clic droit sur l'icône Split : options.";
            Say("« " + w.Process + " » en plein écran sur l'écran " + (w.Zone + 1), text);
        }

        // Fenêtre sans bordure qui occupe déjà exactement une moitié (jeu réglé à la résolution de la moitié) :
        // on la traite comme un plein écran de cette moitié, pour qu'elle passe devant la barre des tâches.
        bool TryAdopt(WinInfo w, RECT wr)
        {
            if (!cfg.InterceptFullscreen || w.Mode == Mode.Full) return false;
            int z = layout.ZoneOf(wr);
            if (z < 0) return false;
            bool fresh = w.Mode == Mode.None && !w.HasLast;
            bool inPlace = wr.Near(layout.Zones[z], 16) && ClientCovers(w.Hwnd, layout.Zones[z]);
            if (!inPlace)
            {
                // Taille exacte d'une moitié, mais posée à cheval sur les deux : Windows ne connaît que l'écran entier,
                // et un jeu sans bordure s'y centre (à son lancement, ou quand on change sa résolution en cours de
                // partie). On la range dans une moitié, sauf si elle en ressort d'elle-même à chaque fois.
                if (w.Stubborn || IsDragging(w.Hwnd)) return false;
                int match = -1, count = 0;
                for (int i = 0; i < layout.Zones.Length; i++)
                    if (SameSize(wr, layout.Zones[i])) { match = i; count++; }
                if (count == 0) return false;
                int wait = w.StrayWait - Environment.TickCount;
                if (w.StrayTries > 0 && wait > 0) { Schedule(w.Hwnd, wait + 15); return false; }   // essai manqué à l'instant
                if (!Dropped(w.Hwnd)) z = count == 1 ? match : StrayZone(w, wr);   // lâchée à la souris : la zone la plus recouverte
                // Jamais une zone d'une autre taille (à trois zones, 25/50/25) : la fenêtre y serait déformée.
                if (!SameSize(wr, layout.Zones[z])) z = layout.MatchingZone(wr, 8);
            }
            w.WasTopmost = NativeTopmost(w);
            w.MadeTopmost = false;
            if (w.Mode != Mode.Max) w.HasMax = false;
            w.Mode = Mode.Full;
            w.Zone = w.LastZone = z;
            w.Expected = wr;
            w.GivenUp = false;
            w.Fails = w.Fights = 0;
            Log.Write("Plein écran adopté dans la zone " + (z + 1) + " : " + w);
            int pref;
            if (!inPlace)
            {
                ApplyFull(w, z, false);
                RECT now;
                bool denied = w.GivenUp;   // fenêtre protégée (déjà signalé)
                if (denied || !Native.GetWindowRect(w.Hwnd, out now) || !now.Near(FullRect(z), 2))
                {
                    // Elle a refusé sa moitié, ou en est ressortie dans la foulée : deux autres essais un peu plus
                    // tard (une application qui démarre se replace parfois une dernière fois), puis on n'insiste plus.
                    Native.GetWindowRect(w.Hwnd, out now);
                    LeaveFull(w, now);
                    if (denied) w.Stubborn = true;
                    else if (++w.StrayTries >= 3) Shun(w);
                    else { w.StrayWait = Environment.TickCount + 400; Schedule(w.Hwnd, 415); }
                    return false;
                }
                w.StrayTries = 0;
                Introduce(w, false);
            }
            else if (fresh && cfg.Remember.TryGetValue(w.Process, out pref) && pref != z && pref >= 0 && pref < layout.Zones.Length)
                ApplyFull(w, pref, false);
            else { UpdateTopmost(); Recheck(); }
            return true;
        }

        // La fenêtre ne veut pas de sa moitié : on ne l'y range plus d'office (le raccourci reste possible).
        void Shun(WinInfo w)
        {
            w.Stubborn = true;
            Say("Fenêtre non rangée", "« " + w.Process + " » ressort d'elle-même de sa " + Part + ". Passez-la en mode fenêtré"
                + (cfg.HotkeyFullscreen.Length > 0 ? ", puis appuyez sur " + cfg.HotkeyFullscreen : "") + ".");
        }

        static bool SameSize(RECT a, RECT b)
        {
            return Math.Abs(a.Width - b.Width) <= 8 && Math.Abs(a.Height - b.Height) <= 8;
        }

        // Une fenêtre sans bordure qui se replace toute seule reste dans sa moitié : jeu qui garde la taille de la
        // moitié (changement de résolution, retour d'Alt+Tab), ou application qui réagit à notre placement en
        // recalculant sa taille (VLC ajuste sa fenêtre à la vidéo).
        bool StaysFull(WinInfo w, RECT wr)
        {
            if (w.GivenUp || w.Forced) return false;
            long style = Native.GetStyle(w.Hwnd);
            if ((style & Native.WS_CAPTION) == Native.WS_CAPTION || (style & Native.WS_THICKFRAME) != 0) return false;
            RECT z = FullRect(w.Zone);
            bool sameSize = Math.Abs(wr.Width - z.Width) <= 2 && Math.Abs(wr.Height - z.Height) <= 2;
            bool reaction = Environment.TickCount - w.LastApply < 300;
            if (!sameSize && !reaction) return false;

            // Glissée à la souris (certaines fenêtres sans bordure se laissent déplacer) : on attend qu'elle soit
            // lâchée, et elle prend alors la moitié qu'elle recouvre le plus.
            if (IsDragging(w.Hwnd)) { Schedule(w.Hwnd, 130); return true; }
            int zone = w.Zone;
            if (sameSize && Dropped(w.Hwnd))
            {
                int d = layout.ZoneOf(wr);
                if (d >= 0 && SameSize(wr, FullRect(d))) zone = d;
                w.Fights = 0;
            }
            else if (!Fight(w))
            {
                if (sameSize) Shun(w);
                return false;
            }
            ApplyFull(w, zone, false);
            return true;
        }

        // Rectangle d'un plein écran dans cette moitié : avec ou sans la barre des tâches, selon le réglage.
        RECT FullRect(int zone)
        {
            return cfg.CoverTaskbar ? layout.Zones[zone] : layout.ZoneWork[zone];
        }

        void ApplyFull(WinInfo w, int zone, bool frameChanged)
        {
            RECT target = FullRect(zone);
            if (w.Mode != Mode.Full)
            {
                w.WasTopmost = NativeTopmost(w);
                w.MadeTopmost = false;
                if (w.Mode != Mode.Max) w.HasMax = false;
                w.Fails = w.Fights = 0;
            }
            w.Mode = Mode.Full;
            w.Zone = w.LastZone = zone;

            bool denied;
            bool ok = PlaceFull(w, target, frameChanged, out denied);
            w.LastApply = Environment.TickCount;
            RECT now;
            Native.GetWindowRect(w.Hwnd, out now);
            w.Expected = now;
            if (!ok)
            {
                if (denied) { w.GivenUp = true; WarnElevated(w); }
                else if (++w.Fails >= 4) GiveUp(w);
                else Schedule(w.Hwnd, w.Chromium ? 40 : 400);   // Chromium n'ignore les demandes qu'un court instant
                return;
            }
            w.Fails = 0;
            w.GivenUp = false;
            Log.Write("Plein écran dans la zone " + (zone + 1) + " : " + w + " -> " + now);
            UpdateTopmost();
            Recheck();
            Schedule(w.Hwnd, 400);
        }

        // Place la fenêtre ; si elle refuse (certaines applications réimposent leur taille quand on leur demande
        // leur avis), on recommence sans le lui demander.
        bool PlaceFull(WinInfo w, RECT t, bool frameChanged, out bool denied)
        {
            denied = false;
            IntPtr h = w.Hwnd;
            if (w.Chromium && !frameChanged && !w.Forced) return PlaceChromium(w, t, out denied);

            uint flags = PFlags | (frameChanged ? Native.SWP_FRAMECHANGED : 0);
            if (!w.NoChanging)
            {
                if (!SetPos(h, t, flags, out denied)) return false;
                if (At(h, t)) return true;
            }
            if (!SetPos(h, t, flags | Native.SWP_NOSENDCHANGING, out denied) || !At(h, t)) return false;
            if (!w.NoChanging) Log.Write("Veto contourné : " + w);
            w.NoChanging = true;
            return true;
        }

        static bool SetPos(IntPtr h, RECT r, uint flags, out bool denied)
        {
            denied = false;
            if (Native.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags)) return true;
            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.Write("SetWindowPos a échoué (" + err + ")");
            denied = err == 5;
            return false;
        }

        static bool At(IntPtr h, RECT r)
        {
            RECT now;
            return Native.GetWindowRect(h, out now) && now.Near(r, 1);
        }

        // L'application défait-elle notre placement aussitôt fait ? Un retour isolé (changement de focus) ne compte pas.
        static bool Fight(WinInfo w)
        {
            if (Environment.TickCount - w.LastApply < 250) w.Fights++;
            else w.Fights = 0;
            return w.Fights <= 5;
        }

        void GiveUp(WinInfo w)
        {
            w.GivenUp = true;
            DropTopmost(w);
            Say("Plein écran non divisible", "« " + w.Process + " » réimpose le plein écran sur tout l'écran. "
                + "Passez-le en mode fenêtré, puis appuyez sur " + cfg.HotkeyFullscreen + ".");
        }

        // La fenêtre a quitté le plein écran (ou on y renonce).
        void LeaveFull(WinInfo w, RECT wr)
        {
            DropTopmost(w);
            if (w.Forced)
            {
                Native.SetLong(w.Hwnd, Native.GWL_STYLE, w.SavedStyle);
                Native.SetLong(w.Hwnd, Native.GWL_EXSTYLE, w.SavedExStyle);
                Native.SetWindowPos(w.Hwnd, IntPtr.Zero, 0, 0, 0, 0, ZFlags | Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED);
                w.Forced = false;
            }
            w.FullSince = 0;
            w.Fights = w.Fails = 0;
            w.GivenUp = false;
            w.HackKnown = w.Unlisted = w.Marked = false;
            if (w.HasMax && wr.Near(w.MaxRect, 2)) { w.Mode = Mode.Max; w.Expected = w.MaxRect; }
            else { w.Mode = Mode.None; w.HasMax = w.HasSaved = false; }
            Log.Write("Fin du plein écran : " + w);
        }

        // Rend à la fenêtre sa place normale dans l'empilement. On se fie à son état réel, pas à nos notes : un
        // « toujours visible » oublié ferait s'ouvrir toutes les nouvelles fenêtres derrière elle. Windows ignore
        // parfois la demande (constaté quand un navigateur est en train de quitter le plein écran) : CheckRaised
        // recommence alors, dans les instants qui suivent, jusqu'à ce que ce soit effectif.
        void DropTopmost(WinInfo w)
        {
            if (!w.WasTopmost && IsTopmost(w.Hwnd))
            {
                Native.SetWindowPos(w.Hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, ZFlags);
                bool refused = IsTopmost(w.Hwnd);
                if (cfg.Debug) Log.Write("Empilement : « toujours visible » retiré : " + w + (refused ? " (pas encore effectif)" : ""));
                if (refused) { w.Raised = true; Recheck(); }
            }
            w.MadeTopmost = false;
        }

        static bool IsTopmost(IntPtr hwnd)
        {
            return (Native.GetExStyle(hwnd) & Native.WS_EX_TOPMOST) != 0;
        }

        // État « toujours visible » de la fenêtre avant que Split s'en occupe. Si c'est Split qui l'y a mise lors d'un
        // plein écran précédent et que ce n'est pas encore retombé, ce n'est pas un état à lui conserver.
        bool NativeTopmost(WinInfo w)
        {
            return IsTopmost(w.Hwnd) && !w.Raised;
        }

        static bool IsExclusiveFullscreen()
        {
            int state;
            return Native.SHQueryUserNotificationState(out state) == 0 && state == Native.QUNS_RUNNING_D3D_FULL_SCREEN;
        }
    }
}
