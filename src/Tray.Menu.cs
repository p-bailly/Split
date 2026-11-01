using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Split
{
    internal sealed partial class Tray
    {
        static ToolStripMenuItem Item(string text, Action onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += delegate { onClick(); };
            return it;
        }

        static ToolStripMenuItem Check(string text, bool isChecked, Action onClick)
        {
            ToolStripMenuItem it = Item(text, onClick);
            it.Checked = isChecked;
            return it;
        }

        static readonly string[] Names2 = { "gauche", "droite" }, Names3 = { "gauche", "centre", "droite" };

        // « moitié » à deux écrans, « zone » au-delà.
        string Part { get { return cfg.Parts.Count > 2 ? "zone" : "moitié"; } }

        static string Number(int n)
        {
            return n == 2 ? "deux" : n == 3 ? "trois" : n == 4 ? "quatre" : n.ToString();
        }

        static string ZoneName(int index, int count)
        {
            return count == 2 ? " (" + Names2[index] + ")" : count == 3 ? " (" + Names3[index] + ")" : "";
        }

        void BuildMenu()
        {
            int zones = cfg.Parts.Count;
            menu.Items.Clear();
            ToolStripMenuItem head = new ToolStripMenuItem("Split — écran divisé en " + Number(zones));
            head.Enabled = false;
            menu.Items.Add(head);
            menu.Items.Add(Check("Activé   (" + cfg.HotkeyToggle + ")", cfg.Enabled, delegate { SetEnabled(!cfg.Enabled); }));

            ToolStripMenuItem ratio = new ToolStripMenuItem("Répartition");
            bool listed = false;
            foreach (string[] group in new string[][] { Splits2, Splits3 })
            {
                if (group == Splits3) ratio.DropDownItems.Add(new ToolStripSeparator());
                foreach (string s in group)
                {
                    string value = s;
                    bool on = cfg.SplitText == value;
                    listed |= on;
                    ratio.DropDownItems.Add(Check(value.Replace("/", " / ") + (group == Splits3 ? "   (trois écrans)" : ""), on, delegate { SetSplit(value); }));
                }
            }
            if (!listed)
            {
                ToolStripMenuItem custom = Check(cfg.SplitLabel + "   (personnalisée)", true, delegate { });
                custom.Enabled = false;
                ratio.DropDownItems.Insert(0, custom);
            }
            menu.Items.Add(ratio);

            ToolStripMenuItem screen = new ToolStripMenuItem("Écran à diviser");
            bool auto = string.Equals(cfg.Monitor, "auto", StringComparison.OrdinalIgnoreCase);
            screen.DropDownItems.Add(Check("Automatique (le plus large)", auto, delegate { SetMonitor("auto"); }));
            foreach (MonitorDesc m in Layout.Monitors())
            {
                string dev = m.Device;
                string label = dev.Replace(@"\\.\", "") + "   " + m.Bounds.Width + " × " + m.Bounds.Height;
                bool on = string.Equals(cfg.Monitor, dev, StringComparison.OrdinalIgnoreCase);
                screen.DropDownItems.Add(Check(label, on, delegate { SetMonitor(dev); }));
            }
            menu.Items.Add(screen);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Check("Agrandir une fenêtre = remplir sa " + Part, cfg.InterceptMaximize,
                delegate { cfg.InterceptMaximize = !cfg.InterceptMaximize; SaveConfig(); engine.Reset(); }));
            menu.Items.Add(Check("Plein écran = remplir sa " + Part, cfg.InterceptFullscreen,
                delegate { cfg.InterceptFullscreen = !cfg.InterceptFullscreen; SaveConfig(); engine.Reset(); }));
            menu.Items.Add(Check("Le plein écran recouvre la barre des tâches", cfg.CoverTaskbar,
                delegate { cfg.CoverTaskbar = !cfg.CoverTaskbar; SaveConfig(); engine.Reconfigure(); }));

            ToolStripMenuItem mouse = new ToolStripMenuItem("Souris");
            for (int i = 0; i < zones; i++)
            {
                int zone = i;
                mouse.DropDownItems.Add(Check("Bloquer dans l'écran " + (zone + 1) + ZoneName(zone, zones), engine.LockedZone == zone, delegate { engine.LockCursorTo(zone); }));
            }
            mouse.DropDownItems.Add(Check("Libre   (" + cfg.HotkeyLockCursor + " pour basculer)", engine.LockedZone < 0, delegate { engine.LockCursorTo(-1); }));
            mouse.DropDownItems.Add(new ToolStripSeparator());
            mouse.DropDownItems.Add(Check("Bloquer automatiquement pendant un plein écran", cfg.AutoLockCursor,
                delegate { cfg.AutoLockCursor = !cfg.AutoLockCursor; SaveConfig(); }));
            menu.Items.Add(mouse);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Montrer les " + Number(zones) + " " + Part + "s", delegate { Overlay.Flash(engine.Layout); }));
            menu.Items.Add(Item("Raccourcis clavier et aide…", ShowHelp));
            menu.Items.Add(Item("Modifier la configuration…", OpenConfig));
            menu.Items.Add(Check("Notifications", cfg.Notifications,
                delegate { cfg.Notifications = !cfg.Notifications; SaveConfig(); }));
            menu.Items.Add(Check("Lancer au démarrage de Windows", StartsWithWindows(),
                delegate { SetStartsWithWindows(!StartsWithWindows()); }));
            if (IsElevated())
            {
                ToolStripMenuItem admin = new ToolStripMenuItem("Mode administrateur actif");
                admin.Enabled = false;
                menu.Items.Add(admin);
            }
            else menu.Items.Add(Item("Redémarrer en administrateur (jeux protégés)", RestartAsAdmin));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Quitter", Quit));
        }

        void SetMonitor(string device)
        {
            cfg.Monitor = device;
            SaveConfig();
            engine.Reconfigure();
            Overlay.Flash(engine.Layout);
        }

        // L'icône reprend la répartition actuelle ; grise quand Split est désactivé.
        void RefreshIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int n = cfg.Parts.Count, total = 0, sum = 0, x = 1;
                    foreach (int p in cfg.Parts) total += p;
                    for (int i = 0; i < n; i++)
                    {
                        sum += cfg.Parts[i];
                        int end = 1 + (int)Math.Round(30.0 * sum / total);
                        int l = x + (i == 0 ? 0 : 1), r = end - (i == n - 1 ? 0 : 1);
                        Color c = cfg.Enabled ? Overlay.Colors[i % Overlay.Colors.Length] : Color.FromArgb(120 + 45 * (i % 2), 120 + 45 * (i % 2), 120 + 45 * (i % 2));
                        using (Brush b = new SolidBrush(c)) g.FillRectangle(b, l, 7, Math.Max(1, r - l), 18);
                        if (engine.LockedZone == i)
                        {
                            int d = Math.Min(8, Math.Max(4, r - l - 2));
                            g.FillEllipse(Brushes.White, (l + r - d) / 2, 16 - d / 2, d, d);
                        }
                        x = end;
                    }
                }
                IntPtr handle = bmp.GetHicon();
                icon.Icon = Icon.FromHandle(handle);
                if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
                iconHandle = handle;
            }
            icon.Text = cfg.Enabled ? "Split — " + cfg.SplitLabel : "Split — désactivé";
        }

        void ShowHelp()
        {
            string nl = Environment.NewLine;
            string text =
                "Votre écran se comporte comme " + Number(cfg.Parts.Count) + " écrans côte à côte (" + cfg.SplitLabel + ")." + nl + nl
                + "• Agrandir une fenêtre (bouton, double-clic sur le titre, Win+Haut) : elle remplit sa " + Part + ". "
                + "Un deuxième clic la restaure. Maintenez Maj pour l'agrandir sur tout l'écran." + nl
                + "• Plein écran (F11, vidéo, jeu « fenêtré sans bordure ») : limité à la " + Part + " où se trouve la fenêtre." + nl
                + "• Répartition (clic droit sur l'icône) : deux écrans, ou trois comme 25 / 50 / 25." + nl + nl
                + "Raccourcis" + nl
                + "  " + cfg.HotkeyLeft + " / " + cfg.HotkeyRight + " : envoyer la fenêtre dans l'écran voisin, à gauche / à droite" + nl
                + "  " + cfg.HotkeyMaximize + " : agrandir dans la " + Part + " (ou restaurer)" + nl
                + "  " + cfg.HotkeyRestore + " : restaurer" + nl
                + "  " + cfg.HotkeyFullscreen + " : plein écran sans bordure dans la " + Part + " (ou en sortir)" + nl
                + "  " + cfg.HotkeyLockCursor + " : bloquer / libérer la souris dans la " + Part + nl
                + "  " + cfg.HotkeyToggle + " : activer / désactiver Split" + nl + nl
                + "Jeux" + nl
                + "Le plein écran « exclusif » prend tout l'écran physique : aucun logiciel ne peut le découper. "
                + "Dans les options du jeu, choisissez « Fenêtré » ou « Fenêtré sans bordure » avec la résolution de sa " + Part + ", "
                + "puis " + cfg.HotkeyFullscreen + "." + nl
                + "Si le jeu tourne en administrateur, utilisez « Redémarrer en administrateur ».";
            MessageBox.Show(text, "Split — aide", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OpenConfig()
        {
            try
            {
                if (!System.IO.File.Exists(Config.FilePath)) SaveConfig();
                Process.Start("notepad.exe", "\"" + Config.FilePath + "\"");
            }
            catch (Exception ex) { Balloon("Split", "Impossible d'ouvrir la configuration : " + ex.Message); }
        }
    }
}
