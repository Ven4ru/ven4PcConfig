using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    /// <summary>Переключатель Ven4Tools: подпись, пояснение и «тумблер» справа. Locked — всегда включён.</summary>
    class Toggle : Painted
    {
        public bool Checked, Locked;
        public string Sub;
        bool hover;
        public event EventHandler Changed;

        public Toggle(string text, string sub)
        {
            Text = text;
            Sub = sub;
            BackColor = Theme.Card;
            Height = Theme.S(56);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnClick(EventArgs e) { Flip(); base.OnClick(e); }
        protected override bool IsInputKey(Keys k) => k == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Flip(); base.OnKeyUp(e); }

        void Flip()
        {
            if (Locked) return;
            Checked = !Checked;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, ClientRectangle);
            int sw = Theme.S(44), sh = Theme.S(24);
            var track = new Rectangle(Width - sw - Theme.S(2), (Height - sh) / 2, sw, sh);
            bool on = Checked || Locked;
            Color fill = on ? (Locked ? Theme.Blend(Theme.Accent, 0x70, Theme.Card) : Theme.Accent) : hover ? Theme.Raised : Theme.Field;
            Theme.FillRound(g, track, sh / 2, fill, on ? fill : Focused ? Theme.Accent : Theme.Border);
            int d = sh - Theme.S(8);
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.Text2))
                g.FillEllipse(b, on ? track.Right - d - Theme.S(4) : track.X + Theme.S(4), track.Y + Theme.S(4), d, d);
            g.SmoothingMode = old;
            int tw = track.X - Theme.S(16);
            Text0(g, Text, Theme.BodySemi, Theme.Text, new Rectangle(0, Theme.S(6), tw, Theme.S(22)), Line);
            Text0(g, Sub, Theme.Small, Theme.Text2, new Rectangle(0, Theme.S(28), tw, Theme.S(20)), Line);
        }
    }

    /// <summary>Список правил «часть названия программы»: флажок вкл/выкл, название, крестик «убрать».</summary>
    class ProgList : Painted
    {
        public List<ProgRule> Rules = new List<ProgRule>();
        public event EventHandler Changed;
        int hover = -1;
        bool hoverX;
        int RowH => Theme.S(34);

        public ProgList() { BackColor = Theme.Card; Cursor = Cursors.Hand; }

        public int Measure() => Math.Max(1, Rules.Count) * RowH;

        Rectangle Box(int i) => new Rectangle(0, i * RowH + (RowH - Theme.S(18)) / 2, Theme.S(18), Theme.S(18));
        Rectangle X(int i) => new Rectangle(Width - Theme.S(30), i * RowH, Theme.S(30), RowH);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Card)) g.FillRectangle(b, ClientRectangle);
            if (Rules.Count == 0) { Text0(g, "Список пуст — добавьте название ниже", Theme.Small, Theme.Muted, new Rectangle(0, 0, Width, RowH), Line); return; }
            for (int i = 0; i < Rules.Count; i++)
            {
                var r = Rules[i];
                if (i == hover) Theme.FillRound(g, new Rectangle(-Theme.S(6), i * RowH, Width + Theme.S(6), RowH), Theme.S(4), Theme.Raised, Color.Empty);
                var box = Box(i);
                Theme.FillRound(g, box, Theme.S(3), r.On ? Theme.Accent : Theme.Field, r.On ? Theme.Accent : Theme.Border);
                if (r.On) Glyph(g, Theme.IcoCheck, 11, Theme.OnAccent, box);
                if (r.On && !Theme.HasIcons) Text0(g, "✓", Theme.SmallSemi, Theme.OnAccent, box, Line | TextFormatFlags.HorizontalCenter);
                Text0(g, r.Name, Theme.Body, r.On ? Theme.Text : Theme.Muted, new Rectangle(Theme.S(30), i * RowH, Width - Theme.S(64), RowH), Line);
                if (i == hover) Text0(g, "×", Theme.Header, hoverX ? Theme.Danger : Theme.Text2, X(i), Line | TextFormatFlags.HorizontalCenter);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = e.Y / RowH;
            if (h >= Rules.Count) h = -1;
            bool hx = h >= 0 && X(h).Contains(e.Location);
            if (h != hover || hx != hoverX) { hover = h; hoverX = hx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = e.Y / RowH;
            if (i < 0 || i >= Rules.Count) return;
            if (X(i).Contains(e.Location)) Rules.RemoveAt(i); else Rules[i].On = !Rules[i].On;
            hover = -1;
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        public void Add(string name)
        {
            name = (name ?? "").Trim();
            if (name == "") return;
            foreach (var r in Rules) if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) { r.On = true; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); return; }
            Rules.Add(new ProgRule(name, true));
            Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
    }

    /// <summary>Раздел «Настройки»: обновления, контроль программ, какие форматы карты выгружать кроме Visio.</summary>
    class SettingsPage : Surface
    {
        readonly Session s;
        readonly PageTitle title = new PageTitle("Настройки", "Сохраняются в папке данных и переносятся вместе с реестром");
        readonly Card cardUpdate = new Card("Обновления", Theme.IcoRefresh);
        readonly Toggle tUpdate = new Toggle("Проверять обновления при запуске", "Новая версия ставится кнопкой «Обновить» рядом с exe, данные не затрагиваются");
        public readonly Para UpdateState = new Para("");
        readonly Card cardProg = new Card("Контроль программ", Theme.IcoShield);
        readonly Para progNote = new Para("ПК с запрещённой программой или без обязательной попадает в «Требует внимания». Название ищется как часть имени программы, без учёта регистра. Флажок — правило включено, крестик — убрать.");
        readonly Caption capForbidden = new Caption("Запрещённые"), capRequired = new Caption("Обязательные");
        readonly ProgList listForbidden = new ProgList(), listRequired = new ProgList();
        readonly InputBox inForbidden = new InputBox(false), inRequired = new InputBox(false);
        readonly V4Button btnForbidden = new V4Button("Добавить", null, false), btnRequired = new V4Button("Добавить", null, false);
        /// <summary>Списки программ изменились — окну нужно пересчитать замечания.</summary>
        public event EventHandler RulesChanged;
        readonly Card cardExport = new Card("Выгрузка карты", Theme.IcoSave);
        readonly Toggle tVisio = new Toggle("Visio (.vsdx)", "Основной формат: этажи — страницы, характеристики ПК — в «Данных фигуры»") { Locked = true };
        readonly Toggle tPng = new Toggle("Картинки PNG", "По файлу на этаж — для печати и чтобы отправить без Visio");
        readonly Toggle tDrawio = new Toggle("draw.io (diagrams.net)", "Бесплатный редактор схем, открывается в браузере");
        readonly Toggle tXlsx = new Toggle("Лист «Карта» в " + Program.XlsxName, "Картинки этажей отдельным листом реестра");
        readonly Para note = new Para("Файлы карты записываются рядом с программой кнопкой «Выгрузить» в разделе «Карта». Лист Excel обновляется при каждом сохранении реестра.");

        public SettingsPage(Session session) : base(Theme.Window)
        {
            s = session;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Visible = false;
            Controls.Add(title);
            Controls.Add(cardUpdate);
            Controls.Add(cardProg);
            Controls.Add(cardExport);
            cardUpdate.Controls.Add(tUpdate);
            cardUpdate.Controls.Add(UpdateState);
            UpdateState.Font0 = Theme.Small;
            UpdateState.Color = Theme.Muted;
            foreach (Control c in new Control[] { progNote, capForbidden, listForbidden, inForbidden, btnForbidden, capRequired, listRequired, inRequired, btnRequired })
                cardProg.Controls.Add(c);
            progNote.Font0 = Theme.Small;
            progNote.Color = Theme.Muted;
            foreach (Control c in new Control[] { tVisio, tPng, tDrawio, tXlsx, note }) cardExport.Controls.Add(c);
            note.Font0 = Theme.Small;
            note.Color = Theme.Muted;
            foreach (var t in new[] { tPng, tDrawio, tXlsx, tUpdate }) t.Changed += (o, e) => Apply(false);
            inForbidden.Placeholder = "Часть названия, например Steam";
            inRequired.Placeholder = "Часть названия, например VipNet";
            listForbidden.Changed += (o, e) => Apply(true);
            listRequired.Changed += (o, e) => Apply(true);
            btnForbidden.Click += (o, e) => { listForbidden.Add(inForbidden.Text); inForbidden.Text = ""; LayoutPage(); };
            btnRequired.Click += (o, e) => { listRequired.Add(inRequired.Text); inRequired.Text = ""; LayoutPage(); };
            inForbidden.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnForbidden.PerformClick(); } };
            inRequired.Box.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; btnRequired.PerformClick(); } };
        }

        /// <summary>Показать текущие настройки (после загрузки данных).</summary>
        public void Fill()
        {
            tPng.Checked = s.Settings.Png;
            tDrawio.Checked = s.Settings.Drawio;
            tXlsx.Checked = s.Settings.XlsxSheet;
            tUpdate.Checked = s.Settings.AutoUpdate;
            listForbidden.Rules = s.Settings.Forbidden;
            listRequired.Rules = s.Settings.Required;
            foreach (var t in new Control[] { tPng, tDrawio, tXlsx, tUpdate, listForbidden, listRequired }) t.Invalidate();
            LayoutPage();
        }

        void Apply(bool rules)
        {
            s.Settings.Png = tPng.Checked;
            s.Settings.Drawio = tDrawio.Checked;
            s.Settings.XlsxSheet = tXlsx.Checked;
            s.Settings.AutoUpdate = tUpdate.Checked;
            s.Settings.Forbidden = listForbidden.Rules;
            s.Settings.Required = listRequired.Rules;
            try { s.SaveSettings(); }
            catch (Exception ex) { ConfirmDialog.Ask(FindForm(), "Настройки", "Не удалось сохранить настройки: " + ex.Message, "Понятно"); }
            if (rules) { LayoutPage(); if (RulesChanged != null) RulesChanged(this, EventArgs.Empty); }
        }

        public void LayoutPage()
        {
            int pad = Theme.S(28), gap = Theme.S(16);
            int W = Math.Min(Theme.S(760), Math.Max(Theme.S(600), ClientSize.Width) - pad * 2);
            int sy = -VerticalScroll.Value;
            title.SetBounds(pad, sy + Theme.S(22), Math.Max(Theme.S(600), ClientSize.Width) - pad * 2, Theme.S(64));
            int inner = W - cardExport.Pad * 2;

            // обновления
            int y = cardUpdate.ContentTop;
            tUpdate.SetBounds(cardUpdate.Pad, y, inner, Theme.S(56));
            y += Theme.S(60);
            int uh = UpdateState.Measure(inner);
            UpdateState.SetBounds(cardUpdate.Pad, y, inner, uh);
            cardUpdate.SetBounds(pad, sy + Theme.S(100), W, y + uh + cardUpdate.Pad);

            // контроль программ: два столбца
            y = cardProg.ContentTop;
            int ph = progNote.Measure(inner);
            progNote.SetBounds(cardProg.Pad, y, inner, ph);
            y += ph + Theme.S(12);
            int colW = (inner - gap) / 2, top = y, bottom = y;
            var cols = new[] { new Control[] { capForbidden, listForbidden, inForbidden, btnForbidden }, new Control[] { capRequired, listRequired, inRequired, btnRequired } };
            for (int c = 0; c < 2; c++)
            {
                int x = cardProg.Pad + c * (colW + gap), cy = top;
                cols[c][0].SetBounds(x, cy, colW, Theme.S(20)); cy += Theme.S(26);
                var list = (ProgList)cols[c][1];
                int lh = list.Measure();
                list.SetBounds(x + Theme.S(4), cy, colW - Theme.S(4), lh); cy += lh + Theme.S(8);
                var btn = (V4Button)cols[c][3];
                cols[c][2].SetBounds(x, cy, colW - btn.Width - Theme.S(8), Theme.S(34));
                btn.SetBounds(x + colW - btn.Width, cy, btn.Width, Theme.S(34));
                cy += Theme.S(34);
                bottom = Math.Max(bottom, cy);
            }
            cardProg.SetBounds(pad, cardUpdate.Bottom + gap, W, bottom + cardProg.Pad);

            // выгрузка карты
            y = cardExport.ContentTop;
            foreach (var t in new[] { tVisio, tPng, tDrawio, tXlsx })
            {
                t.SetBounds(cardExport.Pad, y, inner, Theme.S(56));
                y += Theme.S(60);
            }
            int nh = note.Measure(inner);
            note.SetBounds(cardExport.Pad, y + Theme.S(6), inner, nh);
            cardExport.SetBounds(pad, cardProg.Bottom + gap, W, y + Theme.S(6) + nh + cardExport.Pad);
            AutoScrollMinSize = new Size(0, cardExport.Bottom - sy + Theme.S(16));
        }
    }
}
