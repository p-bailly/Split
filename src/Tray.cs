using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Split
{
    // Icône dans la zone de notification + menu.
    internal sealed partial class Tray : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Répartitions proposées dans le menu : à deux écrans, puis à trois (toute autre s'écrit dans la configuration).
        static readonly string[] Splits2 = { "50/50", "60/40", "40/60", "67/33", "33/67", "70/30", "30/70", "75/25", "25/75" };
        static readonly string[] Splits3 = { "25/50/25", "33/34/33", "20/60/20", "30/40/30" };

        readonly Config cfg;
        readonly Engine engine;
        readonly Hotkeys hotkeys = new Hotkeys();
        readonly NotifyIcon icon = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer reload = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        readonly SynchronizationContext ui;
        FileSystemWatcher watcher;
        DateTime ownWrite;          // date du fichier après notre dernier enregistrement
        DateTime seen;              // date du fichier la dernière fois qu'on l'a regardé
        IntPtr iconHandle;

        public Tray()
        {
            cfg = Config.Load();
            Log.Enabled = cfg.Debug;
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            engine = new Engine(cfg);
            engine.Notify = Balloon;
            engine.Changed = RefreshIcon;
            engine.Save = SaveConfig;
            engine.Start();

            icon.ContextMenuStrip = menu;
            icon.Visible = true;
            icon.DoubleClick += delegate { Overlay.Flash(engine.Layout); };
            menu.Opening += delegate { BuildMenu(); };
            BuildMenu();
            RefreshIcon();
            RegisterHotkeys();
            hotkeys.Test = RunTestCommand;
            hotkeys.Quit = Quit;
            WatchConfig();

            if (cfg.FirstRun)
            {
                cfg.FirstRun = false;
                SaveConfig();
                Overlay.Flash(engine.Layout);
                Balloon("Split est actif", "Agrandissez une fenêtre ou mettez une vidéo en plein écran : elle reste dans sa moitié. "
                    + "Clic droit sur cette icône pour les options et les raccourcis.");
            }
        }

        void Balloon(string title, string text)
        {
            if (!cfg.Notifications) return;
            try { icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info); }
            catch { }
        }

        void SaveConfig()
        {
            cfg.Save();
            ownWrite = seen = ConfigStamp();
        }

        // Sert à reconnaître nos propres enregistrements parmi les modifications du fichier.
        static DateTime ConfigStamp()
        {
            try { return File.GetLastWriteTimeUtc(Config.FilePath); }
            catch { return DateTime.MinValue; }
        }

        // ---------- raccourcis ----------

        void RegisterHotkeys()
        {
            hotkeys.Clear();
            List<string> failed = new List<string>();
            Try(cfg.HotkeyLeft, delegate { engine.MoveBy(-1); }, failed);
            Try(cfg.HotkeyRight, delegate { engine.MoveBy(1); }, failed);
            Try(cfg.HotkeyMaximize, delegate { engine.ToggleMaximize(); }, failed);
            Try(cfg.HotkeyRestore, delegate { engine.Restore(); }, failed);
            Try(cfg.HotkeyFullscreen, delegate { engine.ToggleFullscreen(); }, failed);
            Try(cfg.HotkeyLockCursor, delegate { engine.ToggleCursorLock(); }, failed);
            Try(cfg.HotkeyToggle, delegate { SetEnabled(!cfg.Enabled); }, failed);
            if (failed.Count > 0)
                Balloon("Raccourci indisponible", string.Join(", ", failed.ToArray())
                    + " : déjà utilisé par un autre programme. Changez-le dans la configuration.");
        }

        // Commandes pilotées par les tests (mode debug uniquement), sur une fenêtre donnée plutôt que la fenêtre active.
        void RunTestCommand(int command, IntPtr hwnd)
        {
            if (!cfg.Debug) return;
            if (command == 5) { engine.TestForeground(hwnd); return; }
            engine.TargetOverride = hwnd;
            try
            {
                switch (command)
                {
                    case 0: engine.MoveBy(-1); break;
                    case 1: engine.MoveBy(1); break;
                    case 2: engine.ToggleMaximize(); break;
                    case 3: engine.Restore(); break;
                    case 4: engine.ToggleFullscreen(); break;
                }
            }
            finally { engine.TargetOverride = IntPtr.Zero; }
        }

        void Try(string combo, Action action, List<string> failed)
        {
            if (!hotkeys.Register(combo, action)) failed.Add(combo);
        }

        // ---------- configuration ----------

        void SetEnabled(bool on)
        {
            engine.SetEnabled(on);
            SaveConfig();
            RefreshIcon();
            Balloon("Split", on ? "Division de l'écran activée." : "Division de l'écran désactivée.");
        }

        void SetSplit(string split)
        {
            List<int> parts = Config.ParseSplit(split);
            if (parts == null) return;
            cfg.Parts = parts;
            SaveConfig();
            engine.Reconfigure();
            RefreshIcon();
            Overlay.Flash(engine.Layout);
        }

        void WatchConfig()
        {
            reload.Interval = 400;
            reload.Tick += delegate { reload.Stop(); ReloadConfig(); };

            // La surveillance du dossier ne signale pas tout (dossier que Windows redirige pour une application
            // empaquetée, lecteur réseau...) : on compare aussi la date du fichier toutes les deux secondes.
            seen = ConfigStamp();
            poll.Interval = 2000;
            poll.Tick += delegate { if (ConfigStamp() != seen) ConfigTouched(); };
            poll.Start();
            try
            {
                string dir = Path.GetDirectoryName(Config.FilePath);
                Directory.CreateDirectory(dir);
                watcher = new FileSystemWatcher(dir, Path.GetFileName(Config.FilePath));
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                FileSystemEventHandler changed = delegate { ui.Post(delegate { ConfigTouched(); }, null); };
                watcher.Changed += changed;
                watcher.Created += changed;
                watcher.Renamed += delegate { ui.Post(delegate { ConfigTouched(); }, null); };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("WatchConfig: " + ex.Message); }
        }

        void ConfigTouched()
        {
            seen = ConfigStamp();
            if (seen == ownWrite) return;   // c'est notre propre enregistrement
            reload.Stop();
            reload.Start();
        }

        void ReloadConfig()
        {
            Config fresh = Config.Load();
            bool wasEnabled = cfg.Enabled;
            bool scopeChanged = fresh.InterceptMaximize != cfg.InterceptMaximize || fresh.InterceptFullscreen != cfg.InterceptFullscreen
                || string.Join(";", fresh.Exclude.ToArray()) != string.Join(";", cfg.Exclude.ToArray());
            foreach (System.Reflection.FieldInfo f in typeof(Config).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                f.SetValue(cfg, f.GetValue(fresh));
            Log.Enabled = cfg.Debug;
            if (cfg.Enabled != wasEnabled) engine.SetEnabled(cfg.Enabled);
            else if (scopeChanged) engine.Reset();
            engine.Reconfigure();
            RegisterHotkeys();
            RefreshIcon();
            Log.Write("Configuration rechargée");
        }

        // ---------- système ----------

        static bool IsElevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool StartsWithWindows()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("Split") != null;
        }

        static void SetStartsWithWindows(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("Split", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("Split", false);
            }
        }

        void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--wait");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Quit();
            }
            catch (System.ComponentModel.Win32Exception) { }   // demande UAC refusée
        }

        void Quit()
        {
            poll.Stop();
            reload.Stop();
            if (watcher != null) watcher.Dispose();
            hotkeys.Dispose();
            engine.Dispose();
            icon.Visible = false;
            icon.Dispose();
            if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
            ExitThread();
        }
    }
}
