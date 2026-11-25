using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    static class App
    {
        public static void Run(string baseDir, string shotDir, bool live)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Init();
            // /shotlive — настоящие данные, но только для снимков: на диск ничего не пишется
            var form = shotDir != null && !live ? new MainForm(Demo.Session(), shotDir, false) : new MainForm(new Session(baseDir) { ReadOnly = live }, shotDir, live);
            Application.Run(form);
        }
    }

    class MainForm : Form
    {
        readonly Session s;
        readonly string shotDir;
        bool collected, busy;

        // каркас
        readonly Surface sidebar = new Surface(Theme.Sidebar);
        readonly Surface main = new Surface(Theme.Window);
        readonly HeaderBar header = new HeaderBar();
        readonly SidebarStatus sideStatus = new SidebarStatus();
        readonly List<NavItem> nav = new List<NavItem>();
        readonly List<Surface> pages = new List<Surface>();
        int page;

        // «Этот компьютер»
        readonly Surface pgPc = Page();
        readonly PageTitle titlePc = new PageTitle("Этот компьютер", "Сведения собираются при запуске — проверьте их и заполните карточку АРМ");
        readonly V4Button btnRecollect = new V4Button("Собрать заново", Theme.IcoRefresh, false);
        readonly Card cardInfo = new Card("Сведения о компьютере", Theme.IcoComputer);
        readonly KvList kvInfo = new KvList();
        readonly Progress progress = new Progress();
        readonly Card cardLic = new Card("Лицензии и состояние", Theme.IcoShield);
        readonly KvList kvLic = new KvList();
        readonly Card cardArm = new Card("Карточка АРМ", Theme.IcoCard);
        readonly Caption capDept = new Caption("Отдел"), capRoom = new Caption("Кабинет", " (пусто — кабинет отдела)"), capId = new Caption("Номер АРМ"), capFio = new Caption("ФИО сотрудника"), capNote = new Caption("Примечание", " (необязательно)");
        readonly InputBox inDept = new InputBox(true), inRoom = new InputBox(true), inId = new InputBox(false), inFio = new InputBox(false), inNote = new InputBox(false);
        readonly V4Button btnSave = new V4Button("Сохранить в реестр", Theme.IcoSave, true);
        readonly Para armHint = new Para("");
        readonly Card cardMsg = new Card("Сообщения", Theme.IcoInfo);
        readonly KvList kvMsg = new KvList { KeyWidth = 0 };
        readonly V4Button btnAdmin = new V4Button("Перезапустить от имени администратора", Theme.IcoShield, false);
        bool adminHint;

        // «Реестр»
        readonly Surface pgReg = Page();
        readonly PageTitle titleReg = new PageTitle("Реестр АРМ", "");
        readonly V4Button btnOpen = new V4Button("Открыть Excel", Theme.IcoExcel, true);
        readonly V4Button btnRebuild = new V4Button("Пересобрать", Theme.IcoRefresh, false);
        readonly V4Button btnFolder = new V4Button("Папка данных", Theme.IcoFolder, false);
        readonly Stat statPc = new Stat("КОМПЬЮТЕРОВ"), statBad = new Stat("ТРЕБУЮТ ВНИМАНИЯ"), statDept = new Stat("ОТДЕЛОВ");
        readonly Card cardIssues = new Card("Требует внимания", Theme.IcoWarn);
        readonly KvList kvIssues = new KvList { KeyWidth = 360, Separators = true };
        readonly Card cardTable = new Card("Компьютеры", Theme.IcoList);
        readonly Table table = new Table();

        // «Карта», «Настройки»
        MapPage mapPage;
        SettingsPage setPage;

        // «О программе»
        readonly Surface pgAbout = Page();
        readonly PageTitle titleAbout = new PageTitle("О программе", "ven4PcConfig " + Program.Version + " · из семейства Ven4Tools");
        readonly Card cardAbout = new Card(null, null);
        readonly Picture mascot = new Picture(LoadMascot());
        readonly Para aboutText = new Para(
            "ven4PcConfig собирает сведения о рабочих местах в общий реестр Excel: железо, мониторы, сеть, " +
            "принтеры, установленные программы, активацию Windows и Office, антивирус и состояние дисков. " +
            "Компьютеры и принтеры раскладываются на карту помещений, её можно выгрузить в Visio.\n\n" +
            "Скопируйте папку программы на флешку и запустите на каждом ПК. Данные каждого ПК лежат отдельным файлом " +
            "в папке «" + Program.DataDirName + "», из них собирается «" + Program.XlsxName + "».");
        readonly Card cardPriv = new Card("Что не собирается", Theme.IcoShield);
        readonly KvList kvPriv = new KvList { KeyWidth = 0 };
        readonly Card cardModes = new Card("Запуск из командной строки", Theme.IcoInfo);
        readonly KvList kvModes = new KvList { KeyWidth = 110, KeyColor = Theme.Accent, KeyFont = Theme.BodySemi, ValueColor = Theme.Text2 };

        static Surface Page()
        {
            var p = new Surface(Theme.Window) { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
            return p;
        }

        static Image LoadMascot()
        {
            try
            {
                using (var st = typeof(MainForm).Assembly.GetManifestResourceStream("PcInventory.mascot.png"))
                    return st == null ? null : new Bitmap(Image.FromStream(st));
            }
            catch { return null; }
        }

        readonly bool live;

        public MainForm(Session session, string shotDir, bool live)
        {
            s = session;
            this.shotDir = shotDir;
            this.live = live;
            Text = "ven4PcConfig — реестр АРМ";
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(Theme.S(1240), wa.Width - 40), Math.Min(Theme.S(820), wa.Height - 40));
            MinimumSize = new Size(Theme.S(980), Theme.S(640));
            if (shotDir != null)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-20000, -20000);
                ShowInTaskbar = false;
                ClientSize = new Size(Theme.S(1240), Theme.S(820));
            }

            mapPage = new MapPage(s, header);
            setPage = new SettingsPage(s);
            mapPage.DataChanged += (o, e) => { RefreshRegistry(); RefreshRooms(); };
            BuildShell();
            BuildPcPage();
            BuildRegistryPage();
            BuildAboutPage();
            ShowPage(0);
        }

        // ================================================================ каркас
        void BuildShell()
        {
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = Theme.S(236);
            sidebar.Paint += PaintSidebar;
            main.Dock = DockStyle.Fill;
            header.Dock = DockStyle.Top;
            header.Height = Theme.S(60);

            string[][] items = { new[] { "Этот компьютер", Theme.IcoComputer }, new[] { "Реестр", Theme.IcoList }, new[] { "Карта", Theme.IcoMap },
                                 new[] { "Настройки", Theme.IcoSettings }, new[] { "О программе", Theme.IcoInfo } };
            for (int i = 0; i < items.Length; i++)
            {
                var n = new NavItem(items[i][0], items[i][1]);
                int idx = i;
                n.Click += (o, e) => ShowPage(idx);
                nav.Add(n);
                sidebar.Controls.Add(n);
            }
            sidebar.Controls.Add(sideStatus);
            sidebar.Resize += (o, e) => LayoutSidebar();

            pages.Add(pgPc); pages.Add(pgReg); pages.Add(mapPage); pages.Add(setPage); pages.Add(pgAbout);
            foreach (var p in pages) main.Controls.Add(p);
            main.Controls.Add(header);
            Controls.Add(main);
            Controls.Add(sidebar);
            LayoutSidebar();
        }

        void PaintSidebar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int x = Theme.S(22), y = Theme.S(24);
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, x, y, Theme.S(3), Theme.S(44));
            int tx = x + Theme.S(14);
            var fl = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var w1 = TextRenderer.MeasureText(g, "Ven4", Theme.Logo, new Size(int.MaxValue, int.MaxValue), fl).Width;
            TextRenderer.DrawText(g, "Ven4", Theme.Logo, new Point(tx, y - Theme.S(2)), Theme.Text, fl);
            TextRenderer.DrawText(g, "PcConfig", Theme.Logo, new Point(tx + w1, y - Theme.S(2)), Theme.Accent, fl);
            TextRenderer.DrawText(g, "РЕЕСТР АРМ", Theme.Eyebrow, new Point(tx, y + Theme.S(28)), Theme.Accent, fl);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            // разделитель над «Настройками», как в Ven4Tools
            int sepY = nav[3].Top - Theme.S(14);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, Theme.S(26), sepY, sidebar.Width - Theme.S(26), sepY);
        }

        void LayoutSidebar()
        {
            int x = Theme.S(18), w = sidebar.Width - Theme.S(36), y = Theme.S(104);
            for (int i = 0; i < nav.Count; i++)
            {
                if (i == 3) y += Theme.S(28);
                nav[i].SetBounds(x, y, w, Theme.S(42));
                y += Theme.S(48);
            }
            sideStatus.SetBounds(x, sidebar.Height - Theme.S(62), w, Theme.S(38));
            sidebar.Invalidate();
        }

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < pages.Count; k++)
            {
                nav[k].Selected = k == i;
                pages[k].Visible = k == i;
            }
            header.Text = nav[i].Text;
            header.Invalidate();
            LayoutPage();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitle(this);
            foreach (var p in pages) Theme.DarkScroll(p);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (pages.Count > 0) LayoutPage();
        }

        void LayoutPage()
        {
            SuspendLayout();
            if (page == 0) LayoutPc();
            else if (page == 1) LayoutRegistry();
            else if (page == 2) mapPage.LayoutPage();
            else if (page == 3) setPage.LayoutPage();
            else LayoutAbout();
            ResumeLayout();
        }

        int PageWidth(Surface p) => Math.Max(Theme.S(600), p.ClientSize.Width);

        // ================================================================ «Этот компьютер»
        void BuildPcPage()
        {
            pgPc.Controls.Add(titlePc);
            pgPc.Controls.Add(btnRecollect);
            cardInfo.Controls.Add(kvInfo);
            cardInfo.Controls.Add(progress);
            cardLic.Controls.Add(kvLic);
            kvInfo.KeyWidth = 140;
            kvLic.KeyWidth = 140;
            foreach (Control c in new Control[] { capDept, inDept, capRoom, inRoom, capId, inId, capFio, inFio, capNote, inNote, btnSave, armHint })
                cardArm.Controls.Add(c);
            cardMsg.Controls.Add(kvMsg);
            cardMsg.Controls.Add(btnAdmin);
            btnAdmin.Visible = false;
            btnAdmin.Click += (o, e) => RestartAsAdmin();
            // порядок Tab: отдел → кабинет → номер → ФИО → примечание → сохранить
            int ti = 0;
            foreach (Control c in new Control[] { inDept, inRoom, inId, inFio, inNote, btnSave }) c.TabIndex = ti++;
            foreach (Control c in new Control[] { capDept, capRoom, capId, capFio, capNote, armHint }) c.TabStop = false;
            cardArm.TabIndex = 0;
            cardInfo.TabStop = false;
            btnRecollect.TabIndex = 10;
            pgPc.Controls.Add(cardInfo);
            pgPc.Controls.Add(cardLic);
            pgPc.Controls.Add(cardArm);
            pgPc.Controls.Add(cardMsg);
            armHint.Color = Theme.Muted;
            armHint.Font0 = Theme.Small;

            btnSave.Enabled = false;
            btnRecollect.Enabled = false;
            btnSave.Click += (o, e) => SaveArm();
            btnRecollect.Click += (o, e) => StartCollect();
            inDept.Picked += (o, e) => SuggestId(true);
            inDept.Box.Leave += (o, e) => SuggestId(false);
            foreach (var ib in new[] { inDept, inRoom, inId, inFio, inNote })
                ib.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter && btnSave.Enabled) { e.SuppressKeyPress = true; SaveArm(); } };
            SetMessages(new List<KvList.Row> { new KvList.Row("", "Сведения ещё собираются — подождите несколько секунд.", "") });
        }

        string lastDept = "";

        void SuggestId(bool force)
        {
            string d = inDept.Text.Trim();
            if (d == lastDept && !force) return;
            bool changed = d != lastDept;
            lastDept = d;
            if (s.Current == null || d == "") return;
            if (inId.Text.Trim() == "" || (changed && d != s.Current["Dept"]))
                inId.Text = Report.SuggestId(s.Others(), d);
        }

        void LayoutPc()
        {
            var p = pgPc;
            int pad = Theme.S(28), gap = Theme.S(16);
            int W = PageWidth(p) - pad * 2;
            int sy = -p.VerticalScroll.Value;
            titlePc.SetBounds(pad, sy + Theme.S(22), W - btnRecollect.Width - gap, Theme.S(64));
            btnRecollect.Location = new Point(pad + W - btnRecollect.Width, sy + Theme.S(30));
            int right = Theme.S(372);
            int left = W - right - gap;
            int y = sy + Theme.S(100);

            // левая колонка
            int inner = left - cardInfo.Pad * 2;
            int h;
            if (!collected)
            {
                kvInfo.Visible = false;
                progress.Visible = true;
                progress.SetBounds(cardInfo.Pad, cardInfo.ContentTop, inner, progress.Height);
                h = cardInfo.ContentTop + progress.Height + cardInfo.Pad;
            }
            else
            {
                progress.Visible = false;
                kvInfo.Visible = true;
                int kh = kvInfo.Measure(inner);
                kvInfo.SetBounds(cardInfo.Pad, cardInfo.ContentTop, inner, kh);
                h = cardInfo.ContentTop + kh + cardInfo.Pad;
            }
            cardInfo.SetBounds(pad, y, left, h);
            int yl = y + h + gap;
            cardLic.Visible = collected;
            if (collected)
            {
                int lh = kvLic.Measure(inner);
                kvLic.SetBounds(cardLic.Pad, cardLic.ContentTop, inner, lh);
                cardLic.SetBounds(pad, yl, left, cardLic.ContentTop + lh + cardLic.Pad);
                yl = cardLic.Bottom + gap;
            }

            // правая колонка
            int rx = pad + left + gap, ri = right - cardArm.Pad * 2, ry = cardArm.ContentTop;
            foreach (var pair in new[] { new Control[] { capDept, inDept }, new Control[] { capRoom, inRoom }, new Control[] { capId, inId }, new Control[] { capFio, inFio }, new Control[] { capNote, inNote } })
            {
                pair[0].SetBounds(cardArm.Pad, ry, ri, Theme.S(20));
                pair[1].SetBounds(cardArm.Pad, ry + Theme.S(24), ri, Theme.S(34));
                ry += Theme.S(70);
            }
            ry += Theme.S(6);
            btnSave.SetBounds(cardArm.Pad, ry, ri, Theme.S(38));
            ry += Theme.S(48);
            int hh = armHint.Measure(ri);
            armHint.SetBounds(cardArm.Pad, ry, ri, hh);
            ry += hh + cardArm.Pad;
            cardArm.SetBounds(rx, y, right, ry);
            int mh = kvMsg.Measure(ri);
            kvMsg.SetBounds(cardMsg.Pad, cardMsg.ContentTop, ri, mh);
            int msgBottom = cardMsg.ContentTop + mh;
            btnAdmin.Visible = adminHint;
            if (adminHint)
            {
                btnAdmin.SetBounds(cardMsg.Pad, msgBottom + Theme.S(14), ri, Theme.S(34));
                msgBottom = btnAdmin.Bottom;
            }
            cardMsg.SetBounds(rx, cardArm.Bottom + gap, right, msgBottom + cardMsg.Pad);

            int bottom = Math.Max(yl, cardMsg.Bottom + gap);
            p.AutoScrollMinSize = new Size(0, bottom - sy + Theme.S(12));
        }

        void SetMessages(List<KvList.Row> rows)
        {
            kvMsg.Set(rows);
            if (page == 0) LayoutPc();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (shotDir != null && !live) { ShowDemo(); TakeShots(); return; }
            new Thread(() =>
            {
                try { s.Load(); }
                catch (Exception ex) { s.LoadErrors.Add(ex.Message); }
                BeginInvoke((MethodInvoker)(() => { setPage.Fill(); RefreshRegistry(); StartCollect(); }));
            }) { IsBackground = true }.Start();
        }

        void StartCollect()
        {
            if (busy) return;
            busy = true;
            collected = false;
            btnSave.Enabled = false;
            btnSave.Text = "Ожидание сбора…";
            btnRecollect.Enabled = false;
            progress.Value = 0;
            progress.Step = "";
            header.Status = "Идёт сбор сведений";
            header.StatusGrade = "warn";
            header.Invalidate();
            LayoutPc();
            new Thread(() =>
            {
                Exception err = null;
                try
                {
                    s.Collect(step => BeginInvoke((MethodInvoker)(() =>
                    {
                        progress.Value++;
                        progress.Step = step;
                        progress.Invalidate();
                    })));
                }
                catch (Exception ex) { err = ex; }
                BeginInvoke((MethodInvoker)(() => CollectDone(err)));
            }) { IsBackground = true }.Start();
        }

        void CollectDone(Exception err)
        {
            busy = false;
            btnRecollect.Enabled = true;
            if (err != null)
            {
                header.Status = "Ошибка сбора";
                header.StatusGrade = Collector.Bad;
                header.Invalidate();
                SetMessages(new List<KvList.Row> { new KvList.Row("", "Не удалось собрать сведения: " + err.Message, Collector.Bad) });
                return;
            }
            collected = true;
            FillPc();
            header.Status = "Сведения собраны";
            header.StatusGrade = Collector.Ok;
            header.Invalidate();
            btnSave.Enabled = true;
            btnSave.Text = "Сохранить в реестр";
            LayoutPc();
            inDept.Box.Focus();
            if (live)
            {
                // проверка на настоящих данных: только снимки на диск, ничего не сохраняется
                Directory.CreateDirectory(shotDir);
                string[] liveNames = { "live_computer", "live_registry", "live_map" };
                for (int i = 0; i < liveNames.Length; i++)
                {
                    ShowPage(i);
                    if (i == 2) mapPage.Canvas.Fit();
                    Application.DoEvents();
                    Save(Path.Combine(shotDir, liveNames[i] + ".png"));
                }
                Close();
            }
        }

        void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // пользователь отказался в окне UAC — остаёмся как есть
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось перезапустить: " + ex.Message, Text); }
        }

        void FillPc()
        {
            var r = s.Current;
            var info = new List<KvList.Row>
            {
                new KvList.Row("Компьютер", r["PcName"] + (r["PcModel"] != "" ? " · " + r["PcModel"] : ""), null),
                new KvList.Row("Процессор", r["Cpu"], null),
                new KvList.Row("ОЗУ", r["Ram"], null),
                new KvList.Row("Мат. плата", r["Board"], null),
                new KvList.Row("Видео", r["Gpu"], null),
                new KvList.Row("Накопители", r["Disk"], null),
                new KvList.Row("Мониторы", r["Monitor"], null),
                new KvList.Row("ОС", Util.Join("  ", new[] { r["Os"], r["OsVer"], r["OsBuild"] != "" ? "(" + r["OsBuild"] + ")" : "" }), null),
                new KvList.Row("Установлена", FormatDate(r["InstallDate"]), null),
                new KvList.Row("Пользователь", r["User"] + (r["UserFullName"] != "" ? " · " + r["UserFullName"] : ""), null),
                new KvList.Row("IP / MAC", Util.Join("  ", new[] { r["Ip"].Replace("\n", ", "), r["Mac"].Replace("\n", ", ") }), null),
                new KvList.Row("Программы", r.Soft.Count + " установлено", null),
            };
            kvInfo.Set(info);
            kvLic.Set(new List<KvList.Row>
            {
                new KvList.Row("Windows", r["WinAct"], r["WinActSt"]),
                new KvList.Row("Office", r["OfficeAct"] != "" ? r["OfficeAct"] : "не установлен", r["OfficeAct"] != "" ? r["OfficeActSt"] : null),
                new KvList.Row("Антивирус", r["AvAct"] != "" ? r["AvAct"] : r["Av"], r["AvActSt"]),
                new KvList.Row("Диски", r["DiskHealth"], r["DiskHealthSt"]),
            });

            var others = s.Others();
            inDept.SetItems(Report.DeptOrder(others));
            inDept.Text = r["Dept"];
            RefreshRooms();
            var room = s.Map.RoomById(r["RoomId"]);
            inRoom.Text = room != null ? RoomTitle(room) : "";
            lastDept = r["Dept"];
            inId.Text = r["ArmId"] != "" ? r["ArmId"] : Report.SuggestId(others, r["Dept"]);
            inFio.Text = r["Fio"];
            inNote.Text = r["Note"];
            armHint.Text = s.Prev != null
                ? "Компьютер уже есть в реестре — сохранение обновит его строку."
                : "Новый компьютер: после сохранения он появится в реестре. Номер АРМ подсказывается по отделу.";

            var msgs = new List<KvList.Row>();
            foreach (var e in s.LoadErrors) msgs.Add(new KvList.Row("", "Не прочитан файл данных: " + e, "warn"));
            foreach (var e in s.CollectErrors) msgs.Add(new KvList.Row("", e, "warn"));
            if (r["SoftSkipped"] != "")
                msgs.Add(new KvList.Row("", "Программы пользователей " + r["SoftSkipped"] + " не прочитаны: нужен запуск от имени администратора.", "warn"));
            bool admin = UserHives.IsAdmin();
            // Счётчики SMART на новых Windows бывают доступны и без прав — предупреждаем, только если чтение реально запрещено
            if (!admin && r["SoftSkipped"] == "" && r["DiskHealth"].Contains("от имени администратора"))
                msgs.Add(new KvList.Row("", "Без прав администратора не видны износ и температура дисков.", "warn"));
            if (msgs.Count == 0) msgs.Add(new KvList.Row("", "Сведения собраны без ошибок.", Collector.Ok));
            adminHint = !admin;
            SetMessages(msgs);
        }

        static string FormatDate(string iso)
        {
            DateTime dt;
            return DateTime.TryParseExact(iso, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dt) ? dt.ToString("dd.MM.yyyy") : iso;
        }

        string RoomTitle(Room room) => MapLayout.RoomTitle(s.Map, room);

        /// <summary>Список кабинетов в карточке АРМ; если ПК перетащили на карте — и его кабинет.</summary>
        void RefreshRooms()
        {
            var items = new List<string>();
            foreach (var room in s.Map.Rooms) if (!room.Auto) items.Add(RoomTitle(room));
            items.Sort(StringComparer.CurrentCultureIgnoreCase);
            inRoom.SetItems(items);
            if (s.Current != null && !inRoom.Box.Focused)
            {
                var cur = s.Map.RoomById(s.Current["RoomId"]);
                inRoom.Text = cur != null ? RoomTitle(cur) : "";
            }
        }

        /// <summary>Текст поля «Кабинет» → id кабинета. Пусто — кабинет отдела; новое название — новый кабинет на первом этаже.</summary>
        string RoomIdFor(string text)
        {
            text = text.Trim();
            if (text == "") return "";
            foreach (var room in s.Map.Rooms)
                if (string.Equals(RoomTitle(room), text, StringComparison.CurrentCultureIgnoreCase)) return room.Id;
            foreach (var room in s.Map.Rooms)
                if (string.Equals(room.Name, text, StringComparison.CurrentCultureIgnoreCase)) return room.Id;
            s.Map.EnsureBuildings();
            if (s.Map.Floors.Count == 0) s.Map.Floors.Add(new Floor { Id = s.Map.NewId("f"), BuildingId = s.Map.Buildings[0].Id, Name = "1 этаж" });
            var r = new Room { Id = s.Map.NewId("r"), FloorId = s.Map.Floors[0].Id, Name = text, W = MapLayout.MinRoomW, H = MapLayout.Head + MapLayout.CellH * 2 + MapLayout.Gap * 3 };
            s.Map.Rooms.Add(r);
            MapLayout.PlaceRoom(s.Map, r);
            s.SaveMap();
            return r.Id;
        }

        void SaveArm()
        {
            if (s.Current == null || busy) return;
            var r = s.Current;
            try { r["RoomId"] = RoomIdFor(inRoom.Text); }
            catch (Exception ex) { SetMessages(new List<KvList.Row> { new KvList.Row("", "Не удалось сохранить карту: " + ex.Message, Collector.Bad) }); return; }
            r["Dept"] = inDept.Text.Trim();
            r["ArmId"] = inId.Text.Trim();
            r["Fio"] = inFio.Text.Trim();
            r["Note"] = inNote.Text.Trim();
            var msgs = new List<KvList.Row>();
            try
            {
                s.SaveCurrent();
                bool xbusy;
                string path = s.BuildXlsx(out xbusy);
                msgs.Add(new KvList.Row("", "Сохранено. В реестре компьютеров: " + s.All.Count + ".", Collector.Ok));
                if (xbusy) msgs.Add(new KvList.Row("", Program.XlsxName + " открыт в Excel — реестр записан в файл " + Path.GetFileName(path) + ".", "warn"));
                armHint.Text = "Компьютер уже есть в реестре — сохранение обновит его строку.";
                inDept.SetItems(Report.DeptOrder(s.Others()));
                header.Status = "Сохранено в реестр";
                header.StatusGrade = Collector.Ok;
            }
            catch (Exception ex)
            {
                msgs.Add(new KvList.Row("", "Не удалось сохранить: " + ex.Message, Collector.Bad));
                header.Status = "Ошибка сохранения";
                header.StatusGrade = Collector.Bad;
            }
            header.Invalidate();
            SetMessages(msgs);
            RefreshRegistry();
            RefreshRooms();
        }

        // ================================================================ «Реестр»
        void BuildRegistryPage()
        {
            foreach (Control c in new Control[] { titleReg, btnOpen, btnRebuild, btnFolder, statPc, statBad, statDept, cardIssues, cardTable })
                pgReg.Controls.Add(c);
            cardIssues.Controls.Add(kvIssues);
            cardTable.Controls.Add(table);
            table.Headers = new[] { "№ АРМ", "Компьютер", "Отдел", "ФИО", "Собрано", "Замечания" };
            table.Px = new[] { 72, 150, 130, 210, 100, 0 }; // «DESKTOP-XXXXXXX» — 15 знаков полужирным
            btnOpen.Click += (o, e) => OpenFile(s.XlsxPath, "Реестр ещё не создан — сохраните хотя бы один компьютер.");
            btnFolder.Click += (o, e) =>
            {
                try { Directory.CreateDirectory(s.DataDir); Process.Start("explorer.exe", "\"" + s.DataDir + "\""); } catch { }
            };
            btnRebuild.Click += (o, e) =>
            {
                try
                {
                    s.Reload();
                    if (s.All.Count == 0) { MessageBox.Show(this, "В папке данных нет ни одного компьютера.", Text); return; }
                    bool xbusy;
                    string path = s.BuildXlsx(out xbusy);
                    RefreshRegistry();
                    header.Status = xbusy ? "Excel был открыт — записан новый файл" : "Реестр пересобран";
                    header.StatusGrade = xbusy ? "warn" : Collector.Ok;
                    header.Invalidate();
                }
                catch (Exception ex) { MessageBox.Show(this, "Не удалось пересобрать реестр: " + ex.Message, Text); }
            };
        }

        void OpenFile(string path, string missing)
        {
            if (!File.Exists(path)) { MessageBox.Show(this, missing, Text); return; }
            try { Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось открыть файл: " + ex.Message, Text); }
        }

        void RefreshRegistry()
        {
            var all = s.All;
            // главная кнопка — то, что сейчас нужно сделать: открыть готовый файл или собрать его
            bool hasXlsx = File.Exists(s.XlsxPath);
            btnOpen.Enabled = hasXlsx;
            btnOpen.Primary = hasXlsx;
            btnRebuild.Primary = !hasXlsx && all.Count > 0;
            btnOpen.Invalidate();
            btnRebuild.Invalidate();
            if (hasXlsx)
                titleReg.Sub = "Обновлён " + File.GetLastWriteTime(s.XlsxPath).ToString("dd.MM.yyyy HH:mm") + " · " + s.XlsxPath; // дата первой: длинный путь сокращается в середине
            else if (all.Count > 0)
                titleReg.Sub = "ПК в данных: " + all.Count + ", файла Excel нет — нажмите «Пересобрать»";
            else
                titleReg.Sub = "Файл реестра появится после первого сохранения: " + s.XlsxPath;
            titleReg.Invalidate();
            var depts = new Dictionary<string, bool>();
            int bad = 0;
            foreach (var r in all)
            {
                if (r["Dept"] != "") depts[r["Dept"]] = true;
                if (Report.ProblemsOf(r).Count > 0) bad++;
            }
            statPc.Value = all.Count.ToString();
            statBad.Value = bad.ToString();
            statBad.Color = bad > 0 ? Theme.Danger : Theme.Success;
            statDept.Value = depts.Count.ToString();

            var rows = new List<KvList.Row>();
            foreach (var c in Report.CheckList())
            {
                var hit = new List<string>();
                foreach (var r in all) if (c.Hit(r)) hit.Add(Report.ShortTitle(r));
                if (hit.Count > 0) rows.Add(new KvList.Row(c.Name, string.Join(", ", hit.ToArray()), Collector.Bad));
            }
            if (rows.Count == 0) rows.Add(new KvList.Row(all.Count == 0 ? "Реестр пуст" : "Замечаний нет", all.Count == 0 ? "" : "по всем " + all.Count + " компьютерам", Collector.Ok));
            kvIssues.Set(rows);

            var sorted = Report.Sorted(all);
            table.Rows.Clear();
            table.RowGrades.Clear();
            table.RowTips.Clear();
            foreach (var r in sorted)
            {
                var probs = Report.ProblemsOf(r);
                DateTime dt;
                string coll = DateTime.TryParseExact(r["Collected"], "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out dt) ? dt.ToString("dd.MM.yyyy") : r["Collected"];
                string issues = probs.Count == 0 ? "нет" : probs[0] + (probs.Count > 1 ? " · ещё " + (probs.Count - 1) : "");
                table.Rows.Add(new[] { r["ArmId"], r["PcName"], r["Dept"], r["Fio"], coll, issues });
                table.RowGrades.Add(probs.Count == 0 ? Collector.Ok : Collector.Bad);
                table.RowTips.Add(probs.Count > 1 ? "• " + string.Join("\n• ", probs.ToArray()) : null);
            }
            table.Invalidate();
            sideStatus.Count = all.Count;
            sideStatus.Invalidate();
            if (page == 1) LayoutRegistry();
            mapPage.Refresh0(false);
        }

        void LayoutRegistry()
        {
            var p = pgReg;
            int pad = Theme.S(28), gap = Theme.S(16);
            int W = PageWidth(p) - pad * 2;
            int sy = -p.VerticalScroll.Value;
            int bx = pad + W;
            foreach (var b in new[] { btnOpen, btnRebuild, btnFolder })
            {
                bx -= b.Width;
                b.Location = new Point(bx, sy + Theme.S(30));
                bx -= Theme.S(10);
            }
            titleReg.SetBounds(pad, sy + Theme.S(22), bx - pad - gap, Theme.S(64));
            int y = sy + Theme.S(100);
            int sw = (W - gap * 2) / 3;
            statPc.SetBounds(pad, y, sw, Theme.S(96));
            statBad.SetBounds(pad + sw + gap, y, sw, Theme.S(96));
            statDept.SetBounds(pad + (sw + gap) * 2, y, W - (sw + gap) * 2, Theme.S(96));
            y += Theme.S(96) + gap;
            int inner = W - cardIssues.Pad * 2;
            int ih = kvIssues.Measure(inner);
            kvIssues.SetBounds(cardIssues.Pad, cardIssues.ContentTop, inner, ih);
            cardIssues.SetBounds(pad, y, W, cardIssues.ContentTop + ih + cardIssues.Pad);
            y = cardIssues.Bottom + gap;
            int th = table.Measure();
            table.SetBounds(cardTable.Pad, cardTable.ContentTop, inner, th);
            cardTable.SetBounds(pad, y, W, cardTable.ContentTop + th + cardTable.Pad);
            p.AutoScrollMinSize = new Size(0, cardTable.Bottom - sy + gap);
        }

        // ================================================================ «О программе»
        void BuildAboutPage()
        {
            foreach (Control c in new Control[] { titleAbout, cardAbout, cardPriv, cardModes }) pgAbout.Controls.Add(c);
            cardAbout.Controls.Add(mascot);
            cardAbout.Controls.Add(aboutText);
            aboutText.Color = Theme.Text;
            cardPriv.Controls.Add(kvPriv);
            cardModes.Controls.Add(kvModes);
            kvPriv.Set(new List<KvList.Row>
            {
                new KvList.Row("", "Способ активации Windows и Office, ключи продуктов и адреса серверов активации — только «активировано» или «нет».", Collector.Ok),
                new KvList.Row("", "Пароли, переписка, файлы пользователей и история браузера не читаются.", Collector.Ok),
                new KvList.Row("", "ФИО, логин, IP и MAC в реестре — персональные данные: храните его как документ с ограниченным доступом.", "warn"),
            });
            kvModes.Set(new List<KvList.Row>
            {
                new KvList.Row("/auto", "собрать без вопросов — отдел, номер и ФИО берутся из прошлого запуска (для скриптов и групповых политик)", null),
                new KvList.Row("/rebuild", "только пересобрать Excel из сохранённых данных", null),
                new KvList.Row("/console", "работа в консоли с вопросами, как в версии 1.0", null),
            });
        }

        void LayoutAbout()
        {
            var p = pgAbout;
            int pad = Theme.S(28), gap = Theme.S(16);
            int W = PageWidth(p) - pad * 2;
            int sy = -p.VerticalScroll.Value;
            titleAbout.SetBounds(pad, sy + Theme.S(22), W, Theme.S(64));
            int y = sy + Theme.S(100);
            int inner = W - cardAbout.Pad * 2;
            int mw = Math.Min(Theme.S(420), inner / 2);
            int mh = mascot.HeightFor(mw);
            mascot.SetBounds(cardAbout.Pad, cardAbout.Pad, mw, mh);
            int tx = cardAbout.Pad + mw + Theme.S(24), tw = inner - mw - Theme.S(24);
            int th = aboutText.Measure(tw);
            aboutText.SetBounds(tx, cardAbout.Pad + Math.Max(0, (mh - th) / 2), tw, th);
            cardAbout.SetBounds(pad, y, W, Math.Max(mh, th) + cardAbout.Pad * 2);
            y = cardAbout.Bottom + gap;
            int half = (W - gap) / 2, hi = half - cardPriv.Pad * 2;
            int ph = kvPriv.Measure(hi), qh = kvModes.Measure(hi);
            kvPriv.SetBounds(cardPriv.Pad, cardPriv.ContentTop, hi, ph);
            kvModes.SetBounds(cardModes.Pad, cardModes.ContentTop, hi, qh);
            int ch = Math.Max(ph, qh) + cardPriv.ContentTop + cardPriv.Pad;
            cardPriv.SetBounds(pad, y, half, ch);
            cardModes.SetBounds(pad + half + gap, y, W - half - gap, ch);
            p.AutoScrollMinSize = new Size(0, cardModes.Bottom - sy + gap);
        }

        // ================================================================ снимки для проверки вида
        void ShowDemo()
        {
            setPage.Fill();
            RefreshRegistry();
            collected = true;
            FillPc();
            btnSave.Enabled = true;
            btnRecollect.Enabled = true;
            header.Status = "Сведения собраны";
            header.StatusGrade = Collector.Ok;
            adminHint = true; // как при обычном запуске без прав администратора
            SetMessages(new List<KvList.Row>
            {
                new KvList.Row("", "Программы пользователей buh2 не прочитаны: нужен запуск от имени администратора.", "warn"),
            });
        }

        void TakeShots()
        {
            Directory.CreateDirectory(shotDir);
            string[] names = { "1_computer", "2_registry", "3_map", "4_settings", "5_about" };
            var size = ClientSize;
            for (int i = 0; i < names.Length; i++)
            {
                ShowPage(i);
                if (i == 2) mapPage.Canvas.Fit();
                Application.DoEvents();
                Save(Path.Combine(shotDir, names[i] + ".png"));
                // вся страница целиком — окно вытягивается по высоте содержимого
                int need = pages[i].AutoScrollMinSize.Height + header.Height;
                if (need > size.Height && i != 2)
                {
                    ClientSize = new Size(size.Width, need);
                    LayoutPage();
                    Application.DoEvents();
                    Save(Path.Combine(shotDir, names[i] + "_full.png"));
                    ClientSize = size;
                }
                if (i == 2)
                {
                    mapPage.ShowFloor(0, 1);
                    Application.DoEvents();
                    Save(Path.Combine(shotDir, "3_map_floor2.png"));
                    mapPage.ShowFloor(1, 0);
                    Application.DoEvents();
                    Save(Path.Combine(shotDir, "3_map_building2.png"));
                    mapPage.ShowFloor(0, 0);
                }
            }
            // состояние «идёт сбор» — как при настоящем запуске: поля пустые, сохранение недоступно
            ShowPage(0);
            collected = false;
            progress.Value = 6;
            progress.Step = "Накопители";
            header.Status = "Идёт сбор сведений";
            header.StatusGrade = "warn";
            btnSave.Enabled = false;
            btnSave.Text = "Ожидание сбора…";
            btnRecollect.Enabled = false;
            adminHint = false;
            foreach (var ib in new[] { inDept, inId, inFio, inNote }) ib.Text = "";
            armHint.Text = "";
            SetMessages(new List<KvList.Row> { new KvList.Row("", "Сведения ещё собираются — подождите несколько секунд.", "") });
            LayoutPc();
            Application.DoEvents();
            Save(Path.Combine(shotDir, "0_collecting.png"));
            Close();
        }

        void Save(string path)
        {
            // главная область и боковая панель рисуются по отдельности и склеиваются — без рамки окна
            var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                using (var b1 = new Bitmap(sidebar.Width, sidebar.Height)) { sidebar.DrawToBitmap(b1, new Rectangle(0, 0, b1.Width, b1.Height)); g.DrawImage(b1, 0, 0); }
                using (var b2 = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(b2, new Rectangle(0, 0, b2.Width, b2.Height)); g.DrawImage(b2, sidebar.Width, 0); }
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
        }
    }

    /// <summary>Верхняя полоса: название раздела и состояние справа (как «Нет активных задач» в Ven4Tools).</summary>
    class HeaderBar : Painted
    {
        public string Status = "", StatusGrade = "";

        public HeaderBar() { BackColor = Theme.Window; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Text0(g, Text, Theme.Header, Theme.Text, new Rectangle(Theme.S(28), 0, Width / 2, Height), Line);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            if (Status == "") return;
            var fl = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int tw = TextRenderer.MeasureText(g, Status, Theme.Small, new Size(int.MaxValue, int.MaxValue), fl).Width;
            int w = tw + Theme.S(40), h = Theme.S(32);
            var r = new Rectangle(Width - Theme.S(28) - w, (Height - h) / 2, w, h);
            Color c = Theme.StatusColor(StatusGrade);
            Theme.FillRound(g, r, Theme.S(4), Theme.Blend(Theme.Accent, 0x18, Theme.Window), Theme.Blend(Theme.Accent, 0x40, Theme.Window));
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = Theme.S(8);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r.X + Theme.S(14), r.Y + (h - d) / 2, d, d);
            g.SmoothingMode = old;
            Text0(g, Status, Theme.Small, Theme.Text, new Rectangle(r.X + Theme.S(28), r.Y, tw + Theme.S(8), h), Line);
        }
    }

    /// <summary>Заголовок страницы: 24 px полужирный + подзаголовок, как «Диагностика» в Ven4Tools.</summary>
    class PageTitle : Painted
    {
        public string Sub;
        public PageTitle(string title, string sub) { Text = title; Sub = sub; BackColor = Theme.Window; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Text0(g, Text, Theme.Title, Theme.Text, new Rectangle(0, 0, Width, Theme.S(34)), Line);
            string sub = Sub ?? "";
            // многоточие в середине пути — только для путей к файлам, обычный текст обрезается в конце
            var fl = sub.IndexOf('\\') >= 0 ? (Line & ~TextFormatFlags.EndEllipsis) | TextFormatFlags.PathEllipsis : Line;
            Text0(g, sub, Theme.Body, Theme.Text2, new Rectangle(0, Theme.S(38), Width, Theme.S(22)), fl);
        }
    }

    /// <summary>Карточка-показатель: подпись капсом и крупное число.</summary>
    class Stat : Card
    {
        public string Label, Value = "0";
        public Color Color = Theme.Text;

        public Stat(string label) : base(null, null) { Label = label; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var fl = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, Label, Theme.Eyebrow, new Rectangle(Pad, Theme.S(18), Width - Pad * 2, Theme.S(18)), Theme.Accent, fl);
            TextRenderer.DrawText(g, Value, Theme.Big, new Rectangle(Pad, Theme.S(40), Width - Pad * 2, Theme.S(42)), Color, fl);
        }
    }

    /// <summary>Плашка внизу боковой панели: сколько ПК в реестре и версия (как «Интернет доступен · 5.4.0.0»).</summary>
    class SidebarStatus : Painted
    {
        public int Count;
        public SidebarStatus() { BackColor = Theme.Sidebar; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, ClientRectangle);
            Theme.FillRound(g, ClientRectangle, Theme.S(4), Theme.Blend(Theme.Accent, 0x14, Theme.Sidebar), Theme.Blend(Theme.Accent, 0x40, Theme.Sidebar));
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int d = Theme.S(7);
            using (var b = new SolidBrush(Count > 0 ? Theme.Success : Theme.Muted)) g.FillEllipse(b, Theme.S(12), (Height - d) / 2, d, d);
            g.SmoothingMode = old;
            Text0(g, "ПК в реестре: " + Count, Theme.Small, Count > 0 ? Theme.Success : Theme.Text2, new Rectangle(Theme.S(26), 0, Width - Theme.S(80), Height), Line);
            Text0(g, Program.Version, Theme.Small, Theme.Text2, new Rectangle(0, 0, Width - Theme.S(10), Height), Line | TextFormatFlags.Right);
        }
    }
}
