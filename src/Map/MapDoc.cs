using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace PcInventory
{
    /// <summary>Здание: у каждого свои этажи (корпус, склад, филиал).</summary>
    class Building
    {
        public string Id = "", Name = "";
    }

    /// <summary>Этаж здания: своя вкладка карты и своя страница в Visio. Подложка — файл в папке «карта».</summary>
    class Floor
    {
        public string Id = "", BuildingId = "", Name = "", Bg = "";
        public int W = 1600, H = 1000;
    }

    /// <summary>Кабинет на этаже. Auto — создан программой для отдела, пока админ его не трогал.</summary>
    class Room
    {
        public string Id = "", FloorId = "", Name = "", Dept = "";
        public int X, Y, W, H;
        public bool Auto;
        public Rectangle Rect => new Rectangle(X, Y, W, H);
    }

    /// <summary>Сохранённое положение значка (ПК «pc:ключ» или принтера «pr:ключ») внутри кабинета.</summary>
    class Place
    {
        public string Item = "", RoomId = "";
        public int X, Y;
    }

    /// <summary>Карта помещений: этажи, кабинеты и места значков. Файл «карта.xml» в папке данных.</summary>
    class MapDoc
    {
        public const string DefaultBuilding = "Главное здание";
        public List<Building> Buildings = new List<Building>();
        public List<Floor> Floors = new List<Floor>();

        public Building BuildingById(string id)
        {
            foreach (var b in Buildings) if (b.Id == id) return b;
            return null;
        }

        /// <summary>Этажи здания в порядке вкладок.</summary>
        public List<Floor> FloorsOf(Building b)
        {
            var list = new List<Floor>();
            foreach (var f in Floors) if (b != null && f.BuildingId == b.Id) list.Add(f);
            return list;
        }

        /// <summary>Хотя бы одно здание; этажи без здания (карта версии без зданий) — в первое.</summary>
        public void EnsureBuildings()
        {
            if (Buildings.Count == 0) Buildings.Add(new Building { Id = NewId("b"), Name = DefaultBuilding });
            foreach (var f in Floors)
                if (BuildingById(f.BuildingId) == null) f.BuildingId = Buildings[0].Id;
        }

        /// <summary>«Склад · 1 этаж», если зданий больше одного, иначе «1 этаж».</summary>
        public string FloorTitle(Floor f)
        {
            if (f == null) return "";
            if (Buildings.Count < 2) return f.Name;
            var b = BuildingById(f.BuildingId);
            return (b != null ? b.Name + " · " : "") + f.Name;
        }
        public List<Room> Rooms = new List<Room>();
        public Dictionary<string, Place> Places = new Dictionary<string, Place>();

        public Floor FloorById(string id)
        {
            foreach (var f in Floors) if (f.Id == id) return f;
            return null;
        }

        public Room RoomById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in Rooms) if (r.Id == id) return r;
            return null;
        }

        /// <summary>Новый id вида «f3», «r12» — на единицу больше наибольшего с тем же префиксом.</summary>
        public string NewId(string prefix)
        {
            int max = 0;
            var ids = new List<string>();
            foreach (var b in Buildings) ids.Add(b.Id);
            foreach (var f in Floors) ids.Add(f.Id);
            foreach (var r in Rooms) ids.Add(r.Id);
            foreach (var id in ids)
            {
                int n;
                if (id.StartsWith(prefix) && int.TryParse(id.Substring(prefix.Length), out n) && n > max) max = n;
            }
            return prefix + (max + 1);
        }

        // ------------------------------------------------------------ файл
        /// <summary>Читает карту. Файла нет — пустая карта без ошибки. Файл повреждён — он переименовывается
        /// в «карта.повреждён.&lt;дата&gt;.xml» (не удаляется), возвращается пустая карта и текст ошибки.</summary>
        public static MapDoc Load(string path, out string error)
        {
            error = null;
            var m = new MapDoc();
            if (!File.Exists(path)) return m;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                if (root == null || root.Name != "map") throw new Exception("неизвестный формат");
                foreach (XmlNode n in root.ChildNodes)
                {
                    var e = n as XmlElement;
                    if (e == null) continue;
                    if (e.Name == "building")
                        m.Buildings.Add(new Building { Id = e.GetAttribute("id"), Name = e.GetAttribute("name") });
                    else if (e.Name == "floor")
                        m.Floors.Add(new Floor { Id = e.GetAttribute("id"), BuildingId = e.GetAttribute("building"), Name = e.GetAttribute("name"), Bg = e.GetAttribute("bg"), W = Int(e, "w", 1600), H = Int(e, "h", 1000) });
                    else if (e.Name == "room")
                        m.Rooms.Add(new Room
                        {
                            Id = e.GetAttribute("id"), FloorId = e.GetAttribute("floor"), Name = e.GetAttribute("name"), Dept = e.GetAttribute("dept"),
                            X = Int(e, "x", 0), Y = Int(e, "y", 0), W = Int(e, "w", 240), H = Int(e, "h", 160), Auto = e.GetAttribute("auto") == "1"
                        });
                    else if (e.Name == "place")
                    {
                        var p = new Place { Item = e.GetAttribute("item"), RoomId = e.GetAttribute("room"), X = Int(e, "x", 0), Y = Int(e, "y", 0) };
                        if (p.Item != "") m.Places[p.Item] = p;
                    }
                }
                return m;
            }
            catch (Exception ex)
            {
                error = Path.GetFileName(path) + ": " + ex.Message;
                try
                {
                    string bad = Path.Combine(Path.GetDirectoryName(path),
                        Path.GetFileNameWithoutExtension(path) + ".повреждён." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml");
                    File.Move(path, bad);
                    error += " — файл сохранён как " + Path.GetFileName(bad) + ", карта построена заново";
                }
                catch { }
                return new MapDoc();
            }
        }

        static int Int(XmlElement e, string name, int def)
        {
            int v;
            return int.TryParse(e.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            var st = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var w = XmlWriter.Create(tmp, st))
            {
                w.WriteStartDocument();
                w.WriteStartElement("map");
                w.WriteAttributeString("format", "1");
                foreach (var b in Buildings)
                {
                    w.WriteStartElement("building");
                    A(w, "id", b.Id); A(w, "name", b.Name);
                    w.WriteEndElement();
                }
                foreach (var f in Floors)
                {
                    w.WriteStartElement("floor");
                    A(w, "id", f.Id); A(w, "building", f.BuildingId); A(w, "name", f.Name); A(w, "bg", f.Bg); A(w, "w", f.W); A(w, "h", f.H);
                    w.WriteEndElement();
                }
                foreach (var r in Rooms)
                {
                    w.WriteStartElement("room");
                    A(w, "id", r.Id); A(w, "floor", r.FloorId); A(w, "name", r.Name); A(w, "dept", r.Dept);
                    A(w, "x", r.X); A(w, "y", r.Y); A(w, "w", r.W); A(w, "h", r.H);
                    if (r.Auto) A(w, "auto", "1");
                    w.WriteEndElement();
                }
                var keys = new List<string>(Places.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys)
                {
                    var p = Places[k];
                    w.WriteStartElement("place");
                    A(w, "item", p.Item); A(w, "room", p.RoomId); A(w, "x", p.X); A(w, "y", p.Y);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void A(XmlWriter w, string n, string v) => w.WriteAttributeString(n, Util.Clean(v ?? ""));
        static void A(XmlWriter w, string n, int v) => w.WriteAttributeString(n, v.ToString(CultureInfo.InvariantCulture));
    }
}
