using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace PcInventory
{
    /// <summary>Активация Windows и Office, состояние и лицензия антивируса.</summary>
    partial class Collector
    {
        const string WinAppId = "55c92734-d682-4d71-983e-d6ec3f16059f";

        public const string Ok = "ok", Bad = "bad";

        /// <summary>Строка листа «Лицензии»: вид, продукт, статус, (канал — не пишется), (ключ — не пишется), действует до, подробности, оценка (ok/bad/"").
        /// Поля 3 и 4 оставлены пустыми ради совместимости формата XML с версией 1.0.</summary>
        void Lic(string kind, string product, string status, string until, string details, string grade) =>
            r.Lic.Add(new[] { kind, Util.Clean(product), Util.Clean(status), "", "", Util.Clean(until), Util.Clean(details), grade });

        /// <summary>Итоговая оценка: «плохо», если есть хоть одно «плохо»; «хорошо», если всё хорошо; иначе — без оценки.</summary>
        static string Combine(List<string> grades)
        {
            if (grades.Count == 0) return "";
            bool allOk = true;
            foreach (var g in grades)
            {
                if (g == Bad) return Bad;
                if (g != Ok) allOk = false;
            }
            return allOk ? Ok : "";
        }

        // ---------------------------------------------------------------- SPP (Windows Vista+ и Office 2013+)
        List<ManagementBaseObject> spp;
        bool sppFailed;

        List<ManagementBaseObject> Spp()
        {
            if (spp != null) return spp;
            int before = Wmi.Errors.Count;
            spp = Wmi.Query(@"root\cimv2", "SELECT * FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");
            sppFailed = Wmi.Errors.Count > before;
            return spp;
        }

        static string LicStatus(long s)
        {
            switch (s)
            {
                case 0: return "не активирована";
                case 1: return "активирована";
                case 2: return "не активирована (пробный период)";
                case 3: return "не активирована (пробный период после замены оборудования)";
                case 4: return "не прошла проверку подлинности (льготный период)";
                case 5: return "не активирована (режим уведомлений)";
                case 6: return "не активирована (продлённый пробный период)";
                default: return "состояние " + s;
            }
        }

        static string Channel(string desc)
        {
            string d = desc.ToUpperInvariant();
            if (d.Contains("KMSCLIENT")) return "KMS";
            if (d.Contains("KMS")) return "KMS-хост";
            if (d.Contains("MAK")) return "MAK";
            if (d.Contains("VIRTUAL_MACHINE")) return "AVMA";
            if (d.Contains("OEM")) return "OEM";
            if (d.Contains("TIMEBASED_SUB") || d.Contains("SUBSCRIPTION")) return "подписка";
            if (d.Contains("TIMEBASED")) return "ограничена по времени";
            if (d.Contains("GRACE")) return "без ключа";
            if (d.Contains("RETAIL")) return "Retail";
            if (d.Contains("VOLUME")) return "корпоративная";
            return "";
        }

        static DateTime WmiDate(string s)
        {
            if (s.Length < 14) return DateTime.MinValue;
            try { return ManagementDateTimeConverter.ToDateTime(s); } catch { return DateTime.MinValue; }
        }

        /// <summary>Сведения об активации. Источник активации (канал, ключ, KMS-сервер, срок продления) в отчёт не выводится —
        /// только факт: активировано или нет. Channel нужен лишь внутри, чтобы отсеять пустые записи подписки.</summary>
        class SppInfo { public string Status, Channel, Until, Details, Grade, Summary; }

        static SppInfo Describe(ManagementBaseObject p)
        {
            var x = new SppInfo();
            long st = Wmi.L(p, "LicenseStatus");
            long grace = Wmi.L(p, "GracePeriodRemaining"); // минуты
            x.Channel = Channel(Wmi.S(p, "Description"));
            x.Status = LicStatus(st);
            x.Grade = st == 1 ? Ok : Bad;
            var det = new List<string>();

            // Ознакомительная (Evaluation) версия перестаёт работать в указанную дату — это не источник активации, а срок жизни ОС.
            DateTime until = DateTime.MinValue;
            DateTime eval = WmiDate(Wmi.S(p, "EvaluationEndDate"));
            if (eval.Year > 1700)
            {
                det.Add("ознакомительная версия до " + eval.ToString("dd.MM.yyyy"));
                if (eval < DateTime.Now) x.Grade = Bad;
                until = eval;
            }
            // Для неактивированной копии показываем, сколько осталось до блокировки.
            string left = "";
            if (st != 1 && grace > 0)
            {
                left = "осталось " + Math.Max(0, (int)(grace / 1440)) + " дн.";
                DateTime g = DateTime.Now.AddMinutes(grace);
                if (until == DateTime.MinValue || g < until) until = g;
            }
            x.Until = until != DateTime.MinValue ? until.ToString("yyyy-MM-dd") : "";

            var sum = new List<string>();
            if (left != "") sum.Add(left);
            if (eval.Year > 1700) sum.Add("ознакомительная до " + eval.ToString("dd.MM.yyyy"));
            x.Summary = x.Status + (sum.Count > 0 ? " (" + string.Join(", ", sum.ToArray()) + ")" : "");
            x.Details = Util.Join("; ", det);
            return x;
        }

        // ---------------------------------------------------------------- Windows
        void WindowsActivation()
        {
            if (Environment.OSVersion.Version.Major < 6)
            {
                // Windows XP / Server 2003
                var a = Wmi.First("SELECT * FROM Win32_WindowsProductActivation");
                if (a == null) { r["WinAct"] = "нет данных"; return; }
                long req = Wmi.L(a, "ActivationRequired"), grace = Wmi.L(a, "RemainingGracePeriod");
                bool ok = req == 0;
                string st = ok ? "активирована" : "не активирована" + (grace > 0 ? " (осталось " + grace + " дн.)" : "");
                r["WinAct"] = st;
                r["WinActSt"] = ok ? Ok : Bad;
                Lic("Windows", r["Os"], ok ? "активирована" : "не активирована", "",
                    grace > 0 ? "до активации осталось " + grace + " дн." : "", r["WinActSt"]);
                return;
            }

            ManagementBaseObject best = null;
            foreach (var p in Spp())
            {
                string app = Wmi.S(p, "ApplicationID").ToLowerInvariant();
                bool isWin = app == WinAppId || (app == "" && Wmi.S(p, "Name").StartsWith("Windows", StringComparison.OrdinalIgnoreCase));
                if (!isWin) continue;
                if (best == null || (Wmi.L(p, "LicenseStatus") == 1 && Wmi.L(best, "LicenseStatus") != 1)) best = p;
            }
            if (best == null)
            {
                if (sppFailed) { r["WinAct"] = "нет данных (служба лицензирования не ответила)"; return; }
                r["WinAct"] = "не активирована (ключ продукта не установлен)";
                r["WinActSt"] = Bad;
                Lic("Windows", r["Os"], "не активирована", "", "ключ продукта не установлен", Bad);
                return;
            }
            var x = Describe(best);
            r["WinAct"] = Up(x.Summary);
            r["WinActSt"] = x.Grade;
            Lic("Windows", r["Os"], x.Status, x.Until, x.Details, x.Grade);
        }

        static string Up(string s) => s.Length > 0 ? char.ToUpper(s[0], Util.Ru) + s.Substring(1) : s;

        // ---------------------------------------------------------------- Office
        static string OfficeName(string name, string family)
        {
            string f = family;
            if (f == "")
            {
                int c = name.IndexOf(", ", StringComparison.Ordinal);
                f = c >= 0 ? name.Substring(c + 2) : name;
            }
            if (f.EndsWith(" edition", StringComparison.OrdinalIgnoreCase)) f = f.Substring(0, f.Length - 8);
            string ver = "";
            // «Office 14, …» — версия в начале названия (Office 2010/2013 не пишут её в редакции)
            if (name.StartsWith("Office ", StringComparison.OrdinalIgnoreCase) && name.Length > 9 && char.IsDigit(name[7]) && char.IsDigit(name[8]))
                ver = name.Substring(7, 2);
            if (f.StartsWith("Office", StringComparison.OrdinalIgnoreCase))
            {
                f = f.Substring(6);
                if (f.Length >= 2 && char.IsDigit(f[0]) && char.IsDigit(f[1])) { ver = f.Substring(0, 2); f = f.Substring(2); }
            }
            int cut = f.IndexOf("VL_", StringComparison.Ordinal);
            if (cut < 0) cut = f.IndexOf("R_", StringComparison.Ordinal);
            if (cut < 0) cut = f.IndexOf('-');
            if (cut > 0) f = f.Substring(0, cut);
            if (f.StartsWith("O365", StringComparison.Ordinal)) f = "365 " + f.Substring(4);
            string year;
            switch (ver)
            {
                case "14": year = "2010"; break;
                case "15": year = "2013"; break;
                case "16": year = "2016"; break;
                case "19": year = "2019"; break;
                case "21": year = "2021"; break;
                case "24": year = "2024"; break;
                default: year = ""; break;
            }
            // «ProPlus2021» → «ProPlus 2021»
            var sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++)
            {
                if (i > 0 && char.IsDigit(f[i]) && char.IsLetter(f[i - 1]) && !(f[i - 1] == 'O' && i == 1)) sb.Append(' ');
                sb.Append(f[i]);
            }
            f = sb.ToString().Trim();
            bool hasYear = f.Contains("20") || f.Contains("365");
            if (f == "") return "Office " + year;
            return "Office " + (hasYear || year == "" ? "" : year + " ") + f;
        }

        void OfficeActivation()
        {
            var lines = new List<string>();
            var grades = new List<string>();

            // Microsoft 365 с входом по учётной записи (лицензии пользователя, «vNext»)
            int vnext = 0;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Office\Licenses");
                if (Directory.Exists(dir)) vnext = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            }
            catch { }
            string c2r = "", shared = "";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration"))
                    if (k != null)
                    {
                        c2r = Convert.ToString(k.GetValue("ProductReleaseIds"));
                        shared = Convert.ToString(k.GetValue("SharedComputerLicensing"));
                    }
            }
            catch { }
            if (vnext > 0)
            {
                lines.Add("Microsoft 365 — активирована");
                grades.Add(Ok);
                Lic("Офис", "Microsoft 365", "активирована", "", "вход учётной записью пользователя " + Environment.UserName, Ok);
            }
            else if (shared == "1")
            {
                lines.Add("Microsoft 365 — общая активация (Shared Computer Activation), лицензия выдаётся при входе пользователя");
                grades.Add("");
                Lic("Офис", "Microsoft 365", "общая активация", "", "Shared Computer Activation", "");
            }

            // SPP (Office 2013+ на Windows 8+) и OSPP (Office 2010, Office 2013–2016 на Windows 7 и старше)
            var items = new List<ManagementBaseObject>();
            foreach (var p in Spp())
            {
                string app = Wmi.S(p, "ApplicationID").ToLowerInvariant();
                if (app.StartsWith("0ff1ce15") || app.StartsWith("59a52881")) items.Add(p);
            }
            items.AddRange(Wmi.Query(@"root\cimv2", "SELECT * FROM OfficeSoftwareProtectionProduct WHERE PartialProductKey IS NOT NULL", true));
            foreach (var p in items)
            {
                var x = Describe(p);
                string name = OfficeName(Wmi.S(p, "Name"), Wmi.S(p, "LicenseFamily"));
                // при подписке по учётной записи остаются неактивные «льготные» записи — их не показываем
                if (vnext > 0 && x.Grade == Bad && (name.Contains("365") || x.Channel == "без ключа")) continue;
                lines.Add(name + " — " + x.Summary);
                grades.Add(x.Grade);
                Lic("Офис", name, x.Status, x.Until, x.Details, x.Grade);
            }

            // Пакеты без механизма активации Microsoft
            bool msOffice = false;
            foreach (var o in r["Office"].Split('\n'))
            {
                if (o == "") continue;
                string l = o.ToLowerInvariant();
                if (l.StartsWith("microsoft")) { msOffice = true; continue; }
                bool free = l.Contains("libreoffice") || l.Contains("openoffice") || l.Contains("onlyoffice");
                string st = free ? "бесплатный, активация не требуется" : "лицензия программно не проверяется";
                lines.Add(o + " — " + st);
                grades.Add(free ? Ok : "");
                Lic("Офис", o, st, "", "", free ? Ok : "");
            }

            if (items.Count == 0 && vnext == 0 && shared != "1" && msOffice)
            {
                string off = r["Office"];
                if (off.Contains("2003") || off.Contains("2007") || off.Contains(" XP"))
                {
                    lines.Add("Microsoft Office 2003/2007 — активация программно не проверяется");
                    grades.Add("");
                    Lic("Офис", off.Split('\n')[0], "не проверяется", "", "в Office 2003/2007 нет интерфейса для проверки активации", "");
                }
                else if (sppFailed)
                {
                    lines.Add("Microsoft Office — нет данных (служба лицензирования не ответила)");
                    grades.Add("");
                }
                else
                {
                    lines.Add("Microsoft Office — не активирован (ключ или лицензия не найдены)");
                    grades.Add(Bad);
                    Lic("Офис", off.Split('\n')[0], "не активирован", "", "ключ продукта не установлен, вход в учётную запись не выполнен", Bad);
                }
            }
            r["OfficeAct"] = string.Join("\n", lines.ToArray());
            r["OfficeActSt"] = Combine(grades);
        }

        // ---------------------------------------------------------------- антивирус: состояние и лицензия
        /// <summary>name, состояние (0 выкл, 1 вкл, 2 приостановлен, 3 лицензия истекла, -1 неизвестно), базы (0 актуальны, 1 устарели, -1 неизвестно).</summary>
        void AntivirusLicense(List<object[]> avs)
        {
            var lines = new List<string>();
            var grades = new List<string>();
            bool third = false;
            foreach (var a in avs) if (IsDefender((string)a[0]) == 0 && ((int)a[1] == 1 || (int)a[1] == 3)) third = true;
            var seen = new List<string>();
            foreach (var a in avs)
            {
                string name = (string)a[0];
                int state = (int)a[1], sig = (int)a[2];
                if (name == "" || seen.Contains(name)) continue;
                seen.Add(name);
                bool def = IsDefender(name) == 1;
                if (def && third && state != 1) continue; // Defender отключается сам при стороннем антивирусе
                string stTxt = state == 0 ? "выключен" : state == 1 ? "включён" : state == 2 ? "приостановлен" : state == 3 ? "включён" : "";
                string sigTxt = sig == 0 ? "базы актуальны" : sig == 1 ? "базы устарели" : "";
                string lic, until = "", g;
                if (def) { lic = "встроен в Windows, лицензия не требуется"; g = state == 1 && sig != 1 ? Ok : Bad; }
                else if (state == 3) { lic = "лицензия истекла или не активирована"; g = Bad; }
                else if (state == 1) { lic = "лицензия действует"; g = sig == 1 ? Bad : Ok; }
                else if (state == -1) { lic = "состояние лицензии Windows не сообщает"; g = ""; }
                else { lic = "лицензия: нет данных (антивирус выключен)"; g = Bad; }

                string details = "по данным Центра безопасности Windows";
                if (name.IndexOf("Dr.Web", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string keyFile;
                    DateTime exp = DrWebKeyExpires(out keyFile);
                    if (exp != DateTime.MinValue)
                    {
                        until = exp.ToString("yyyy-MM-dd");
                        if (exp.Date < DateTime.Today) { lic = "ключ истёк " + exp.ToString("dd.MM.yyyy"); g = Bad; }
                        else if (state != 3) lic = "ключ действует до " + exp.ToString("dd.MM.yyyy");
                        details += "; ключевой файл " + keyFile;
                    }
                }
                lines.Add(name + " — " + Util.Join(", ", new[] { stTxt, sigTxt, lic }));
                grades.Add(g);
                Lic("Антивирус", name, Util.Join(", ", new[] { stTxt, sigTxt, lic }), until, details, g);
            }
            if (lines.Count == 0)
            {
                bool server = r["Os"].IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0;
                lines.Add(server ? "нет данных (на серверных ОС нет Центра безопасности)" : "антивирус не обнаружен");
                grades.Add(server ? "" : Bad);
            }
            r["AvAct"] = string.Join("\n", lines.ToArray());
            r["AvActSt"] = Combine(grades);
        }

        /// <summary>Срок действия ключевого файла Dr.Web (строка Expires= в *.key).</summary>
        static DateTime DrWebKeyExpires(out string file)
        {
            file = "";
            DateTime best = DateTime.MinValue;
            var bases = new List<string>();
            foreach (var v in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE" })
            {
                string b = Environment.GetEnvironmentVariable(v);
                if (!Util.Blank(b) && !bases.Contains(b)) bases.Add(b);
            }
            foreach (var b in bases)
                foreach (var sub in new[] { "DrWeb", "Doctor Web", @"Doctor Web\Dr.Web", "DrWeb Enterprise Agent", @"Application Data\Doctor Web" })
                {
                    string dir = Path.Combine(b, sub);
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        var files = new List<string>(Directory.GetFiles(dir, "*.key"));
                        foreach (var d in Directory.GetDirectories(dir))
                            try { files.AddRange(Directory.GetFiles(d, "*.key")); } catch { }
                        foreach (var f in files)
                        {
                            try
                            {
                                foreach (var line in File.ReadAllLines(f, Encoding.Default))
                                {
                                    string t = line.Trim();
                                    if (!t.StartsWith("Expires", StringComparison.OrdinalIgnoreCase)) continue;
                                    int eq = t.IndexOf('=');
                                    if (eq < 0) continue;
                                    string v = t.Substring(eq + 1).Trim();
                                    DateTime dt;
                                    if (v.Length >= 10 && DateTime.TryParseExact(v.Substring(0, 10), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dt) && dt > best)
                                    {
                                        best = dt;
                                        file = Path.GetFileName(f);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            return best;
        }
    }
}
