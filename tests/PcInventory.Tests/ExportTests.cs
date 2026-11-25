using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using PcInventory;
using Xunit;
using static PcInventory.Tests.TestData;

namespace PcInventory.Tests
{
    public class ExportTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "v4pc-export-" + Guid.NewGuid().ToString("N"));
        public ExportTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        /// <summary>Два этажа, на втором — подложка; 3 ПК, сетевой и локальный принтеры.</summary>
        ResolvedMap Sample(bool withBg)
        {
            var doc = new MapDoc();
            doc.Floors.Add(new Floor { Id = "f1", Name = "1 этаж" });
            doc.Floors.Add(new Floor { Id = "f2", Name = "2 этаж", Bg = withBg ? "f2.png" : "" });
            doc.Rooms.Add(new Room { Id = "r1", FloorId = "f2", Name = "204", Dept = "Кадры", X = 60, Y = 60, W = 400, H = 220 });
            if (withBg)
                using (var bmp = new Bitmap(320, 200))
                {
                    using (var g = Graphics.FromImage(bmp)) g.Clear(Color.LightGray);
                    bmp.Save(Path.Combine(dir, "f2.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            var pcs = List(
                Pc("a", "Бухгалтерия", "1.1", "Смирнова Ольга Петровна", NetPrinter(@"\\srv\hp")),
                Pc("b", "Бухгалтерия", "1.2", "Иванов Пётр", NetPrinter(@"\\srv\hp"), LocalPrinter("Canon & Co <USB>")),
                Pc("c", "Кадры", "2.1", "Петрова Анна"));
            return MapLayout.Resolve(doc, pcs);
        }

        static Dictionary<string, byte[]> Unzip(byte[] data)
        {
            var d = new Dictionary<string, byte[]>();
            using (var z = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
                foreach (var e in z.Entries)
                    using (var s = e.Open())
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); d[e.FullName] = ms.ToArray(); }
            return d;
        }

        static XmlDocument Xml(byte[] b)
        {
            var x = new XmlDocument();
            x.LoadXml(Encoding.UTF8.GetString(b));
            return x;
        }

        [Fact]
        public void Render_FloorSizedImage_WithRooms()
        {
            var m = Sample(false);
            var f = m.Doc.Floors[0];
            using (var bmp = MapRenderer.Render(m, f, dir, MapPalette.Light))
            {
                Assert.Equal(f.W, bmp.Width);
                Assert.Equal(f.H, bmp.Height);
                var room = m.RoomsOn(f)[0];
                var px = bmp.GetPixel(room.X + room.W / 2, room.Y + room.H - 4);
                Assert.NotEqual(Color.White.ToArgb(), px.ToArgb());
            }
        }

        [Fact]
        public void Vsdx_StructureAndShapeData()
        {
            var m = Sample(true);
            var parts = Unzip(VsdxWriter.Build(m, dir));
            foreach (var name in new[] { "[Content_Types].xml", "_rels/.rels", "docProps/core.xml", "docProps/app.xml", "visio/document.xml",
                                         "visio/_rels/document.xml.rels", "visio/windows.xml", "visio/pages/pages.xml", "visio/pages/_rels/pages.xml.rels",
                                         "visio/pages/page1.xml", "visio/pages/page2.xml", "visio/media/image2.png", "visio/pages/_rels/page2.xml.rels",
                                         "visio/pages/_rels/page1.xml.rels", "visio/media/icon-pc-ok.png", "visio/media/icon-printer.png" })
                Assert.True(parts.ContainsKey(name), "нет части " + name);
            foreach (var kv in parts)
                if (kv.Key.EndsWith(".xml") || kv.Key.EndsWith(".rels")) Xml(kv.Value);

            var ns = new XmlNamespaceManager(new NameTable());
            ns.AddNamespace("v", "http://schemas.microsoft.com/office/visio/2012/main");
            Assert.Equal(2, Xml(parts["visio/pages/pages.xml"]).SelectNodes("//v:Page", ns).Count);
            Assert.Contains("image/png", Encoding.UTF8.GetString(parts["[Content_Types].xml"]));

            foreach (var fi in new[] { 0, 1 })
            {
                var f = m.Doc.Floors[fi];
                var page = Xml(parts["visio/pages/page" + (fi + 1) + ".xml"]);
                int expected = m.RoomsOn(f).Count + m.ItemsOn(f).Count + (fi == 1 ? 1 : 0);
                Assert.Equal(expected, page.SelectNodes("//v:Shapes/v:Shape", ns).Count);
            }
            var page1 = Xml(parts["visio/pages/page1.xml"]);
            var cpu = page1.SelectSingleNode("//v:Shape[@Name='1.1 · A']/v:Section[@N='Property']/v:Row[@N='Cpu']/v:Cell[@N='Label']", ns);
            Assert.NotNull(cpu);
            Assert.Equal("Процессор", cpu.Attributes["V"].Value);
        }

        /// <summary>Образцы для ручной проверки в LibreOffice, Visio и draw.io: пишутся, только если задана V4PC_SAMPLES.</summary>
        [Fact]
        public void WriteSamples_WhenRequested()
        {
            string outDir = Environment.GetEnvironmentVariable("V4PC_SAMPLES");
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            var m = Sample(true);
            File.WriteAllBytes(Path.Combine(outDir, "Карта_АРМ.vsdx"), VsdxWriter.Build(m, dir));
            File.WriteAllText(Path.Combine(outDir, "Карта_АРМ.drawio"), DrawioWriter.Build(m, dir), new UTF8Encoding(false));
            foreach (var f in m.Doc.Floors)
                using (var bmp = MapRenderer.Render(m, f, dir, MapPalette.Light))
                    bmp.Save(Path.Combine(outDir, "Карта_АРМ_" + f.Name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>Выгрузка по настоящей папке программы (ручная проверка): только если задана V4PC_REALDIR.</summary>
        [Fact]
        public void ExportRealDir_WhenRequested()
        {
            string real = Environment.GetEnvironmentVariable("V4PC_REALDIR");
            if (string.IsNullOrEmpty(real)) return;
            var s = new Session(real.TrimEnd('\\') + "\\");
            s.Load();
            var errors = new List<string>();
            MapExport.Export(s, errors);
            Assert.Empty(errors);
        }

        [Fact]
        public void Drawio_ValidXml_DataAndParents()
        {
            var m = Sample(true);
            var x = new XmlDocument();
            x.LoadXml(DrawioWriter.Build(m, dir));
            Assert.Equal(2, x.SelectNodes("/mxfile/diagram").Count);
            var pc = x.SelectSingleNode("//object[@Процессор]");
            Assert.NotNull(pc);
            Assert.Equal("Intel Core i5-10500", pc.Attributes["Процессор"].Value);
            // значок лежит внутри своего кабинета
            foreach (XmlElement o in x.SelectNodes("//object[starts-with(@id,'item')]"))
            {
                string parent = ((XmlElement)o.SelectSingleNode("mxCell")).GetAttribute("parent");
                Assert.NotNull(x.SelectSingleNode("//object[@id='" + parent + "']"));
            }
            Assert.NotNull(x.SelectSingleNode("//mxCell[starts-with(@id,'bg')]"));
        }
    }
}
