using System;
using System.IO;
using System.Text;
using System.Xml;

namespace PcInventory
{
    /// <summary>Настройки программы — «настройки.xml» в папке данных, переносятся вместе с реестром.
    /// Visio выгружается всегда; остальные форматы карты — по галочкам.</summary>
    class AppSettings
    {
        public bool Png, Drawio, XlsxSheet;

        public static AppSettings Load(string path)
        {
            var s = new AppSettings();
            if (!File.Exists(path)) return s;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var ex = doc.DocumentElement == null ? null : doc.DocumentElement["export"];
                if (ex != null)
                {
                    s.Png = ex.GetAttribute("png") == "1";
                    s.Drawio = ex.GetAttribute("drawio") == "1";
                    s.XlsxSheet = ex.GetAttribute("xlsx") == "1";
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
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
