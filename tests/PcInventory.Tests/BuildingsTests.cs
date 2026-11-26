using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using PcInventory;
using Xunit;
using static PcInventory.Tests.TestData;

namespace PcInventory.Tests
{
    public class BuildingsTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "v4pc-bld-" + Guid.NewGuid().ToString("N"));
        public BuildingsTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        static PcRecord In(PcRecord r, string building) { r["Building"] = building; return r; }

        [Fact]
        public void PcBuilding_CreatesBuildingAndDeptRoomInside_DefaultBuildingRemovedWhenEmpty()
        {
            var d = new MapDoc();
            var m = MapLayout.Resolve(d, List(In(Pc("a", "ИТ"), "ЦИТС"), In(Pc("b", "ИТ"), "Склад №2"), In(Pc("c", "Бухгалтерия"), "ЦИТС")));
            Assert.Equal(2, d.Buildings.Count); // «Главное здание» по умолчанию не нужно — все ПК в своих зданиях
            Assert.NotNull(d.BuildingByName("цитс"));
            Assert.Equal("ЦИТС", d.BuildingOf(m.PcRooms["a"]).Name);
            Assert.Equal("Склад №2", d.BuildingOf(m.PcRooms["b"]).Name);
            Assert.NotSame(m.PcRooms["a"], m.PcRooms["b"]); // отдел ИТ в разных зданиях — разные кабинеты
            // кабинеты внутри рамки здания (ниже её заголовка)
            foreach (var f in d.Floors)
            {
                var frame = MapRenderer.Frame(m, f, false);
                foreach (var r in m.RoomsOn(f))
                {
                    Assert.True(frame.Contains(r.Rect));
                    Assert.True(r.Y >= frame.Y + MapRenderer.FrameHead);
                }
            }
        }

        [Fact]
        public void Hidden_ItemsNotOnMap_PcRoomKept()
        {
            var d = new MapDoc();
            var pcs = List(Pc("a", "Склад", printers: new[] { LocalPrinter("Old HP 1020"), LocalPrinter("Canon") }), Pc("b", "Склад"));
            var m = MapLayout.Resolve(d, pcs);
            Assert.Equal(4, m.Items.Count);
            d.Hidden["pr:a|old hp 1020"] = "Old HP 1020";
            d.Hidden["pc:b"] = "B";
            var m2 = MapLayout.Resolve(d, pcs);
            Assert.Equal(2, m2.Items.Count);
            Assert.Null(m2.ItemByKey("pr:a|old hp 1020"));
            Assert.Null(m2.ItemByKey("pc:b"));
            Assert.NotNull(m2.RoomOfPc(pcs[1])); // скрытый ПК остаётся в реестре со своим кабинетом
            Assert.Equal(3, m2.AllPrinters.Count + 1);
            // круговая запись скрытых
            string path = Path.Combine(dir, "карта.xml");
            d.Save(path);
            string err;
            Assert.Equal(2, MapDoc.Load(path, out err).Hidden.Count);
        }

        [Fact]
        public void RemoveRecord_MovesFileToDeleted()
        {
            var s = new Session(dir + Path.DirectorySeparatorChar);
            s.Store.Save(Pc("a", "Склад"));
            s.Store.Save(Pc("b", "Склад"));
            s.Load();
            string moved = s.RemoveRecord(s.All.Find(p => p.Key == "b"));
            Assert.True(File.Exists(moved));
            Assert.Contains("удалённые", moved);
            var again = new Session(s.BaseDir); again.Load();
            Assert.Single(again.All);
            Assert.Empty(again.LoadErrors);
        }

        static MapDoc TwoBuildings()
        {
            var d = new MapDoc();
            d.Buildings.Add(new Building { Id = "b1", Name = "Главный корпус" });
            d.Buildings.Add(new Building { Id = "b2", Name = "Склад" });
            d.Floors.Add(new Floor { Id = "f1", BuildingId = "b1", Name = "1 этаж" });
            d.Floors.Add(new Floor { Id = "f2", BuildingId = "b1", Name = "2 этаж" });
            d.Floors.Add(new Floor { Id = "f3", BuildingId = "b2", Name = "1 этаж" });
            d.Rooms.Add(new Room { Id = "r1", FloorId = "f3", Name = "Приёмка", Dept = "Склад", X = 40, Y = 40, W = 400, H = 200 });
            return d;
        }

        [Fact]
        public void OldMapWithoutBuildings_GetsDefaultBuilding()
        {
            var d = new MapDoc();
            d.Floors.Add(new Floor { Id = "f1", Name = "1 этаж" });
            MapLayout.Resolve(d, List(Pc("a", "Склад")));
            Assert.Single(d.Buildings);
            Assert.Equal(MapDoc.DefaultBuilding, d.Buildings[0].Name);
            Assert.Equal(d.Buildings[0].Id, d.Floors[0].BuildingId);
        }

        [Fact]
        public void RoundTrip_AndTitles()
        {
            var d = TwoBuildings();
            string path = Path.Combine(dir, "карта.xml");
            d.Save(path);
            string err;
            var r = MapDoc.Load(path, out err);
            Assert.Equal(2, r.Buildings.Count);
            Assert.Equal("b2", r.FloorById("f3").BuildingId);
            Assert.Equal(2, r.FloorsOf(r.Buildings[0]).Count);
            Assert.Equal("Склад · 1 этаж · Приёмка", MapLayout.RoomTitle(r, r.RoomById("r1")));
        }

        [Fact]
        public void MoveRoom_ToFloorOfOtherBuilding()
        {
            var d = TwoBuildings();
            var pcs = List(In(Pc("a", "Склад"), "Склад"), In(Pc("b", "Склад"), "Склад"));
            var m = MapLayout.Resolve(d, pcs);
            Assert.Equal("r1", m.PcRooms["a"].Id); // кабинет отдела «Склад» в здании «Склад»
            var room = d.RoomById("r1");
            Assert.Equal(2, MapLayout.MoveRoom(m, room, d.FloorById("f2")).Count); // у обоих ПК здание стало «Главный корпус»
            Assert.Equal("Главный корпус", pcs[0]["Building"]);
            Assert.Equal("f2", room.FloorId);
            Assert.False(room.Auto);
            var m2 = MapLayout.Resolve(d, pcs);
            Assert.Equal(2, m2.ItemsOn(d.FloorById("f2")).Count); // ПК переехали вместе с кабинетом
            Assert.Empty(m2.ItemsOn(d.FloorById("f3")));
        }

        [Fact]
        public void MoveItem_PcAndPrinter()
        {
            var d = TwoBuildings();
            var pcs = List(Pc("a", "Бухгалтерия", printers: new[] { LocalPrinter("Canon") }));
            var m = MapLayout.Resolve(d, pcs);
            var target = d.RoomById("r1");
            var pc = MapLayout.MoveItem(d, m.ItemByKey("pc:a"), target);
            Assert.Equal("r1", pc["RoomId"]);
            Assert.Null(MapLayout.MoveItem(d, m.ItemByKey("pr:a|canon"), target));
            var m2 = MapLayout.Resolve(d, pcs);
            Assert.Equal("r1", m2.ItemByKey("pc:a").Room.Id);
            Assert.Equal("r1", m2.ItemByKey("pr:a|canon").Room.Id);
            Assert.True(target.Rect.Contains(m2.ItemByKey("pr:a|canon").Rect));
            Assert.False(m2.ItemByKey("pc:a").Rect.IntersectsWith(m2.ItemByKey("pr:a|canon").Rect));
        }

        [Fact]
        public void Vsdx_PageNamesWithBuilding_AndIconGeometry()
        {
            var d = TwoBuildings();
            var m = MapLayout.Resolve(d, List(In(Pc("a", "Склад", printers: new[] { LocalPrinter("Canon") }), "Склад")));
            var ns = new XmlNamespaceManager(new NameTable());
            ns.AddNamespace("v", "http://schemas.microsoft.com/office/visio/2012/main");
            XmlDocument pages = null, page3 = null;
            using (var z = new ZipArchive(new MemoryStream(VsdxWriter.Build(m, dir)), ZipArchiveMode.Read))
                foreach (var e in z.Entries)
                {
                    if (e.FullName != "visio/pages/pages.xml" && e.FullName != "visio/pages/page3.xml") continue;
                    var x = new XmlDocument();
                    using (var r = new StreamReader(e.Open(), Encoding.UTF8)) x.LoadXml(r.ReadToEnd());
                    if (e.FullName.EndsWith("pages.xml")) pages = x; else page3 = x;
                }
            Assert.NotNull(pages.SelectSingleNode("//v:Page[@Name='Склад · 1 этаж']", ns));
            // ПК и принтер — картинки-значки без подписи; имя и характеристики — в подсказке (Comment) и «Данных фигуры»
            foreach (var prefix in new[] { "PC.", "Printer." })
            {
                var sh = (XmlElement)page3.SelectSingleNode("//v:Shape[starts-with(@NameU,'" + prefix + "')]", ns);
                Assert.Equal("Foreign", sh.GetAttribute("Type"));
                Assert.NotNull(sh.SelectSingleNode("v:ForeignData/v:Rel", ns));
                Assert.Null(sh.SelectSingleNode("v:Text", ns));
                var comment = (XmlElement)sh.SelectSingleNode("v:Cell[@N='Comment']", ns);
                Assert.Contains(prefix == "PC." ? "A" : "Canon", comment.GetAttribute("V"));
            }
            Assert.Contains("Процессор", ((XmlElement)page3.SelectSingleNode("//v:Shape[starts-with(@NameU,'PC.')]/v:Cell[@N='Comment']", ns)).GetAttribute("V"));
        }
    }
}
