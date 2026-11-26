using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace PcInventory
{
    class PcRecord
    {
        public string Key = "";
        /// <summary>Запись прочитана из файла старого формата и очищена — файл нужно перезаписать.</summary>
        public bool Migrated;
        public readonly Dictionary<string, string> F = new Dictionary<string, string>();
        /// <summary>Комплектующие: тип, наименование, характеристики, серийный номер.</summary>
        public List<string[]> Parts = new List<string[]>();
        /// <summary>Программы: название, версия, издатель, дата установки, для кого установлена («все пользователи» или имена).</summary>
        public List<string[]> Soft = new List<string[]>();
        /// <summary>Лицензии: вид, продукт, статус, канал/тип, ключ, действует до, подробности, оценка (ok/bad/"").</summary>
        public List<string[]> Lic = new List<string[]>();
        /// <summary>История изменений: дата сбора, вид (добавлено/удалено/изменено), что, подробности.</summary>
        public List<string[]> Changes = new List<string[]>();

        public string this[string k]
        {
            get { string v; return F.TryGetValue(k, out v) ? v : ""; }
            set { F[k] = Util.Clean(value); }
        }
    }

    /// <summary>Каждый ПК хранится отдельным XML-файлом в папке рядом с exe; Excel каждый раз собирается из них заново.</summary>
    class Store
    {
        readonly string dir;
        public Store(string dir) { this.dir = dir; }

        public List<PcRecord> LoadAll(List<string> errors)
        {
            var list = new List<PcRecord>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.xml"))
            {
                if (Service(Path.GetFileName(f))) continue;
                PcRecord r;
                try { r = Load(f); }
                catch (Exception ex) { errors.Add(Path.GetFileName(f) + ": " + ex.Message); continue; }
                list.Add(r);
                // старый файл хранил источник активации (ключ, KMS-сервер) — переписываем его уже очищенным
                if (r.Migrated)
                    try { Save(r, f); r.Migrated = false; }
                    catch (Exception ex) { errors.Add(Path.GetFileName(f) + ": не удалось обновить до нового формата — " + ex.Message); }
            }
            return list;
        }

        /// <summary>Служебные файлы в папке данных (карта, настройки, отложенные повреждённые копии) — не компьютеры.</summary>
        static bool Service(string name) =>
            string.Equals(name, "карта.xml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "настройки.xml", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf(".повреждён.", StringComparison.OrdinalIgnoreCase) >= 0;

        static PcRecord Load(string path)
        {
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "pc") throw new Exception("неизвестный формат");
            var r = new PcRecord { Key = root.GetAttribute("key") };
            if (r.Key == "") r.Key = Path.GetFileNameWithoutExtension(path);
            foreach (XmlNode n in root.ChildNodes)
            {
                var e = n as XmlElement;
                if (e == null) continue;
                if (e.Name == "f") r.F[e.GetAttribute("n")] = e.InnerText.Replace("\r", ""); // XmlWriter пишет перевод строки как \r\n
                else if (e.Name == "part") r.Parts.Add(Attrs(e, 4));
                else if (e.Name == "sw") r.Soft.Add(Attrs(e, 5));
                else if (e.Name == "lic") r.Lic.Add(Attrs(e, 8));
                else if (e.Name == "chg") r.Changes.Add(Attrs(e, 4));
            }
            int format;
            if (!int.TryParse(root.GetAttribute("format"), out format) || format < 2) { Migrate1(r); r.Migrated = true; }
            return r;
        }

        // ------------------------------------------------------------ перевод данных версии 1.0
        /// <summary>Версия 1.0 писала источник активации (канал, ключ, KMS-сервер, срок продления).
        /// Теперь отчёт показывает только факт активации — старые записи очищаются при чтении.</summary>
        static void Migrate1(PcRecord r)
        {
            r["WinAct"] = StripSource(r["WinAct"]);
            r["OfficeAct"] = StripSource(r["OfficeAct"]);
            foreach (var l in r.Lic)
            {
                if (l[0] == "Антивирус") continue;
                l[3] = ""; l[4] = "";
                var keep = new List<string>();
                foreach (var part in l[6].Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = part.Trim();
                    bool drop = false;
                    foreach (var bad in new[] { "channel", "edition", "KMS", "(R)", "_", "файлов лицензий", "Click-to-Run" })
                        if (p.IndexOf(bad, StringComparison.OrdinalIgnoreCase) >= 0) drop = true;
                    if (!drop) keep.Add(p);
                }
                l[6] = string.Join("; ", keep.ToArray());
                // срок у активированной копии — это срок продления KMS или подписки, т. е. источник активации
                if (l[7] == "ok" && l[6].IndexOf("ознакомительная", StringComparison.Ordinal) < 0) l[5] = "";
                if (l[5] == "бессрочно") l[5] = "";
            }
        }

        static readonly string[] SourceWords =
            { "KMS", "KMS-хост", "MAK", "AVMA", "OEM", "подписка", "ограничена по времени", "без ключа", "Retail", "корпоративная" };

        /// <summary>«Активирована (Retail, ключ ...XXXXX)» → «Активирована»; «осталось N дн.» и пр. сохраняются.</summary>
        static string StripSource(string text)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i];
                if (!s.EndsWith(")")) continue;
                int open = s.LastIndexOf(" (", StringComparison.Ordinal);
                if (open < 0) continue;
                string inner = s.Substring(open + 2, s.Length - open - 3);
                var keep = new List<string>();
                foreach (var part in inner.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = part.Trim();
                    bool drop = p.StartsWith("KMS ") || p.StartsWith("ключ ") || p.StartsWith("до ") || p.StartsWith("вход учётной");
                    foreach (var w in SourceWords) if (p == w) drop = true;
                    if (!drop) keep.Add(p);
                }
                lines[i] = s.Substring(0, open) + (keep.Count > 0 ? " (" + string.Join(", ", keep.ToArray()) + ")" : "");
            }
            return string.Join("\n", lines);
        }

        static string[] Attrs(XmlElement e, int n)
        {
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = e.GetAttribute("v" + i);
            return a;
        }

        /// <summary>Убрать ПК из реестра: файл не стирается, а переносится в папку «удалённые» (вернуть — переложить обратно).</summary>
        public string Remove(PcRecord r)
        {
            string src = Path.Combine(dir, r.Key + ".xml");
            if (!File.Exists(src)) return null;
            string trash = Path.Combine(dir, "удалённые");
            Directory.CreateDirectory(trash);
            string dst = Path.Combine(trash, r.Key + ".xml");
            if (File.Exists(dst)) dst = Path.Combine(trash, r.Key + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml");
            File.Move(src, dst);
            return dst;
        }

        public string Save(PcRecord r)
        {
            Directory.CreateDirectory(dir);
            return Save(r, Path.Combine(dir, r.Key + ".xml"));
        }

        static string Save(PcRecord r, string path)
        {
            string tmp = path + ".tmp";
            var st = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var w = XmlWriter.Create(tmp, st))
            {
                w.WriteStartDocument();
                w.WriteStartElement("pc");
                w.WriteAttributeString("key", r.Key);
                w.WriteAttributeString("format", "2");
                var keys = new List<string>(r.F.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys)
                {
                    w.WriteStartElement("f");
                    w.WriteAttributeString("n", k);
                    w.WriteString(Util.Clean(r.F[k]));
                    w.WriteEndElement();
                }
                Write(w, "part", r.Parts);
                Write(w, "sw", r.Soft);
                Write(w, "lic", r.Lic);
                Write(w, "chg", r.Changes);
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return path;
        }

        static void Write(XmlWriter w, string name, List<string[]> rows)
        {
            foreach (var row in rows)
            {
                w.WriteStartElement(name);
                for (int i = 0; i < row.Length; i++) w.WriteAttributeString("v" + i, Util.Clean(row[i]));
                w.WriteEndElement();
            }
        }
    }
}
