using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    /// <summary>Раздел «Карта»: этажи-вкладки, холст с кабинетами, ПК и принтерами, выгрузка в Visio и др.</summary>
    class MapPage : Surface
    {
        readonly Session s;
        readonly HeaderBar header;
        readonly PageTitle title = new PageTitle("Карта АРМ", "");
        readonly V4Button btnExport = new V4Button("Выгрузить", Theme.IcoSave, true);
        readonly V4Button btnReset = new V4Button("Расставить заново", Theme.IcoRefresh, false);
        readonly V4Button btnBg = new V4Button("Подложка…", Theme.IcoFolder, false);
        readonly V4Button btnRoom = new V4Button("Кабинет +", Theme.IcoCard, false);
        readonly V4Button btnHidden = new V4Button("Скрытые", null, false) { Visible = false };
        readonly ContextMenuStrip hiddenMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Body };
        readonly FloorTabs tabs = new FloorTabs();
        readonly V4Button btnBuilding = new V4Button("Здание", null, false);
        readonly ContextMenuStrip bldMenu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Body };
        public readonly MapCanvas Canvas = new MapCanvas();
        ResolvedMap map;
        int bld, floor; // здание и этаж внутри здания
        string lastExport;

        /// <summary>Записи ПК или кабинеты изменились — окну нужно обновить реестр и карточку АРМ.</summary>
        public event EventHandler DataChanged;

        public MapPage(Session session, HeaderBar header) : base(Theme.Window)
        {
            s = session;
            this.header = header;
            Dock = DockStyle.Fill;
            Visible = false;
            foreach (Control c in new Control[] { title, btnExport, btnReset, btnBg, btnRoom, btnHidden, btnBuilding, tabs, Canvas }) Controls.Add(c);
            hiddenMenu.BackColor = Theme.Raised;
            hiddenMenu.ForeColor = Theme.Text;
            btnHidden.Click += (o, e) => ShowHiddenMenu();
            Canvas.DeletePc += (o, e) => DeletePc(e.Item.Pc);
            bldMenu.BackColor = Theme.Raised;
            bldMenu.ForeColor = Theme.Text;
            btnBuilding.Click += (o, e) => ShowBuildingMenu();
            Canvas.S = s;
            Canvas.Changed += (o, e) => Refresh0(true);
            Canvas.EditRoom += (o, e) => EditRoom(e.Room);
            Canvas.DeleteRoom += (o, e) => DeleteRoom(e.Room);
            Canvas.OpenItem += (o, e) => OpenItem(e.Item);
            tabs.Selected += (o, e) => { floor = tabs.Index; Refresh0(false); Canvas.Fit(); };
            tabs.Add += (o, e) => AddFloor();
            tabs.Rename += (o, e) => RenameFloor();
            tabs.Delete += (o, e) => DeleteFloor();
            tabs.ClearBg += (o, e) => ClearBg();
            btnExport.Click += (o, e) => Export();
            btnReset.Click += (o, e) => ResetLayout();
            btnBg.Click += (o, e) => PickBg();
            btnRoom.Click += (o, e) => AddRoom();
        }

        /// <summary>Открыть здание и этаж по номерам (снимки вида).</summary>
        public void ShowFloor(int building, int i)
        {
            bld = building;
            floor = i;
            Refresh0(false);
            Canvas.Fit();
        }

        Building CurBuilding => map != null && bld < map.Doc.Buildings.Count ? map.Doc.Buildings[bld] : null;
        List<Floor> BFloors => map != null ? map.Doc.FloorsOf(CurBuilding) : new List<Floor>();
        Floor CurFloor { get { var fl = BFloors; return floor < fl.Count ? fl[floor] : null; } }

        /// <summary>Пересчитать раскладку и перерисовать. changed — правка карты/ПК: сообщить окну.</summary>
        public void Refresh0(bool changed)
        {
            map = s.ResolveMap();
            if (bld >= map.Doc.Buildings.Count) bld = map.Doc.Buildings.Count - 1;
            if (bld < 0) bld = 0;
            var fl = BFloors;
            if (floor >= fl.Count) floor = fl.Count - 1;
            if (floor < 0) floor = 0;
            var names = new List<string>();
            foreach (var f in fl) names.Add(f.Name);
            tabs.Set(names, floor);
            Canvas.SetMap(map, CurFloor);
            btnBuilding.Text = (CurBuilding != null ? CurBuilding.Name : "Здание") + "  ▾";
            btnBuilding.Width = btnBuilding.Preferred();
            btnHidden.Visible = map.Doc.Hidden.Count > 0;
            btnHidden.Text = "Скрытые (" + map.Doc.Hidden.Count + ")";
            btnHidden.Width = btnHidden.Preferred();
            int pcs = 0, pr = 0;
            foreach (var i in map.Items) if (i.IsPc) pcs++; else pr++;
            string sub = s.All.Count == 0 ? "Компьютеры появятся на карте после сохранения в реестр"
                : "ПК: " + pcs + " · принтеров: " + pr + " · кабинетов: " + map.Doc.Rooms.Count;
            if (s.MapError != null) sub = "Карта была повреждена и построена заново — " + s.MapError;
            else if (CurFloor != null && CurFloor.Bg != "" && !File.Exists(Path.Combine(s.MapDir, CurFloor.Bg)) && !s.DemoBg.ContainsKey(CurFloor.Id))
                sub = "Не найдена подложка «" + CurFloor.Bg + "» в папке «карта» — этаж показан без неё";
            else if (map.Overflow.Count > 0) sub += " · значки не помещаются в кабинет — растяните его за угол";
            if (lastExport != null) sub = lastExport;
            title.Sub = sub;
            title.Invalidate();
            LayoutPage(); // ширина кнопки здания зависит от названия — вкладки этажей сдвигаются за ней
            if (changed && DataChanged != null) DataChanged(this, EventArgs.Empty);
        }

        public void LayoutPage()
        {
            int pad = Theme.S(28), gap = Theme.S(16);
            int W = Math.Max(Theme.S(600), ClientSize.Width) - pad * 2;
            int bx = pad + W;
            foreach (var b in new[] { btnExport, btnReset, btnBg, btnRoom })
            {
                bx -= b.Width;
                b.Location = new Point(bx, Theme.S(30));
                bx -= Theme.S(10);
            }
            title.SetBounds(pad, Theme.S(22), bx - pad - gap, Theme.S(64));
            btnBuilding.SetBounds(pad, Theme.S(96), btnBuilding.Width, Theme.S(36));
            int tx = pad + btnBuilding.Width + Theme.S(12);
            int tr = pad + W;
            if (btnHidden.Visible)
            {
                btnHidden.SetBounds(pad + W - btnHidden.Width, Theme.S(96), btnHidden.Width, Theme.S(36));
                tr = btnHidden.Left - Theme.S(12);
            }
            tabs.SetBounds(tx, Theme.S(96), tr - tx, Theme.S(36));
            int top = Theme.S(140);
            Canvas.SetBounds(pad, top, W, Math.Max(Theme.S(200), ClientSize.Height - top - Theme.S(24)));
        }

        // ------------------------------------------------------------ кабинеты
        List<string> Depts() => Report.DeptOrder(s.All);

        void AddRoom()
        {
            var f = CurFloor;
            if (f == null) return;
            using (var d = new RoomDialog("Новый кабинет", "", "", Depts()))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                var r = new Room { Id = s.Map.NewId("r"), FloorId = f.Id, Name = d.RoomName, Dept = d.Dept, W = MapLayout.MinRoomW, H = MapLayout.Head + MapLayout.CellH * 2 + MapLayout.Gap * 3 };
                s.Map.Rooms.Add(r);
                MapLayout.PlaceRoom(s.Map, r);
                Save();
            }
        }

        void EditRoom(Room r)
        {
            using (var d = new RoomDialog("Кабинет", r.Name, r.Dept, Depts()))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                r.Name = d.RoomName;
                r.Dept = d.Dept;
                r.Auto = false;
                Save();
            }
        }

        void DeleteRoom(Room r)
        {
            int n = 0;
            foreach (var i in map.Items) if (i.Room == r) n++;
            if (!ConfirmDialog.Ask(FindForm(), "Удалить кабинет", "Удалить кабинет «" + r.Name + "»?" +
                (n > 0 ? " Значков в нём: " + n + ". Компьютеры вернутся в кабинеты своих отделов, принтеры — к своим компьютерам." : ""), "Удалить")) return;
            s.Map.Rooms.Remove(r);
            var drop = new List<string>();
            foreach (var kv in s.Map.Places) if (kv.Value.RoomId == r.Id) drop.Add(kv.Key);
            foreach (var k in drop) s.Map.Places.Remove(k);
            foreach (var pc in s.All)
                if (pc["RoomId"] == r.Id)
                {
                    pc["RoomId"] = "";
                    try { s.SaveRecord(pc); } catch { }
                    if (s.Current != null && s.Current.Key == pc.Key) s.Current["RoomId"] = "";
                }
            Save();
        }

        /// <summary>«Скрытые (N)»: вернуть убранный значок на карту или все сразу.</summary>
        void ShowHiddenMenu()
        {
            hiddenMenu.Items.Clear();
            var keys = new List<string>(s.Map.Hidden.Keys);
            keys.Sort((a, b) => string.Compare(s.Map.Hidden[a], s.Map.Hidden[b], StringComparison.CurrentCultureIgnoreCase));
            foreach (var k in keys)
            {
                string key = k;
                string kind = k.StartsWith("pc:") ? "ПК " : "принтер ";
                var mi = new ToolStripMenuItem("Вернуть " + kind + s.Map.Hidden[k]) { ForeColor = Theme.Text };
                mi.Click += (o, e) => { s.Map.Hidden.Remove(key); Save(); };
                hiddenMenu.Items.Add(mi);
            }
            hiddenMenu.Items.Add(new ToolStripSeparator());
            var all = new ToolStripMenuItem("Вернуть все") { ForeColor = Theme.Text };
            all.Click += (o, e) => { s.Map.Hidden.Clear(); Save(); };
            hiddenMenu.Items.Add(all);
            hiddenMenu.Show(btnHidden, new Point(0, btnHidden.Height + Theme.S(2)));
        }

        /// <summary>Удалить лишний ПК из реестра (файл уходит в «удалённые», вернуть — переложить обратно и «Пересобрать»).</summary>
        void DeletePc(PcRecord pc)
        {
            if (s.Current != null && s.Current.Key == pc.Key)
            {
                ConfirmDialog.Ask(FindForm(), "Удалить компьютер", "Это компьютер, на котором запущена программа: при сохранении он снова попадёт в реестр. Чтобы он не мешал на карте, уберите его с карты.", "Понятно");
                return;
            }
            if (!ConfirmDialog.Ask(FindForm(), "Удалить компьютер", "Удалить «" + Report.ShortTitle(pc) + (pc["Fio"] != "" ? " — " + pc["Fio"] : "") +
                "» из реестра и с карты? Его файл переносится в папку «" + Program.DataDirName + "\\удалённые» — вернуть можно, переложив его обратно и нажав «Пересобрать» в разделе «Реестр».", "Удалить")) return;
            try
            {
                s.RemoveRecord(pc);
                bool busy;
                if (s.All.Count > 0) s.BuildXlsx(out busy);
            }
            catch (Exception ex) { ConfirmDialog.Ask(FindForm(), "Удалить компьютер", "Не удалось удалить: " + ex.Message, "Понятно"); }
            Save();
        }

        void OpenItem(MapItem it)
        {
            if (it.IsPc)
                using (var d = new PcInfoDialog(it.Pc, map.RoomTitle(it.Room))) d.ShowDialog(FindForm());
        }

        void ResetLayout()
        {
            if (!ConfirmDialog.Ask(FindForm(), "Расставить заново",
                "Кабинеты отделов, созданные программой, будут построены заново, значки встанут по порядку. " +
                "Кабинеты, которые вы добавили или двигали сами, останутся на месте.", "Расставить")) return;
            MapLayout.Reset(s.Map);
            Save();
            Canvas.Fit();
        }

        // ------------------------------------------------------------ здания
        void ShowBuildingMenu()
        {
            bldMenu.Items.Clear();
            for (int i = 0; i < s.Map.Buildings.Count; i++)
            {
                int idx = i;
                var mi = new ToolStripMenuItem((i == bld ? "✓  " : "     ") + s.Map.Buildings[i].Name) { ForeColor = Theme.Text };
                mi.Click += (o, e) => { bld = idx; floor = 0; lastExport = null; Refresh0(false); Canvas.Fit(); LayoutPage(); };
                bldMenu.Items.Add(mi);
            }
            bldMenu.Items.Add(new ToolStripSeparator());
            bldMenu.Items.Add(new ToolStripMenuItem("Новое здание…", null, (o, e) => AddBuilding()) { ForeColor = Theme.Text });
            bldMenu.Items.Add(new ToolStripMenuItem("Переименовать здание…", null, (o, e) => RenameBuilding()) { ForeColor = Theme.Text });
            bldMenu.Items.Add(new ToolStripMenuItem("Удалить здание", null, (o, e) => DeleteBuilding()) { ForeColor = Theme.Text });
            bldMenu.MinimumSize = new Size(btnBuilding.Width, 0);
            bldMenu.Show(btnBuilding, new Point(0, btnBuilding.Height + Theme.S(2)));
        }

        void AddBuilding()
        {
            using (var d = new TextDialog("Новое здание", "Название (корпус, склад, филиал)", "Здание " + (s.Map.Buildings.Count + 1)))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                var b = new Building { Id = s.Map.NewId("b"), Name = d.Value };
                s.Map.Buildings.Add(b);
                s.Map.Floors.Add(new Floor { Id = s.Map.NewId("f"), BuildingId = b.Id, Name = "1 этаж" });
                bld = s.Map.Buildings.Count - 1;
                floor = 0;
                Save();
                Canvas.Fit();
                LayoutPage();
            }
        }

        void RenameBuilding()
        {
            var b = CurBuilding;
            if (b == null) return;
            using (var d = new TextDialog("Здание", "Название", b.Name))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                b.Name = d.Value;
                Save();
                LayoutPage();
            }
        }

        void DeleteBuilding()
        {
            var b = CurBuilding;
            if (b == null) return;
            if (s.Map.Buildings.Count < 2) { ConfirmDialog.Ask(FindForm(), "Удалить здание", "Единственное здание удалить нельзя.", "Понятно"); return; }
            foreach (var f in s.Map.FloorsOf(b))
                if (map.RoomsOn(f).Count > 0)
                {
                    ConfirmDialog.Ask(FindForm(), "Удалить здание", "В здании «" + b.Name + "» есть кабинеты. Перенесите их на этажи другого здания (правый щелчок по кабинету) или удалите, затем удалите здание.", "Понятно");
                    return;
                }
            if (!ConfirmDialog.Ask(FindForm(), "Удалить здание", "Удалить пустое здание «" + b.Name + "» вместе с его этажами?", "Удалить")) return;
            s.Map.Floors.RemoveAll(f => f.BuildingId == b.Id);
            s.Map.Buildings.Remove(b);
            bld = 0;
            floor = 0;
            Save();
            Canvas.Fit();
            LayoutPage();
        }

        // ------------------------------------------------------------ этажи
        void AddFloor()
        {
            var b = CurBuilding;
            if (b == null) return;
            using (var d = new TextDialog("Новый этаж", "Название", (s.Map.FloorsOf(b).Count + 1) + " этаж"))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                s.Map.Floors.Add(new Floor { Id = s.Map.NewId("f"), BuildingId = b.Id, Name = d.Value });
                floor = s.Map.FloorsOf(b).Count - 1;
                Save();
                Canvas.Fit();
            }
        }

        void RenameFloor()
        {
            var f = CurFloor;
            if (f == null) return;
            using (var d = new TextDialog("Этаж", "Название", f.Name))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                f.Name = d.Value;
                Save();
            }
        }

        void DeleteFloor()
        {
            var f = CurFloor;
            if (f == null) return;
            if (BFloors.Count < 2) { ConfirmDialog.Ask(FindForm(), "Удалить этаж", "Единственный этаж здания удалить нельзя — удалите здание целиком.", "Понятно"); return; }
            if (map.RoomsOn(f).Count > 0)
            {
                ConfirmDialog.Ask(FindForm(), "Удалить этаж", "На этаже «" + f.Name + "» есть кабинеты. Перенесите их на другой этаж (правый щелчок по кабинету) или удалите, затем удалите этаж.", "Понятно");
                return;
            }
            if (!ConfirmDialog.Ask(FindForm(), "Удалить этаж", "Удалить пустой этаж «" + f.Name + "»?", "Удалить")) return;
            s.Map.Floors.Remove(f);
            floor = Math.Max(0, floor - 1);
            Save();
            Canvas.Fit();
        }

        void PickBg()
        {
            var f = CurFloor;
            if (f == null) return;
            using (var dlg = new OpenFileDialog { Title = "Подложка этажа «" + f.Name + "» — план помещений", Filter = "Картинки (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" })
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    if (new FileInfo(dlg.FileName).Length > 8 * 1024 * 1024) throw new Exception("файл больше 8 МБ — уменьшите картинку");
                    int w, h;
                    using (var img = Image.FromFile(dlg.FileName)) { w = img.Width; h = img.Height; }
                    Directory.CreateDirectory(s.MapDir);
                    // копия в папке данных: карта не зависит от исходного пути и переносится на флешке
                    string name = f.Id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    if (!s.ReadOnly) File.Copy(dlg.FileName, Path.Combine(s.MapDir, name));
                    f.Bg = name;
                    // этаж принимает пропорции плана: длинная сторона — 1600
                    if (w >= h) { f.W = 1600; f.H = Math.Max(400, 1600 * h / Math.Max(1, w)); }
                    else { f.H = 1600; f.W = Math.Max(400, 1600 * w / Math.Max(1, h)); }
                    Save();
                    Canvas.Fit();
                }
                catch (OutOfMemoryException) { ConfirmDialog.Ask(FindForm(), "Подложка", "Файл не похож на картинку.", "Понятно"); }
                catch (Exception ex) { ConfirmDialog.Ask(FindForm(), "Подложка", "Не удалось взять подложку: " + ex.Message, "Понятно"); }
            }
        }

        void ClearBg()
        {
            var f = CurFloor;
            if (f == null || f.Bg == "") return;
            f.Bg = ""; // сам файл остаётся в папке «карта»
            Save();
        }

        void Save()
        {
            try { s.SaveMap(); }
            catch (Exception ex) { ConfirmDialog.Ask(FindForm(), "Карта", "Не удалось сохранить карту: " + ex.Message, "Понятно"); }
            lastExport = null;
            Refresh0(true);
        }

        // ------------------------------------------------------------ выгрузка
        void Export()
        {
            if (s.ReadOnly) return;
            var errors = new List<string>();
            List<string> files;
            Cursor = Cursors.WaitCursor;
            try { files = MapExport.Export(s, errors); }
            finally { Cursor = Cursors.Default; }
            var names = new List<string>();
            foreach (var f in files) names.Add(Path.GetFileName(f));
            header.Status = errors.Count == 0 ? "Карта выгружена" : "Выгрузка с ошибками";
            header.StatusGrade = errors.Count == 0 ? Collector.Ok : "warn";
            header.Invalidate();
            lastExport = "Выгружено " + DateTime.Now.ToString("HH:mm") + ": " + string.Join(", ", names.ToArray());
            Refresh0(false);
            if (errors.Count > 0)
                ConfirmDialog.Ask(FindForm(), "Выгрузка карты", "Не всё удалось записать:\n" + string.Join("\n", errors.ToArray()), "Понятно");
            if (files.Count > 0)
                try { Process.Start("explorer.exe", "/select,\"" + files[0] + "\""); } catch { }
        }
    }

    /// <summary>Вкладки этажей и «+» для нового этажа; правый щелчок — переименовать, убрать подложку, удалить.</summary>
    class FloorTabs : Painted
    {
        readonly List<string> names = new List<string>();
        readonly List<Rectangle> rects = new List<Rectangle>();
        Rectangle plus;
        int hover = -2;
        public int Index;
        public event EventHandler Selected, Add, Rename, Delete, ClearBg;
        readonly ContextMenuStrip menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Body };

        public FloorTabs()
        {
            BackColor = Theme.Window;
            Cursor = Cursors.Hand;
            menu.BackColor = Theme.Raised;
            menu.ForeColor = Theme.Text;
            menu.Items.Add("Переименовать этаж…", null, (s, e) => Fire(Rename));
            menu.Items.Add("Убрать подложку", null, (s, e) => Fire(ClearBg));
            menu.Items.Add("Удалить этаж", null, (s, e) => Fire(Delete));
        }

        void Fire(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }

        public void Set(List<string> list, int index)
        {
            names.Clear();
            names.AddRange(list);
            Index = index;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Window)) g.FillRectangle(b, ClientRectangle);
            rects.Clear();
            int x = 0, h = Height;
            var fl = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            for (int i = 0; i < names.Count; i++)
            {
                int w = TextRenderer.MeasureText(g, names[i], Theme.BodySemi, new Size(int.MaxValue, int.MaxValue), fl).Width + Theme.S(32);
                var r = new Rectangle(x, 0, w, h);
                rects.Add(r);
                bool sel = i == Index;
                Theme.FillRound(g, r, Theme.S(4), sel ? Theme.Blend(Theme.Accent, 0x26, Theme.Window) : hover == i ? Theme.Raised : Theme.Card,
                                sel ? Theme.Blend(Theme.Accent, 0x80, Theme.Window) : Theme.Border);
                Text0(g, names[i], Theme.BodySemi, sel ? Theme.Accent : Theme.Text, r, Line | TextFormatFlags.HorizontalCenter);
                x += w + Theme.S(8);
            }
            plus = new Rectangle(x, 0, Theme.S(40), h);
            Theme.FillRound(g, plus, Theme.S(4), hover == -1 ? Theme.Raised : Theme.Card, Theme.Border);
            Text0(g, "+", Theme.Header, Theme.Text2, plus, Line | TextFormatFlags.HorizontalCenter);
            string hint = "Правый щелчок по этажу — переименовать, убрать подложку, удалить";
            int hx = plus.Right + Theme.S(16);
            if (hx < Width - Theme.S(100))
                Text0(g, hint, Theme.Small, Theme.Muted, new Rectangle(hx, 0, Width - hx, h), Line);
        }

        int Hit(Point p)
        {
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i;
            return plus.Contains(p) ? -1 : -2;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h == -2 ? Cursors.Default : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -2; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int h = Hit(e.Location);
            if (h == -1 && e.Button == MouseButtons.Left) { Fire(Add); return; }
            if (h < 0) return;
            if (h != Index) { Index = h; Invalidate(); Fire(Selected); }
            if (e.Button == MouseButtons.Right) menu.Show(this, e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Hit(e.Location) >= 0) Fire(Rename);
        }
    }
}
