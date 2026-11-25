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
            var m = MapLayout.Resolve(d, List(Pc("a", "Склад"), Pc("b", "Склад")));
            var room = d.RoomById("r1");
            MapLayout.MoveRoom(d, room, d.FloorById("f2"));
            Assert.Equal("f2", room.FloorId);
            Assert.False(room.Auto);
            var m2 = MapLayout.Resolve(d, List(Pc("a", "Склад"), Pc("b", "Склад")));
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
            var m = MapLayout.Resolve(d, List(Pc("a", "Склад", printers: new[] { LocalPrinter("Canon") })));
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
            // у значка ПК, кроме рамки (Geometry IX=0), есть линии значка (IX=1..3)
            var pc = page3.SelectSingleNode("//v:Shape[starts-with(@NameU,'PC.')]", ns);
            Assert.Equal(4, pc.SelectNodes("v:Section[@N='Geometry']", ns).Count);
            var pr = page3.SelectSingleNode("//v:Shape[starts-with(@NameU,'Printer.')]", ns);
            Assert.Equal(4, pr.SelectNodes("v:Section[@N='Geometry']", ns).Count);
        }
    }
}
