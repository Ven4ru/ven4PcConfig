using System.Collections.Generic;
using PcInventory;
using Xunit;
using static PcInventory.Tests.TestData;

namespace PcInventory.Tests
{
    public class MapLayoutTests
    {
        [Fact]
        public void AutoRoomsPerDept_OnFirstFloor()
        {
            var doc = new MapDoc();
            var m = MapLayout.Resolve(doc, List(Pc("a", "Бухгалтерия"), Pc("b", "Бухгалтерия"), Pc("c", "Склад")));
            Assert.Single(doc.Floors);
            Assert.Equal("1 этаж", doc.Floors[0].Name);
            Assert.Equal(2, doc.Rooms.Count);
            Assert.All(doc.Rooms, r => Assert.True(r.Auto));
            Assert.Same(m.PcRooms["a"], m.PcRooms["b"]);
            Assert.NotSame(m.PcRooms["a"], m.PcRooms["c"]);
            Assert.False(doc.Rooms[0].Rect.IntersectsWith(doc.Rooms[1].Rect));
        }

        [Fact]
        public void NoDept_GoesToNoDeptRoom()
        {
            var doc = new MapDoc();
            var m = MapLayout.Resolve(doc, List(Pc("a", "")));
            Assert.Equal(MapLayout.NoDept, m.PcRooms["a"].Name);
        }

        [Fact]
        public void RoomId_Respected_AndDeletedRoomFallsBackToDept()
        {
            var doc = new MapDoc();
            doc.Floors.Add(new Floor { Id = "f1", Name = "1 этаж" });
            doc.Rooms.Add(new Room { Id = "r5", FloorId = "f1", Name = "204", X = 40, Y = 40, W = 300, H = 200 });
            var a = Pc("a", "Бухгалтерия"); a["RoomId"] = "r5";
            var b = Pc("b", "Бухгалтерия"); b["RoomId"] = "r99";
            var m = MapLayout.Resolve(doc, List(a, b));
            Assert.Equal("r5", m.PcRooms["a"].Id);
            Assert.Equal("Бухгалтерия", m.PcRooms["b"].Name);
            Assert.True(m.PcRooms["b"].Auto);
        }

        [Fact]
        public void AdminRoomBoundToDept_UsedInsteadOfAuto()
        {
            var doc = new MapDoc();
            doc.Floors.Add(new Floor { Id = "f1", Name = "1 этаж" });
            doc.Rooms.Add(new Room { Id = "r1", FloorId = "f1", Name = "Каб. 12", Dept = "Склад", X = 40, Y = 40, W = 300, H = 200 });
            var m = MapLayout.Resolve(doc, List(Pc("a", "склад")));
            Assert.Equal("r1", m.PcRooms["a"].Id);
            Assert.Single(doc.Rooms);
        }

        [Fact]
        public void SavedPlace_Respected()
        {
            var doc = new MapDoc();
            doc.Floors.Add(new Floor { Id = "f1", Name = "1 этаж" });
            doc.Rooms.Add(new Room { Id = "r1", FloorId = "f1", Name = "Склад", Dept = "Склад", X = 100, Y = 100, W = 400, H = 300 });
            doc.Places["pc:a"] = new Place { Item = "pc:a", RoomId = "r1", X = 200, Y = 150 };
            var m = MapLayout.Resolve(doc, List(Pc("a", "Склад"), Pc("b", "Склад")));
            Assert.Equal(300, m.ItemByKey("pc:a").Rect.X);
            Assert.Equal(250, m.ItemByKey("pc:a").Rect.Y);
            Assert.False(m.ItemByKey("pc:b").Rect.IntersectsWith(m.ItemByKey("pc:a").Rect));
        }

        [Fact]
        public void NetworkPrinter_OneIcon_LocalPerPc_VirtualSkipped()
        {
            var a = Pc("a", "Склад", printers: new[] { NetPrinter(@"\\srv\HP-Sklad"), LocalPrinter("Canon LBP"), LocalPrinter("Microsoft Print to PDF") });
            var b = Pc("b", "Склад", printers: new[] { NetPrinter(@"\\SRV\hp-sklad"), LocalPrinter("Canon LBP"), IpPrinter("Kyocera", "10.0.0.50") });
            var c = Pc("c", "Кадры", printers: new[] { IpPrinter("Kyocera 2", "10.0.0.50") });
            var printers = Printers.FromPcs(List(a, b, c));
            Assert.Equal(4, printers.Count); // общий сетевой, 2 локальных Canon, общий по IP
            var net = printers.Find(p => p.Key == @"pr:\\srv\hp-sklad");
            Assert.Equal(2, net.Pcs.Count);
            Assert.Equal("HP-Sklad", net.Short);
            Assert.Equal(2, printers.Find(p => p.Key == "pr:ip:10.0.0.50").Pcs.Count);
            Assert.DoesNotContain(printers, p => p.Name.Contains("PDF"));
        }

        [Fact]
        public void AutoRoom_FitsAllIcons()
        {
            var pcs = new List<PcRecord>();
            for (int i = 0; i < 7; i++) pcs.Add(Pc("p" + i, "Бухгалтерия"));
            var doc = new MapDoc();
            var m = MapLayout.Resolve(doc, pcs);
            var room = doc.Rooms[0];
            foreach (var it in m.Items) Assert.True(room.Rect.Contains(it.Rect));
            Assert.Empty(m.Overflow);
        }

        [Fact]
        public void Reset_KeepsAdminRooms()
        {
            var doc = new MapDoc();
            doc.Floors.Add(new Floor { Id = "f1", Name = "1 этаж" });
            doc.Rooms.Add(new Room { Id = "r1", FloorId = "f1", Name = "Каб. 12", X = 40, Y = 40, W = 300, H = 200 });
            MapLayout.Resolve(doc, List(Pc("a", "Склад")));
            doc.Places["pc:a"] = new Place { Item = "pc:a", RoomId = "r1" };
            Assert.Equal(2, doc.Rooms.Count);
            MapLayout.Reset(doc);
            Assert.Single(doc.Rooms);
            Assert.Empty(doc.Places);
        }

        [Fact]
        public void ShortFio()
        {
            Assert.Equal("Смирнова О. П.", MapLayout.ShortFio("Смирнова Ольга Петровна"));
            Assert.Equal("Иванов", MapLayout.ShortFio("Иванов"));
        }
    }
}
