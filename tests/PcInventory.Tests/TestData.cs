using System.Collections.Generic;
using PcInventory;

namespace PcInventory.Tests
{
    static class TestData
    {
        public static PcRecord Pc(string key, string dept, string arm = "", string fio = "", params string[][] printers)
        {
            var r = new PcRecord { Key = key };
            r["PcName"] = key.ToUpperInvariant();
            r["Dept"] = dept;
            r["ArmId"] = arm;
            r["Fio"] = fio;
            r["Cpu"] = "Intel Core i5-10500";
            r["Ram"] = "16 ГБ";
            foreach (var p in printers) r.Parts.Add(new[] { "Принтер", p[0], p[1], "" });
            return r;
        }

        public static string[] NetPrinter(string name) => new[] { name, "драйвер HP Universal; порт " + name + "; сетевой" };
        public static string[] LocalPrinter(string name) => new[] { name, "драйвер Canon; порт USB001" };
        public static string[] IpPrinter(string name, string ip) => new[] { name, "драйвер Kyocera; порт IP_" + ip };

        public static List<PcRecord> List(params PcRecord[] pcs) => new List<PcRecord>(pcs);
    }
}
