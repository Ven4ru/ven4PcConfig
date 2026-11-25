using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    /// <summary>
    /// Холст этажа: подложка, кабинеты и значки (рисует MapRenderer в тёмной палитре).
    /// Колесо — масштаб, перетаскивание пустого места — сдвиг; кабинеты и значки двигаются мышью,
    /// кабинет тянется за правый нижний угол. Изменения сразу пишутся в карту и в XML ПК.
    /// </summary>
    class MapCanvas : Painted
    {
        public Session S;
        public ResolvedMap Map;
        public Floor Floor;
        Image bg;
        string bgFloor;

        float zoom = 1f;
        PointF offset = new PointF(24, 24);
        string selected, hover;

        enum Drag { None, Pan, Room, Resize, Item }
        Drag drag;
        Point downScreen, lastWorld;
        Room dragRoom;
        MapItem dragItem;
        Rectangle startRect;
        bool moved;
        readonly ToolTip tip = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 30000 };
        readonly ContextMenuStrip roomMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Body };

        /// <summary>Карта изменилась (кабинет, значок, ПК) — страница пересчитывает раскладку и обновляет остальное окно.</summary>
        public event EventHandler Changed;
        public event EventHandler<RoomEventArgs> EditRoom, DeleteRoom;
        public event EventHandler<ItemEventArgs> OpenItem;

        public class RoomEventArgs : EventArgs { public Room Room; }
        public class ItemEventArgs : EventArgs { public MapItem Item; }

        public MapCanvas()
        {
            BackColor = Theme.Card;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            foreach (var m in new[] { roomMenu, itemMenu }) { m.BackColor = Theme.Raised; m.ForeColor = Theme.Text; }
        }

        readonly ContextMenuStrip itemMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Body };

        ToolStripMenuItem Item(string text, EventHandler click)
        {
            var mi = new ToolStripMenuItem(text) { ForeColor = Theme.Text };
            if (click != null) mi.Click += click;
            return mi;
        }

        /// <summary>Подменю у пункта: тёмное, как основное меню.</summary>
        static void Dark(ToolStripMenuItem mi)
        {
            var dd = (ToolStripDropDownMenu)mi.DropDown;
            dd.Renderer = new DarkRenderer();
            dd.ShowImageMargin = false;
            dd.BackColor = Theme.Raised;
            dd.Font = Theme.Body;
        }

        /// <summary>Меню кабинета: изменить, перенести на другой этаж (любого здания), удалить.</summary>
        void ShowRoomMenu(Room r, Point at)
        {
            roomMenu.Items.Clear();
            roomMenu.Items.Add(Item("Изменить кабинет…", (s, e) => Raise(EditRoom)));
            var move = Item("Перенести на этаж", null);
            Dark(move);
            foreach (var f in Map.Doc.Floors)
            {
                if (f.Id == r.FloorId) continue;
                var target = f;
                move.DropDownItems.Add(Item(Map.Doc.Buildings.Count > 1 ? Map.Doc.FloorTitle(f) : f.Name, (s, e) =>
                {
                    MapLayout.MoveRoom(Map.Doc, r, target);
                    selected = null;
                    Commit();
                }));
            }
            if (move.DropDownItems.Count == 0) { move.Enabled = false; move.Text = "Перенести на этаж (других этажей нет)"; }
            roomMenu.Items.Add(move);
            roomMenu.Items.Add(Item("Удалить кабинет", (s, e) => Raise(DeleteRoom)));
            roomMenu.Show(this, at);
        }

        /// <summary>Меню значка: сведения о ПК и перенос в любой кабинет любого этажа.</summary>
        void ShowItemMenu(MapItem it, Point at)
        {
            itemMenu.Items.Clear();
            if (it.IsPc) itemMenu.Items.Add(Item("Сведения о компьютере…", (s, e) => { if (OpenItem != null) OpenItem(this, new ItemEventArgs { Item = it }); }));
            var move = Item("Перенести в кабинет", null);
            Dark(move);
            foreach (var f in Map.Doc.Floors)
                foreach (var r in Map.RoomsOn(f))
                {
                    var target = r;
                    var mi = Item(Map.RoomTitle(r), (s, e) =>
                    {
                        var pc = MapLayout.MoveItem(Map.Doc, it, target);
                        if (pc != null)
                        {
                            try { S.SaveRecord(pc); } catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить ПК: " + ex.Message, "Карта"); }
                            if (S.Current != null && S.Current.Key == pc.Key) S.Current["RoomId"] = target.Id;
                        }
                        Commit();
                    });
                    if (r == it.Room) { mi.Enabled = false; mi.Text += " — сейчас здесь"; }
                    move.DropDownItems.Add(mi);
                }
            itemMenu.Items.Add(move);
            itemMenu.Show(this, at);
        }

        void Raise(EventHandler<RoomEventArgs> h)
        {
            var r = SelectedRoom;
            if (h != null && r != null) h(this, new RoomEventArgs { Room = r });
        }

        public Room SelectedRoom => selected != null && selected.StartsWith("room:") && Map != null ? Map.Doc.RoomById(selected.Substring(5)) : null;

        /// <summary>Новая раскладка (после любой правки) — выделение и масштаб сохраняются.</summary>
        public void SetMap(ResolvedMap m, Floor f)
        {
            Map = m;
            if (Floor == null || f == null || Floor.Id != f.Id) { zoom = 1f; offset = new PointF(24, 24); selected = null; }
            Floor = f;
            if (bgFloor != (f == null ? null : f.Id + "|" + f.Bg))
            {
                if (bg != null) bg.Dispose();
                bg = S != null && f != null ? S.Background(f) : null;
                bgFloor = f == null ? null : f.Id + "|" + f.Bg;
            }
            Invalidate();
        }

        /// <summary>Вписать этаж в холст (кнопка-масштаб не нужна: колесо и это при смене этажа).</summary>
        public void Fit()
        {
            if (Floor == null || Map == null) return;
            var c = MapRenderer.Content(Map, Floor);
            var area = bg != null || c.IsEmpty ? new Rectangle(0, 0, Floor.W, Floor.H) : Rectangle.Inflate(c, 24, 24);
            float sc = Theme.Scale;
            float z = Math.Min((Width - 32) / (area.Width * sc), (Height - 32) / (area.Height * sc));
            zoom = Math.Max(0.5f, Math.Min(1.25f, z));
            offset = new PointF(16 - area.X * zoom * sc, 16 - area.Y * zoom * sc);
            Invalidate();
        }

        float K => zoom * Theme.Scale;
        PointF ToWorld(Point p) => new PointF((p.X - offset.X) / K, (p.Y - offset.Y) / K);
        Point W(Point p) { var w = ToWorld(p); return new Point((int)Math.Round(w.X), (int)Math.Round(w.Y)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Window)) g.FillRectangle(b, ClientRectangle);
            if (Map == null || Floor == null) return;
            var st = g.Save();
            g.TranslateTransform(offset.X, offset.Y);
            g.ScaleTransform(K, K);
            MapRenderer.DrawFloor(g, Map, Floor, MapPalette.Dark, bg, S != null && S.Current != null ? "pc:" + S.Current.Key : null, selected, hover);
            g.Restore(st);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            // подсказка по управлению в углу
            string hint = "Колесо — масштаб · перетащите пустое место, кабинет или значок · правый щелчок — перенос на другой этаж или в кабинет";
            TextRenderer.DrawText(g, hint, Theme.Small, new Rectangle(Theme.S(12), Height - Theme.S(28), Width - Theme.S(24), Theme.S(22)), Theme.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        // ------------------------------------------------------------ попадания
        MapItem ItemAt(Point world)
        {
            var items = Map.ItemsOn(Floor);
            for (int i = items.Count - 1; i >= 0; i--) if (items[i].Rect.Contains(world)) return items[i];
            return null;
        }

        Room RoomAt(Point world)
        {
            var rooms = Map.RoomsOn(Floor);
            for (int i = rooms.Count - 1; i >= 0; i--) if (rooms[i].Rect.Contains(world)) return rooms[i];
            return null;
        }

        // ------------------------------------------------------------ мышь
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
            if (Map == null || Floor == null) return;
            var w = W(e.Location);
            downScreen = e.Location;
            lastWorld = w;
            moved = false;
            var sr = SelectedRoom;
            if (e.Button == MouseButtons.Right)
            {
                var hit = ItemAt(w);
                if (hit != null) { selected = hit.Key; Invalidate(); ShowItemMenu(hit, e.Location); return; }
                var r = RoomAt(w);
                if (r != null) { selected = "room:" + r.Id; Invalidate(); ShowRoomMenu(r, e.Location); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (sr != null && Rectangle.Inflate(MapRenderer.Handle(sr), 4, 4).Contains(w)) { drag = Drag.Resize; dragRoom = sr; startRect = sr.Rect; return; }
            var it = ItemAt(w);
            if (it != null) { drag = Drag.Item; dragItem = it; startRect = it.Rect; selected = it.Key; Invalidate(); return; }
            var room = RoomAt(w);
            if (room != null) { drag = Drag.Room; dragRoom = room; startRect = room.Rect; selected = "room:" + room.Id; Invalidate(); return; }
            selected = null;
            drag = Drag.Pan;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Map == null || Floor == null) return;
            var w = W(e.Location);
            if (drag == Drag.None)
            {
                var it = ItemAt(w);
                string h = it != null ? it.Key : null;
                var sr = SelectedRoom;
                Cursor = sr != null && Rectangle.Inflate(MapRenderer.Handle(sr), 4, 4).Contains(w) ? Cursors.SizeNWSE : it != null || RoomAt(w) != null ? Cursors.SizeAll : Cursors.Default;
                if (h != hover)
                {
                    hover = h;
                    if (it != null) tip.SetToolTip(this, Tip(it)); else tip.SetToolTip(this, null);
                    Invalidate();
                }
                return;
            }
            if (!moved && Math.Abs(e.X - downScreen.X) + Math.Abs(e.Y - downScreen.Y) < 4) return;
            if (!moved) tip.SetToolTip(this, null);
            moved = true;
            int dx = w.X - lastWorld.X, dy = w.Y - lastWorld.Y;
            if (drag == Drag.Pan)
            {
                offset = new PointF(offset.X + e.X - downScreen.X, offset.Y + e.Y - downScreen.Y);
                downScreen = e.Location;
            }
            else if (drag == Drag.Room)
            {
                dragRoom.X += dx; dragRoom.Y += dy;
                foreach (var i in Map.Items) if (i.Room == dragRoom) i.Rect.Offset(dx, dy);
            }
            else if (drag == Drag.Resize)
            {
                dragRoom.W = Math.Max(MapLayout.CellW + MapLayout.Gap * 2, dragRoom.W + dx);
                dragRoom.H = Math.Max(MapLayout.Head + MapLayout.CellH + MapLayout.Gap * 2, dragRoom.H + dy);
            }
            else if (drag == Drag.Item) dragItem.Rect.Offset(dx, dy);
            lastWorld = w;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var d = drag;
            drag = Drag.None;
            if (!moved || Map == null) return;
            if (d == Drag.Room || d == Drag.Resize)
            {
                dragRoom.X = Math.Max(0, dragRoom.X);
                dragRoom.Y = Math.Max(0, dragRoom.Y);
                dragRoom.Auto = false; // кабинет, который админ двигал, программа больше не пересчитывает
                Commit();
            }
            else if (d == Drag.Item) DropItem(dragItem);
        }

        /// <summary>Значок отпущен: кабинет под его серединой становится его кабинетом.</summary>
        void DropItem(MapItem it)
        {
            var center = new Point(it.Rect.X + it.Rect.Width / 2, it.Rect.Y + it.Rect.Height / 2);
            var room = RoomAt(center);
            if (room == null) { it.Rect = startRect; Invalidate(); return; }
            Map.Doc.Places[it.Key] = new Place { Item = it.Key, RoomId = room.Id, X = it.Rect.X - room.X, Y = it.Rect.Y - room.Y };
            if (it.IsPc && it.Pc["RoomId"] != room.Id && room != Map.RoomOfPc(it.Pc))
            {
                it.Pc["RoomId"] = room.Id;
                try { S.SaveRecord(it.Pc); } catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить ПК: " + ex.Message, "Карта"); }
                if (S.Current != null && S.Current.Key == it.Pc.Key) S.Current["RoomId"] = room.Id;
            }
            Commit();
        }

        void Commit()
        {
            try { S.SaveMap(); } catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить карту: " + ex.Message, "Карта"); }
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Map == null || Floor == null || e.Button != MouseButtons.Left) return;
            var w = W(e.Location);
            var it = ItemAt(w);
            if (it != null) { if (OpenItem != null) OpenItem(this, new ItemEventArgs { Item = it }); return; }
            var r = RoomAt(w);
            if (r != null) { selected = "room:" + r.Id; Raise(EditRoom); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var before = ToWorld(e.Location);
            zoom = Math.Max(0.5f, Math.Min(2f, zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f)));
            offset = new PointF(e.X - before.X * K, e.Y - before.Y * K);
            Invalidate();
        }

        protected override bool IsInputKey(Keys k) => k == Keys.Delete || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete) Raise(DeleteRoom);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; Invalidate(); }
        }

        static string Tip(MapItem it)
        {
            var lines = new List<string>();
            if (it.IsPc)
            {
                var r = it.Pc;
                lines.Add(Report.ShortTitle(r) + (r["Fio"] != "" ? " — " + r["Fio"] : ""));
                foreach (var kv in new[] { new[] { "Процессор", r["Cpu"] }, new[] { "ОЗУ", r["Ram"] }, new[] { "Накопители", r["Disk"].Replace("\n", "; ") },
                                           new[] { "ОС", Util.Join(" ", new[] { r["Os"], r["OsVer"] }) }, new[] { "IP", r["Ip"].Replace("\n", ", ") },
                                           new[] { "Пользователь", r["User"] } })
                    if (kv[1] != "") lines.Add(kv[0] + ": " + kv[1]);
                var probs = Report.ProblemsOf(r);
                lines.Add(probs.Count == 0 ? "Замечаний нет" : "Замечания: " + string.Join("; ", probs.ToArray()));
            }
            else
            {
                var p = it.Printer;
                lines.Add(p.Name + (p.Network ? " (сетевой)" : ""));
                if (p.Driver != "") lines.Add("Драйвер: " + p.Driver);
                if (p.Port != "") lines.Add("Порт: " + p.Port);
                var pcs = new List<string>();
                foreach (var pc in p.Pcs) pcs.Add(Report.ShortTitle(pc));
                lines.Add("Компьютеры: " + string.Join(", ", pcs.ToArray()));
            }
            return string.Join("\n", lines.ToArray());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (bg != null) bg.Dispose(); tip.Dispose(); roomMenu.Dispose(); itemMenu.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
