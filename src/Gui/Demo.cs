using System;
using System.Collections.Generic;
using System.IO;

namespace PcInventory.Gui
{
    /// <summary>
    /// Выдуманный реестр для снимков окна (/shot): по ним проверяется внешний вид, и на снимки
    /// (в README, в обсуждения) не попадают сведения о настоящих компьютерах и людях.
    /// </summary>
    static class Demo
    {
        public static Session Session()
        {
            var s = new Session(Path.GetTempPath());
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string old = DateTime.Now.AddDays(-120).ToString("yyyy-MM-dd HH:mm");

            var cur = Pc("BUH-03", "Бухгалтерия", "1.3", "Смирнова Ольга Петровна", now, "Windows 11 Pro 64-bit", "24H2", "16", "SSD", "38", Collector.Ok, Collector.Ok, Collector.Ok, Collector.Ok);
            cur["PcModel"] = "HP ProDesk 400 G7 MT";
            cur["Cpu"] = "Intel Core i5-10500";
            cur["Ram"] = "16 ГБ DDR4 @ 2666MHz (2x8 ГБ)";
            cur["Board"] = "HP 8767 (KBC Version 08.D0.00)";
            cur["Gpu"] = "Intel UHD Graphics 630";
            cur["Disk"] = "476GB Samsung SSD 980 500GB (NVMe SSD)\n931GB WDC WD10EZEX-08WN4A0 (SATA HDD)";
            cur["DiskHealth"] = "Samsung SSD 980 500GB — исправен, износ 3%, 41 °C, наработка 9120 ч\nWDC WD10EZEX-08WN4A0 — исправен, 36 °C, наработка 23544 ч";
            cur["Monitor"] = "HP P24v G4 23.8\" (1920x1080@60Hz)";
            cur["OsBuild"] = "10.0.26100.2314";
            cur["InstallDate"] = "2025-03-14";
            cur["User"] = "CORP\\o.smirnova";
            cur["UserFullName"] = "Смирнова Ольга Петровна";
            cur["Ip"] = "10.0.12.43";
            cur["Mac"] = "3C-52-82-1A-7F-09";
            cur["WinAct"] = "Активирована";
            cur["OfficeAct"] = "Office 2021 Standard — активирована";
            cur["AvAct"] = "Kaspersky Endpoint Security — включён, базы актуальны, лицензия действует";
            cur["Av"] = "Kaspersky Endpoint Security";
            for (int i = 0; i < 87; i++) cur.Soft.Add(new[] { "Программа " + i, "1.0", "", "", Collector.AllUsers });

            s.All = new List<PcRecord>
            {
                Pc("BUH-01", "Бухгалтерия", "1.1", "Ковалёва Анна Сергеевна", now, "Windows 11 Pro 64-bit", "24H2", "16", "SSD", "54", Collector.Ok, Collector.Ok, Collector.Ok, Collector.Ok),
                Pc("BUH-02", "Бухгалтерия", "1.2", "Орлов Денис Игоревич", now, "Windows 10 Pro 64-bit", "22H2", "8", "HDD", "7", Collector.Ok, Collector.Bad, Collector.Ok, Collector.Ok),
                cur,
                Pc("OK-01", "Отдел кадров", "2.1", "Белова Ирина Николаевна", now, "Windows 11 Pro 64-bit", "23H2", "8", "SSD", "41", Collector.Ok, Collector.Ok, Collector.Ok, Collector.Ok),
                Pc("OK-02", "Отдел кадров", "2.2", "Захаров Павел Андреевич", old, "Windows 7 Профессиональная 64-bit", "SP1", "4", "HDD", "22", Collector.Bad, Collector.Ok, Collector.Bad, Collector.Ok),
                Pc("SKLAD-01", "Склад", "3.1", "Ефимов Олег Викторович", now, "Windows 10 Корпоративная LTSC 2021 64-bit", "21H2", "8", "SSD", "63", Collector.Ok, "", Collector.Ok, Collector.Bad),
                Pc("SKLAD-02", "Склад", "3.2", "", now, "Windows 11 Pro 64-bit", "24H2", "16", "SSD", "70", Collector.Ok, "", Collector.Ok, Collector.Ok),
            };
            s.Current = cur;
            s.Prev = cur;
            s.ReadOnly = true;

            // история изменений и программы для контроля (снимки разделов «Этот компьютер» и «Реестр»)
            string d1 = DateTime.Now.AddDays(-35).ToString("yyyy-MM-dd HH:mm"), d2 = DateTime.Now.AddDays(-3).ToString("yyyy-MM-dd HH:mm");
            cur.Changes.Add(new[] { d1, History.Changed, "ОС", "Windows 11 Pro 23H2 → Windows 11 Pro 24H2" });
            cur.Changes.Add(new[] { d2, History.Added, "Накопитель", "WDC WD10EZEX-08WN4A0 (с/н WD-WCC6Y4KJ2X1)" });
            cur.Changes.Add(new[] { d2, History.Removed, "ОЗУ", "Kingston 4 ГБ DDR4 2666 (с/н 7A1B2C3D)" });
            cur.Changes.Add(new[] { d2, History.Added, "ОЗУ", "Kingston 8 ГБ DDR4 2666 (с/н 9F8E7D6C)" });
            cur.Changes.Add(new[] { d2, History.Added, "Программа", "Telegram Desktop 5.8" });
            foreach (var r in s.All)
            {
                r.Soft.Add(new[] { "7-Zip 24.08 (x64)", "24.08", "Igor Pavlov", "", Collector.AllUsers });
                if (r.Key == "SKLAD-02") r.Soft.Add(new[] { "qBittorrent 5.0.2", "5.0.2", "The qBittorrent project", "", Collector.AllUsers });
                if (r.Key == "BUH-02") r.Soft.Add(new[] { "AnyDesk", "9.0.4", "AnyDesk Software GmbH", "", Collector.AllUsers });
            }

            // принтеры: общий сетевой в бухгалтерии, локальный в кадрах, сетевой по IP на складе
            foreach (var r in s.All)
            {
                if (r["Dept"] == "Бухгалтерия") r.Parts.Add(new[] { "Принтер", @"\\srv-01\HP-Buh", @"драйвер HP Universal Printing PCL 6; порт \\srv-01\HP-Buh; сетевой", "" });
                if (r.Key == "OK-02") r.Parts.Add(new[] { "Принтер", "Canon LBP2900", "драйвер Canon LBP2900; порт USB001", "" });
                if (r["Dept"] == "Склад") r.Parts.Add(new[] { "Принтер", "Kyocera ECOSYS M2040dn", "драйвер Kyocera ECOSYS M2040dn KX; порт IP_10.0.12.80", "" });
            }

            // карта: 1 этаж — кабинеты отделов строит программа; 2 этаж — план-подложка и кабинеты, заданные вручную
            var m = s.Map;
            m.Buildings.Add(new Building { Id = "b1", Name = "Главный корпус" });
            m.Buildings.Add(new Building { Id = "b2", Name = "Склад" });
            m.Floors.Add(new Floor { Id = "f1", BuildingId = "b1", Name = "1 этаж" });
            m.Floors.Add(new Floor { Id = "f2", BuildingId = "b1", Name = "2 этаж", Bg = "demo.png", W = 1600, H = 900 });
            m.Floors.Add(new Floor { Id = "f3", BuildingId = "b2", Name = "1 этаж" });
            m.Rooms.Add(new Room { Id = "r1", FloorId = "f2", Name = "Каб. 204", Dept = "Отдел кадров", X = 70, Y = 70, W = 560, H = 230 });
            m.Rooms.Add(new Room { Id = "r2", FloorId = "f2", Name = "Серверная", X = 690, Y = 70, W = 300, H = 230 });
            m.Rooms.Add(new Room { Id = "r3", FloorId = "f3", Name = "Приёмка", Dept = "Склад", X = 40, Y = 40, W = 560, H = 200 });
            s.DemoBg["f2"] = Plan(1600, 900);
            return s;
        }

        /// <summary>Условный план этажа: стены, коридор, двери — чтобы показать подложку на снимках.</summary>
        static System.Drawing.Bitmap Plan(int w, int h)
        {
            var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var wall = new System.Drawing.Pen(System.Drawing.Color.FromArgb(60, 60, 60), 8))
            {
                g.Clear(System.Drawing.Color.FromArgb(236, 238, 240));
                g.DrawRectangle(wall, 40, 40, w - 80, h - 80);
                g.DrawLine(wall, 40, 340, w - 40, 340);
                g.DrawLine(wall, 40, 470, w - 40, 470);
                foreach (int x in new[] { 600, 1010, 1300 }) g.DrawLine(wall, x, 40, x, 340);
                foreach (int x in new[] { 520, 1060 }) g.DrawLine(wall, x, 470, x, h - 40);
                using (var door = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(236, 238, 240)))
                    foreach (int x in new[] { 260, 760, 1120, 1420 }) g.FillRectangle(door, x, 334, 70, 12);
            }
            return bmp;
        }

        static PcRecord Pc(string name, string dept, string id, string fio, string collected, string os, string osVer, string ramGb,
                           string sysKind, string freePct, string win, string office, string av, string disk)
        {
            var r = new PcRecord { Key = name };
            r["PcName"] = name;
            r["Dept"] = dept;
            r["ArmId"] = id;
            r["Fio"] = fio;
            r["Collected"] = collected;
            r["Os"] = os;
            r["OsVer"] = osVer;
            r["RamGb"] = ramGb;
            r["Ram"] = ramGb + " ГБ DDR4";
            r["SysDiskKind"] = sysKind;
            r["SysFreePct"] = freePct;
            r["SysFreeGb"] = "100";
            r["WinActSt"] = win;
            r["OfficeActSt"] = office;
            r["AvActSt"] = av;
            r["DiskHealthSt"] = disk;
            return r;
        }
    }
}
