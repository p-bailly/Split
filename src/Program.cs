using System;
using System.Threading;
using System.Windows.Forms;

namespace Split
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (Array.IndexOf(args, "--quit") >= 0)
            {
                // Demande aux instances en cours (sur ce bureau) de se fermer proprement : elles rendent bordures,
                // premier plan et souris.
                IntPtr running = IntPtr.Zero;
                for (int i = 0; i < 16; i++)
                {
                    running = Native.FindWindowEx(IntPtr.Zero, running, null, Hotkeys.WindowTitle);
                    if (running == IntPtr.Zero) break;
                    Native.PostMessage(running, Hotkeys.QuitMessage, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }
            bool wait = Array.IndexOf(args, "--wait") >= 0;   // relance en administrateur : l'ancienne instance se ferme
            int cfgArg = Array.IndexOf(args, "--config");
            if (cfgArg >= 0 && cfgArg + 1 < args.Length) Config.OverridePath = args[cfgArg + 1];
            // Une instance de test (--config) ne gêne pas l'instance normale : verrou et journal séparés.
            string lockName = "Split.SingleInstance.7c1f0c4e";
            if (Config.OverridePath != null)
                lockName += "." + System.IO.Path.GetFullPath(Config.OverridePath).ToLowerInvariant().GetHashCode().ToString("x");
            bool mine;
            using (Mutex mutex = new Mutex(true, lockName, out mine))
            {
                if (!mine && wait)
                {
                    try { mine = mutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { mine = true; }
                }
                if (!mine)
                {
                    MessageBox.Show("Split est déjà lancé : son icône se trouve près de l'horloge.", "Split",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Enabled = true;
                    Log.Write("Erreur : " + e.Exception);
                };
                Application.Run(new Tray());
                mutex.ReleaseMutex();
            }
        }
    }
}
