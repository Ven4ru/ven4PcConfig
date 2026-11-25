using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using PcInventory;
using Xunit;
using static PcInventory.Tests.TestData;

namespace PcInventory.Tests
{
    public class SessionExportTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "v4pc-session-" + Guid.NewGuid().ToString("N"));
        public SessionExportTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        Session Prepared(bool all)
        {
            var s = new Session(dir + Path.DirectorySeparatorChar);
            s.Store.Save(Pc("a", "Бухгалтерия", "1.1", "Смирнова Ольга", NetPrinter(@"\\srv\hp")));
            s.Store.Save(Pc("b", "Склад", "2.1", "Иванов Пётр"));
            s.Settings = new AppSettings { Png = all, Drawio = all, XlsxSheet = all };
            s.SaveSettings();
            s.Load();
            return s;
        }

        [Fact]
        public void Export_AllFormats()
        {
            var s = Prepared(true);
            var errors = new List<string>();
            var files = MapExport.Export(s, errors);
            Assert.Empty(errors);
            Assert.Contains(Path.Combine(s.BaseDir, "Карта_АРМ.vsdx"), files);
            Assert.True(File.Exists(Path.Combine(s.BaseDir, "Карта_АРМ.drawio")));
            Assert.True(File.Exists(Path.Combine(s.BaseDir, "Карта_АРМ_1 этаж.png")));
            Assert.True(File.Exists(s.XlsxPath));
            // карта записана: кабинеты отделов остаются на своих местах при следующем запуске
            Assert.True(File.Exists(s.MapPath));
        }

        [Fact]
        public void Export_OnlyVisioByDefault()
        {
            var s = Prepared(false);
            var files = MapExport.Export(s, new List<string>());
            Assert.Single(files);
            Assert.False(File.Exists(Path.Combine(s.BaseDir, "Карта_АРМ.drawio")));
        }

        [Fact]
        public void Xlsx_MapSheetWithImage_AndRoomColumn()
        {
            var s = Prepared(true);
            bool busy;
            string path = s.BuildXlsx(out busy);
            var parts = new Dictionary<string, string>();
            using (var z = ZipFile.OpenRead(path))
                foreach (var e in z.Entries)
                    using (var r = new StreamReader(e.Open(), Encoding.UTF8))
                        parts[e.FullName] = e.FullName.EndsWith(".png") ? "" : r.ReadToEnd();
            Assert.True(parts.ContainsKey("xl/media/image1.png"));
            Assert.True(parts.ContainsKey("xl/drawings/drawing1.xml"));
            Assert.True(parts.ContainsKey("xl/drawings/_rels/drawing1.xml.rels"));
            Assert.True(parts.ContainsKey("xl/worksheets/_rels/sheet6.xml.rels"));
            Assert.Contains("<drawing r:id=\"rId1\"/>", parts["xl/worksheets/sheet6.xml"]);
            Assert.Contains("drawing+xml", parts["[Content_Types].xml"]);
            foreach (var kv in parts)
                if (kv.Key.EndsWith(".xml") || kv.Key.EndsWith(".rels")) new XmlDocument().LoadXml(kv.Value);
            // «Кабинет» сразу после «Отдел» на листе «Реестр»
            string reg = parts["xl/worksheets/sheet2.xml"];
            int dept = reg.IndexOf(">Отдел<", StringComparison.Ordinal), room = reg.IndexOf(">Кабинет<", StringComparison.Ordinal), fio = reg.IndexOf(">ФИО<", StringComparison.Ordinal);
            Assert.True(dept > 0 && room > dept && fio > room);
            Assert.Contains(">Склад<", reg);
        }
    }
}
