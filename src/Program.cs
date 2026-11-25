using System;
using System.Collections.Generic;
using System.IO;

namespace PcInventory
{
    static class Program
    {
        public const string XlsxName = "Реестр_АРМ.xlsx";
        public const string DataDirName = "ven4PcConfig_данные";

        public static readonly string Version = "1.4.0";

        [STAThread]
        static int Main(string[] args)
        {
            bool auto = false, rebuild = false, noPause = false, console = false, help = false, live = false;
            string shot = null;
            for (int i = 0; i < args.Length; i++)
            {
                string x = args[i].TrimStart('/', '-').ToLowerInvariant();
                if (x == "auto" || x == "silent") { auto = true; noPause = true; console = true; }
                else if (x == "rebuild") { rebuild = true; console = true; }
                else if (x == "nopause") noPause = true;
                else if (x == "console") console = true;
                else if (x == "shot" && i + 1 < args.Length) shot = args[++i]; // снимки окна на демо-данных (для проверки вида)
                else if (x == "shotlive" && i + 1 < args.Length) { shot = args[++i]; live = true; } // снимки на данных этого ПК, без сохранения
                else if (x == "?" || x == "h" || x == "help") { help = true; console = true; }
            }

            // Без ключей — окно. С ключами /auto, /rebuild, /console — прежний консольный режим (для скриптов и GPO).
            if (!console)
            {
                Gui.App.Run(AppDomain.CurrentDomain.BaseDirectory, shot, live);
                return 0;
            }

            ConsoleHost.Attach();
            if (help)
            {
                Console.WriteLine("ven4PcConfig " + Version + " — сбор сведений об АРМ в реестр Excel.\n" +
                                  "  (без параметров)  окно программы\n" +
                                  "  /console          то же в консоли, с вопросами (отдел, номер, ФИО)\n" +
                                  "  /auto             собрать без вопросов (значения берутся из прошлого запуска)\n" +
                                  "  /rebuild          только пересобрать Excel из сохранённых данных\n" +
                                  "  /nopause          не ждать нажатия клавиши в конце");
                return 0;
            }

            try { Console.Title = "ven4PcConfig — сбор сведений об АРМ"; } catch { }
            int code;
            try { code = Run(AppDomain.CurrentDomain.BaseDirectory, auto, rebuild); }
            catch (Exception ex)
            {
                Color(ConsoleColor.Red, "\nОШИБКА: " + ex.Message);
                code = 1;
            }
            if (!noPause)
            {
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                try { Console.ReadKey(true); } catch { }
            }
            return code;
        }

        static int Run(string baseDir, bool auto, bool rebuild)
        {
            Color(ConsoleColor.Cyan, "=== ven4PcConfig " + Version + ": сбор сведений об АРМ ===");
            var s = new Session(baseDir);
            s.Load();
            foreach (var e in s.LoadErrors) Color(ConsoleColor.DarkYellow, "Не прочитан файл данных: " + e);

            if (!rebuild)
            {
                Console.Write("Сбор данных: ");
                bool first = true;
                var rec = s.Collect(step => { Console.Write((first ? "" : ", ") + step); first = false; });
                Console.WriteLine(" — готово.\n");

                PrintSummary(rec);
                foreach (var e in s.CollectErrors) Color(ConsoleColor.DarkYellow, "  ! " + e);
                if (rec["SoftSkipped"] != "")
                    Color(ConsoleColor.DarkYellow, "  ! Программы пользователей " + rec["SoftSkipped"] +
                        " не прочитаны — для этого запустите программу от имени администратора");

                if (!auto) Ask(rec, s.Others(), s.Prev);

                string saved = s.SaveCurrent();
                Color(ConsoleColor.Green, "\nДанные ПК сохранены: " + saved);
            }

            if (s.All.Count == 0) { Console.WriteLine("Нет сохранённых данных — Excel не создан."); return 0; }
            bool busy;
            string path = s.BuildXlsx(out busy);
            if (busy) Color(ConsoleColor.Yellow, XlsxName + " открыт в другой программе — реестр сохранён в отдельный файл.");
            Color(ConsoleColor.Green, "Реестр Excel: " + path + "  (ПК в реестре: " + s.All.Count + ")");
            return 0;
        }

        static void PrintSummary(PcRecord r)
        {
            string[][] lines =
            {
                new[] { "Компьютер", r["PcName"] + (r["PcModel"] != "" ? "  [" + r["PcModel"] + "]" : "") },
                new[] { "Процессор", r["Cpu"] }, new[] { "ОЗУ", r["Ram"] }, new[] { "Мат. плата", r["Board"] },
                new[] { "Видео", r["Gpu"] }, new[] { "Накопители", r["Disk"] }, new[] { "Сост. дисков", r["DiskHealth"] }, new[] { "Мониторы", r["Monitor"] },
                new[] { "ОС", r["Os"] + "  " + r["OsVer"] + "  (" + r["OsBuild"] + ")" }, new[] { "Установлена", r["InstallDate"] },
                new[] { "Антивирус", r["AvAct"] != "" ? r["AvAct"] : r["Av"] }, new[] { "Активация ОС", r["WinAct"] },
                new[] { "Офис", r["OfficeAct"] != "" ? r["OfficeAct"] : r["Office"] }, new[] { "Пользователь", r["User"] + (r["UserFullName"] != "" ? " (" + r["UserFullName"] + ")" : "") },
                new[] { "IP / MAC", r["Ip"].Replace("\n", ", ") + "  " + r["Mac"].Replace("\n", ", ") },
                new[] { "Программы", r.Soft.Count.ToString() },
            };
            foreach (var l in lines)
            {
                string v = l[1].Replace("\n", "\n" + new string(' ', 15));
                Console.WriteLine("  " + l[0].PadRight(13) + v);
            }
        }

        // ------------------------------------------------------------ диалог
        static void Ask(PcRecord rec, List<PcRecord> others, PcRecord prev)
        {
            Color(ConsoleColor.Cyan, "\nУточните данные (Enter — оставить значение в [скобках], «-» — очистить):");

            var depts = Report.DeptOrder(others);
            if (depts.Count > 0)
            {
                Console.WriteLine("  Отделы в реестре:");
                for (int i = 0; i < depts.Count; i++) Console.WriteLine("    " + (i + 1) + ". " + depts[i]);
            }
            string d = Prompt("Отдел (номер из списка или название)", rec["Dept"]);
            int num;
            if (int.TryParse(d, out num) && num >= 1 && num <= depts.Count) d = depts[num - 1];
            bool deptChanged = d != rec["Dept"];
            rec["Dept"] = d;

            string idDef = rec["ArmId"];
            if (idDef == "" || deptChanged) idDef = Report.SuggestId(others, d);
            rec["ArmId"] = Prompt("Номер АРМ (например 1.2)", idDef);
            rec["Fio"] = Prompt("ФИО сотрудника", rec["Fio"]);
            rec["Note"] = Prompt("Примечание", rec["Note"]);
        }

        static string Prompt(string label, string def)
        {
            Console.Write("  " + label + (def != "" ? " [" + def + "]" : "") + ": ");
            string s = Console.ReadLine();
            if (s == null) return def;
            s = s.Trim();
            if (s == "") return def;
            if (s == "-") return "";
            return s;
        }

        public static void Color(ConsoleColor c, string text)
        {
            var old = Console.ForegroundColor;
            try { Console.ForegroundColor = c; Console.WriteLine(text); }
            finally { Console.ForegroundColor = old; }
        }
    }

    /// <summary>Построение книги Excel из всех сохранённых ПК.</summary>
    static partial class Report
    {
        const string NoDept = "Без отдела";

        static readonly string[] MainHeaders =
        {
            "№ п.п.", "ID + PC Name", "Процессор", "ОЗУ", "Motherboard", "Graphics", "Накопитель", "Монитор",
            "Операционная система", "OS ver.", "Дата установки", "Антивирус", "Примечание",
            "Отдел", "Кабинет", "ФИО", "Модель ПК", "Серийный номер", "Активация Windows", "Офисный пакет", "Активация Office",
            "Антивирус: состояние и лицензия", "Состояние дисков", "Сборка ОС", "BIOS",
            "Пользователь (логин)", "Домен / раб. группа", "IP-адрес", "MAC-адрес", "Дата сбора",
        };
        static readonly double[] MainWidths = { 6, 34, 30, 26, 26, 28, 42, 34, 28, 9, 12, 22, 20, 18, 20, 26, 26, 18, 34, 30, 44, 44, 48, 16, 28, 22, 24, 16, 19, 16 };

        /// <summary>Сравнение номеров вида 1.2, 1.10, 2.1.</summary>
        public static int CompareId(string a, string b)
        {
            if (a == b) return 0;
            if (a == "") return 1;
            if (b == "") return -1;
            string[] pa = a.Split('.', ',', '-', ' '), pb = b.Split('.', ',', '-', ' ');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                if (i >= pa.Length) return -1;
                if (i >= pb.Length) return 1;
                int x, y;
                bool nx = int.TryParse(pa[i], out x), ny = int.TryParse(pb[i], out y);
                int c = nx && ny ? x.CompareTo(y) : string.Compare(pa[i], pb[i], StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
            }
            return 0;
        }

        static string DeptOf(PcRecord r) => r["Dept"] == "" ? NoDept : r["Dept"];

        static int CompareRec(PcRecord a, PcRecord b)
        {
            int c = CompareId(a["ArmId"], b["ArmId"]);
            if (c != 0) return c;
            return string.Compare(a["PcName"], b["PcName"], StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Порядок отделов: по наименьшему номеру АРМ в отделе, «Без отдела» — в конце.</summary>
        public static List<string> DeptOrder(List<PcRecord> recs)
        {
            var sorted = new List<PcRecord>(recs);
            sorted.Sort(CompareRec);
            var res = new List<string>();
            foreach (var r in sorted)
                if (r["Dept"] != "" && !res.Contains(r["Dept"])) res.Add(r["Dept"]);
            return res;
        }

        public static string SuggestId(List<PcRecord> recs, string dept)
        {
            int deptNo = 0, maxSub = 0, maxDept = 0;
            foreach (var r in recs)
            {
                string[] p = r["ArmId"].Split('.');
                int a, b;
                if (p.Length < 1 || !int.TryParse(p[0], out a)) continue;
                maxDept = Math.Max(maxDept, a);
                if (dept != "" && r["Dept"] == dept)
                {
                    deptNo = a;
                    if (p.Length > 1 && int.TryParse(p[1], out b)) maxSub = Math.Max(maxSub, b);
                }
            }
            if (dept == "") return "";
            if (deptNo == 0) return (maxDept + 1) + ".1";
            return deptNo + "." + (maxSub + 1);
        }

        static string Title(PcRecord r)
        {
            string t = r["PcName"];
            if (r["ArmId"] != "") t = r["ArmId"] + " - " + t;
            if (r["Fio"] != "") t += " (" + r["Fio"] + ")";
            return t;
        }

        /// <param name="map">карта: из неё колонка «Кабинет»; null — колонка пустая</param>
        /// <param name="mapDir">папка подложек; mapSheet — добавить лист «Карта» с картинками этажей</param>
        public static byte[] Build(List<PcRecord> recs, ResolvedMap map = null, string mapDir = null, bool mapSheet = false)
        {
            // группировка по отделам
            var order = DeptOrder(recs);
            bool anyNoDept = false;
            foreach (var r in recs) if (r["Dept"] == "") anyNoDept = true;
            if (anyNoDept) order.Add(NoDept);
            var groups = new List<List<PcRecord>>();
            foreach (var d in order)
            {
                var g = new List<PcRecord>();
                foreach (var r in recs) if (DeptOf(r) == d) g.Add(r);
                g.Sort(CompareRec);
                groups.Add(g);
            }
            bool showGroups = order.Count > 1 || (order.Count == 1 && order[0] != NoDept);

            // --- Реестр
            var main = new XSheet { Name = "Реестр", Widths = MainWidths };
            main.Rows.Add(HeaderRow(MainHeaders));
            int n = 0;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                if (showGroups)
                {
                    var gr = new XCell[MainHeaders.Length];
                    for (int i = 0; i < gr.Length; i++) gr[i] = new XCell("", Xlsx.Group);
                    gr[1] = new XCell(order[gi], Xlsx.Group);
                    gr[13] = new XCell(order[gi], Xlsx.Group);
                    main.Rows.Add(gr);
                }
                foreach (var r in groups[gi])
                {
                    n++;
                    object inst = r["InstallDate"];
                    DateTime dt;
                    if (DateTime.TryParseExact(r["InstallDate"], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dt)) inst = dt;
                    object coll = r["Collected"];
                    if (DateTime.TryParseExact(r["Collected"], "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out dt)) coll = dt;
                    main.Rows.Add(new[]
                    {
                        new XCell(n, Xlsx.Center), T(Title(r)), T(r["Cpu"]), T(r["Ram"]), T(r["Board"]), T(r["Gpu"]), T(r["Disk"]),
                        T(r["Monitor"]), T(r["Os"]), new XCell(r["OsVer"], Xlsx.Center), new XCell(inst, Xlsx.Date), T(r["Av"]), T(r["Note"]),
                        T(r["Dept"]), T(map != null ? map.RoomTitle(map.RoomOfPc(r)) : ""), T(r["Fio"]), T(r["PcModel"]), T(r["Serial"]), G(r["WinAct"], r["WinActSt"]), T(r["Office"]),
                        G(r["OfficeAct"], r["OfficeActSt"]), G(r["AvAct"], r["AvActSt"]), G(r["DiskHealth"], r["DiskHealthSt"]), T(r["OsBuild"]), T(r["Bios"]),
                        T(r["User"]), T(r["Domain"]), T(r["Ip"]), T(r["Mac"]), new XCell(coll, Xlsx.DateTimeSt),
                    });
                }
            }

            // --- Комплектующие и ПО
            var parts = new XSheet { Name = "Комплектующие", Widths = new double[] { 18, 34, 14, 40, 60, 24 } };
            parts.Rows.Add(HeaderRow(new[] { "Отдел", "ПК", "Тип", "Наименование", "Характеристики", "Серийный номер" }));
            var lic = new XSheet { Name = "Лицензии", Widths = new double[] { 18, 34, 12, 40, 50, 13, 60 } };
            lic.Rows.Add(HeaderRow(new[] { "Отдел", "ПК", "Вид", "Продукт", "Статус", "Действует до", "Подробности" }));
            var soft = new XSheet { Name = "Программы", Widths = new double[] { 18, 34, 55, 18, 32, 14, 24 } };
            soft.Rows.Add(HeaderRow(new[] { "Отдел", "ПК", "Программа", "Версия", "Издатель", "Дата установки", "Установлено для" }));
            foreach (var g in groups)
                foreach (var r in g)
                {
                    string title = Title(r);
                    foreach (var p in r.Parts)
                        parts.Rows.Add(new[] { T(r["Dept"]), T(title), T(p[0]), T(p[1]), T(p[2]), T(p[3]) });
                    foreach (var l in r.Lic)
                    {
                        object u = l[5];
                        DateTime dt;
                        if (DateTime.TryParseExact(l[5], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dt)) u = dt;
                        lic.Rows.Add(new[] { T(r["Dept"]), T(title), T(l[0]), T(l[1]), G(l[2], l[7]),
                            u is DateTime ? new XCell(u, Xlsx.Date) : new XCell(u, Xlsx.Center), T(l[6]) });
                    }
                    foreach (var s in r.Soft)
                    {
                        object d = s[3];
                        DateTime dt;
                        if (DateTime.TryParseExact(s[3], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dt)) d = dt;
                        soft.Rows.Add(new[] { T(r["Dept"]), T(title), T(s[0]), T(s[1]), T(s[2]), new XCell(d, Xlsx.Date), T(s.Length > 4 ? s[4] : "") });
                    }
                }

            // --- Изменения: история по всем ПК, новые сверху
            var chg = new XSheet { Name = "Изменения", Widths = new double[] { 16, 18, 34, 12, 16, 80 } };
            chg.Rows.Add(HeaderRow(new[] { "Дата сбора", "Отдел", "ПК", "Изменение", "Что", "Подробности" }));
            var allChanges = new List<KeyValuePair<PcRecord, string[]>>();
            foreach (var g in groups) foreach (var r in g) foreach (var c in r.Changes) allChanges.Add(new KeyValuePair<PcRecord, string[]>(r, c));
            allChanges.Sort((a, b) => string.CompareOrdinal(b.Value[0], a.Value[0]));
            foreach (var kv in allChanges)
            {
                object d = kv.Value[0];
                DateTime dt;
                if (DateTime.TryParseExact(kv.Value[0], "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out dt)) d = dt;
                int st = kv.Value[1] == History.Removed ? Xlsx.Bad : kv.Value[1] == History.Added ? Xlsx.Good : Xlsx.Text;
                chg.Rows.Add(new[] { new XCell(d, Xlsx.DateTimeSt), T(kv.Key["Dept"]), T(Title(kv.Key)), new XCell(kv.Value[1], st), T(kv.Value[2]), T(kv.Value[3]) });
            }

            var sheets = new List<XSheet> { Summary(order, groups), main, lic, parts, soft, chg };
            if (mapSheet && map != null) sheets.Add(MapSheet(map, mapDir));
            return Xlsx.Build(sheets);
        }

        /// <summary>Лист «Карта»: по этажу — строка с названием и картинка этажа под ней (светлая палитра, как PNG).</summary>
        static XSheet MapSheet(ResolvedMap map, string mapDir)
        {
            var sh = new XSheet { Name = "Карта", Widths = new double[] { 60 }, Filter = false, Freeze = false };
            const int RowPx = 18, MaxW = 1100;
            foreach (var f in map.Doc.Floors)
            {
                if (sh.Rows.Count > 0) sh.Rows.Add(new XCell[0]);
                sh.Rows.Add(new[] { new XCell(map.Doc.FloorTitle(f), Xlsx.Group) });
                using (var bmp = MapRenderer.Render(map, f, mapDir, MapPalette.Light, true))
                {
                    int w = bmp.Width, h = bmp.Height;
                    if (w > MaxW) { h = h * MaxW / w; w = MaxW; }
                    sh.Images.Add(new XImage { Png = MapRenderer.Png(bmp), Row = sh.Rows.Count, Col = 0, W = w, H = h });
                    for (int k = 0; k < (h + RowPx - 1) / RowPx + 1; k++) sh.Rows.Add(new XCell[0]);
                }
            }
            return sh;
        }

        static XCell T(string s) => new XCell(s ?? "", Xlsx.Text);

        /// <summary>Ячейка с подсветкой: зелёная — всё в порядке, красная — требует внимания.</summary>
        static XCell G(string s, string grade) =>
            new XCell(s ?? "", s == "" ? Xlsx.Text : grade == Collector.Ok ? Xlsx.Good : grade == Collector.Bad ? Xlsx.Bad : Xlsx.Text);

        static XCell[] HeaderRow(string[] h)
        {
            var row = new XCell[h.Length];
            for (int i = 0; i < h.Length; i++) row[i] = new XCell(h[i], Xlsx.Header);
            return row;
        }
    }
}
