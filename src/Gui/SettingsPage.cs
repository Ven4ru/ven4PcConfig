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

    /// <summary>Раздел «Настройки»: какие форматы карты выгружать кроме Visio.</summary>
    class SettingsPage : Surface
    {
        readonly Session s;
        readonly PageTitle title = new PageTitle("Настройки", "Сохраняются в папке данных и переносятся вместе с реестром");
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
            Controls.Add(cardExport);
            foreach (Control c in new Control[] { tVisio, tPng, tDrawio, tXlsx, note }) cardExport.Controls.Add(c);
            note.Font0 = Theme.Small;
            note.Color = Theme.Muted;
            foreach (var t in new[] { tPng, tDrawio, tXlsx }) t.Changed += (o, e) => Apply();
        }

        /// <summary>Показать текущие настройки (после загрузки данных).</summary>
        public void Fill()
        {
            tPng.Checked = s.Settings.Png;
            tDrawio.Checked = s.Settings.Drawio;
            tXlsx.Checked = s.Settings.XlsxSheet;
            foreach (var t in new[] { tPng, tDrawio, tXlsx }) t.Invalidate();
        }

        void Apply()
        {
            s.Settings.Png = tPng.Checked;
            s.Settings.Drawio = tDrawio.Checked;
            s.Settings.XlsxSheet = tXlsx.Checked;
            try { s.SaveSettings(); }
            catch (Exception ex) { ConfirmDialog.Ask(FindForm(), "Настройки", "Не удалось сохранить настройки: " + ex.Message, "Понятно"); }
        }

        public void LayoutPage()
        {
            int pad = Theme.S(28);
            int W = Math.Min(Theme.S(760), Math.Max(Theme.S(600), ClientSize.Width) - pad * 2);
            int sy = -VerticalScroll.Value;
            title.SetBounds(pad, sy + Theme.S(22), Math.Max(Theme.S(600), ClientSize.Width) - pad * 2, Theme.S(64));
            int inner = W - cardExport.Pad * 2, y = cardExport.ContentTop;
            foreach (var t in new[] { tVisio, tPng, tDrawio, tXlsx })
            {
                t.SetBounds(cardExport.Pad, y, inner, Theme.S(56));
                y += Theme.S(60);
            }
            int nh = note.Measure(inner);
            note.SetBounds(cardExport.Pad, y + Theme.S(6), inner, nh);
            cardExport.SetBounds(pad, sy + Theme.S(100), W, y + Theme.S(6) + nh + cardExport.Pad);
            AutoScrollMinSize = new Size(0, cardExport.Bottom - sy + Theme.S(16));
        }
    }
}
