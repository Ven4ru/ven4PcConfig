using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;

namespace PcInventory
{
    static class Util
    {
        public static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        public static bool Blank(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) return false;
            return true;
        }

        /// <summary>Убирает управляющие/недопустимые для XML символы, схлопывает пробелы (переводы строк сохраняет).</summary>
        public static string Clean(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; prevSpace = false; }
                    continue;
                }
                if (char.IsLowSurrogate(c) || c == '￾' || c == '￿') continue;
                if (c == '\r') continue;
                if (c == '\n')
                {
                    while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
                    sb.Append('\n'); prevSpace = true; continue;
                }
                if (c < 0x20 || c == ' ' || c == '\t') c = ' ';
                if (c == ' ')
                {
                    if (prevSpace) continue;
                    prevSpace = true;
                }
                else prevSpace = false;
                sb.Append(c);
            }
            return sb.ToString().Trim(' ', '\n');
        }

        public static string Join(string sep, IEnumerable<string> items)
        {
            var list = new List<string>();
            foreach (var s in items) if (!Blank(s)) list.Add(s);
            return string.Join(sep, list.ToArray());
        }

        public static string Gb(double gb)
        {
            if (Math.Abs(gb - Math.Round(gb)) < 0.05) return Math.Round(gb).ToString("0", Ru);
            return gb.ToString("0.#", Ru);
        }

        public static string Size(long bytes)
        {
            if (bytes <= 0) return "";
            double gb = bytes / 1073741824.0;
            if (gb >= 1) return Gb(gb) + " ГБ";
            return (bytes / 1048576).ToString(Ru) + " МБ";
        }

        static readonly string[] JunkValues =
        {
            "to be filled by o.e.m.", "to be filled by oem", "default string", "system serial number", "system product name",
            "system manufacturer", "system version", "not specified", "not available", "none", "n/a", "na", "oem", "o.e.m.",
            "123456789", "0123456789", "1234567890", "chassis serial number", "base board serial number", "serial number",
            "not applicable", "invalid", "unknown", "x.x", "type1productconfigid", "empty", "undefined", "standard",
            "notebook", "desktop", "all series", "default", "sku", "pc"
        };

        /// <summary>Значения-заглушки из BIOS («To be filled by O.E.M.» и т.п.).</summary>
        public static bool Junk(string s)
        {
            if (Blank(s)) return true;
            string l = s.Trim().ToLowerInvariant();
            foreach (var j in JunkValues) if (l == j) return true;
            bool allFiller = true;
            foreach (char c in l) if ("0fx-. ".IndexOf(c) < 0) { allFiller = false; break; }
            return allFiller;
        }

        public static string NoJunk(string s) => Junk(s) ? "" : s.Trim();

        static readonly string[][] Vendors =
        {
            new[] { "asustek", "ASUS" }, new[] { "asus", "ASUS" }, new[] { "gigabyte", "Gigabyte" }, new[] { "micro-star", "MSI" },
            new[] { "msi", "MSI" }, new[] { "asrock", "ASRock" }, new[] { "hewlett-packard", "HP" }, new[] { "hewlett packard", "HP" },
            new[] { "hp", "HP" }, new[] { "dell", "Dell" }, new[] { "lenovo", "Lenovo" }, new[] { "acer", "Acer" },
            new[] { "intel", "Intel" }, new[] { "biostar", "Biostar" }, new[] { "elitegroup", "ECS" }, new[] { "ecs", "ECS" },
            new[] { "foxconn", "Foxconn" }, new[] { "supermicro", "Supermicro" }, new[] { "fujitsu", "Fujitsu" },
            new[] { "toshiba", "Toshiba" }, new[] { "samsung", "Samsung" }, new[] { "sony", "Sony" }, new[] { "huawei", "Huawei" },
            new[] { "microsoft", "Microsoft" }, new[] { "pegatron", "Pegatron" }, new[] { "medion", "Medion" },
            new[] { "packard bell", "Packard Bell" }, new[] { "honor", "Honor" }, new[] { "xiaomi", "Xiaomi" },
            new[] { "apple", "Apple" }, new[] { "vmware", "VMware" }, new[] { "innotek", "VirtualBox" }, new[] { "qemu", "QEMU" },
            new[] { "american megatrends", "AMI" }, new[] { "phoenix", "Phoenix" }, new[] { "award", "Award" }, new[] { "insyde", "Insyde" }, new[] { "aquarius", "Aquarius" }, new[] { "depo", "DEPO" }, new[] { "iru", "iRU" }, new[] { "kraftway", "Kraftway" },
        };

        public static string Vendor(string m)
        {
            if (Junk(m)) return "";
            string l = m.ToLowerInvariant();
            foreach (var v in Vendors)
            {
                int i = l.IndexOf(v[0], StringComparison.Ordinal);
                if (i < 0) continue;
                bool startOk = i == 0 || !char.IsLetter(l[i - 1]);
                int e = i + v[0].Length;
                bool endOk = e >= l.Length || !char.IsLetter(l[e]);
                if (startOk && endOk) return v[1];
            }
            string r = m.Trim();
            foreach (var suf in new[] { ", Inc.", " Inc.", " INC.", " Corporation", " Corp.", " Co., Ltd.", " Co.,Ltd.", " CO., LTD.", " Ltd.", " LTD.", " GmbH" })
                if (r.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) r = r.Substring(0, r.Length - suf.Length).Trim();
            return r;
        }

        /// <summary>Склеивает «Производитель Модель», не дублируя производителя.</summary>
        public static string WithVendor(string vendor, string model)
        {
            model = NoJunk(model);
            if (vendor == "") return model;
            if (model == "") return vendor;
            if (model.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)) return model;
            return vendor + " " + model;
        }

        public static string Hash8(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        public static string SafeFileName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }

    static class Wmi
    {
        public static readonly List<string> Errors = new List<string>();

        public static List<ManagementBaseObject> Query(string ns, string wql, bool quiet = false)
        {
            var list = new List<ManagementBaseObject>();
            try
            {
                var scope = new ManagementScope(ns);
                var opts = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = true, Rewindable = false };
                using (var s = new ManagementObjectSearcher(scope, new ObjectQuery(wql), opts))
                    foreach (ManagementBaseObject o in s.Get()) list.Add(o);
            }
            catch (Exception ex)
            {
                if (!quiet) Errors.Add(wql + " — " + ex.Message.Trim());
            }
            return list;
        }

        public static List<ManagementBaseObject> Cim(string wql) => Query(@"root\cimv2", wql);

        public static ManagementBaseObject First(string wql)
        {
            var l = Cim(wql);
            return l.Count > 0 ? l[0] : null;
        }

        public static object Raw(ManagementBaseObject o, string p)
        {
            if (o == null) return null;
            try { return o[p]; } catch { return null; }
        }

        public static string S(ManagementBaseObject o, string p)
        {
            var v = Raw(o, p);
            if (v == null) return "";
            if (v is string[] arr) return Util.Join(", ", arr);
            return Util.Clean(v.ToString());
        }

        public static long L(ManagementBaseObject o, string p)
        {
            var v = Raw(o, p);
            if (v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        public static bool B(ManagementBaseObject o, string p)
        {
            var v = Raw(o, p);
            return v is bool b && b;
        }

        public static string[] Arr(ManagementBaseObject o, string p) => Raw(o, p) as string[] ?? new string[0];

        /// <summary>Строка из массива uint16 (WmiMonitorID).</summary>
        public static string U16(ManagementBaseObject o, string p)
        {
            var v = Raw(o, p);
            var sb = new StringBuilder();
            if (v is Array a)
                foreach (var x in a)
                {
                    int c = Convert.ToInt32(x);
                    if (c == 0) break;
                    sb.Append((char)c);
                }
            return Util.Clean(sb.ToString());
        }
    }
}
