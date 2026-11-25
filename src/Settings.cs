using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace PcInventory
{
    /// <summary>Правило контроля программ: часть названия программы (без учёта регистра) и включено ли оно.</summary>
    class ProgRule
    {
        public string Name = "";
        public bool On = true;
        public ProgRule() { }
        public ProgRule(string name, bool on) { Name = name; On = on; }

        public bool Matches(string program) => On && Name != "" && program.IndexOf(Name, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Настройки программы — «настройки.xml» в папке данных, переносятся вместе с реестром.
    /// Visio выгружается всегда; остальные форматы карты — по галочкам.</summary>
    class AppSettings
    {
        public bool Png, Drawio, XlsxSheet;
        /// <summary>Проверять при запуске, не вышла ли новая версия (GitHub).</summary>
        public bool AutoUpdate = true;
        public List<ProgRule> Forbidden = DefaultForbidden();
        public List<ProgRule> Required = DefaultRequired();

        /// <summary>Примеры запрещённых программ: торрент-клиенты включены; удалённый доступ выключен —
        /// им часто пользуется сам админ, включить можно в «Настройках».</summary>
        public static List<ProgRule> DefaultForbidden() => new List<ProgRule>
        {
            new ProgRule("µTorrent", true), new ProgRule("uTorrent", true), new ProgRule("BitTorrent", true), new ProgRule("qBittorrent", true),
            new ProgRule("MediaGet", true), new ProgRule("Zona", true),
            new ProgRule("AnyDesk", false), new ProgRule("TeamViewer", false), new ProgRule("RustDesk", false), new ProgRule("Ammyy", false),
        };

        /// <summary>Примеры обязательных программ — выключены (у каждой организации свой набор).</summary>
        public static List<ProgRule> DefaultRequired() => new List<ProgRule>
        {
            new ProgRule("1С:Предприятие", false), new ProgRule("Microsoft Office", false),
        };

        public static AppSettings Load(string path)
        {
            var s = new AppSettings();
            if (!File.Exists(path)) return s;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                var ex = root == null ? null : root["export"];
                if (ex != null)
                {
                    s.Png = ex.GetAttribute("png") == "1";
                    s.Drawio = ex.GetAttribute("drawio") == "1";
                    s.XlsxSheet = ex.GetAttribute("xlsx") == "1";
                }
                var up = root == null ? null : root["update"];
                if (up != null) s.AutoUpdate = up.GetAttribute("auto") != "0";
                var pr = root == null ? null : root["programs"];
                if (pr != null)
                {
                    // список задан пользователем — примеры по умолчанию больше не подставляются
                    s.Forbidden = new List<ProgRule>();
                    s.Required = new List<ProgRule>();
                    foreach (XmlNode n in pr.ChildNodes)
                    {
                        var e = n as XmlElement;
                        if (e == null || e.GetAttribute("name").Trim() == "") continue;
                        var rule = new ProgRule(e.GetAttribute("name").Trim(), e.GetAttribute("on") != "0");
                        if (e.Name == "forbidden") s.Forbidden.Add(rule);
                        else if (e.Name == "required") s.Required.Add(rule);
                    }
                }
            }
            catch { } // испорченные настройки — просто значения по умолчанию
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            var st = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var w = XmlWriter.Create(tmp, st))
            {
                w.WriteStartDocument();
                w.WriteStartElement("settings");
                w.WriteAttributeString("format", "1");
                w.WriteStartElement("export");
                w.WriteAttributeString("png", Png ? "1" : "0");
                w.WriteAttributeString("drawio", Drawio ? "1" : "0");
                w.WriteAttributeString("xlsx", XlsxSheet ? "1" : "0");
                w.WriteEndElement();
                w.WriteStartElement("update");
                w.WriteAttributeString("auto", AutoUpdate ? "1" : "0");
                w.WriteEndElement();
                w.WriteStartElement("programs");
                foreach (var r in Forbidden) Rule(w, "forbidden", r);
                foreach (var r in Required) Rule(w, "required", r);
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void Rule(XmlWriter w, string kind, ProgRule r)
        {
            w.WriteStartElement(kind);
            w.WriteAttributeString("name", Util.Clean(r.Name));
            w.WriteAttributeString("on", r.On ? "1" : "0");
            w.WriteEndElement();
        }

        // ------------------------------------------------------------ контроль программ
        /// <summary>Запрещённые программы, найденные на ПК (названия как в списке программ).</summary>
        public List<string> FoundForbidden(PcRecord r)
        {
            var res = new List<string>();
            foreach (var s in r.Soft)
                foreach (var rule in Forbidden)
                    if (rule.Matches(s[0])) { if (!res.Contains(s[0])) res.Add(s[0]); break; }
            return res;
        }

        /// <summary>Обязательные программы (правила), которых на ПК нет.</summary>
        public List<string> MissingRequired(PcRecord r)
        {
            var res = new List<string>();
            foreach (var rule in Required)
            {
                if (!rule.On || rule.Name == "") continue;
                bool found = false;
                foreach (var s in r.Soft) if (rule.Matches(s[0])) { found = true; break; }
                if (!found) res.Add(rule.Name);
            }
            return res;
        }
    }
}
