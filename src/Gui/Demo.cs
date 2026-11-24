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
            return s;
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
