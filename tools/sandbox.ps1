# Lance un script de test sur un bureau Windows separe et cache : ses fenetres sont invisibles, ne peuvent pas
# prendre le focus et ne recoivent aucun clic. Une instance de Split dediee y tourne (configuration tools\test.ini
# copiee dans %TEMP%\split-sandbox, journal au meme endroit) ; l'instance normale n'est pas touchee.
#
#   powershell -ExecutionPolicy Bypass -File tools\sandbox.ps1 -Script tools\test-ghost.ps1 -ScriptArgs "-Scenario full -X 2400"
#   powershell -ExecutionPolicy Bypass -File tools\sandbox.ps1 -Script tools\test-chrome.ps1
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [string]$ScriptArgs = "",
    [string]$Ini = "$PSScriptRoot\test.ini",
    [string]$Exe = "",         # autre executable que Split.exe (essayer une compilation sans remplacer celui qui tourne)
    [int]$TimeoutSec = 90,
    [switch]$NoSplit,          # observer une application seule, sans Split
    [switch]$ShowLog
)
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class Sandbox {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct STARTUPINFO { public int cb; public string reserved, desktop, title; public int x, y, xs, ys, xc, yc, fill, flags; public short show, r2; public IntPtr r3, i, o, e; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int pid, tid; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static IntPtr Create(string name) {
        IntPtr h = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (h == IntPtr.Zero) throw new Exception("CreateDesktop " + Marshal.GetLastWin32Error());
        return h;
    }
    // Les processus lances par celui-ci heritent du meme bureau.
    public static int Start(string desktop, string cmdline) {
        STARTUPINFO si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.desktop = "WinSta0\\" + desktop;
        PROCESS_INFORMATION pi;
        if (!CreateProcess(null, new StringBuilder(cmdline), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi)) throw new Exception("CreateProcess " + Marshal.GetLastWin32Error());
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        return pi.pid;
    }
}
"@
$desktop = "SplitSandbox"
$work = Join-Path $env:TEMP "split-sandbox"
New-Item -ItemType Directory -Force $work | Out-Null
$split = if ($Exe) { (Resolve-Path $Exe).Path } else { Join-Path (Split-Path $PSScriptRoot -Parent) "Split.exe" }
$testIni = Join-Path $work "test.ini"
Copy-Item $Ini $testIni -Force
$log = Join-Path $work "split.log"
if (Test-Path $log) { [IO.File]::Delete($log) }
$out = Join-Path $work "output.txt"
if (Test-Path $out) { [IO.File]::Delete($out) }
$scriptPath = (Resolve-Path $Script).Path

$handle = [Sandbox]::Create($desktop)
try {
    if (-not $NoSplit) { [Sandbox]::Start($desktop, "`"$split`" --config `"$testIni`"") | Out-Null; Start-Sleep -Milliseconds 1500 }
    $testPid = [Sandbox]::Start($desktop, "cmd.exe /c powershell -NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" $ScriptArgs > `"$out`" 2>&1")
    $p = Get-Process -Id $testPid -ErrorAction SilentlyContinue
    if ($p -and -not $p.WaitForExit($TimeoutSec * 1000)) { "DELAI DEPASSE : arret du test"; Stop-Process -Id $testPid -Force -ErrorAction SilentlyContinue }
}
finally {
    if (-not $NoSplit) { [Sandbox]::Start($desktop, "`"$split`" --quit") | Out-Null; Start-Sleep -Milliseconds 900 }
    # Ce qui serait reste ouvert apres un script interrompu : navigateur de test (profil temporaire du bac a sable)
    # et instance de Split du bac a sable.
    Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and (($_.CommandLine -match 'split-sandbox\\browser-profile' -and $_.CommandLine -notmatch '--type=') -or $_.CommandLine -match 'split-sandbox\\test\.ini') } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    [Sandbox]::CloseDesktop($handle) | Out-Null
}
Get-Content $out -ErrorAction SilentlyContinue
if ($ShowLog) { "--- journal de Split ---"; Get-Content $log -Encoding UTF8 -ErrorAction SilentlyContinue }
