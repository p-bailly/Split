using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Split
{
    internal sealed class Config
    {
        public bool Enabled = true;
        // Largeur de chaque « écran », en %, de gauche à droite : 50/50, 60/40... ou trois zones (25/50/25)
        public List<int> Parts = new List<int>(new int[] { 50, 50 });
        public string Monitor = "auto";             // "auto" ou nom de périphérique (\\.\DISPLAY1)
        public bool InterceptMaximize = true;
        public bool InterceptFullscreen = true;
        public bool CoverTaskbar = true;
        public string NewFullscreenZone = "cursor"; // cursor | left | right | center
        public bool AutoLockCursor = false;
        public bool SkipExclusive = true;
        public bool Notifications = true;
        public bool Debug = false;
        public string Trace = "";                   // nom de processus à suivre en détail dans le journal
        public bool FirstRun = true;
        public List<string> Exclude = new List<string>();
        // Moitié préférée par application pour le plein écran (retenue quand on déplace un jeu avec le raccourci)
        public Dictionary<string, int> Remember = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string HotkeyLeft = "Ctrl+Alt+Left";
        public string HotkeyRight = "Ctrl+Alt+Right";
        public string HotkeyMaximize = "Ctrl+Alt+Up";
        public string HotkeyRestore = "Ctrl+Alt+Down";
        public string HotkeyFullscreen = "Ctrl+Alt+Enter";
        public string HotkeyLockCursor = "Ctrl+Alt+L";
        public string HotkeyToggle = "Ctrl+Alt+S";

        static readonly string[] DefaultExclude = {
            "ShareX", "Greenshot", "Lightshot", "Flameshot", "SnippingTool", "ScreenClippingHost",
            "ScreenSketch", "ZoomIt", "ZoomIt64", "Magnify", "LockApp", "LogonUI",
            "PowerToys.FancyZonesEditor", "PowerToys.ColorPickerUI", "PowerToys.MeasureToolUI"
        };

        // Dossier de la configuration et du journal : %APPDATA%\Split, ou celui du fichier donné par --config.
        public static string Dir
        {
            get
            {
                if (OverridePath != null) return Path.GetDirectoryName(Path.GetFullPath(OverridePath));
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Split");
            }
        }

        public static string OverridePath;   // option --config (tests) : instance indépendante de l'instance normale

        public static string FilePath { get { return OverridePath ?? Path.Combine(Dir, "config.ini"); } }

        public bool IsExcluded(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            foreach (string e in Exclude)
                if (string.Equals(e, processName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Config Load()
        {
            Config c = new Config();
            c.Exclude.AddRange(DefaultExclude);
            try
            {
                if (!File.Exists(FilePath)) { c.Save(); return c; }
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    c.Apply(line.Substring(0, eq).Trim().ToLowerInvariant(), line.Substring(eq + 1).Trim());
                }
            }
            catch (Exception ex) { Log.Write("Config.Load: " + ex.Message); }
            return c;
        }

        bool splitSeen;   // « split » l'emporte sur l'ancienne clé « ratio », quel que soit l'ordre des lignes

        public string SplitText { get { return string.Join("/", Parts); } }

        public string SplitLabel { get { return string.Join(" / ", Parts); } }

        // « 50/50 », « 60/40 », « 25/50/25 »... : deux à quatre largeurs en %, ramenées à un total de 100.
        public static List<int> ParseSplit(string text)
        {
            List<int> parts = new List<int>();
            foreach (string p in text.Split(new char[] { '/', ',', ';', '-', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (!int.TryParse(p.Trim(), out n) || n <= 0) return null;
                parts.Add(n);
            }
            if (parts.Count < 2 || parts.Count > 4) return null;
            int total = 0;
            foreach (int n in parts) total += n;
            int sum = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i] = i == parts.Count - 1 ? 100 - sum : (int)Math.Round(parts[i] * 100.0 / total);
                sum += parts[i];
                if (parts[i] < 10) return null;   // une zone de moins de 10 % de l'écran ne sert à rien
            }
            return parts;
        }

        void Apply(string key, string v)
        {
            switch (key)
            {
                case "enabled": Enabled = Bool(v, Enabled); break;
                case "split":
                    List<int> parsed = ParseSplit(v);
                    if (parsed != null) { Parts = parsed; splitSeen = true; }
                    break;
                case "ratio":   // ancienne clé : largeur de la zone gauche, en %
                    int r;
                    if (!splitSeen && int.TryParse(v, out r)) Parts = new List<int>(new int[] { Math.Max(15, Math.Min(85, r)), 100 - Math.Max(15, Math.Min(85, r)) });
                    break;
                case "monitor": if (v.Length > 0) Monitor = v; break;
                case "interceptmaximize": InterceptMaximize = Bool(v, InterceptMaximize); break;
                case "interceptfullscreen": InterceptFullscreen = Bool(v, InterceptFullscreen); break;
                case "covertaskbar": CoverTaskbar = Bool(v, CoverTaskbar); break;
                case "newfullscreenzone": NewFullscreenZone = v.ToLowerInvariant(); break;
                case "autolockcursor": AutoLockCursor = Bool(v, AutoLockCursor); break;
                case "skipexclusive": SkipExclusive = Bool(v, SkipExclusive); break;
                case "notifications": Notifications = Bool(v, Notifications); break;
                case "debug": Debug = Bool(v, Debug); break;
                case "trace": Trace = v; break;
                case "firstrun": FirstRun = Bool(v, FirstRun); break;
                case "exclude":
                    Exclude.Clear();
                    foreach (string p in v.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string name = p.Trim();
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                        if (name.Length > 0) Exclude.Add(name);
                    }
                    break;
                case "remember":
                    Remember.Clear();
                    foreach (string p in v.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int colon = p.LastIndexOf(':');
                        int zone;
                        if (colon > 0 && int.TryParse(p.Substring(colon + 1).Trim(), out zone) && zone >= 1 && zone <= 4)
                            Remember[p.Substring(0, colon).Trim()] = zone - 1;
                    }
                    break;
                case "hotkeyleft": HotkeyLeft = v; break;
                case "hotkeyright": HotkeyRight = v; break;
                case "hotkeymaximize": HotkeyMaximize = v; break;
                case "hotkeyrestore": HotkeyRestore = v; break;
                case "hotkeyfullscreen": HotkeyFullscreen = v; break;
                case "hotkeylockcursor": HotkeyLockCursor = v; break;
                case "hotkeytoggle": HotkeyToggle = v; break;
            }
        }

        static bool Bool(string v, bool fallback)
        {
            v = v.ToLowerInvariant();
            if (v == "true" || v == "1" || v == "oui" || v == "yes") return true;
            if (v == "false" || v == "0" || v == "non" || v == "no") return false;
            return fallback;
        }

        static string B(bool b) { return b ? "true" : "false"; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> l = new List<string>();
                l.Add("; Split - configuration (rechargée automatiquement à chaque enregistrement)");
                l.Add("enabled=" + B(Enabled));
                l.Add("; Largeur de chaque écran en %, de gauche à droite : 50/50, 60/40... ou trois écrans : 25/50/25");
                l.Add("split=" + SplitText);
                l.Add("; Écran à diviser : auto (le plus large) ou un nom comme \\\\.\\DISPLAY1");
                l.Add("monitor=" + Monitor);
                l.Add("; Agrandir (bouton Agrandir, double-clic, Win+Haut) remplit la moitié au lieu de tout l'écran");
                l.Add("interceptMaximize=" + B(InterceptMaximize));
                l.Add("; Plein écran (F11, vidéos, jeux sans bordure) limité à la moitié");
                l.Add("interceptFullscreen=" + B(InterceptFullscreen));
                l.Add("; Le plein écran recouvre la barre des tâches de sa moitié");
                l.Add("coverTaskbar=" + B(CoverTaskbar));
                l.Add("; Écran utilisé quand une fenêtre démarre directement en plein écran : cursor, left, right ou center");
                l.Add("newFullscreenZone=" + NewFullscreenZone);
                l.Add("; Bloquer la souris dans la moitié tant qu'une fenêtre plein écran y est active");
                l.Add("autoLockCursor=" + B(AutoLockCursor));
                l.Add("; Ne pas toucher aux jeux en plein écran exclusif (impossible à diviser)");
                l.Add("skipExclusive=" + B(SkipExclusive));
                l.Add("; Applications ignorées (noms de processus sans .exe, séparés par ;)");
                l.Add("exclude=" + string.Join(";", Exclude.ToArray()));
                l.Add("; Écran préféré (1 = le plus à gauche, 2 = le suivant...) des applications lancées directement en plein écran");
                List<string> mem = new List<string>();
                foreach (KeyValuePair<string, int> kv in Remember) mem.Add(kv.Key + ":" + (kv.Value + 1));
                l.Add("remember=" + string.Join(";", mem.ToArray()));
                l.Add("");
                l.Add("; Raccourcis : Ctrl, Alt, Shift, Win + touche (Left, Right, Up, Down, Enter, F1..F24, A..Z...). Vide = désactivé");
                l.Add("hotkeyLeft=" + HotkeyLeft);
                l.Add("hotkeyRight=" + HotkeyRight);
                l.Add("hotkeyMaximize=" + HotkeyMaximize);
                l.Add("hotkeyRestore=" + HotkeyRestore);
                l.Add("hotkeyFullscreen=" + HotkeyFullscreen);
                l.Add("hotkeyLockCursor=" + HotkeyLockCursor);
                l.Add("hotkeyToggle=" + HotkeyToggle);
                l.Add("");
                l.Add("; Bulles d'information près de l'horloge");
                l.Add("notifications=" + B(Notifications));
                l.Add("; Journal de diagnostic dans split.log");
                l.Add("debug=" + B(Debug));
                l.Add("trace=" + Trace);
                l.Add("firstRun=" + B(FirstRun));
                File.WriteAllLines(FilePath, l.ToArray(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.Write("Config.Save: " + ex.Message); }
        }
    }

    internal static class Log
    {
        public static bool Enabled;
        static readonly object Gate = new object();

        public static void Write(string msg)
        {
            if (!Enabled) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Config.Dir);
                    string path = Path.Combine(Config.Dir, "split.log");
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
