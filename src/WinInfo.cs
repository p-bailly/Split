using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Split
{
    internal enum Mode
    {
        None,
        Max,      // agrandie dans une moitié
        Full,     // plein écran dans une moitié
        Bypass    // vraie maximisation voulue par l'utilisateur (Maj) : on ne touche pas
    }

    // Ce que Split sait d'une fenêtre de premier niveau.
    internal sealed class WinInfo
    {
        public IntPtr Hwnd;
        public uint Pid;
        public string ClassName = "";
        public string Process = "";
        public bool Shell;                 // surface du système (bureau, barre des tâches...) : jamais touchée

        public bool HasLast;               // dernier rectangle vu à l'état normal
        public RECT Last;
        public bool WasArranged;           // Windows la tenait déjà pour ancrée (Win+Gauche/Droite, glissée contre un bord)
        public bool NoTrim;                // ancrée, elle reprend d'elle-même sa largeur : on ne la recoupe plus

        public Mode Mode;
        public int Zone;
        public int LastZone = -1;          // dernière moitié que Split lui a donnée (agrandie ou plein écran)
        public RECT Expected;              // rectangle où Split a placé la fenêtre

        public bool HasSaved;              // rectangle à retrouver quand on « restaure »
        public RECT Saved;
        public bool HasMax;                // état agrandi conservé pendant un plein écran
        public RECT MaxRect;
        public bool Refit;

        public bool Forced;                // plein écran sans bordure imposé par le raccourci
        public long SavedStyle, SavedExStyle;
        public RECT ForcedRect;

        public bool MadeTopmost, WasTopmost;
        public bool Raised;                // Split l'a passée devant la barre des tâches, et ne l'a pas encore revue à sa place normale
        public bool RaiseStuck;            // la dernière tentative est restée sans effet (pour ne le noter qu'une fois au journal)
        public int FullSince, Fights, Fails, LastApply;
        public bool GivenUp, Warned;
        public bool NoChanging;            // n'accepte sa moitié que si on lui retire son droit de veto
        public bool Stubborn;              // ressort d'elle-même de sa moitié à chaque fois : on ne l'y range plus d'office
        public int StrayTries, StrayWait;  // essais manqués pour la ranger dans une moitié ; pas de nouvel essai avant cet instant

        // Navigateurs Chromium (Chrome, Edge, Brave...) : en plein écran, la fenêtre se redonne elle-même la taille
        // de l'écran physique à chaque changement de focus. Hack = elle est dans son état « arrière-plan ».
        public bool Chromium, Hack, HackKnown;
        public bool Unlisted;              // ne figure plus dans la liste des « plein écran » de son navigateur (voir Engine.Chromium.cs)
        public bool Marked;                // signalée à la barre des tâches comme plein écran actif

        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "MultitaskingViewFrame", "TaskSwitcherWnd", "XamlExplorerHostIslandWindow", "ForegroundStaging",
            "ApplicationManager_DesktopShellWindow", "NotifyIconOverflowWindow", "DV2ControlHost",
            "TaskListThumbnailWnd", "Shell_InputSwitchTopLevelWindow", "EdgeUiInputTopWndClass",
            "LockScreenBackstopFrame", "tooltips_class32", "#32768", "SysShadow", "IME", "MSCTFIME UI"
        };

        public static string ClassOf(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            return Native.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        public static bool IsShellClass(IntPtr hwnd)
        {
            return ShellClasses.Contains(ClassOf(hwnd));
        }

        public static WinInfo Describe(IntPtr hwnd)
        {
            WinInfo w = new WinInfo();
            w.Hwnd = hwnd;
            Native.GetWindowThreadProcessId(hwnd, out w.Pid);
            w.ClassName = ClassOf(hwnd);
            w.Process = ProcessName(w.Pid);
            w.Chromium = w.ClassName.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);

            w.Shell = ShellClasses.Contains(w.ClassName);
            if (!w.Shell && string.Equals(w.Process, "explorer", StringComparison.OrdinalIgnoreCase))
                w.Shell = w.ClassName != "CabinetWClass" && w.ClassName != "ExploreWClass";
            return w;
        }

        static string ProcessName(uint pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (!Native.QueryFullProcessImageName(h, 0, sb, ref size)) return "";
                return Path.GetFileNameWithoutExtension(sb.ToString());
            }
            catch { return ""; }
            finally { Native.CloseHandle(h); }
        }

        public override string ToString()
        {
            return "0x" + Hwnd.ToInt64().ToString("X") + " " + Process + "/" + ClassName;
        }
    }
}
