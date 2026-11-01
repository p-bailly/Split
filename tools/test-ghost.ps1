# Fenetre de test "fantome" pour verifier Split sans rien montrer a l'ecran ni prendre le focus :
# jamais activee, reduite a 1 pixel par une region, placee en bas de la pile.
#
# Le plus simple, dans le bac a sable (bureau cache, instance de Split dediee) :
#        powershell -ExecutionPolicy Bypass -File tools\sandbox.ps1 -Script tools\test-ghost.ps1 -ScriptArgs "-Scenario full -X 2400"
# Ou sur le bureau de travail, avec une instance de test lancee a la main (aucun raccourci global, journal active) :
#        Split.exe --config tools\test.ini
#        powershell -ExecutionPolicy Bypass -File tools\test-ghost.ps1 -Scenario full -X 2400
# Scenarios : full (plein ecran puis retour), stubborn (reimpose sa taille), adopt (sans bordure, deja a la
# taille d'une moitie), centered (idem mais au milieu de l'ecran, comme un jeu), centeredcmd (fenetre ordinaire
# qui devient ainsi, puis raccourci "droite"), stray (deja au milieu avant de prendre la taille d'une moitie),
# recenter (plein ecran reduit a une moitie, puis le "jeu" change de resolution et se recentre), fighter et
# fighter2 (revient au milieu a chaque fois, tout de suite ou juste apres : Split doit renoncer), drag (deplacee
# au clavier comme a la souris vers l'autre moitie), snap (ancree par Windows en debordant sur l'autre moitie, puis
# redimensionnee a la main), snapv (etiree en hauteur seulement), resplit (passage de deux a trois zones pendant un
# plein ecran), sticky (se replace toute seule), max (agrandir deux fois), cmd (commandes clavier).
# Trois zones : sandbox.ps1 -Ini tools\test3.ini (les rectangles attendus sont alors ceux des zones 25 / 50 / 25).
# Le script affiche ce que la fenetre a subi ; split.log (a cote du fichier de configuration) dit ce que Split a decide.
param([string]$Scenario = "full", [int]$X = 300, [int]$StayMs = 3000, [string]$Out = "$PSScriptRoot\ghost.log")
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @"
using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing; using System.IO;
using System.Runtime.InteropServices; using System.Text; using System.Windows.Forms;
public class Ghost : Form {
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr h, string name, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);

    readonly string scenario, outPath; readonly int stay;
    readonly List<string> lines = new List<string>(); readonly Stopwatch sw = Stopwatch.StartNew();
    readonly Timer timer = new Timer(); readonly Rectangle normal, full, half, centered;
    string phase = "normal", last = ""; int cmdStep, recenters; bool busy;

    public Ghost(string scenario, int x, int stay, string outPath) {
        this.scenario = scenario; this.stay = stay; this.outPath = outPath;
        Text = "SplitGhost"; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        normal = new Rectangle(x, 200, 800, 500); full = Screen.PrimaryScreen.Bounds;
        half = new Rectangle(full.Left + full.Width / 2, full.Top, full.Width / 2, full.Height);
        centered = new Rectangle(full.Left + full.Width / 4, full.Top, full.Width / 2, full.Height);
        if (scenario == "adopt" || scenario == "sticky" || scenario == "drag" || scenario == "resplit") { phase = "zone"; FormBorderStyle = FormBorderStyle.None; Bounds = half; }
        else if (scenario == "centered" || scenario == "fighter" || scenario == "fighter2") { phase = "zone"; FormBorderStyle = FormBorderStyle.None; Bounds = centered; }
        else if (scenario == "stray") { phase = "zone"; FormBorderStyle = FormBorderStyle.None; Bounds = new Rectangle(centered.X, centered.Y, centered.Width - 20, centered.Height - 80); }
        else Bounds = normal;
        SizeChanged += delegate { if (this.scenario == "stubborn" && phase == "full" && Width != full.Width) Bounds = full; };
        // "Jeu" qui tient a rester au milieu de l'ecran : dans la foulee (fighter) ou juste apres (fighter2)
        LocationChanged += delegate {
            if (busy || Left == centered.Left || !IsHandleCreated) return;
            if (this.scenario == "fighter") { recenters++; busy = true; Bounds = centered; busy = false; }
            else if (this.scenario == "fighter2") { recenters++; BeginInvoke((MethodInvoker)delegate { Bounds = centered; }); }
        };
        timer.Interval = 100; timer.Tick += delegate { Step(); };
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetWindowRgn(Handle, CreateRectRgn(0, 0, 1, 1), false); }

    public void Go() {
        Show();                                                              // sans activation
        SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // HWND_BOTTOM
        timer.Start(); Application.Run();
    }
    void FakeMaximize() {   // imite l'etat "agrandi" sans activer la fenetre
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        SetLong(Handle, -16, new IntPtr(GetLong(Handle, -16).ToInt64() | 0x01000000));
        SetWindowPos(Handle, IntPtr.Zero, wa.Left - 8, wa.Top - 8, wa.Width + 16, wa.Height + 16, 0x0004 | 0x0010 | 0x0020);
    }
    void Step() {
        long ms = sw.ElapsedMilliseconds;
        if (scenario == "max") {
            if (phase == "normal" && ms >= 1500) { phase = "max1"; FakeMaximize(); }
            else if (phase == "max1" && ms >= 3500) { phase = "max2"; FakeMaximize(); }
            else if (phase == "max2" && ms >= 5500) { Finish(); return; }
        } else if (scenario == "cmd") {
            // droite, agrandir, gauche, restaurer, plein ecran force, droite, fin du plein ecran
            int[] cmds = { 1, 2, 0, 3, 4, 1, 4 };
            string[] names = { "droite", "agrandir", "gauche", "restaurer", "plein-ecran", "droite", "fin-plein-ecran" };
            if (cmdStep < cmds.Length && ms >= 1500 + 1200 * cmdStep) {
                phase = (cmdStep + 1) + ":" + names[cmdStep];
                PostMessage(FindWindow(null, "Split.Hotkeys"), RegisterWindowMessage("Split.Test"), new IntPtr(cmds[cmdStep]), Handle);
                cmdStep++;
            } else if (cmdStep >= cmds.Length && ms >= 1500 + 1200 * cmdStep) { Finish(); return; }
        } else if (scenario == "sticky") {
            if (phase == "zone" && ms >= 1500) { phase = "moved"; Location = new Point(full.Left, full.Top); }
            else if (phase == "moved" && ms >= 3500) { phase = "exit"; FormBorderStyle = FormBorderStyle.Sizable; Bounds = normal; }
            else if (phase == "exit" && ms >= 5000) { Finish(); return; }
        } else if (scenario == "centeredcmd") {
            // fenetre ordinaire qui devient sans bordure, a la taille d'une moitie mais au milieu de l'ecran : Split la range
            // dans la moitie ou elle se trouvait ; puis raccourci vers l'autre moitie
            if (phase == "normal" && ms >= 1500) { phase = "milieu"; FormBorderStyle = FormBorderStyle.None; Bounds = centered; }
            else if (phase == "milieu" && ms >= 2700) { phase = "autre-cote"; PostMessage(FindWindow(null, "Split.Hotkeys"), RegisterWindowMessage("Split.Test"), new IntPtr(Left >= half.Left ? 0 : 1), Handle); }
            else if (phase == "autre-cote" && ms >= 3900) { Finish(); return; }
        } else if (scenario == "adopt" || scenario == "centered") {
            if (phase == "zone" && ms >= 1500 + stay) { phase = "exit"; FormBorderStyle = FormBorderStyle.Sizable; Bounds = normal; }
            else if (phase == "exit" && ms >= 3500 + stay) { Finish(); return; }
        } else if (scenario == "stray") {
            // sans bordure, au milieu, d'abord a une autre taille (Split n'y touche pas), puis a la taille d'une moitie
            if (phase == "zone" && ms >= 1500) { phase = "taille"; Bounds = centered; }
            else if (phase == "taille" && ms >= 3500) { Finish(); return; }
        } else if (scenario == "recenter") {
            // plein ecran sur tout l'ecran (Split le reduit a une moitie), puis changement de resolution comme un jeu :
            // passage par un etat fenetre, et retour sans bordure a la taille d'une moitie, centre sur l'ecran entier
            if (phase == "normal" && ms >= 1500) { phase = "full"; FormBorderStyle = FormBorderStyle.None; Bounds = full; }
            else if (phase == "full" && ms >= 3000) { phase = "interm"; FormBorderStyle = FormBorderStyle.Sizable; Bounds = new Rectangle(centered.X + 300, 150, 1296, 759); }
            else if (phase == "interm" && ms >= 3300) { phase = "centre"; FormBorderStyle = FormBorderStyle.None; Bounds = centered; }
            else if (phase == "centre" && ms >= 5300) { Finish(); return; }
        } else if (scenario == "fighter" || scenario == "fighter2") {
            if (ms >= 4500) { lines.Add("recentrages : " + recenters); Finish(); return; }
        } else if (scenario == "drag") {
            // deplacement modal de Windows (comme en glissant la fenetre), pilote au clavier : 150 pas vers la gauche, puis Entree
            if (phase == "zone" && ms >= 1500) {
                phase = "glisse";
                PostMessage(Handle, 0x0112, new IntPtr(0xF010), IntPtr.Zero);                       // WM_SYSCOMMAND, SC_MOVE
                for (int i = 0; i < 150; i++) PostMessage(Handle, 0x0100, new IntPtr(0x25), IntPtr.Zero);   // WM_KEYDOWN, VK_LEFT
                PostMessage(Handle, 0x0100, new IntPtr(0x0D), IntPtr.Zero);                         // VK_RETURN
            }
            else if (phase == "glisse" && ms >= 4500) { Finish(); return; }
        } else if (scenario == "snap") {
            // "ancree" par Windows (simule par une propriete que Split lit en mode debug) en debordant sur l'autre moitie :
            // recoupee ; elle regrossit toute seule : recoupee ; puis son bord gauche est tire a la main (redimensionnement
            // modal de Windows, pilote au clavier) : respecte
            if (phase == "normal" && ms >= 1500) { phase = "ancree"; SetProp(Handle, "Split.Arranged", new IntPtr(1)); Bounds = new Rectangle(half.Left - 111, 0, half.Width + 118, full.Height - 33); }
            else if (phase == "ancree" && ms >= 3000) { phase = "grossit"; Bounds = new Rectangle(half.Left - 220, 0, half.Width + 227, full.Height - 33); }
            else if (phase == "grossit" && ms >= 4500) {
                phase = "main";
                PostMessage(Handle, 0x0112, new IntPtr(0xF000), IntPtr.Zero);                       // WM_SYSCOMMAND, SC_SIZE
                for (int i = 0; i < 21; i++) PostMessage(Handle, 0x0100, new IntPtr(0x25), IntPtr.Zero);    // VK_LEFT : bord gauche, puis 20 pas
                PostMessage(Handle, 0x0100, new IntPtr(0x0D), IntPtr.Zero);                         // VK_RETURN
            }
            else if (phase == "main" && ms >= 7000) { Finish(); return; }
        } else if (scenario == "resplit") {
            // la repartition passe de 50/50 a 25/50/25 (fichier de configuration du bac a sable) pendant qu'un "jeu"
            // occupe la moitie droite : il doit prendre la zone centrale, qui a sa taille
            if (phase == "zone" && ms >= 1500) { phase = "trois-zones"; File.AppendAllText(Path.Combine(Path.GetTempPath(), "split-sandbox\\test.ini"), "\r\nsplit=25/50/25\r\n"); }
            else if (phase == "trois-zones" && ms >= 5500) { Finish(); return; }
        } else if (scenario == "snapv") {
            // fenetre large contre le bord gauche, puis etiree en hauteur (meme position, meme largeur) : pas un ancrage de cote
            if (phase == "normal" && ms >= 1500) { phase = "large"; Bounds = new Rectangle(0, 200, 2500, 500); }
            else if (phase == "large" && ms >= 2500) { phase = "etiree"; SetProp(Handle, "Split.Arranged", new IntPtr(1)); Bounds = new Rectangle(0, 0, 2500, full.Height - 33); }
            else if (phase == "etiree" && ms >= 4000) { Finish(); return; }
        } else {
            if (phase == "normal" && ms >= 1500) { phase = "full"; FormBorderStyle = FormBorderStyle.None; Bounds = full; }
            else if (phase == "full" && ms >= 1500 + stay) { phase = "exit"; FormBorderStyle = FormBorderStyle.Sizable; Bounds = normal; }
            else if (phase == "exit" && ms >= 3500 + stay) { Finish(); return; }
        }
        RECT r; GetWindowRect(Handle, out r);
        bool framed = (GetLong(Handle, -16).ToInt64() & 0x00C00000) == 0x00C00000;
        bool top = (GetLong(Handle, -20).ToInt64() & 8) != 0;
        string s = phase + " [" + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T) + "]"
            + (IsZoomed(Handle) ? " agrandie" : "") + (framed ? "" : " sans-cadre") + (top ? " premier-plan" : "");
        if (s != last) { last = s; lines.Add(ms.ToString().PadLeft(6) + " ms  " + s); }
    }
    void Finish() { timer.Stop(); File.WriteAllLines(outPath, lines.ToArray()); Application.ExitThread(); }
}
"@
(New-Object Ghost $Scenario, $X, $StayMs, $Out).Go()
Get-Content $Out
