using System;
using System.Runtime.InteropServices;

namespace Split
{
    // Barre des tâches de Windows : une fenêtre « marquée plein écran » la fait passer derrière tant qu'elle est active
    // (ITaskbarList2::MarkFullscreenWindow), quelle que soit sa taille.
    internal static class Taskbar
    {
        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        class TaskbarList { }

        [ComImport, Guid("602D4995-B13A-429b-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList2
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        }

        static ITaskbarList2 list;
        static bool unavailable;

        public static bool MarkFullscreen(IntPtr hwnd, bool fullscreen)
        {
            if (unavailable) return false;
            try
            {
                if (list == null)
                {
                    list = (ITaskbarList2)new TaskbarList();
                    list.HrInit();
                }
                list.MarkFullscreenWindow(hwnd, fullscreen);
                return true;
            }
            catch (Exception ex)
            {
                unavailable = true;
                Log.Write("MarkFullscreenWindow indisponible : " + ex.Message);
                return false;
            }
        }
    }
}
