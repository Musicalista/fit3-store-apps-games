using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Fit3Manager
{
    /// <summary>Small helpers for a clean, friendly look with plain WinForms (flat buttons, white cards, one accent colour).</summary>
    public static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(244, 246, 251), Side = Color.FromArgb(28, 33, 62), SideHot = Color.FromArgb(46, 54, 98), Accent = Color.FromArgb(66, 87, 216), Ok = Color.FromArgb(43, 182, 115), Bad = Color.FromArgb(222, 74, 68),
            Warn = Color.FromArgb(232, 162, 58), Text = Color.FromArgb(26, 29, 41), Muted = Color.FromArgb(104, 112, 134), Line = Color.FromArgb(222, 226, 240), Chip = Color.FromArgb(233, 236, 247);
        public static Font F(float size, bool bold) { return new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }

        public static Label Lbl(string text, float size, bool bold, Color color)
        {
            Label l = new Label(); l.Text = text; l.Font = F(size, bold); l.ForeColor = color; l.AutoSize = true; l.BackColor = Color.Transparent; return l;
        }
        public static Label Wrap(string text, float size, Color color, int width)
        {
            Label l = new Label(); l.Text = text; l.Font = F(size, false); l.ForeColor = color; l.AutoSize = false; l.Width = width; l.BackColor = Color.Transparent;
            using (Graphics g = l.CreateGraphics()) { SizeF s = g.MeasureString(text, l.Font, width); l.Height = (int)Math.Ceiling(s.Height) + 4; }
            return l;
        }
        public static Button Btn(string text, Color back, Color fore, EventHandler click)
        {
            Button b = new Button(); b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.BackColor = back; b.ForeColor = fore; b.Font = F(10f, true); b.Height = 38; b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(14, 2, 14, 2); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; b.Margin = new Padding(0, 0, 10, 0);
            b.FlatAppearance.MouseOverBackColor = Shade(back, -14); if (click != null) b.Click += click; return b;
        }
        public static Button Primary(string text, EventHandler h) { return Btn(text, Accent, Color.White, h); }
        public static Button Soft(string text, EventHandler h) { return Btn(text, Chip, Text, h); }
        public static Button Danger(string text, EventHandler h) { return Btn(text, Bad, Color.White, h); }
        public static Color Shade(Color c, int d) { return Color.FromArgb(Math.Max(0, Math.Min(255, c.R + d)), Math.Max(0, Math.Min(255, c.G + d)), Math.Max(0, Math.Min(255, c.B + d))); }
        public static Panel Card(int w, int h)
        {
            Panel p = new Panel(); p.Width = w; p.Height = h; p.BackColor = Color.White; p.Margin = new Padding(0, 0, 14, 14); p.Padding = new Padding(14);
            p.Paint += delegate(object s, PaintEventArgs e) { using (Pen pen = new Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); };
            return p;
        }
        public static FlowLayoutPanel Row() { FlowLayoutPanel f = new FlowLayoutPanel(); f.AutoSize = true; f.WrapContents = true; f.Margin = new Padding(0, 6, 0, 0); f.BackColor = Color.Transparent; return f; }
    }
}
