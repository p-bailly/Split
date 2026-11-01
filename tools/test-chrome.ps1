# Verifie le plein ecran d'un navigateur Chromium (Chrome, sinon Edge) avec Split. A lancer DANS LE BAC A SABLE :
#
#   powershell -ExecutionPolicy Bypass -File tools\sandbox.ps1 -Script tools\test-chrome.ps1 -ScriptArgs "-X 200"     (moitie gauche)
#   powershell -ExecutionPolicy Bypass -File tools\sandbox.ps1 -Script tools\test-chrome.ps1 -ScriptArgs "-X 2300"    (moitie droite)
#
# Le script ouvre un navigateur isole (profil temporaire, pilote par DevTools) avec trois fenetres : W dans la moitie
# testee, B et C dans l'autre. W passe en plein ecran, puis les fenetres s'activent tour a tour. Le rectangle de W est
# echantillonne : toute ligne plus large que la moitie est un debordement (duree = ecart avec la ligne suivante).
# Attendu : W revient dans sa moitie apres chaque changement de focus qui la concerne, et ne bouge pas quand B et C
# s'activent entre elles.
# Ne pas lancer hors du bac a sable : les fenetres seraient visibles et prendraient le focus.
param([int]$X = 200, [int]$Port = 9377,
      [string]$Split = "50/50",   # repartition attendue ; pour trois zones : -Split 25/50/25 avec sandbox.ps1 -Ini tools\test3.ini
      [switch]$Taskbar,       # ajoute une fausse barre des taches : verifie aussi que W ne reste pas "toujours visible"
      [switch]$Yield)         # ... qui s'efface devant un plein ecran actif, comme la vraie
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Web.Extensions @"
using System; using System.Collections.Generic; using System.Diagnostics; using System.Net.WebSockets; using System.Runtime.InteropServices;
using System.Text; using System.Threading; using System.Web.Script.Serialization;
public static class Rig {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint tid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder info, int len, out int needed);

    public static string DesktopName() { var sb = new StringBuilder(256); int n; GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, sb, 512, out n); return sb.ToString(); }
    public static List<IntPtr> Windows(int pid) {
        var res = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint p; GetWindowThreadProcessId(h, out p); if (p != pid) return true;
            var c = new StringBuilder(64); GetClassName(h, c, 64);
            if (c.ToString() == "Chrome_WidgetWin_1" && (GetLong(h, -16).ToInt64() & 0x10000000) != 0 && (GetLong(h, -20).ToInt64() & 0x80) == 0) res.Add(h);
            return true; }, IntPtr.Zero);
        return res;
    }
    public static string R(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T); }
    // Commande Split (mode debug) sur une fenetre donnee : 0 gauche, 1 droite, 2 agrandir, 3 restaurer, 4 plein ecran
    public static void SplitCmd(int cmd, IntPtr target) { PostMessage(FindWindow(null, "Split.Hotkeys"), RegisterWindowMessage("Split.Test"), new IntPtr(cmd), target); }

    static ClientWebSocket ws; static int nextId = 1; static JavaScriptSerializer js = new JavaScriptSerializer();
    public static void Connect(string url) { ws = new ClientWebSocket(); if (!ws.ConnectAsync(new Uri(url), CancellationToken.None).Wait(8000)) throw new Exception("DevTools : connexion impossible"); }
    public static Dictionary<string, object> Cdp(string method, string paramsJson) {
        int id = nextId++;
        string msg = "{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + (string.IsNullOrEmpty(paramsJson) ? "{}" : paramsJson) + "}";
        if (!ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000)) throw new Exception("DevTools : envoi bloque");
        byte[] buf = new byte[1 << 16];
        while (true) {
            var sb = new StringBuilder(); WebSocketReceiveResult r;
            do { var t = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (!t.Wait(8000)) throw new Exception("DevTools : pas de reponse a " + method); r = t.Result; sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
            var d = js.Deserialize<Dictionary<string, object>>(sb.ToString());
            if (d.ContainsKey("error") && !d.ContainsKey("id")) throw new Exception("DevTools : " + sb);
            if (d.ContainsKey("id") && Convert.ToInt32(d["id"]) == id) return d;
        }
    }

    // Fausse barre des taches (le bureau cache n'a pas d'Explorateur) : fenetre "toujours visible" de la meme classe,
    // en bas de l'ecran. Avec yield, elle imite la vraie : elle passe tout en bas de la pile tant que la fenetre
    // active est un plein ecran sans bordure, et revient devant ensuite.
    delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WNDCLASS { public uint style; public WndProc proc; public int clsExtra, wndExtra; public IntPtr instance, icon, cursor, background; public string menu, name; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WNDCLASS c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string n);
    static WndProc proc = DefWindowProc; public static IntPtr Bar;
    public static bool Top(IntPtr h) { return (GetLong(h, -20).ToInt64() & 8) != 0; }
    public static void StartTaskbar(int x, int y, int w, int h, bool yield) {
        var ready = new ManualResetEvent(false);
        var t = new Thread(delegate() {
            WNDCLASS c = new WNDCLASS(); c.proc = proc; c.instance = GetModuleHandle(null); c.name = "Shell_TrayWnd";
            RegisterClass(ref c);
            Bar = CreateWindowEx(0x88, "Shell_TrayWnd", "", 0x90000000, x, y, w, h, IntPtr.Zero, IntPtr.Zero, c.instance, IntPtr.Zero);
            ready.Set();
            MSG m; while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) DispatchMessage(ref m);
        });
        t.IsBackground = true; t.Start(); ready.WaitOne(3000);
        if (!yield) return;
        var s = new Thread(delegate() {
            while (true) {
                IntPtr fg = GetForegroundWindow(); RECT r = new RECT(); bool rude = false;
                if (fg != IntPtr.Zero && fg != Bar && GetWindowRect(fg, out r))
                    rude = (GetLong(fg, -16).ToInt64() & 0x00C00000) != 0x00C00000 && r.R - r.L >= w / 2 - 8 && r.B - r.T >= y + h - 2;
                if (rude && Top(Bar)) SetWindowPos(Bar, new IntPtr(1), 0, 0, 0, 0, 0x13);          // HWND_BOTTOM
                else if (!rude && !Top(Bar)) SetWindowPos(Bar, new IntPtr(-1), 0, 0, 0, 0, 0x13);  // HWND_TOPMOST
                Thread.Sleep(25);
            }
        });
        s.IsBackground = true; s.Start();
    }

    static Thread sampler; static volatile bool sampling; public static List<string> Samples = new List<string>();
    public static void StartSampling(IntPtr h) {
        Samples.Clear(); sampling = true; var sw = Stopwatch.StartNew();
        sampler = new Thread(delegate() { string last = ""; while (sampling) { string s = R(h); if (s != last) { last = s; lock (Samples) Samples.Add(sw.Elapsed.TotalMilliseconds.ToString("0.0").PadLeft(9) + " ms  " + s); } Thread.SpinWait(2000); } });
        sampler.IsBackground = true; sampler.Start();
    }
    public static void Mark(string s) { lock (Samples) Samples.Add("          --- " + s); }
    public static void StopSampling() { sampling = false; if (sampler != null) sampler.Join(500); }
}
"@
if ([Rig]::DesktopName() -eq "Default") { Write-Host "A lancer avec tools\sandbox.ps1 (bureau cache), pas sur le bureau de travail."; exit 1 }

$exe = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
         "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) { Write-Host "Aucun navigateur Chromium trouve."; exit 1 }
$profileDir = Join-Path $env:TEMP "split-sandbox\browser-profile"
$psi = New-Object System.Diagnostics.ProcessStartInfo $exe, "--user-data-dir=`"$profileDir`" --remote-debugging-port=$Port --no-first-run --no-default-browser-check --no-startup-window"
$psi.UseShellExecute = $false                        # creation directe : le navigateur reste sur ce bureau
$psi.RedirectStandardError = $true                   # ses messages de diagnostic ne nous interessent pas
$browser = [System.Diagnostics.Process]::Start($psi)
$browser.BeginErrorReadLine()
$ver = $null
for ($i = 0; $i -lt 60 -and -not $ver; $i++) { try { $ver = Invoke-RestMethod "http://127.0.0.1:$Port/json/version" -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 200 } }
if (-not $ver) { Write-Host "DevTools injoignable"; exit 1 }
[Rig]::Connect($ver.webSocketDebuggerUrl)

function New-Window([int]$left, [int]$top) {
    $before = [Rig]::Windows($browser.Id)
    $t = [Rig]::Cdp("Target.createTarget", "{`"url`":`"about:blank`",`"newWindow`":true,`"background`":true,`"left`":$left,`"top`":$top,`"width`":900,`"height`":600}")
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 100 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 50; $h = [Rig]::Windows($browser.Id) | Where-Object { $before -notcontains $_ } | Select-Object -First 1; if (-not $h) { $h = [IntPtr]::Zero } }
    $id = ([Rig]::Cdp("Browser.getWindowForTarget", "{`"targetId`":`"$($t.result.targetId)`"}")).result.windowId
    return @{ Hwnd = $h; Id = $id }
}
# Active la fenetre, et la designe a Split comme fenetre active : sur un bureau cache, Windows n'en annonce aucune.
function Activate($win) { [Rig]::SetForegroundWindow($win.Hwnd) | Out-Null; [Rig]::SplitCmd(5, $win.Hwnd) }
function Set-Fullscreen($win, [bool]$on) { [Rig]::Cdp("Browser.setWindowBounds", "{`"windowId`":$($win.Id),`"bounds`":{`"windowState`":`"$(if ($on) { 'fullscreen' } else { 'normal' })`"}}") | Out-Null }

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
# Zones attendues, d'apres la repartition donnee (la meme que dans le fichier .ini passe a sandbox.ps1)
$parts = @($Split -split '/' | ForEach-Object { [int]$_ }); $total = ($parts | Measure-Object -Sum).Sum
$zones = @(); $acc = 0; $edge = $screen.Left
for ($i = 0; $i -lt $parts.Count; $i++) {
    $acc += $parts[$i]
    $right = if ($i -eq $parts.Count - 1) { $screen.Right } else { $screen.Left + [int][math]::Round($screen.Width * $acc / $total) }
    $zones += ,@($edge, ($right - $edge)); $edge = $right
}
$wz = 0; for ($i = 0; $i -lt $zones.Count; $i++) { if ($X + 450 -ge $zones[$i][0]) { $wz = $i } }     # zone de W (d'apres son milieu)
$oz = if ($wz -lt $zones.Count - 1) { $wz + 1 } else { $wz - 1 }                                    # zone voisine, pour B et C
$ox = $zones[$oz][0] + 30
$w = New-Window $X 150; $b = New-Window $ox 150; $c = New-Window ($ox + 200) 350
$inZone = "^$($zones[$wz][0]),0 $($zones[$wz][1])x"    # W dans sa zone (hauteur de l'ecran, ou 1 pixel de moins)
$inOther = "^$($zones[$oz][0]),0 $($zones[$oz][1])x"
$toOther = if ($oz -gt $wz) { 1 } else { 0 }           # commande Split : 1 = vers la droite, 0 = vers la gauche
$results = New-Object System.Collections.Generic.List[string]
function Step([string]$label, [scriptblock]$action, [string]$expect) {
    [Rig]::Mark($label); & $action; Start-Sleep -Milliseconds 600
    $r = [Rig]::R($w.Hwnd)
    $z = if ($Taskbar) { "  " + $(if ([Rig]::Top($w.Hwnd)) { "toujours-visible" } else { "normale" }) + $(if ([Rig]::Top([Rig]::Bar)) { "" } else { ", barre effacee" }) +
        $(if ($d -and [Rig]::Top($d.Hwnd)) { ", D TOUJOURS-VISIBLE" } else { "" }) + $(if ([Rig]::Top($b.Hwnd)) { ", B TOUJOURS-VISIBLE" } else { "" }) } else { "" }
    $results.Add(("{0,-6} {1,-46} {2}{3}" -f $(if ($r -match $expect) { "OK" } else { "ECHEC" }), $label, $r, $z))
}
if ($Taskbar) { [Rig]::StartTaskbar($screen.Left, $screen.Bottom - 40, $screen.Width, 40, [bool]$Yield); Start-Sleep -Milliseconds 1300 }   # Split relit la liste des barres chaque seconde
Activate $w; Start-Sleep -Milliseconds 300
$start = [Rig]::R($w.Hwnd)
[Rig]::StartSampling($w.Hwnd)
Step "W passe en plein ecran"            { Set-Fullscreen $w $true }                          $inZone
Step "B activee (W perd le focus)"       { Activate $b }   $inZone
Step "C activee"                         { Activate $c }   $inZone
Step "B activee"                         { Activate $b }   $inZone
Step "W reactivee"                       { Activate $w }   $inZone
Step "B activee (W perd le focus)"       { Activate $b }   $inZone
Step "W reactivee"                       { Activate $w }   $inZone
Step "raccourci : W dans la zone voisine" { [Rig]::SplitCmd($toOther, $w.Hwnd) }             $inOther
Step "raccourci : W revient"             { [Rig]::SplitCmd(1 - $toOther, $w.Hwnd) }          $inZone
Step "W sort du plein ecran"             { Set-Fullscreen $w $false }                         ("^" + [regex]::Escape($start) + "$")
if ($Taskbar) {
    # Un second plein ecran, quitte pendant que W n'a pas le focus, puis un troisieme, quitte aussitot apres l'avoir repris
    Step "W repasse en plein ecran"          { Set-Fullscreen $w $true }                          $inZone
    Step "B activee (W perd le focus)"       { Activate $b }   $inZone
    Step "W sort du plein ecran sans focus"  { Set-Fullscreen $w $false }                         ("^" + [regex]::Escape($start) + "$")
    Step "W reactivee, plein ecran"          { Activate $w; Set-Fullscreen $w $true }   $inZone
    Step "B activee (W perd le focus)"       { Activate $b }   $inZone
    [Rig]::Mark("W reactivee et sortie aussitot"); Activate $w; Start-Sleep -Milliseconds 60
    Step "W sort du plein ecran aussitot"    { Set-Fullscreen $w $false }                         ("^" + [regex]::Escape($start) + "$")
    # Une autre fenetre D, dans la meme moitie que W, s'active par-dessus W en plein ecran (comme une fenetre ancree
    # sur laquelle on clique pendant une video) : W doit passer derriere, et D ne doit pas devenir "toujours visible"
    $d = New-Window ($X + 150) 250
    Step "W en plein ecran, D ouverte par-dessus" { Activate $w; Set-Fullscreen $w $true } $inZone
    Step "D activee par-dessus W"            { Activate $d }   $inZone
    Step "W reactivee"                       { Activate $w }   $inZone
    Step "D activee par-dessus W"            { Activate $d }   $inZone
    Step "B activee (autre moitie)"          { Activate $b }   $inZone
    Step "D activee par-dessus W"            { Activate $d }   $inZone
    Step "W sort du plein ecran sans focus"  { Set-Fullscreen $w $false }                         ("^" + [regex]::Escape($start) + "$")
    Start-Sleep -Milliseconds 1500
    $results.Add(("{0,-6} {1,-46} {2}" -f $(if ([Rig]::Top($d.Hwnd) -or [Rig]::Top($b.Hwnd)) { "ECHEC" } else { "OK" }), "aucune autre fenetre n'est `"toujours visible`"", $(if ([Rig]::Top($d.Hwnd)) { "D toujours-visible" } elseif ([Rig]::Top($b.Hwnd)) { "B toujours-visible" } else { "normales" })))
    $results.Add(("{0,-6} {1,-46} {2}" -f $(if ([Rig]::Top($w.Hwnd)) { "ECHEC" } else { "OK" }), "W n'est plus `"toujours visible`" a la fin", $(if ([Rig]::Top($w.Hwnd)) { "toujours-visible" } else { "normale" })))
}
[Rig]::StopSampling()

$results | ForEach-Object { Write-Host $_ }
Write-Host ""
Write-Host "Rectangle de W au fil du temps :"
[Rig]::Samples | ForEach-Object { Write-Host $_ }
try { [Rig]::Cdp("Browser.close", $null) | Out-Null } catch { }
Start-Sleep -Milliseconds 800
Stop-Process -Id $browser.Id -Force -ErrorAction SilentlyContinue
