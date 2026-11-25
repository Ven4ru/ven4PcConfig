using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PcInventory;
using PcInventory.Gui;
using Xunit;
using static PcInventory.Tests.TestData;

namespace PcInventory.Tests
{
    /// <summary>Холст без показа окна: мышь подаётся напрямую в обработчики.</summary>
    class TestCanvas : MapCanvas
    {
        public void Down(Point p, MouseButtons b = MouseButtons.Left) => OnMouseDown(new MouseEventArgs(b, 1, p.X, p.Y, 0));
        public void Move(Point p, MouseButtons b = MouseButtons.Left) => OnMouseMove(new MouseEventArgs(b, 0, p.X, p.Y, 0));
        public void Up(Point p, MouseButtons b = MouseButtons.Left) => OnMouseUp(new MouseEventArgs(b, 1, p.X, p.Y, 0));
        public void Drag(Point from, Point to)
        {
            Down(from);
            Move(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            Move(to);
            Up(to);
        }
    }

    public class MapCanvasTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "v4pc-canvas-" + Guid.NewGuid().ToString("N"));
        readonly Session s;
        readonly TestCanvas c = new TestCanvas();
        int changed;

        public MapCanvasTests()
        {
            Theme.Init();
            Theme.Scale = 1f; // координаты экрана = координаты карты + сдвиг 24
            Directory.CreateDirectory(dir);
            s = new Session(dir + Path.DirectorySeparatorChar);
            s.Store.Save(Pc("a", "Бухгалтерия", "1.1", "Смирнова Ольга"));
            s.Store.Save(Pc("b", "Склад", "2.1", "Иванов Пётр"));
            s.Load();
            c.S = s;
            c.Size = new Size(1600, 1000);
            c.Changed += (o, e) => { changed++; Resolve(); };
            Resolve();
        }

        void Resolve()
        {
            var m = s.ResolveMap();
            c.SetMap(m, m.Doc.Floors[0]);
        }

        public void Dispose() { c.Dispose(); try { Directory.Delete(dir, true); } catch { } }

        static Point Scr(Rectangle world) => new Point(world.X + 24 + 10, world.Y + 24 + 10); // сдвиг холста по умолчанию 24

        [Fact]
        public void DragPc_ToOtherRoom_SavesRoomIdAndPlace()
        {
            var pc = c.Map.ItemByKey("pc:a");
            var target = s.Map.Rooms.Find(r => r.Name == "Склад");
            // хватаем значок в точке (+10, +10) от угла — середина значка встанет в середину кабинета
            var to = new Point(target.X + 24 + target.W / 2 - MapLayout.CellW / 2 + 10, target.Y + 24 + target.H / 2 - MapLayout.CellH / 2 + 10);
            c.Drag(Scr(pc.Rect), to);
            Assert.Equal(1, changed);
            Assert.Equal(target.Id, s.All.Find(p => p.Key == "a")["RoomId"]);
            Assert.Equal(target.Id, s.Map.Places["pc:a"].RoomId);
            // сохранено на диск: карта и XML компьютера
            string err;
            Assert.Equal(target.Id, MapDoc.Load(s.MapPath, out err).Places["pc:a"].RoomId);
            var reread = new Session(s.BaseDir);
            reread.Load();
            Assert.Equal(target.Id, reread.All.Find(p => p.Key == "a")["RoomId"]);
        }

        [Fact]
        public void DragPc_OutsideRooms_Reverts()
        {
            var pc = c.Map.ItemByKey("pc:a");
            var before = pc.Rect;
            c.Drag(Scr(pc.Rect), new Point(1500, 950));
            Assert.Equal(0, changed);
            Assert.Equal(before, c.Map.ItemByKey("pc:a").Rect);
            Assert.False(s.Map.Places.ContainsKey("pc:a"));
        }

        [Fact]
        public void DragRoom_MovesAndStopsBeingAuto()
        {
            var room = s.Map.Rooms.Find(r => r.Name == "Бухгалтерия");
            int x0 = room.X;
            var head = new Point(room.X + 24 + 20, room.Y + 24 + 10);
            c.Drag(head, new Point(head.X + 300, head.Y + 400));
            var after = s.Map.RoomById(room.Id);
            Assert.Equal(x0 + 300, after.X);
            Assert.False(after.Auto);
            Assert.True(after.Rect.Contains(c.Map.ItemByKey("pc:a").Rect));
        }

        [Fact]
        public void ResizeRoom_ByHandle()
        {
            var room = s.Map.Rooms.Find(r => r.Name == "Склад");
            var head = new Point(room.X + 24 + 20, room.Y + 24 + 10);
            c.Down(head); c.Up(head); // выделить
            var h = MapRenderer.Handle(room);
            var from = new Point(h.X + 24 + 5, h.Y + 24 + 5);
            int w0 = room.W;
            c.Drag(from, new Point(from.X + 120, from.Y + 80));
            Assert.Equal(w0 + 120, s.Map.RoomById(room.Id).W);
            Assert.False(s.Map.RoomById(room.Id).Auto);
        }

        [Fact]
        public void Dialogs_Build()
        {
            using (var d = new RoomDialog("Кабинет", "204", "Склад", new System.Collections.Generic.List<string> { "Склад" })) Assert.Equal("204", d.RoomName);
            using (var d = new TextDialog("Этаж", "Название", "2 этаж")) Assert.Equal("2 этаж", d.Value);
            using (var d = new PcInfoDialog(s.All[0], "Бухгалтерия")) Assert.True(d.ClientSize.Height > 100);
            using (var d = new ConfirmDialog("Удалить", "Удалить кабинет?", "Удалить")) Assert.NotNull(d);
        }
    }
}
