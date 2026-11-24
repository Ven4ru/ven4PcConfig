using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PcInventory
{
    /// <summary>Текущие режимы дисплеев через WinAPI (работает с Windows 2000/XP).</summary>
    static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        public class Mode
        {
            public string Adapter = "";
            /// <summary>Путь монитора вида DISPLAY\PHL08E5\5&amp;...&amp;UID4352 (Vista+), иначе "".</summary>
            public string MonitorPath = "";
            /// <summary>Ключ драйвера монитора вида {4d36e96e-...}\0001 (для XP).</summary>
            public string DriverKey = "";
            public string MonitorName = "";
            public int W, H, Hz;
            public bool Used;
            public string Res => W > 0 ? W + "x" + H + (Hz > 1 ? "@" + Hz + "Hz" : "") : "";
        }

        public static List<Mode> Get()
        {
            var res = new List<Mode>();
            try
            {
                var ad = new DISPLAY_DEVICE();
                ad.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                for (uint i = 0; EnumDisplayDevices(null, i, ref ad, 0); i++)
                {
                    string adName = ad.DeviceName;
                    bool attached = (ad.StateFlags & 0x1) != 0;
                    bool mirror = (ad.StateFlags & 0x8) != 0;
                    ad.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (!attached || mirror) continue;

                    var dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    int w = 0, h = 0, hz = 0;
                    if (EnumDisplaySettings(adName, -1, ref dm)) { w = dm.dmPelsWidth; h = dm.dmPelsHeight; hz = dm.dmDisplayFrequency; }

                    int before = res.Count;
                    // Сначала с флагом EDD_GET_DEVICE_INTERFACE_NAME (Vista+), затем без него (XP)
                    var paths = new List<string>();
                    var mon = new DISPLAY_DEVICE();
                    mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    for (uint j = 0; EnumDisplayDevices(adName, j, ref mon, 1); j++)
                    {
                        if ((mon.StateFlags & 0x1) != 0) paths.Add(mon.DeviceID ?? "");
                        mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    }
                    var plain = new List<string[]>();
                    mon = new DISPLAY_DEVICE();
                    mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    for (uint j = 0; EnumDisplayDevices(adName, j, ref mon, 0); j++)
                    {
                        if ((mon.StateFlags & 0x1) != 0) plain.Add(new[] { mon.DeviceID ?? "", mon.DeviceString ?? "" });
                        mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    }
                    int n = Math.Max(paths.Count, plain.Count);
                    for (int k = 0; k < n; k++)
                    {
                        var m = new Mode { W = w, H = h, Hz = hz };
                        if (k < paths.Count) m.MonitorPath = InterfaceToInstance(paths[k]);
                        if (k < plain.Count)
                        {
                            m.MonitorName = Util.Clean(plain[k][1]);
                            // MONITOR\PHL08E5\{4d36e96e-...}\0001
                            string id = plain[k][0];
                            int b = id.IndexOf('{');
                            if (b >= 0) m.DriverKey = id.Substring(b).ToUpperInvariant();
                        }
                        res.Add(m);
                    }
                    if (res.Count == before) res.Add(new Mode { W = w, H = h, Hz = hz });
                }
            }
            catch { }
            return res;
        }

        static string InterfaceToInstance(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(@"\\?\")) return "";
            string s = id.Substring(4);
            int e = s.IndexOf("#{", StringComparison.Ordinal);
            if (e > 0) s = s.Substring(0, e);
            return s.Replace('#', '\\').ToUpperInvariant();
        }
    }

    /// <summary>Разбор EDID монитора.</summary>
    class Edid
    {
        public string Mfr = "", Product = "", Name = "", Serial = "";
        public int Year, WidthCm, HeightCm;

        public static Edid Parse(byte[] b)
        {
            if (b == null || b.Length < 128) return null;
            var e = new Edid();
            int m = (b[8] << 8) | b[9];
            e.Mfr = new string(new[] { (char)('A' - 1 + ((m >> 10) & 31)), (char)('A' - 1 + ((m >> 5) & 31)), (char)('A' - 1 + (m & 31)) });
            e.Product = ((b[11] << 8) | b[10]).ToString("X4");
            e.Year = b[17] + 1990;
            e.WidthCm = b[21]; e.HeightCm = b[22];
            uint sn = BitConverter.ToUInt32(b, 12);
            for (int d = 54; d <= 108; d += 18)
            {
                if (b[d] != 0 || b[d + 1] != 0) continue;
                byte tag = b[d + 3];
                var sb = new StringBuilder();
                for (int i = d + 5; i < d + 18 && b[i] != 0x0A && b[i] != 0; i++) sb.Append((char)b[i]);
                string text = Util.Clean(sb.ToString());
                if (tag == 0xFC) e.Name += text;
                else if (tag == 0xFF) e.Serial = text;
            }
            if (e.Serial == "" && sn != 0 && sn != 0x01010101) e.Serial = sn.ToString();
            return e;
        }

        /// <summary>EDID из реестра по ключу драйвера монитора (для XP, где нет WmiMonitorID).</summary>
        public static Edid FromRegistryByDriver(string driverKey)
        {
            if (Util.Blank(driverKey)) return null;
            try
            {
                using (var disp = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY"))
                {
                    if (disp == null) return null;
                    foreach (var model in disp.GetSubKeyNames())
                        using (var mk = disp.OpenSubKey(model))
                        {
                            if (mk == null) continue;
                            foreach (var inst in mk.GetSubKeyNames())
                                using (var ik = mk.OpenSubKey(inst))
                                {
                                    if (ik == null) continue;
                                    string drv = (ik.GetValue("Driver") as string ?? "").ToUpperInvariant();
                                    if (drv != driverKey) continue;
                                    using (var pk = ik.OpenSubKey("Device Parameters"))
                                        if (pk != null) return Parse(pk.GetValue("EDID") as byte[]);
                                }
                        }
                }
            }
            catch { }
            return null;
        }
    }

    /// <summary>Сырые таблицы SMBIOS (тип памяти, если WMI его не знает).</summary>
    static class Smbios
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetSystemFirmwareTable(uint sig, uint id, IntPtr buf, uint size);

        public class MemDev { public int Type, Speed, ConfSpeed; public long SizeMb; }

        static byte[] Raw()
        {
            // 1) GetSystemFirmwareTable (Vista+ / XP x64), блок с заголовком 8 байт
            try
            {
                const uint RSMB = 0x52534D42;
                uint n = GetSystemFirmwareTable(RSMB, 0, IntPtr.Zero, 0);
                if (n > 8)
                {
                    IntPtr p = Marshal.AllocHGlobal((int)n);
                    try
                    {
                        uint got = GetSystemFirmwareTable(RSMB, 0, p, n);
                        if (got > 8 && got <= n)
                        {
                            var b = new byte[got];
                            Marshal.Copy(p, b, 0, (int)got);
                            int len = BitConverter.ToInt32(b, 4);
                            var t = new byte[Math.Min(len, b.Length - 8)];
                            Array.Copy(b, 8, t, 0, t.Length);
                            return t;
                        }
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }
            }
            catch { }
            // 2) WMI (XP 32-bit)
            foreach (var o in Wmi.Query(@"root\wmi", "SELECT SMBiosData FROM MSSmBios_RawSMBiosTables", true))
                if (Wmi.Raw(o, "SMBiosData") is byte[] d && d.Length > 0) return d;
            return null;
        }

        public static List<MemDev> Memory()
        {
            var res = new List<MemDev>();
            byte[] b;
            try { b = Raw(); } catch { b = null; }
            if (b == null) return res;
            int p = 0, end = b.Length;
            try
            {
                while (p + 4 <= end)
                {
                    int type = b[p], hl = b[p + 1];
                    if (hl < 4) break;
                    if (type == 17 && hl > 0x12 && p + hl <= end)
                    {
                        int sz = BitConverter.ToUInt16(b, p + 0x0C);
                        if (sz != 0 && sz != 0xFFFF)
                        {
                            long mb = (sz & 0x8000) != 0 ? (sz & 0x7FFF) / 1024 : sz;
                            if (sz == 0x7FFF && hl > 0x1F) mb = BitConverter.ToInt32(b, p + 0x1C);
                            var m = new MemDev { Type = b[p + 0x12], SizeMb = mb };
                            if (hl > 0x16) m.Speed = BitConverter.ToUInt16(b, p + 0x15);
                            if (hl > 0x21) m.ConfSpeed = BitConverter.ToUInt16(b, p + 0x20);
                            res.Add(m);
                        }
                    }
                    if (type == 127) break;
                    int q = p + hl;
                    while (q + 1 < end && !(b[q] == 0 && b[q + 1] == 0)) q++;
                    p = q + 2;
                }
            }
            catch { }
            return res;
        }
    }

    static class UserInfo
    {
        [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetUserNameEx(int format, StringBuilder buf, ref uint size);

        /// <summary>Отображаемое имя текущего пользователя (ФИО из AD или локальной учётки).</summary>
        public static string DisplayName()
        {
            try
            {
                uint size = 512;
                var sb = new StringBuilder((int)size);
                if (GetUserNameEx(3, sb, ref size))
                {
                    string s = Util.Clean(sb.ToString());
                    if (s != "") return s;
                }
            }
            catch { }
            try
            {
                string user = Environment.UserName.Replace("'", "\\'");
                string dom = Environment.MachineName.Replace("'", "\\'");
                foreach (var o in Wmi.Query(@"root\cimv2",
                    "SELECT FullName FROM Win32_UserAccount WHERE LocalAccount=TRUE AND Name='" + user + "' AND Domain='" + dom + "'", true))
                {
                    string fn = Wmi.S(o, "FullName");
                    if (fn != "") return fn;
                }
            }
            catch { }
            return "";
        }
    }
}
