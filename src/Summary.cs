using System;
using System.Collections.Generic;
using System.Globalization;

namespace PcInventory
{
    /// <summary>Лист «Сводка»: что требует внимания, операционные системы, отделы.</summary>
    static partial class Report
    {
        public class Check
        {
            public string Name;
            public Predicate<PcRecord> Hit;
            public Check(string name, Predicate<PcRecord> hit) { Name = name; Hit = hit; }
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static double Num(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, Inv, out v) ? v : -1;
        }

        /// <summary>Объём ОЗУ в ГБ: из поля RamGb, а в данных версии 1.0 — из начала строки «16 ГБ DDR4 …».</summary>
        static double RamGb(PcRecord r)
        {
            double v = Num(r["RamGb"]);
            if (v >= 0) return v;
            string s = r["Ram"];
            int sp = s.IndexOf(' ');
            return sp > 0 ? Num(s.Substring(0, sp).Replace(',', '.')) : -1;
        }

        /// <summary>Дата окончания поддержки ОС Microsoft (для ознакомления; Windows 11 по выпускам не проверяется).</summary>
        public static DateTime SupportEnd(string os)
        {
            string o = os.ToUpperInvariant();
            if (o.Contains("SERVER"))
            {
                if (o.Contains("2003")) return new DateTime(2015, 7, 14);
                if (o.Contains("2008")) return new DateTime(2020, 1, 14);
                if (o.Contains("2012")) return new DateTime(2023, 10, 10);
                if (o.Contains("2016")) return new DateTime(2027, 1, 12);
                return DateTime.MaxValue;
            }
            if (o.Contains(" XP")) return new DateTime(2014, 4, 8);
            if (o.Contains("VISTA")) return new DateTime(2017, 4, 11);
            if (o.Contains("WINDOWS 7")) return new DateTime(2020, 1, 14);
            if (o.Contains("WINDOWS 8")) return new DateTime(2023, 1, 10);
            if (o.Contains("WINDOWS 10"))
            {
                if (o.Contains("LTSC") && o.Contains("2021")) return o.Contains("IOT") ? new DateTime(2032, 1, 13) : new DateTime(2027, 1, 12);
                if (o.Contains("LTSC") && o.Contains("2019")) return new DateTime(2029, 1, 9);
                if (o.Contains("LTSB") && o.Contains("2016")) return new DateTime(2026, 10, 13);
                return new DateTime(2025, 10, 14);
            }
            return DateTime.MaxValue;
        }

        static readonly Check[] Checks =
        {
            new Check("Windows не активирована", r => r["WinActSt"] == Collector.Bad),
            new Check("Office не активирован", r => r["OfficeActSt"] == Collector.Bad),
            new Check("Антивирус выключен, базы устарели или не найден", r => r["AvActSt"] == Collector.Bad),
            new Check("ОС без поддержки Microsoft (нет обновлений безопасности)", r => r["Os"] != "" && SupportEnd(r["Os"]) < DateTime.Today),
            new Check("Неисправность, износ или перегрев диска", r => r["DiskHealthSt"] == Collector.Bad),
            new Check("Система на жёстком диске (HDD), а не на SSD", r => r["SysDiskKind"] == "HDD"),
            new Check("Мало места на системном диске (меньше 10 % или 10 ГБ)", r =>
                (Num(r["SysFreePct"]) >= 0 && Num(r["SysFreePct"]) < 10) || (Num(r["SysFreeGb"]) >= 0 && Num(r["SysFreeGb"]) < 10)),
            new Check("ОЗУ меньше 8 ГБ", r => RamGb(r) > 0 && RamGb(r) < 8),
            new Check(ForbiddenCheck, r => Rules.FoundForbidden(r).Count > 0),
            new Check(RequiredCheck, r => Rules.MissingRequired(r).Count > 0),
            new Check("Сбор сведений прошёл с ошибками (часть ячеек может быть пустой)", r => r["CollectErrors"] != ""),
            new Check("Сведения устарели (собраны более 90 дней назад)", r =>
            {
                DateTime dt;
                return DateTime.TryParseExact(r["Collected"], "yyyy-MM-dd HH:mm", null, DateTimeStyles.None, out dt) && dt < DateTime.Now.AddDays(-90);
            }),
        };

        const string ForbiddenCheck = "Установлены запрещённые программы (список — в «Настройках»)";
        const string RequiredCheck = "Нет обязательных программ (список — в «Настройках»)";

        /// <summary>Списки запрещённых и обязательных программ. Ставит сессия после чтения настроек;
        /// до этого действуют примеры по умолчанию.</summary>
        public static AppSettings Rules = new AppSettings();

        static int Problems(PcRecord r) => ProblemsOf(r).Count;

        /// <summary>Проверки листа «Сводка» — их же показывает окно на странице «Реестр».</summary>
        public static Check[] CheckList() => Checks;

        /// <summary>Короткие названия замечаний для одного ПК (у контроля программ — с названиями программ).</summary>
        public static List<string> ProblemsOf(PcRecord r)
        {
            var list = new List<string>();
            foreach (var c in Checks)
            {
                if (!c.Hit(r)) continue;
                if (c.Name == ForbiddenCheck) list.Add("запрещённые программы: " + string.Join(", ", Rules.FoundForbidden(r).ToArray()));
                else if (c.Name == RequiredCheck) list.Add("нет программ: " + string.Join(", ", Rules.MissingRequired(r).ToArray()));
                else list.Add(ShortName(c.Name));
            }
            return list;
        }

        static string ShortName(string name)
        {
            int p = name.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? name.Substring(0, p) : name;
        }

        /// <summary>Поиск в окне: ФИО, имя ПК, номер АРМ, IP, MAC, логин, отдел, кабинет; если не нашлось —
        /// установленные программы (тогда viaProgram — какая программа совпала).</summary>
        public static bool Matches(PcRecord r, string q, string room, out string viaProgram)
        {
            viaProgram = null;
            foreach (var v in new[] { r["PcName"], r["Fio"], r["ArmId"], r["Ip"], r["Mac"], r["User"], r["Dept"], room ?? "" })
                if (v.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
            foreach (var s in r.Soft)
                if (s[0].IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) { viaProgram = s[0] + (s[1] != "" ? " " + s[1] : ""); return true; }
            return false;
        }

        /// <summary>«1.2 BUH-03» — номер АРМ и имя ПК без ФИО (для коротких списков).</summary>
        public static string ShortTitle(PcRecord r) => (r["ArmId"] != "" ? r["ArmId"] + " " : "") + r["PcName"];

        /// <summary>Все ПК в порядке реестра: по отделам, внутри — по номеру АРМ.</summary>
        public static List<PcRecord> Sorted(List<PcRecord> recs)
        {
            var order = DeptOrder(recs);
            var res = new List<PcRecord>();
            foreach (var d in order)
            {
                var g = new List<PcRecord>();
                foreach (var r in recs) if (r["Dept"] == d) g.Add(r);
                g.Sort(CompareRec);
                res.AddRange(g);
            }
            var rest = new List<PcRecord>();
            foreach (var r in recs) if (r["Dept"] == "") rest.Add(r);
            rest.Sort(CompareRec);
            res.AddRange(rest);
            return res;
        }

        static string Names(List<PcRecord> list)
        {
            var s = new List<string>();
            foreach (var r in list) s.Add(Title(r));
            return string.Join(", ", s.ToArray());
        }

        static XSheet Summary(List<string> order, List<List<PcRecord>> groups)
        {
            var all = new List<PcRecord>();
            foreach (var g in groups) all.AddRange(g);

            var sh = new XSheet { Name = "Сводка", Widths = new double[] { 58, 10, 110 }, Filter = false };
            sh.Rows.Add(HeaderRow(new[] { "Показатель", "Кол-во ПК", "Компьютеры" }));

            int problemPcs = 0;
            foreach (var r in all) if (Problems(r) > 0) problemPcs++;
            sh.Rows.Add(new[] { new XCell("Всего компьютеров в реестре", Xlsx.Group), new XCell(all.Count, Xlsx.GroupCenter),
                new XCell("сводка на " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"), Xlsx.Group) });

            // --- требует внимания
            sh.Rows.Add(new[] { new XCell("Требует внимания", Xlsx.Group), new XCell(problemPcs, Xlsx.GroupCenter),
                new XCell(problemPcs == 0 ? "замечаний нет" : "компьютеров хотя бы с одним замечанием", Xlsx.Group) });
            foreach (var c in Checks)
            {
                var hit = new List<PcRecord>();
                foreach (var r in all) if (c.Hit(r)) hit.Add(r);
                int st = hit.Count > 0 ? Xlsx.Bad : Xlsx.Good;
                sh.Rows.Add(new[] { new XCell(c.Name, st), new XCell(hit.Count, st), new XCell(Names(hit), st) });
            }

            // --- операционные системы
            sh.Rows.Add(new[] { new XCell("Операционные системы", Xlsx.Group), new XCell("", Xlsx.GroupCenter), new XCell("", Xlsx.Group) });
            var osNames = new List<string>();
            var osPcs = new Dictionary<string, List<PcRecord>>();
            foreach (var r in all)
            {
                string os = r["Os"] != "" ? r["Os"] + (r["OsVer"] != "" ? " " + r["OsVer"] : "") : "нет данных";
                if (!osPcs.ContainsKey(os)) { osPcs[os] = new List<PcRecord>(); osNames.Add(os); }
                osPcs[os].Add(r);
            }
            osNames.Sort((a, b) =>
            {
                int c = osPcs[b].Count.CompareTo(osPcs[a].Count);
                return c != 0 ? c : string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (var os in osNames)
            {
                var pcs = osPcs[os];
                bool old = pcs[0]["Os"] != "" && SupportEnd(pcs[0]["Os"]) < DateTime.Today;
                sh.Rows.Add(new[] { new XCell(os + (old ? " — без поддержки" : ""), old ? Xlsx.Bad : Xlsx.Text),
                    new XCell(pcs.Count, Xlsx.Center), T(Names(pcs)) });
            }

            // --- отделы
            if (order.Count > 1 || (order.Count == 1 && order[0] != NoDept))
            {
                sh.Rows.Add(new[] { new XCell("По отделам", Xlsx.Group), new XCell("", Xlsx.GroupCenter), new XCell("", Xlsx.Group) });
                for (int i = 0; i < order.Count; i++)
                {
                    int bad = 0;
                    foreach (var r in groups[i]) if (Problems(r) > 0) bad++;
                    sh.Rows.Add(new[] { T(order[i]), new XCell(groups[i].Count, Xlsx.Center),
                        new XCell(bad == 0 ? "замечаний нет" : "требуют внимания: " + bad, bad == 0 ? Xlsx.Good : Xlsx.Bad) });
                }
            }
            return sh;
        }
    }
}
