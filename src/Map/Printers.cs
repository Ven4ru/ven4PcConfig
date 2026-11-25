using System;
using System.Collections.Generic;

namespace PcInventory
{
    /// <summary>Принтер на карте. Сетевой (очередь «\\сервер\имя» или порт с IP) — один на всю организацию,
    /// локальный — свой у каждого ПК.</summary>
    class MapPrinter
    {
        public string Key = "", Name = "", Driver = "", Port = "";
        public bool Network;
        public List<PcRecord> Pcs = new List<PcRecord>();

        /// <summary>Короткая подпись значка: имя очереди без сервера.</summary>
        public string Short
        {
            get
            {
                string n = Name;
                if (n.StartsWith(@"\\")) { int i = n.LastIndexOf('\\'); if (i > 1 && i < n.Length - 1) n = n.Substring(i + 1); }
                return n;
            }
        }
    }

    static class Printers
    {
        /// <summary>Виртуальные «принтеры» — печать в файл, факс, отправка: на карту и в реестр не попадают.</summary>
        static readonly string[] Virtual =
            { "microsoft print to pdf", "xps document writer", "onenote", "fax", "факс", "send to", "отправить в", "pdf24", "anydesk", "pdfcreator", "foxit pdf", "adobe pdf" };

        public static bool IsVirtual(string name)
        {
            string l = (name ?? "").ToLowerInvariant();
            foreach (var x in Virtual) if (l.Contains(x)) return true;
            return false;
        }

        /// <summary>Принтеры из комплектующих ПК (тип «Принтер»), сетевые склеены. Порядок — как у ПК.</summary>
        public static List<MapPrinter> FromPcs(List<PcRecord> pcs)
        {
            var list = new List<MapPrinter>();
            var byKey = new Dictionary<string, MapPrinter>();
            foreach (var pc in pcs)
                foreach (var part in pc.Parts)
                {
                    if (part.Length < 3 || part[0] != "Принтер") continue;
                    string name = part[1];
                    if (name == "" || IsVirtual(name)) continue;
                    string driver = "", port = "";
                    bool net = false;
                    foreach (var piece in part[2].Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (piece.StartsWith("драйвер ")) driver = piece.Substring(8);
                        else if (piece.StartsWith("порт ")) port = piece.Substring(5);
                        else if (piece == "сетевой") net = true;
                    }
                    string ip = IpOf(port);
                    string key;
                    if (name.StartsWith(@"\\")) { key = "pr:" + name.ToLowerInvariant(); net = true; }
                    else if (ip != "") { key = "pr:ip:" + ip; net = true; }
                    else key = "pr:" + pc.Key + "|" + name.ToLowerInvariant();

                    MapPrinter p;
                    if (!byKey.TryGetValue(key, out p))
                    {
                        p = new MapPrinter { Key = key, Name = name, Driver = driver, Port = port, Network = net };
                        byKey[key] = p;
                        list.Add(p);
                    }
                    if (!p.Pcs.Contains(pc)) p.Pcs.Add(pc);
                }
            return list;
        }

        /// <summary>IP-адрес из имени порта: «192.168.1.50», «IP_192.168.1.50», «192.168.1.50:9100».</summary>
        static string IpOf(string port)
        {
            string p = port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) ? port.Substring(3) : port;
            int colon = p.IndexOf(':');
            if (colon > 0) p = p.Substring(0, colon);
            var parts = p.Split('.');
            if (parts.Length != 4) return "";
            foreach (var x in parts)
            {
                int n;
                if (!int.TryParse(x, out n) || n < 0 || n > 255) return "";
            }
            return p;
        }
    }
}
