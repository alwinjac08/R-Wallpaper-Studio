using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PrivacyWallpaperService
{
    // Keeps the tray menu in the small service process; opening it does not load WinUI.
    internal sealed class ModernTrayMenu : ContextMenuStrip
    {
        internal float ScaleFactor = 1;
        internal bool Dark;
        internal Color Surface, TextColor, Muted, Hover, Line;
        private readonly bool? previewDark;
        private readonly Font menuFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        internal readonly Font IconFont = new Font("Segoe MDL2 Assets", 12f);
        internal readonly Font StrongFont = new Font("Segoe UI", 10f, FontStyle.Bold);

        internal ModernTrayMenu(bool? previewDark = null)
        {
            this.previewDark = previewDark;
            Font = menuFont; ShowImageMargin = false; ShowCheckMargin = false; AutoSize = false;
            DropShadowEnabled = true; Renderer = new FluentRenderer();
            RefreshAppearance();
        }
        internal int Px(float value) { return (int)Math.Round(value * ScaleFactor); }
        internal ToolStripMenuItem AddAction(string text, string glyph, EventHandler action)
        {
            var item = new ToolStripMenuItem(text, null, action) { Tag = glyph, AutoSize = false };
            Items.Add(item); return item;
        }
        internal void AddDivider() { Items.Add(new ToolStripSeparator { AutoSize = false }); }
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            RefreshAppearance(); base.OnOpening(e);
        }
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            try
            {
                int corners = 2, dark = Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 33, ref corners, 4);
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        internal void RefreshAppearance()
        {
            using (var g = CreateGraphics()) ScaleFactor = g.DpiX / 96f;
            Dark = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    Dark = Convert.ToInt32(key == null ? 1 : key.GetValue("SystemUsesLightTheme", 1)) == 0;
            } catch { }
            if (previewDark.HasValue) Dark = previewDark.Value;
            Surface = ColorTranslator.FromHtml(Dark ? "#282828" : "#F9F9F9");
            TextColor = ColorTranslator.FromHtml(Dark ? "#F5F5F5" : "#1A1A1A");
            Muted = ColorTranslator.FromHtml(Dark ? "#A0A0A0" : "#767676");
            Hover = ColorTranslator.FromHtml(Dark ? "#383838" : "#EDEDED");
            Line = ColorTranslator.FromHtml(Dark ? "#454545" : "#DDDDDD");
            if (SystemInformation.HighContrast)
            {
                Surface = SystemColors.Menu; TextColor = SystemColors.MenuText;
                Muted = SystemColors.GrayText; Hover = SystemColors.Highlight; Line = SystemColors.WindowFrame;
            }
            BackColor = Surface; ForeColor = TextColor;
            Padding = new Padding(Px(5), Px(6), Px(5), Px(6));
            foreach (ToolStripItem item in Items)
            {
                item.Size = new Size(Px(362), Px(item is ToolStripSeparator ? 11 : 38));
                item.Margin = Padding.Empty;
            }
            int height = Padding.Vertical;
            foreach (ToolStripItem item in Items) height += item.Height;
            Size = new Size(Px(372), height);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { menuFont.Dispose(); IconFont.Dispose(); StrongFont.Dispose(); }
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private sealed class FluentRenderer : ToolStripRenderer
        {
            private static GraphicsPath Rounded(Rectangle r, int radius)
            {
                int d = radius * 2; var path = new GraphicsPath();
                path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right-d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom-d, d, d, 90, 90);
                path.CloseFigure(); return path;
            }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            { e.Graphics.Clear(((ModernTrayMenu)e.ToolStrip).Surface); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                var m = (ModernTrayMenu)e.ToolStrip; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(m.Line)) using (var path = Rounded(new Rectangle(0, 0, m.Width-1, m.Height-1), m.Px(8)))
                    e.Graphics.DrawPath(pen, path);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var m = (ModernTrayMenu)e.ToolStrip; var item = (ToolStripMenuItem)e.Item;
                var bounds = new Rectangle(0, 0, item.Width, item.Height);
                bool selected = item.Selected && item.Enabled;
                if (selected)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var brush = new SolidBrush(m.Hover)) using (var path = Rounded(bounds, m.Px(4)))
                        e.Graphics.FillPath(brush, path);
                }
                Color ink = !item.Enabled ? m.Muted : selected && SystemInformation.HighContrast ? SystemColors.HighlightText : m.TextColor;
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(e.Graphics, (string)item.Tag, m.IconFont,
                    new Rectangle(m.Px(12), 0, m.Px(22), item.Height), ink, flags | TextFormatFlags.HorizontalCenter);
                string shortcut = item.ShortcutKeyDisplayString;
                int right = m.Px(string.IsNullOrEmpty(shortcut) ? 32 : 116);
                TextRenderer.DrawText(e.Graphics, item.Text, item.Font,
                    new Rectangle(m.Px(44), 0, item.Width-m.Px(44)-right, item.Height), ink, flags | TextFormatFlags.EndEllipsis);
                if (!string.IsNullOrEmpty(shortcut))
                    TextRenderer.DrawText(e.Graphics, shortcut, m.Font,
                        new Rectangle(item.Width-m.Px(116), 0, m.Px(85), item.Height), selected && SystemInformation.HighContrast ? ink : m.Muted, flags | TextFormatFlags.Right);
                if (item.Checked)
                    TextRenderer.DrawText(e.Graphics, "\uE73E", m.IconFont,
                        new Rectangle(item.Width-m.Px(28), 0, m.Px(18), item.Height), ink, flags | TextFormatFlags.HorizontalCenter);
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                var m = (ModernTrayMenu)e.ToolStrip;
                using (var pen = new Pen(m.Line)) e.Graphics.DrawLine(pen, m.Px(9), e.Item.Height/2, e.Item.Width-m.Px(9), e.Item.Height/2);
            }
            // Text, icons, checkmarks and shortcut hints are rendered together for consistent alignment.
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { }
            protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        }
    }
}
