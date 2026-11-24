using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcInventory.Gui
{
    /// <summary>
    /// Цвета и шрифты окна. Палитра — тема «Бирюзовая» клиента Ven4Tools (Ven4Tools/Services/ThemeService.cs,
    /// тема по умолчанию): те же подложки, текст, рамки и акцент #00BCD4, те же цвета статусов.
    /// Размеры заданы в пикселях при 96 dpi и умножаются на Scale.
    /// </summary>
    static class Theme
    {
        public static readonly Color Window = Rgb(0x0A, 0x0A, 0x14);
        public static readonly Color Sidebar = Rgb(0x0D, 0x10, 0x18);
        public static readonly Color Card = Rgb(0x11, 0x16, 0x1F);
        public static readonly Color Raised = Rgb(0x15, 0x1C, 0x27);
        public static readonly Color Field = Rgb(0x0A, 0x0E, 0x16);
        public static readonly Color Text = Rgb(0xE2, 0xE8, 0xF0);
        public static readonly Color Text2 = Rgb(0xAA, 0xB8, 0xCE);
        public static readonly Color Muted = Rgb(0x7A, 0x8F, 0xA8);
        public static readonly Color Border = Rgb(0x1E, 0x2A, 0x38);
        public static readonly Color Accent = Rgb(0x00, 0xBC, 0xD4);
        public static readonly Color OnAccent = Rgb(0x06, 0x13, 0x0D);
        public static readonly Color Success = Rgb(0x4A, 0xDE, 0x80);
        public static readonly Color Warning = Rgb(0xFB, 0xBF, 0x24);
        public static readonly Color Danger = Rgb(0xF8, 0x71, 0x71);
        public static readonly Color Info = Rgb(0x38, 0xBD, 0xF8);

        /// <summary>Нажатый акцент — на 15 % темнее (как AccentPressed в Ven4Tools).</summary>
        public static Color AccentPressed => Rgb((int)(Accent.R * 0.85), (int)(Accent.G * 0.85), (int)(Accent.B * 0.85));

        public static float Scale = 1f;
        public static Font Body, BodySemi, Small, SmallSemi, Title, Header, CardTitle, Eyebrow, Logo, Big;
        static string iconFamily;

        public static Color Rgb(int r, int g, int b) => Color.FromArgb(Clamp(r), Clamp(g), Clamp(b));
        static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        /// <summary>Цвет fg с прозрачностью alpha поверх bg — для подложек «мягкого» акцента и статусов.</summary>
        public static Color Blend(Color fg, int alpha, Color bg)
        {
            double a = alpha / 255.0;
            return Rgb((int)(fg.R * a + bg.R * (1 - a)), (int)(fg.G * a + bg.G * (1 - a)), (int)(fg.B * a + bg.B * (1 - a)));
        }

        public static int S(int px) => (int)Math.Round(px * Scale);

        public static void Init()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; } catch { Scale = 1f; }
            if (Scale < 1f) Scale = 1f;
            string ui = Has("Segoe UI") ? "Segoe UI" : "Tahoma";
            string semi = Has("Segoe UI Semibold") ? "Segoe UI Semibold" : ui;
            Body = F(ui, 13.5f, FontStyle.Regular);
            BodySemi = F(semi, 13.5f, semi == ui ? FontStyle.Bold : FontStyle.Regular);
            Small = F(ui, 12f, FontStyle.Regular);
            SmallSemi = F(semi, 12f, semi == ui ? FontStyle.Bold : FontStyle.Regular);
            CardTitle = F(semi, 14.5f, semi == ui ? FontStyle.Bold : FontStyle.Regular);
            Header = F(semi, 16f, semi == ui ? FontStyle.Bold : FontStyle.Regular);
            Title = F(semi, 24f, semi == ui ? FontStyle.Bold : FontStyle.Regular);
            Big = F(semi, 28f, semi == ui ? FontStyle.Bold : FontStyle.Regular);
            Eyebrow = F(ui, 11f, FontStyle.Bold);
            Logo = F(ui, 19f, FontStyle.Bold);
            iconFamily = Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : Has("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
        }

        static Font F(string family, float px, FontStyle st) => new Font(family, px * Scale, st, GraphicsUnit.Pixel);

        static bool Has(string family)
        {
            foreach (var f in FontFamily.Families) if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Значки Segoe Fluent Icons / MDL2 (Windows 10+). На старых Windows значков нет — только текст.</summary>
        public static bool HasIcons => iconFamily != null;

        public static Font Icon(float px) => iconFamily == null ? null : new Font(iconFamily, px * Scale, FontStyle.Regular, GraphicsUnit.Pixel);

        // Значки (коды Segoe MDL2 Assets — есть и в Segoe Fluent Icons)
        public const string IcoComputer = "", IcoList = "", IcoInfo = "", IcoRefresh = "",
            IcoSave = "", IcoExcel = "", IcoFolder = "", IcoDown = "", IcoCheck = "",
            IcoWarn = "", IcoCard = "", IcoShield = "", IcoChip = "", IcoPeople = "";

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != Color.Empty) using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = old;
        }

        public static Color StatusColor(string grade) =>
            grade == Collector.Ok ? Success : grade == Collector.Bad ? Danger : grade == "warn" ? Warning : Muted;

        // ---------------------------------------------------------------- тёмные полосы прокрутки и заголовок окна
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Тёмная полоса прокрутки (Windows 10 1809+). На старых системах просто ничего не меняется.</summary>
        public static void DarkScroll(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        }

        /// <summary>Тёмный заголовок окна (Windows 10 1809+ / 11), как у Ven4Tools.</summary>
        public static void DarkTitle(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int caption = Window.R | (Window.G << 8) | (Window.B << 16); // DWMWA_CAPTION_COLOR (Windows 11)
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
            }
            catch { }
        }
    }
}
