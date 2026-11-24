using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace PcInventory
{
    /// <summary>
    /// Разделы реестра других пользователей этого ПК (HKEY_USERS\&lt;SID&gt;).
    /// Загруженный раздел (пользователь сейчас в системе) читается напрямую — чужой только с правами администратора.
    /// Незагруженный (пользователь не в системе) подключается из NTUSER.DAT на время чтения и сразу отключается —
    /// это требует прав администратора.
    /// </summary>
    static class UserHives
    {
        public delegate void KeyAction(RegistryKey key, string user);

        const string ProfileList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
        static readonly UIntPtr HKEY_USERS = new UIntPtr(0x80000003u);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegLoadKey(UIntPtr hKey, string subKey, string file);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        static extern int RegUnLoadKey(UIntPtr hKey, string subKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokPriv1Luid newState, int len, IntPtr prev, IntPtr relen);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct TokPriv1Luid { public int Count; public long Luid; public int Attr; }

        static bool EnablePrivilege(string name)
        {
            IntPtr tok;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out tok)) return false; // ADJUST_PRIVILEGES | QUERY
            try
            {
                var tp = new TokPriv1Luid { Count = 1, Attr = 2 }; // SE_PRIVILEGE_ENABLED
                if (!LookupPrivilegeValue(null, name, out tp.Luid)) return false;
                if (!AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
                return Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(tok); }
        }

        public static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static string AccountName(string sid, string profilePath)
        {
            try
            {
                string n = ((NTAccount)new SecurityIdentifier(sid).Translate(typeof(NTAccount))).Value;
                int slash = n.IndexOf('\\');
                return slash >= 0 ? n.Substring(slash + 1) : n;
            }
            catch { }
            return Path.GetFileName(profilePath.TrimEnd('\\'));
        }

        /// <summary>
        /// Для каждого пользователя ПК, кроме текущего, открывает HKU\&lt;SID&gt;\&lt;subKey&gt; и вызывает action.
        /// Имена пользователей, чьи разделы прочитать не удалось, попадают в skipped.
        /// </summary>
        public static void ForEachOther(string subKey, KeyAction action, List<string> skipped)
        {
            string me = "";
            try { me = WindowsIdentity.GetCurrent().User.Value; } catch { }
            bool admin = IsAdmin();
            bool privOk = false, privTried = false;

            var profiles = new List<string[]>(); // sid, путь профиля
            using (var pl = Registry.LocalMachine.OpenSubKey(ProfileList))
            {
                if (pl == null) return;
                foreach (var sid in pl.GetSubKeyNames())
                {
                    // только обычные учётные записи: локальные/доменные (S-1-5-21) и Azure AD (S-1-12-1); .bak — повреждённые профили
                    if (!(sid.StartsWith("S-1-5-21-") || sid.StartsWith("S-1-12-1-")) || sid.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(sid, me, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (var p = pl.OpenSubKey(sid))
                        {
                            string path = p == null ? "" : Environment.ExpandEnvironmentVariables(Convert.ToString(p.GetValue("ProfileImagePath")) ?? "");
                            if (path != "") profiles.Add(new[] { sid, path });
                        }
                    }
                    catch { }
                }
            }

            int n = 0;
            foreach (var pr in profiles)
            {
                string sid = pr[0], path = pr[1];
                string user = AccountName(sid, path);
                bool loaded = false;
                try { using (var k = Registry.Users.OpenSubKey(sid)) loaded = k != null; } catch { loaded = true; }

                if (loaded)
                {
                    try
                    {
                        using (var k = Registry.Users.OpenSubKey(sid + "\\" + subKey))
                            if (k != null) action(k, user);
                    }
                    catch { skipped.Add(user); }
                    continue;
                }

                string hive = Path.Combine(path, "NTUSER.DAT");
                if (!File.Exists(hive)) continue; // профиль удалён, а запись осталась
                if (!admin) { skipped.Add(user); continue; }
                if (!privTried)
                {
                    privTried = true;
                    privOk = EnablePrivilege("SeBackupPrivilege") & EnablePrivilege("SeRestorePrivilege");
                }
                if (!privOk) { skipped.Add(user); continue; }

                string mount = "ven4PcConfig_" + System.Diagnostics.Process.GetCurrentProcess().Id + "_" + (n++);
                if (RegLoadKey(HKEY_USERS, mount, hive) != 0) { skipped.Add(user); continue; } // файл занят или повреждён
                try
                {
                    using (var k = Registry.Users.OpenSubKey(mount + "\\" + subKey))
                        if (k != null) action(k, user);
                }
                catch { skipped.Add(user); }
                finally
                {
                    // раздел отключается, только когда закрыты все его дескрипторы
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    RegUnLoadKey(HKEY_USERS, mount);
                }
            }
        }
    }
}
