using System;
using System.Runtime.InteropServices;

namespace PcInventory
{
    /// <summary>
    /// Программа собрана как оконная (без чёрного окна консоли при обычном запуске). Для ключей /auto, /rebuild,
    /// /console она подключается к консоли, из которой её запустили, или открывает свою.
    /// Если вывод перенаправлен в файл или канал — ничего не трогаем, пишем туда.
    /// </summary>
    static class ConsoleHost
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern bool AllocConsole();
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")] static extern int GetFileType(IntPtr h);

        public static void Attach()
        {
            try
            {
                IntPtr h = GetStdHandle(-11); // STD_OUTPUT_HANDLE
                int type = h == IntPtr.Zero || h == new IntPtr(-1) ? 0 : GetFileType(h);
                if (type == 1 || type == 3) return; // FILE_TYPE_DISK / FILE_TYPE_PIPE — вывод перенаправлен
                if (!AttachConsole(-1)) AllocConsole(); // ATTACH_PARENT_PROCESS
            }
            catch { }
        }
    }
}
