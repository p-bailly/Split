using System;

namespace Split
{
    // Navigateurs Chromium (Chrome, Edge, Brave, Opera...) en plein écran.
    //
    // Leur fenêtre (ui/views/win/hwnd_message_handler.cc dans les sources de Chromium) :
    //  - se redonne la taille de l'écran PHYSIQUE entier à chaque fois qu'on lui demande son avis sur un placement
    //    (WM_WINDOWPOSCHANGING), sauf si Windows l'a « ancrée » (Win+Flèche), si elle est dans son état
    //    « arrière-plan », ou si elle se trouve hors de tout écran ; juste après, elle ignore un court instant tout
    //    placement qui lui demande son avis ;
    //  - passe dans cet état « arrière-plan » quand elle perd le focus : elle prend alors la taille de l'écran entier
    //    moins 1 pixel de haut ; dans cet état elle n'accepte que les hauteurs « écran - 1 », et n'adapte son
    //    contenu à une nouvelle taille que si on lui a demandé son avis ;
    //  - reprend la taille de l'écran entier quand elle retrouve le focus ;
    //  - est remise dans l'état « arrière-plan » chaque fois qu'une autre fenêtre du même navigateur s'active, tant
    //    qu'elle figure dans sa liste des « plein écran par écran ».
    // Split ne peut pas empêcher les sursauts liés au focus sans s'injecter dans le navigateur ; il les corrige
    // aussitôt (3 à 5 ms), avec la manière de faire que l'état courant accepte. Masquer le débordement par une région
    // ne marche pas : la partie exclue d'une fenêtre Chromium s'affiche en noir au lieu de laisser voir derrière.
    internal sealed partial class Engine
    {
        const uint NoVeto = PFlags | Native.SWP_NOSENDCHANGING;

        bool PlaceChromium(WinInfo w, RECT t, out bool denied)
        {
            IntPtr h = w.Hwnd;
            RECT m = layout.Bounds, want = t;
            bool active = w.HackKnown && !w.Hack;        // état « premier plan » : elle réimpose l'écran entier

            string recipe;
            if (active && !w.Unlisted)
            {
                // Passage hors de tout écran, où elle ne réagit à rien : un changement d'affichage reçu là lui fait
                // croire que son écran a disparu, et elle se retire de la liste des « plein écran » de son navigateur.
                // Sans cela, chaque autre fenêtre du même navigateur qui s'active lui redonnerait la taille de l'écran.
                recipe = "sans veto, hors écran";
                int x = Math.Max(-32000, layout.VirtualLeft - t.Width - 2000);
                if (!SetPos(h, new RECT(x, t.Top, x + t.Width, t.Bottom), NoVeto, out denied)) return false;
                IntPtr ignored;
                IntPtr size = new IntPtr(((m.Height & 0x7FFF) << 16) | (m.Width & 0xFFFF));
                Native.SendMessageTimeout(h, Native.WM_DISPLAYCHANGE, new IntPtr(32), size, Native.SMTO_ABORTIFHUNG, 300, out ignored);
                w.Unlisted = true;
                if (!SetPos(h, t, NoVeto, out denied)) return false;
            }
            else if (Native.IsArranged(h))
            {
                recipe = "ancrée";
                if (!SetPos(h, t, PFlags, out denied)) return false;
                if (t.Height != m.Height - 1) w.Hack = false;   // une autre hauteur la fait sortir de l'état « arrière-plan »
            }
            else if (active)
            {
                recipe = "sans veto";
                if (!SetPos(h, t, NoVeto, out denied)) return false;
            }
            else if (w.HackKnown && t.Height == m.Height)
            {
                recipe = "arrière-plan";
                want = new RECT(t.Left, t.Top, t.Right, t.Bottom - 1);
                if (!SetPos(h, want, PFlags, out denied)) return false;
            }
            else
            {
                recipe = "détour";
                if (!Detour(h, t, out denied)) return false;
            }

            RECT cur;
            if (!At(h, want) && Native.GetWindowRect(h, out cur))
            {
                if (cur.Near(m, 1))
                {
                    // Elle a repris tout l'écran : elle est donc dans son état « premier plan » (mal deviné),
                    // où le placement sans veto passe toujours.
                    recipe += " puis sans veto";
                    w.Hack = false;
                    w.HackKnown = true;
                    want = t;
                    if (!SetPos(h, t, NoVeto, out denied)) return false;
                }
                else if (cur.Width == m.Width && cur.Height == m.Height - 1)
                {
                    // Demande ignorée : elle vient tout juste de se replacer. ApplyFull réessaiera dans un instant.
                    recipe += ", ignorée";
                }
                else
                {
                    recipe += " puis détour";
                    want = t;
                    if (!Detour(h, t, out denied)) return false;
                }
            }
            bool ok = At(h, want);
            if (cfg.Debug) Log.Write("Chromium " + (ok ? "placé" : "RÉCALCITRANT") + " (" + recipe + ") : " + w);
            return ok;
        }

        // Chromium ne surveille que ce qui se passe à l'écran : on donne la taille hors de tout écran, puis on ramène
        // la fenêtre sans toucher à sa taille. Sert quand son état n'est pas connu ou n'accepte pas la taille voulue.
        bool Detour(IntPtr h, RECT t, out bool denied)
        {
            int x = Math.Max(-32000, layout.VirtualLeft - t.Width - 2000);
            if (!SetPos(h, new RECT(x, t.Top, x + t.Width, t.Bottom), PFlags, out denied)) return false;
            return Native.SetWindowPos(h, IntPtr.Zero, t.Left, t.Top, 0, 0, PFlags | Native.SWP_NOSIZE);
        }

        // Navigateur actif : on demande à la barre des tâches de s'effacer devant lui, comme pour un vrai plein écran ;
        // dès qu'elle l'a fait, la fenêtre n'a plus besoin d'être « toujours visible ». Ainsi, quand elle perd le focus
        // et se redonne un instant la taille de l'écran entier, elle se trouve derrière la fenêtre où l'on vient de cliquer.
        void YieldTaskbar(WinInfo w, bool own)
        {
            if (!own) { w.Marked = false; return; }   // en perdant le focus, le navigateur retire lui-même la marque
            if (!w.Marked) w.Marked = Taskbar.MarkFullscreen(w.Hwnd, true);
            if (!w.Marked || w.WasTopmost || !IsTopmost(w.Hwnd)) return;
            foreach (IntPtr bar in layout.Taskbars)
                if ((Native.GetExStyle(bar) & Native.WS_EX_TOPMOST) != 0) return;   // la barre n'a pas (encore) cédé
            DropTopmost(w);
        }
    }
}
