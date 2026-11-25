using System;
using System.IO;
using PcInventory;
using Xunit;

namespace PcInventory.Tests
{
    public class MapDocTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "v4pc-mapdoc-" + Guid.NewGuid().ToString("N"));
        public MapDocTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        [Fact]
        public void RoundTrip()
        {
            var m = new MapDoc();
            m.Floors.Add(new Floor { Id = "f1", Name = "1 этаж", Bg = "f1.png", W = 1800, H = 900 });
            m.Rooms.Add(new Room { Id = "r1", FloorId = "f1", Name = "Отдел кадров", Dept = "Кадры", X = 10, Y = 20, W = 300, H = 200, Auto = true });
            m.Places[@"pr:\\srv\hp"] = new Place { Item = @"pr:\\srv\hp", RoomId = "r1", X = 5, Y = 40 };
            string path = Path.Combine(dir, "карта.xml");
            m.Save(path);

            string err;
            var r = MapDoc.Load(path, out err);
            Assert.Null(err);
            Assert.Single(r.Floors);
            Assert.Equal("1 этаж", r.Floors[0].Name);
            Assert.Equal(1800, r.Floors[0].W);
            Assert.Equal("f1.png", r.Floors[0].Bg);
            var room = r.RoomById("r1");
            Assert.Equal("Отдел кадров", room.Name);
            Assert.Equal("Кадры", room.Dept);
            Assert.True(room.Auto);
            Assert.Equal(300, room.W);
            Assert.Equal(40, r.Places[@"pr:\\srv\hp"].Y);
        }

        [Fact]
        public void MissingFile_EmptyMapNoError()
        {
            string err;
            var m = MapDoc.Load(Path.Combine(dir, "нет.xml"), out err);
            Assert.Null(err);
            Assert.Empty(m.Floors);
        }

        [Fact]
        public void BrokenFile_RenamedNotDeleted()
        {
            string path = Path.Combine(dir, "карта.xml");
            File.WriteAllText(path, "<map><floor id=");
            string err;
            var m = MapDoc.Load(path, out err);
            Assert.NotNull(err);
            Assert.Empty(m.Floors);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(dir, "карта.повреждён.*.xml"));
        }

        [Fact]
        public void NewId_Increments()
        {
            var m = new MapDoc();
            m.Rooms.Add(new Room { Id = "r1" });
            m.Rooms.Add(new Room { Id = "r7" });
            m.Floors.Add(new Floor { Id = "f2" });
            Assert.Equal("r8", m.NewId("r"));
            Assert.Equal("f3", m.NewId("f"));
        }

        [Fact]
        public void Settings_RoundTrip()
        {
            string path = Path.Combine(dir, "настройки.xml");
            Assert.False(AppSettings.Load(path).Png);
            new AppSettings { Png = true, XlsxSheet = true }.Save(path);
            var s = AppSettings.Load(path);
            Assert.True(s.Png);
            Assert.False(s.Drawio);
            Assert.True(s.XlsxSheet);
        }
    }
}
