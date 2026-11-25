using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    /// <summary>Тёмное модальное окно в стиле программы: карточка с содержимым и кнопки внизу справа.</summary>
    class DarkDialog : Form
    {
        protected readonly Card Body = new Card(null, null);
        protected readonly V4Button Ok, Cancel;
        protected int ContentWidth => ClientSize.Width - Theme.S(28) * 2 - Body.Pad * 2;

        public DarkDialog(string title, string okText, bool cancel = true)
        {
            Text = title;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Ok = new V4Button(okText, null, true);
            Ok.Click += (s, e) => { if (Validate0()) { DialogResult = DialogResult.OK; Close(); } };
            Controls.Add(Body);
            Controls.Add(Ok);
            AcceptButton = null;
            if (cancel)
            {
                Cancel = new V4Button("Отмена", null, false);
                Cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(Cancel);
            }
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                else if (e.KeyCode == Keys.Enter && !(ActiveControl is TextBox tb && tb.Multiline)) { e.SuppressKeyPress = true; Ok.PerformClick(); }
            };
        }

        /// <summary>Проверка перед закрытием по «ОК»; false — окно остаётся открытым.</summary>
        protected virtual bool Validate0() => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitle(this);
        }

        /// <summary>Расставляет карточку и кнопки под содержимое высотой contentH.</summary>
        protected void Arrange(int width, int contentH)
        {
            int pad = Theme.S(28);
            ClientSize = new Size(width, pad + contentH + Body.Pad * 2 + Theme.S(16) + Ok.Height + pad);
            Body.SetBounds(pad, pad, width - pad * 2, contentH + Body.Pad * 2);
            int bx = width - pad;
            Ok.Width = Math.Max(Ok.Preferred(), Theme.S(110));
            bx -= Ok.Width;
            Ok.Location = new Point(bx, Body.Bottom + Theme.S(16));
            if (Cancel != null)
            {
                Cancel.Width = Math.Max(Cancel.Preferred(), Theme.S(110));
                Cancel.Location = new Point(bx - Theme.S(10) - Cancel.Width, Ok.Top);
            }
        }
    }

    /// <summary>Кабинет: название и привязка к отделу (ПК отдела без выбранного кабинета встают сюда).</summary>
    class RoomDialog : DarkDialog
    {
        readonly Caption capName = new Caption("Название"), capDept = new Caption("Отдел", " (необязательно)");
        readonly InputBox inName = new InputBox(false), inDept = new InputBox(true);
        readonly Para hint = new Para("Компьютеры отдела, у которых в карточке АРМ не выбран кабинет, встают в кабинет своего отдела.");
        public string RoomName => inName.Text.Trim();
        public string Dept => inDept.Text.Trim();

        public RoomDialog(string title, string name, string dept, List<string> depts) : base(title, "Сохранить")
        {
            foreach (Control c in new Control[] { capName, inName, capDept, inDept, hint }) Body.Controls.Add(c);
            inName.Text = name;
            inDept.Text = dept;
            inDept.SetItems(depts);
            hint.Font0 = Theme.Small;
            hint.Color = Theme.Muted;
            int w = Theme.S(460), x = Body.Pad, y = Body.Pad;
            Arrange(w, 0);
            int cw = Body.Width - Body.Pad * 2;
            capName.SetBounds(x, y, cw, Theme.S(20)); inName.SetBounds(x, y + Theme.S(24), cw, Theme.S(34)); y += Theme.S(70);
            capDept.SetBounds(x, y, cw, Theme.S(20)); inDept.SetBounds(x, y + Theme.S(24), cw, Theme.S(34)); y += Theme.S(70);
            int hh = hint.Measure(cw);
            hint.SetBounds(x, y, cw, hh);
            Arrange(w, y + hh - Body.Pad);
            ActiveControl = inName.Box;
        }

        protected override bool Validate0()
        {
            if (RoomName != "") return true;
            inName.Box.Focus();
            return false;
        }
    }

    /// <summary>Ввод одного названия (этаж).</summary>
    class TextDialog : DarkDialog
    {
        readonly Caption cap;
        readonly InputBox input = new InputBox(false);
        public string Value => input.Text.Trim();

        public TextDialog(string title, string label, string value) : base(title, "Сохранить")
        {
            cap = new Caption(label);
            Body.Controls.Add(cap);
            Body.Controls.Add(input);
            input.Text = value;
            int w = Theme.S(420);
            Arrange(w, Theme.S(58));
            int cw = Body.Width - Body.Pad * 2;
            cap.SetBounds(Body.Pad, Body.Pad, cw, Theme.S(20));
            input.SetBounds(Body.Pad, Body.Pad + Theme.S(24), cw, Theme.S(34));
            ActiveControl = input.Box;
        }

        protected override bool Validate0() => Value != "";
    }

    /// <summary>Вопрос с подтверждением (удаление кабинета, этажа, «Расставить заново»).</summary>
    class ConfirmDialog : DarkDialog
    {
        public ConfirmDialog(string title, string text, string okText) : base(title, okText)
        {
            var p = new Para(text) { Color = Theme.Text };
            Body.Controls.Add(p);
            int w = Theme.S(460);
            Arrange(w, 0);
            int cw = Body.Width - Body.Pad * 2, h = p.Measure(cw);
            p.SetBounds(Body.Pad, Body.Pad, cw, h);
            Arrange(w, h);
        }

        public static bool Ask(IWin32Window owner, string title, string text, string okText)
        {
            using (var d = new ConfirmDialog(title, text, okText)) return d.ShowDialog(owner) == DialogResult.OK;
        }
    }

    /// <summary>Все сведения о компьютере с карты — те же строки, что на странице «Этот компьютер».</summary>
    class PcInfoDialog : DarkDialog
    {
        public PcInfoDialog(PcRecord r, string room) : base(Report.ShortTitle(r), "Закрыть", false)
        {
            var kv = new KvList { KeyWidth = 150, Separators = true };
            Body.Controls.Add(kv);
            var rows = new List<KvList.Row>
            {
                new KvList.Row("Номер АРМ", r["ArmId"], null),
                new KvList.Row("Компьютер", r["PcName"] + (r["PcModel"] != "" ? " · " + r["PcModel"] : ""), null),
                new KvList.Row("Отдел", r["Dept"], null),
                new KvList.Row("Кабинет", room, null),
                new KvList.Row("ФИО", r["Fio"], null),
                new KvList.Row("Процессор", r["Cpu"], null),
                new KvList.Row("ОЗУ", r["Ram"], null),
                new KvList.Row("Мат. плата", r["Board"], null),
                new KvList.Row("Видео", r["Gpu"], null),
                new KvList.Row("Накопители", r["Disk"], null),
                new KvList.Row("Мониторы", r["Monitor"], null),
                new KvList.Row("ОС", Util.Join("  ", new[] { r["Os"], r["OsVer"], r["OsBuild"] != "" ? "(" + r["OsBuild"] + ")" : "" }), null),
                new KvList.Row("Пользователь", r["User"], null),
                new KvList.Row("IP / MAC", Util.Join("  ", new[] { r["Ip"].Replace("\n", ", "), r["Mac"].Replace("\n", ", ") }), null),
                new KvList.Row("Windows", r["WinAct"], r["WinActSt"]),
                new KvList.Row("Office", r["OfficeAct"] != "" ? r["OfficeAct"] : "не установлен", r["OfficeAct"] != "" ? r["OfficeActSt"] : null),
                new KvList.Row("Антивирус", r["AvAct"] != "" ? r["AvAct"] : r["Av"], r["AvActSt"]),
                new KvList.Row("Диски", r["DiskHealth"], r["DiskHealthSt"]),
                new KvList.Row("Замечания", string.Join("\n", Report.ProblemsOf(r).ToArray()), Report.ProblemsOf(r).Count > 0 ? Collector.Bad : Collector.Ok),
                new KvList.Row("Собрано", r["Collected"], null),
            };
            // последние изменения из истории, новые сверху
            for (int i = r.Changes.Count - 1, n = 0; i >= 0 && n < 8; i--, n++)
            {
                var c = r.Changes[i];
                rows.Add(new KvList.Row(n == 0 ? "Изменения" : "", c[0] + " · " + c[1] + " · " + c[2] + ": " + c[3],
                    c[1] == History.Removed ? "warn" : c[1] == History.Added ? Collector.Ok : null));
            }
            rows.RemoveAll(x => x.Value == "");
            kv.Set(rows);
            int w = Theme.S(720);
            Arrange(w, 0);
            int cw = Body.Width - Body.Pad * 2, h = kv.Measure(cw);
            var wa = Screen.PrimaryScreen.WorkingArea;
            int maxH = wa.Height - Theme.S(220);
            if (h > maxH) { Body.AutoScroll = true; }
            kv.SetBounds(Body.Pad, Body.Pad, cw - (h > maxH ? Theme.S(18) : 0), h);
            Arrange(w, Math.Min(h, maxH));
        }
    }
}
