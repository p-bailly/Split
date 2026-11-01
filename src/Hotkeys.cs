using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Split
{
    // Raccourcis clavier globaux (RegisterHotKey), décrits par un texte du type "Ctrl+Alt+Left".
    internal sealed class Hotkeys : NativeWindow, IDisposable
    {
        readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();
        int nextId = 1;

        // Message « Split.Test » (wParam = commande, lParam = fenêtre visée) : permet aux tests automatiques
        // de déclencher une commande sur une fenêtre précise. Ignoré hors mode debug.
        static readonly int TestMessage = Native.RegisterWindowMessage("Split.Test");
        public Action<int, IntPtr> Test;

        // Message « Split.Quit » : fermeture propre demandée par « Split.exe --quit » (mise à jour, build.bat).
        public const string WindowTitle = "Split.Hotkeys";
        public static readonly int QuitMessage = Native.RegisterWindowMessage("Split.Quit");
        public Action Quit;

        public Hotkeys()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = WindowTitle;
            CreateHandle(cp);
        }

        // Renvoie false si la combinaison est invalide ou déjà prise par un autre programme.
        public bool Register(string combo, Action action)
        {
            if (string.IsNullOrWhiteSpace(combo)) return true;
            uint mods, vk;
            if (!Parse(combo, out mods, out vk)) return false;
            int id = nextId++;
            if (!Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk)) return false;
            actions[id] = action;
            return true;
        }

        public void Clear()
        {
            foreach (int id in actions.Keys) Native.UnregisterHotKey(Handle, id);
            actions.Clear();
        }

        public static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            foreach (string raw in combo.Split('+'))
            {
                string p = raw.Trim().ToLowerInvariant();
                switch (p)
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": case "maj": mods |= Native.MOD_SHIFT; break;
                    case "win": case "windows": mods |= Native.MOD_WIN; break;
                    default:
                        if (p == "entrée" || p == "entree") p = "enter";
                        else if (p == "gauche") p = "left";
                        else if (p == "droite") p = "right";
                        else if (p == "haut") p = "up";
                        else if (p == "bas") p = "down";
                        else if (p == "espace") p = "space";
                        else if (p == "esc" || p == "échap" || p == "echap") p = "escape";
                        else if (p.Length == 1 && char.IsDigit(p[0])) p = "d" + p;
                        Keys k;
                        if (!Enum.TryParse<Keys>(p, true, out k)) return false;
                        vk = (uint)(k & Keys.KeyCode);
                        break;
                }
            }
            return vk != 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Action a;
                if (actions.TryGetValue(m.WParam.ToInt32(), out a))
                {
                    try { a(); }
                    catch (Exception ex) { Log.Write("Raccourci : " + ex); }
                }
                return;
            }
            if (m.Msg == QuitMessage && QuitMessage != 0 && Quit != null)
            {
                Quit();
                return;
            }
            if (m.Msg == TestMessage && TestMessage != 0 && Test != null)
            {
                try { Test(m.WParam.ToInt32(), m.LParam); }
                catch (Exception ex) { Log.Write("Test : " + ex); }
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Clear();
            DestroyHandle();
        }
    }
}
