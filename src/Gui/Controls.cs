using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    /// <summary>Основа всех нарисованных элементов: двойная буферизация, перерисовка при изменении размера.</summary>
    class Painted : Control
    {
        public Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false); // подписи и списки не ловят Tab; кнопка включает это сама
            TabStop = false;
        }

        protected static TextFormatFlags Line => TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        protected static TextFormatFlags Wrap => TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;

        protected void Text0(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags fl) =>
            TextRenderer.DrawText(g, s, f, r, c, fl);

        protected void Glyph(Graphics g, string glyph, float px, Color c, Rectangle r)
        {
            if (!Theme.HasIcons || string.IsNullOrEmpty(glyph)) return;
            using (var f = Theme.Icon(px))
                TextRenderer.DrawText(g, glyph, f, r, c, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Однотонная поверхность (окно, боковая панель, страница) с двойной буферизацией.</summary>
    class Surface : Panel
    {
        public Surface(Color bg)
        {
            BackColor = bg;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>Карточка Ven4Tools: подложка #11161F, рамка в 1 px, скругление 4, заголовок полужирным со значком.</summary>
    class Card : Panel
    {
        public string Title, Icon;
        public Color Accent = Color.Empty; // цветная полоска слева (для карточек-показателей)

        public Card(string title, string icon)
        {
            Title = title;
            Icon = icon;
            BackColor = Theme.Card;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Где начинается содержимое под заголовком.</summary>
        public int ContentTop => Title != null ? Theme.S(50) : Theme.S(16);
        public int Pad => Theme.S(18);

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Window)) g.FillRectangle(b, ClientRectangle);
            Theme.FillRound(g, ClientRectangle, Theme.S(6), Theme.Card, Theme.Border);
            if (Accent != Color.Empty)
                using (var b = new SolidBrush(Accent)) g.FillRectangle(b, 0, Theme.S(12), Theme.S(3), Height - Theme.S(24));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Title == null) return;
            var g = e.Graphics;
            int x = Pad;
            if (Theme.HasIcons && Icon != null)
            {
                using (var f = Theme.Icon(16))
                    TextRenderer.DrawText(g, Icon, f, new Rectangle(x, Theme.S(16), Theme.S(18), Theme.S(20)), Theme.Accent,
                        TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += Theme.S(26);
            }
            TextRenderer.DrawText(g, Title, Theme.CardTitle, new Rectangle(x, Theme.S(14), Width - x - Pad, Theme.S(24)), Theme.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Строки «название — значение» (как «Информация о системе» в Диагностике Ven4Tools).
    /// У значения может быть оценка: точка зелёного/красного/жёлтого цвета.</summary>
    class KvList : Painted
    {
        public class Row
        {
            public string Key, Value, Grade;
            public Row(string k, string v, string grade) { Key = k; Value = v; Grade = grade; }
        }

        public readonly List<Row> Rows = new List<Row>();
        public int KeyWidth = 150;
        public Color ValueColor = Theme.Text;
        public Color KeyColor = Theme.Text2;
        public Font KeyFont = Theme.Body;
        /// <summary>Тонкая линия между строками (для длинных списков, чтобы строки не сливались).</summary>
        public bool Separators;

        public KvList() { BackColor = Theme.Card; }

        public void Set(List<Row> rows) { Rows.Clear(); Rows.AddRange(rows); Invalidate(); }

        int ValueLeft => Theme.S(KeyWidth);
        int Gap => Separators ? Theme.S(17) : Theme.S(10);
        int ItemGap => Theme.S(6); // между пунктами одного значения (например, двумя дисками)

        int ValueWidth(int width, Row r) => Math.Max(Theme.S(60), width - ValueLeft - (r.Grade != null ? Theme.S(18) : 0));

        static string[] Items(Row r) => (r.Value == "" ? "—" : r.Value).Split('\n');

        int ItemHeight(Graphics g, string item, int w) =>
            TextRenderer.MeasureText(g, item, Theme.Body, new Size(w, int.MaxValue), Wrap).Height;

        int RowHeight(Graphics g, Row r, int width)
        {
            var items = Items(r);
            int h = 0, w = ValueWidth(width, r);
            foreach (var it in items) h += ItemHeight(g, it, w);
            h += (items.Length - 1) * ItemGap;
            if (KeyWidth > 0)
                h = Math.Max(h, TextRenderer.MeasureText(g, r.Key, Theme.Body, new Size(ValueLeft - Theme.S(16), int.MaxValue), Wrap).Height);
            return Math.Max(h, Theme.S(20));
        }

        public int Measure(int width)
        {
            if (Rows.Count == 0) return 0;
            int h = 0;
            using (var g = CreateGraphics())
                foreach (var r in Rows) h += RowHeight(g, r, width) + Gap;
            return h - Gap;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int y = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i];
                int h = RowHeight(g, r, Width);
                if (KeyWidth > 0)
                    TextRenderer.DrawText(g, r.Key, KeyFont, new Rectangle(0, y, ValueLeft - Theme.S(16), h), KeyColor, Wrap);
                int x = ValueLeft, w = ValueWidth(Width, r), iy = y;
                foreach (var it in Items(r))
                {
                    int ih = ItemHeight(g, it, w);
                    if (r.Grade != null)
                    {
                        var old = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        int d = Theme.S(8);
                        // точка по центру первой строки пункта
                        using (var b = new SolidBrush(Theme.StatusColor(r.Grade))) g.FillEllipse(b, x, iy + (Theme.Body.Height - d) / 2, d, d);
                        g.SmoothingMode = old;
                    }
                    TextRenderer.DrawText(g, it, Theme.Body, new Rectangle(x + (r.Grade != null ? Theme.S(18) : 0), iy, w, ih),
                        r.Value == "" ? Theme.Muted : ValueColor, Wrap);
                    iy += ih + ItemGap;
                }
                y += h + Gap;
                if (Separators && i < Rows.Count - 1)
                    using (var p = new Pen(Theme.Rgb(0x18, 0x20, 0x2D))) g.DrawLine(p, 0, y - Gap / 2, Width, y - Gap / 2);
            }
        }
    }

    /// <summary>Кнопка Ven4Tools: высота 32, скругление 4. Главная — заливка акцентом, обычная — приподнятая подложка с рамкой.</summary>
    class V4Button : Painted
    {
        public bool Primary;
        public string Icon;
        bool hover, down;

        public V4Button(string text, string icon, bool primary)
        {
            Text = text;
            Icon = icon;
            Primary = primary;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Height = Theme.S(34);
            Width = Preferred();
        }

        public int Preferred()
        {
            int w = TextRenderer.MeasureText(Text, Theme.BodySemi, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            return w + Theme.S(Theme.HasIcons && Icon != null ? 52 : 32);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override bool IsInputKey(Keys k) => k == Keys.Enter || k == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        /// <summary>Нажать кнопку из кода (Enter в диалоге).</summary>
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(b, ClientRectangle);
            Color fill, border, fg;
            if (!Enabled)
            {
                // недоступная кнопка заметно «тише» обычной вторичной
                fill = Theme.Rgb(0x0E, 0x13, 0x1B); border = Theme.Rgb(0x19, 0x22, 0x2E); fg = Theme.Rgb(0x4A, 0x5A, 0x6E);
            }
            else if (Primary)
            {
                fill = down ? Theme.AccentPressed : hover ? Theme.Blend(Color.White, 0x22, Theme.Accent) : Theme.Accent;
                border = fill; fg = Theme.OnAccent;
            }
            else
            {
                // как IconButtonStyle Ven4Tools: при наведении — акцент, иначе приподнятая подложка с рамкой
                fill = down ? Theme.AccentPressed : hover ? Theme.Accent : Theme.Raised;
                border = hover || down ? fill : Theme.Border;
                fg = hover || down ? Theme.OnAccent : Theme.Text;
            }
            if (Focused && !hover && Enabled) border = Primary ? Theme.Text : Theme.Accent;
            Theme.FillRound(g, ClientRectangle, Theme.S(4), fill, border);

            var size = TextRenderer.MeasureText(g, Text, Theme.BodySemi, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            bool icon = Theme.HasIcons && Icon != null;
            int total = size.Width + (icon ? Theme.S(22) : 0);
            int x = Math.Max(Theme.S(10), (Width - total) / 2);
            if (icon)
            {
                Glyph(g, Icon, 15, fg, new Rectangle(x, 0, Theme.S(16), Height));
                x += Theme.S(22);
            }
            Text0(g, Text, Theme.BodySemi, fg, new Rectangle(x, 0, Width - x - Theme.S(8), Height), Line);
        }
    }

    /// <summary>Поле ввода Ven4Tools: тёмная подложка, рамка, в фокусе — рамка акцентом. Может иметь выпадающий список.</summary>
    class InputBox : Painted
    {
        public readonly TextBox Box = new TextBox();
        readonly ContextMenuStrip menu;
        bool dropHover;
        public event EventHandler Picked;

        public InputBox(bool dropDown)
        {
            BackColor = Theme.Field;
            Height = Theme.S(34);
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Field;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Body;
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            if (dropDown)
            {
                menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Theme.Body };
                menu.BackColor = Theme.Raised;
                menu.ForeColor = Theme.Text;
            }
        }

        public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

        public void SetItems(List<string> items)
        {
            if (menu == null) return;
            menu.Items.Clear();
            foreach (var it in items)
            {
                var mi = new ToolStripMenuItem(it) { ForeColor = Theme.Text };
                mi.Click += (s, e) => { Box.Text = ((ToolStripMenuItem)s).Text; Box.Focus(); Box.SelectAll(); if (Picked != null) Picked(this, EventArgs.Empty); };
                menu.Items.Add(mi);
            }
            Invalidate();
        }

        Rectangle DropRect => new Rectangle(Width - Theme.S(34), 0, Theme.S(34), Height);
        bool HasDrop => menu != null && menu.Items.Count > 0;

        protected override void OnLayout(LayoutEventArgs e)
        {
            int right = HasDrop || menu != null ? Theme.S(36) : Theme.S(10);
            Box.Location = new Point(Theme.S(10), (Height - Box.PreferredHeight) / 2 + 1);
            Box.Width = Math.Max(Theme.S(20), Width - Theme.S(10) - right);
            base.OnLayout(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = HasDrop && DropRect.Contains(e.Location);
            if (h != dropHover) { dropHover = h; Cursor = h ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { dropHover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (HasDrop && DropRect.Contains(e.Location))
            {
                menu.MinimumSize = new Size(Width, 0);
                menu.Show(this, new Point(0, Height + Theme.S(2)));
            }
            else Box.Focus();
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Card)) g.FillRectangle(b, ClientRectangle);
            Theme.FillRound(g, ClientRectangle, Theme.S(4), Theme.Field, Box.Focused ? Theme.Accent : Theme.Border);
            if (menu != null)
            {
                var r = DropRect;
                if (dropHover) Theme.FillRound(g, Rectangle.Inflate(r, -Theme.S(4), -Theme.S(4)), Theme.S(3), Theme.Raised, Color.Empty);
                if (Theme.HasIcons) Glyph(g, Theme.IcoDown, 12, HasDrop ? Theme.Text2 : Theme.Border, r);
                else Text0(g, "▾", Theme.Body, HasDrop ? Theme.Text2 : Theme.Border, r, Line | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    /// <summary>Тёмное меню (выпадающий список отделов).</summary>
    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Theme.OnAccent : Theme.Text;
            base.OnRenderItemText(e);
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Raised;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Theme.Accent;
            public override Color MenuItemSelected => Theme.Accent;
            public override Color MenuItemSelectedGradientBegin => Theme.Accent;
            public override Color MenuItemSelectedGradientEnd => Theme.Accent;
            public override Color ImageMarginGradientBegin => Theme.Raised;
            public override Color ImageMarginGradientMiddle => Theme.Raised;
            public override Color ImageMarginGradientEnd => Theme.Raised;
        }
    }

    /// <summary>Подпись над полем ввода.</summary>
    class Caption : Painted
    {
        public string Hint; // приглушённое продолжение: «(необязательно)»
        public Caption(string text, string hint = null) { Text = text; Hint = hint; BackColor = Theme.Card; Height = Theme.S(20); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Text0(g, Text, Theme.SmallSemi, Theme.Text2, ClientRectangle, Line);
            if (Hint == null) return;
            int w = TextRenderer.MeasureText(g, Text, Theme.SmallSemi, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            Text0(g, Hint, Theme.Small, Theme.Rgb(0x64, 0x74, 0x8B), new Rectangle(w, 0, Width - w, Height), Line);
        }
    }

    /// <summary>Пункт боковой панели: значок и название; выбранный — мягкая акцентная подложка и акцентный текст.</summary>
    class NavItem : Painted
    {
        public string Icon;
        bool selected, hover;

        public NavItem(string text, string icon)
        {
            Text = text;
            Icon = icon;
            BackColor = Theme.Sidebar;
            Cursor = Cursors.Hand;
            Height = Theme.S(40);
        }

        public bool Selected { get => selected; set { selected = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, ClientRectangle);
            if (selected) Theme.FillRound(g, ClientRectangle, Theme.S(6), Theme.Blend(Theme.Accent, 0x24, Theme.Sidebar), Color.Empty);
            else if (hover) Theme.FillRound(g, ClientRectangle, Theme.S(6), Theme.Raised, Color.Empty);
            Color fg = selected ? Theme.Accent : Theme.Text;
            int x = Theme.S(12);
            if (Theme.HasIcons)
            {
                Glyph(g, Icon, 16, fg, new Rectangle(x, 0, Theme.S(20), Height));
                x += Theme.S(32);
            }
            Text0(g, Text, Theme.BodySemi, fg, new Rectangle(x, 0, Width - x - Theme.S(8), Height), Line);
        }
    }

    /// <summary>Полоса хода сбора: подпись и тонкая линия.</summary>
    class Progress : Painted
    {
        public int Value, Max = 14;
        public string Step = "";

        public Progress() { BackColor = Theme.Card; Height = Theme.S(46); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            string t = "Идёт сбор сведений" + (Step != "" ? " · " + Step : "") + "  (" + Math.Min(Value, Max) + " из " + Max + ")";
            Text0(g, t, Theme.Body, Theme.Text2, new Rectangle(0, 0, Width, Theme.S(22)), Line);
            var track = new Rectangle(0, Theme.S(32), Width, Theme.S(6));
            Theme.FillRound(g, track, Theme.S(3), Theme.Raised, Color.Empty);
            int w = Max > 0 ? (int)(track.Width * Math.Min(1.0, Value / (double)Max)) : 0;
            if (w > Theme.S(6)) Theme.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), Theme.S(3), Theme.Accent, Color.Empty);
        }
    }

    /// <summary>Таблица без собственной прокрутки (прокручивается страница): шапка на приподнятой подложке, строки через одну.</summary>
    class Table : Painted
    {
        public string[] Headers = new string[0];
        /// <summary>Ширины колонок в пикселях (при 96 dpi); 0 — колонка забирает остаток ширины.</summary>
        public int[] Px = new int[0];
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly List<string> RowGrades = new List<string>(); // оценка последней колонки
        public readonly List<string> RowTips = new List<string>();   // полный текст последней колонки для подсказки
        int RowH => Theme.S(34);
        readonly ToolTip tip = new ToolTip { InitialDelay = 300, ReshowDelay = 100, AutoPopDelay = 20000 };
        string tipText = "";

        public Table() { BackColor = Theme.Card; }

        public int Measure() => RowH * (Rows.Count + 1) + (Rows.Count == 0 ? RowH : 0);

        int[] Xs()
        {
            int[] xs = new int[Headers.Length + 1];
            int fixedW = 0, fills = 0;
            foreach (var p in Px) { if (p > 0) fixedW += Theme.S(p); else fills++; }
            int fillW = fills > 0 ? Math.Max(Theme.S(80), (Width - fixedW) / fills) : 0;
            int x = 0;
            for (int i = 0; i < Headers.Length; i++) { xs[i] = x; x += i < Px.Length && Px[i] > 0 ? Theme.S(Px[i]) : fillW; }
            xs[Headers.Length] = Math.Max(x, Width);
            return xs;
        }

        // Подсказка с полным текстом ячейки, если он не помещается (длинные ФИО, список замечаний)
        protected override void OnMouseMove(MouseEventArgs e)
        {
            string t = "";
            int r = e.Y / RowH - 1;
            if (r >= 0 && r < Rows.Count)
            {
                var xs = Xs();
                for (int c = 0; c < Headers.Length; c++)
                    if (e.X >= xs[c] && e.X < xs[c + 1] && c < Rows[r].Length)
                    {
                        int avail = xs[c + 1] - xs[c] - Theme.S(12) - (c == Headers.Length - 1 ? Theme.S(16) : 0);
                        int need = TextRenderer.MeasureText(Rows[r][c], c == 1 ? Theme.BodySemi : Theme.Body).Width;
                        if (need > avail) t = Rows[r][c];
                        if (c == Headers.Length - 1 && r < RowTips.Count && RowTips[r] != null) t = RowTips[r];
                    }
            }
            if (t != tipText) { tipText = t; if (t == "") tip.Hide(this); else tip.SetToolTip(this, t); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { tipText = ""; tip.Hide(this); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.FillRound(g, new Rectangle(0, 0, Width, RowH), Theme.S(4), Theme.Raised, Color.Empty);
            int[] xs = Xs();
            int pad = Theme.S(12);
            for (int i = 0; i < Headers.Length; i++)
                Text0(g, Headers[i].ToUpperInvariant(), Theme.Eyebrow, Theme.Text2, new Rectangle(xs[i] + pad, 0, xs[i + 1] - xs[i] - pad, RowH), Line);
            if (Rows.Count == 0)
            {
                Text0(g, "В реестре пока нет компьютеров", Theme.Body, Theme.Muted, new Rectangle(pad, RowH, Width - pad, RowH), Line);
                return;
            }
            for (int r = 0; r < Rows.Count; r++)
            {
                int y = RowH * (r + 1);
                if (r % 2 == 1) using (var b = new SolidBrush(Theme.Blend(Theme.Raised, 0x80, Theme.Card))) g.FillRectangle(b, 0, y, Width, RowH);
                using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, y + RowH - 1, Width, y + RowH - 1);
                var row = Rows[r];
                for (int c = 0; c < Headers.Length && c < row.Length; c++)
                {
                    Color fg = c == 1 ? Theme.Text : Theme.Text2;
                    Font f = c == 1 ? Theme.BodySemi : Theme.Body;
                    var rect = new Rectangle(xs[c] + pad, y, xs[c + 1] - xs[c] - pad, RowH);
                    if (c == Headers.Length - 1 && r < RowGrades.Count && RowGrades[r] != null)
                    {
                        Color sc = Theme.StatusColor(RowGrades[r]);
                        var old = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        int d = Theme.S(8);
                        using (var b = new SolidBrush(sc)) g.FillEllipse(b, rect.X, y + (RowH - d) / 2, d, d);
                        g.SmoothingMode = old;
                        rect = new Rectangle(rect.X + Theme.S(16), y, rect.Width - Theme.S(16), RowH);
                        fg = RowGrades[r] == Collector.Bad ? Theme.Danger : Theme.Text2;
                    }
                    Text0(g, row[c] == "" ? "—" : row[c], f, row[c] == "" ? Theme.Muted : fg, rect, Line);
                }
            }
        }
    }

    /// <summary>Картинка с масштабированием по ширине и скруглёнными углами.</summary>
    class Picture : Painted
    {
        public Image Image;
        public Picture(Image img) { Image = img; BackColor = Theme.Card; }

        public int HeightFor(int width) => Image == null ? 0 : width * Image.Height / Image.Width;

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Image == null) return;
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(6)))
            {
                g.SetClip(path);
                g.DrawImage(Image, new Rectangle(0, 0, Width, Height));
                g.ResetClip();
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>Простой текст с переносами (абзац на карточке).</summary>
    class Para : Painted
    {
        public Color Color = Theme.Text2;
        public Font Font0 = Theme.Body;
        public Para(string text) { Text = text; BackColor = Theme.Card; }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
                return TextRenderer.MeasureText(g, Text, Font0, new Size(width, int.MaxValue), Wrap).Height;
        }

        protected override void OnPaint(PaintEventArgs e) => Text0(e.Graphics, Text, Font0, Color, ClientRectangle, Wrap);
    }
}
