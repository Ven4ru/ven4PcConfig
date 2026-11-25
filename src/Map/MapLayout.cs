using System;
using System.Collections.Generic;
using System.Drawing;

namespace PcInventory
{
    /// <summary>Значок на карте: ПК или принтер, его кабинет и место на этаже (в условных пикселях).</summary>
    class MapItem
    {
        public string Key = "";
        public PcRecord Pc;
        public MapPrinter Printer;
        public Room Room;
        public Rectangle Rect;
        public string Line1 = "", Line2 = "", Grade = "";
        public bool IsPc => Pc != null;
    }

    /// <summary>Готовая к показу и выгрузке карта: кабинеты из MapDoc плюс вычисленные места всех значков.</summary>
    class ResolvedMap
    {
        public MapDoc Doc;
        public List<MapItem> Items = new List<MapItem>();
        public readonly Dictionary<string, Room> PcRooms = new Dictionary<string, Room>();
        /// <summary>Кабинеты, значки которых не поместились в заданный админом размер.</summary>
        public readonly Dictionary<string, bool> Overflow = new Dictionary<string, bool>();

        public List<Room> RoomsOn(Floor f)
        {
            var list = new List<Room>();
            foreach (var r in Doc.Rooms) if (r.FloorId == f.Id) list.Add(r);
            return list;
        }

        public List<MapItem> ItemsOn(Floor f)
        {
            var list = new List<MapItem>();
            foreach (var i in Items) if (i.Room != null && i.Room.FloorId == f.Id) list.Add(i);
            return list;
        }

        public Room RoomOfPc(PcRecord pc)
        {
            Room r;
            return pc != null && PcRooms.TryGetValue(pc.Key, out r) ? r : null;
        }

        /// <summary>«2 этаж · 204», если этажей больше одного, иначе просто название кабинета.</summary>
        public string RoomTitle(Room r)
        {
            if (r == null) return "";
            if (Doc.Floors.Count < 2) return r.Name;
            var f = Doc.FloorById(r.FloorId);
            return (f != null ? f.Name + " · " : "") + r.Name;
        }

        public MapItem ItemByKey(string key)
        {
            foreach (var i in Items) if (i.Key == key) return i;
            return null;
        }
    }

    /// <summary>
    /// Раскладка карты: в какой кабинет попадает каждый ПК и принтер и где стоит его значок.
    /// Кабинеты для отделов без своего кабинета создаются автоматически (Auto) на первом этаже.
    /// </summary>
    static class MapLayout
    {
        public const int CellW = 170, CellH = 64, Head = 34, Gap = 8, Margin = 40, MinRoomW = 364;
        public const string NoDept = "Без отдела";

        public static ResolvedMap Resolve(MapDoc doc, List<PcRecord> pcs)
        {
            if (doc.Floors.Count == 0) doc.Floors.Add(new Floor { Id = doc.NewId("f"), Name = "1 этаж" });
            var first = doc.Floors[0];
            var m = new ResolvedMap { Doc = doc };
            var sorted = Report.Sorted(pcs);
            var created = new List<Room>();

            // 1. кабинет каждого ПК
            foreach (var pc in sorted)
            {
                Room room = doc.RoomById(pc["RoomId"]);
                if (room == null)
                {
                    string dept = pc["Dept"].Trim() != "" ? pc["Dept"].Trim() : NoDept;
                    room = DeptRoom(doc, dept);
                    if (room == null)
                    {
                        room = new Room { Id = doc.NewId("r"), FloorId = first.Id, Name = dept, Dept = dept, Auto = true };
                        doc.Rooms.Add(room);
                        created.Add(room);
                    }
                }
                m.PcRooms[pc.Key] = room;
                m.Items.Add(new MapItem
                {
                    Key = "pc:" + pc.Key, Pc = pc, Room = room,
                    Line1 = pc["ArmId"] != "" ? pc["ArmId"] + " · " + pc["PcName"] : pc["PcName"],
                    Line2 = pc["Fio"] != "" ? ShortFio(pc["Fio"]) : Login(pc["User"]),
                    Grade = Report.ProblemsOf(pc).Count > 0 ? Collector.Bad : Collector.Ok,
                });
            }

            // 2. принтеры: в кабинет, куда их перетащили, иначе к первому ПК
            foreach (var p in Printers.FromPcs(sorted))
            {
                Place pl;
                Room room = doc.Places.TryGetValue(p.Key, out pl) ? doc.RoomById(pl.RoomId) : null;
                if (room == null) room = m.PcRooms[p.Pcs[0].Key];
                m.Items.Add(new MapItem { Key = p.Key, Printer = p, Room = room, Line1 = p.Short, Line2 = p.Network ? "сетевой" : "", Grade = "" });
            }

            // 3. автоматические кабинеты без значков больше не нужны (отдел исчез из реестра)
            var used = new Dictionary<string, bool>();
            foreach (var i in m.Items) used[i.Room.Id] = true;
            doc.Rooms.RemoveAll(r => r.Auto && !used.ContainsKey(r.Id));

            // 4. размеры auto-кабинетов, места новых кабинетов, места значков
            foreach (var r in doc.Rooms)
                if (r.Auto) SizeAuto(r, Count(m, r));
            foreach (var r in created) PlaceRoom(doc, r);
            foreach (var r in doc.Rooms) PlaceItems(m, r);
            foreach (var f in doc.Floors) FitFloor(m, f);
            return m;
        }

        /// <summary>«Расставить заново»: забыть места значков и auto-кабинеты. Кабинеты админа остаются.</summary>
        public static void Reset(MapDoc doc)
        {
            doc.Places.Clear();
            doc.Rooms.RemoveAll(r => r.Auto);
        }

        static Room DeptRoom(MapDoc doc, string dept)
        {
            foreach (var r in doc.Rooms)
                if (string.Equals(r.Dept.Trim(), dept, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        static int Count(ResolvedMap m, Room r)
        {
            int n = 0;
            foreach (var i in m.Items) if (i.Room == r) n++;
            return n;
        }

        /// <summary>Сколько колонок значков помещается в ширину кабинета.</summary>
        public static int Cols(int roomW) => Math.Max(1, (roomW - Gap) / (CellW + Gap));

        static void SizeAuto(Room r, int n)
        {
            int cols = Math.Max(2, Math.Min(3, n));
            int rows = Math.Max(1, (n + cols - 1) / cols);
            r.W = Math.Max(MinRoomW, Gap + cols * (CellW + Gap));
            r.H = Head + Gap + rows * (CellH + Gap);
        }

        /// <summary>Новый кабинет — в первое свободное место первого этажа, строками слева направо.</summary>
        public static void PlaceRoom(MapDoc doc, Room r)
        {
            var floor = doc.FloorById(r.FloorId);
            int maxW = Math.Max(floor != null ? floor.W : 1600, r.W + Margin * 2);
            for (int y = Margin; ; y += 24)
                for (int x = Margin; x + r.W <= maxW - Margin || x == Margin; x += 24)
                {
                    var rect = new Rectangle(x, y, r.W, r.H);
                    bool free = true;
                    foreach (var o in doc.Rooms)
                        if (o != r && o.FloorId == r.FloorId && (o.X != 0 || o.Y != 0 || !o.Auto) && Rectangle.Inflate(o.Rect, 24, 24).IntersectsWith(rect)) { free = false; break; }
                    if (free) { r.X = x; r.Y = y; return; }
                }
        }

        static void PlaceItems(ResolvedMap m, Room r)
        {
            var items = new List<MapItem>();
            foreach (var i in m.Items) if (i.Room == r) items.Add(i);
            var taken = new List<Rectangle>();
            // значки с сохранённым местом в этом кабинете
            foreach (var i in items)
            {
                Place p;
                if (m.Doc.Places.TryGetValue(i.Key, out p) && p.RoomId == r.Id)
                {
                    i.Rect = new Rectangle(r.X + p.X, r.Y + p.Y, CellW, CellH);
                    taken.Add(i.Rect);
                }
            }
            // остальные — в свободные ячейки, сначала ПК, потом принтеры (так они идут в Items)
            int cols = Cols(r.W), cell = 0;
            foreach (var i in items)
            {
                if (i.Rect.Width > 0) continue;
                Rectangle rect;
                do
                {
                    int c = cell % cols, row = cell / cols;
                    rect = new Rectangle(r.X + Gap + c * (CellW + Gap), r.Y + Head + Gap + row * (CellH + Gap), CellW, CellH);
                    cell++;
                } while (Hits(taken, rect));
                i.Rect = rect;
                taken.Add(rect);
            }
            foreach (var i in items)
                if (!r.Rect.Contains(i.Rect)) { m.Overflow[r.Id] = true; break; }
        }

        static bool Hits(List<Rectangle> taken, Rectangle rect)
        {
            foreach (var t in taken) if (t.IntersectsWith(rect)) return true;
            return false;
        }

        /// <summary>Этаж растёт, если кабинеты или значки вышли за его край.</summary>
        static void FitFloor(ResolvedMap m, Floor f)
        {
            int right = 0, bottom = 0;
            foreach (var r in m.RoomsOn(f)) { right = Math.Max(right, r.X + r.W); bottom = Math.Max(bottom, r.Y + r.H); }
            foreach (var i in m.ItemsOn(f)) { right = Math.Max(right, i.Rect.Right); bottom = Math.Max(bottom, i.Rect.Bottom); }
            f.W = Math.Max(f.W, right + Margin);
            f.H = Math.Max(f.H, bottom + Margin);
        }

        /// <summary>«CORP\o.smirnova» → «o.smirnova» — подпись значка, если ФИО не заполнено.</summary>
        static string Login(string user)
        {
            int i = (user ?? "").LastIndexOf('\\');
            return i >= 0 ? user.Substring(i + 1) : user ?? "";
        }

        /// <summary>«Смирнова Ольга Петровна» → «Смирнова О. П.»</summary>
        public static string ShortFio(string fio)
        {
            var parts = (fio ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return fio ?? "";
            string s = parts[0];
            for (int i = 1; i < parts.Length && i < 3; i++) s += " " + parts[i].Substring(0, 1) + ".";
            return s;
        }
    }
}
