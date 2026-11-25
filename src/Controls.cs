// 批量改名工具 — 現代化介面元件（全部自己畫，跟著主題變色）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class G
{
    public static float S = 1f;   // DPI 縮放
    public static int P(float v) { return (int)Math.Round(v * S); }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
    public static void HQ(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }
    public static void Fill(Graphics g, RectangleF r, float rad, Color c) { using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }
    public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
    {
        using (var p = Round(new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w), rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
    }
    public static Color SurfaceOf(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Theme.Bg;
    }
    public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
    {
        TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPrefix);
    }
}

// ---------- 按鈕 ----------
public enum BtnKind { Primary, Outline, Soft, Ghost, Danger }

public class FlatBtn : Button
{
    public BtnKind Kind = BtnKind.Soft;
    bool hover, down;

    public FlatBtn(string text, BtnKind kind = BtnKind.Soft)
    {
        Text = text; Kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand; AutoSize = true; Margin = new Padding(G.P(3));
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    public override Size GetPreferredSize(Size proposed)
    {
        var sz = TextRenderer.MeasureText(Text, Font);
        return new Size(sz.Width + G.P(Kind == BtnKind.Ghost ? 16 : 26), Math.Max(sz.Height + G.P(14), G.P(34)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g);
        g.Clear(G.SurfaceOf(this));
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        float rad = G.P(8);
        Color fill = Color.Empty, border = Color.Empty, fore;
        switch (Kind)
        {
            case BtnKind.Primary:
                fill = hover ? Theme.AccentHover : Theme.Accent; fore = Theme.OnAccent; break;
            case BtnKind.Outline:
                fill = hover ? Theme.AccentSoft : Color.Empty; border = Theme.Accent; fore = Theme.Accent; break;
            case BtnKind.Danger:
                fill = hover ? Theme.DangerSoft : Color.Empty; border = Theme.Danger; fore = Theme.Danger; break;
            case BtnKind.Ghost:
                fill = hover ? Theme.CardAlt : Color.Empty; fore = hover ? Theme.Text : Theme.TextDim; break;
            default:
                fill = hover ? Theme.Mix(Theme.CardAlt, Theme.Text, 0.06) : Theme.CardAlt; border = Theme.Border; fore = Theme.Text; break;
        }
        if (!Enabled) { fore = Theme.Mix(fore, G.SurfaceOf(this), 0.55); if (fill != Color.Empty) fill = Theme.Mix(fill, G.SurfaceOf(this), 0.5); if (border != Color.Empty) border = Theme.Mix(border, G.SurfaceOf(this), 0.5); }
        if (down && Enabled) r.Inflate(-0.6f, -0.6f);
        if (fill != Color.Empty) G.Fill(g, r, rad, fill);
        if (border != Color.Empty) G.Stroke(g, r, rad, border, Math.Max(1f, G.S * 1.2f));
        if (Focused && ShowFocusCues) G.Stroke(g, RectangleF.Inflate(r, -2, -2), rad - 2, Theme.Mix(fore, fill == Color.Empty ? G.SurfaceOf(this) : fill, 0.6), 1);
        G.Text(g, Text, Font, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

// ---------- 膠囊式分頁列 ----------
public class PillTabs : Control
{
    public List<string> Items = new List<string>();
    int sel, hot = -1;
    public event EventHandler SelectedIndexChanged;
    readonly List<Rectangle> rects = new List<Rectangle>();

    public PillTabs()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }
    public int SelectedIndex
    {
        get { return sel; }
        set { if (value == sel) return; sel = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
    }
    void Layout2()
    {
        rects.Clear();
        int x = G.P(4), h = Height - G.P(8);
        foreach (var it in Items)
        {
            int w = TextRenderer.MeasureText(it, Font).Width + G.P(30);
            rects.Add(new Rectangle(x, G.P(4), w, h));
            x += w + G.P(2);
        }
    }
    public override Size GetPreferredSize(Size proposed)
    {
        Layout2();
        int w = rects.Count > 0 ? rects[rects.Count - 1].Right + G.P(4) : 10;
        return new Size(w, TextRenderer.MeasureText("字", Font).Height + G.P(22));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = rects.FindIndex(r => r.Contains(e.Location));
        if (h != hot) { hot = h; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        int h = rects.FindIndex(r => r.Contains(e.Location));
        if (h >= 0) SelectedIndex = h;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Layout2();
        var g = e.Graphics; G.HQ(g);
        g.Clear(G.SurfaceOf(this));
        if (rects.Count == 0) return;
        var all = new RectangleF(0, 0, rects[rects.Count - 1].Right + G.P(4), Height);
        G.Fill(g, all, Height / 2f, Theme.Dark ? Theme.Card : Theme.Mix(Theme.Bg, Theme.Border, 0.5));
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            Color fore = Theme.TextDim;
            if (i == sel) { G.Fill(g, r, r.Height / 2f, Theme.Inverse); fore = Theme.OnInverse; }
            else if (i == hot) { G.Fill(g, r, r.Height / 2f, Theme.Mix(Theme.Card, Theme.Text, 0.08)); fore = Theme.Text; }
            using (var f = new Font(Font, i == sel ? FontStyle.Bold : FontStyle.Regular))
                G.Text(g, Items[i], f, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}

// ---------- 卡片 ----------
public class Card : Panel
{
    public bool AccentStrip;
    public int Radius = 10;
    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Padding = new Padding(G.P(16), G.P(12), G.P(16), G.P(12));
        Margin = new Padding(G.P(6));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g);
        g.Clear(G.SurfaceOf(this));
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        G.Fill(g, r, G.P(Radius), Theme.Card);
        G.Stroke(g, r, G.P(Radius), Theme.Border, 1);
        if (AccentStrip)
            using (var p = G.Round(r, G.P(Radius)))
            {
                g.SetClip(p);
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 0, G.P(4), Height);
                g.ResetClip();
            }
    }
}

// ---------- 文字 ----------
public enum LabelRole { Text, Dim, Title, Big, Accent, Success, Danger, Warning }

public class TLabel : Label
{
    public LabelRole Role;
    public TLabel(string text, LabelRole role = LabelRole.Text)
    {
        Text = text; Role = role; AutoSize = true; UseMnemonic = false;
        Margin = new Padding(G.P(3), G.P(5), G.P(3), G.P(3));
    }
    public void ApplyFont(Font baseFont)
    {
        if (Role == LabelRole.Title) Font = new Font(baseFont.FontFamily, baseFont.Size + 2f, FontStyle.Bold);
        else if (Role == LabelRole.Big) Font = new Font(baseFont.FontFamily, baseFont.Size + 5f, FontStyle.Bold);
        else if (Role == LabelRole.Dim) Font = new Font(baseFont.FontFamily, baseFont.Size - 0.5f);
    }
    public Color RoleColor()
    {
        switch (Role)
        {
            case LabelRole.Dim: return Theme.TextDim;
            case LabelRole.Accent: return Theme.Accent;
            case LabelRole.Success: return Theme.Success;
            case LabelRole.Danger: return Theme.Danger;
            case LabelRole.Warning: return Theme.Warning;
            default: return Theme.Text;
        }
    }
}

// ---------- 小標籤（膠囊） ----------
public enum ChipKind { Neutral, Accent, Success, Danger, Warning, Info }

public class Chip : Control
{
    public ChipKind Kind;
    public Chip(string text, ChipKind kind = ChipKind.Neutral)
    {
        Text = text; Kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true; Margin = new Padding(G.P(3), G.P(6), G.P(3), G.P(3));
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (AutoSize) Size = GetPreferredSize(Size.Empty); Invalidate(); }
    public override Size GetPreferredSize(Size proposed)
    {
        var sz = TextRenderer.MeasureText(Text, Font);
        return new Size(sz.Width + G.P(18), sz.Height + G.P(8));
    }
    void Colors(out Color fill, out Color fore, out Color border)
    {
        border = Color.Empty;
        switch (Kind)
        {
            case ChipKind.Accent: fill = Theme.AccentSoft; fore = Theme.Accent; border = Theme.Mix(Theme.AccentSoft, Theme.Accent, 0.4); break;
            case ChipKind.Success: fill = Theme.SuccessSoft; fore = Theme.Success; break;
            case ChipKind.Danger: fill = Theme.DangerSoft; fore = Theme.Danger; border = Theme.Mix(Theme.DangerSoft, Theme.Danger, 0.4); break;
            case ChipKind.Warning: fill = Theme.WarningSoft; fore = Theme.Warning; break;
            case ChipKind.Info: fill = Theme.InfoSoft; fore = Theme.Info; break;
            default: fill = Theme.CardAlt; fore = Theme.TextDim; border = Theme.Border; break;
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g); g.Clear(G.SurfaceOf(this));
        Color fill, fore, border; Colors(out fill, out fore, out border);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        G.Fill(g, r, r.Height / 2, fill);
        if (border != Color.Empty) G.Stroke(g, r, r.Height / 2, border, 1);
        G.Text(g, Text, Font, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

// ---------- 輸入框（圓角外框） ----------
public class InputBox : Panel
{
    public readonly TextBox Box;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
    string placeholder = "";

    public InputBox(string text = "", bool multiline = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = new TextBox { BorderStyle = BorderStyle.None, Text = text, Multiline = multiline, Dock = DockStyle.Fill };
        if (multiline) { Box.ScrollBars = ScrollBars.Vertical; Box.AcceptsReturn = true; }
        Padding = new Padding(G.P(10), G.P(7), G.P(10), G.P(6));
        Margin = new Padding(G.P(3));
        Controls.Add(Box);
        Box.GotFocus += (s, e) => Invalidate();
        Box.LostFocus += (s, e) => Invalidate();
        Box.HandleCreated += (s, e) => ApplyPlaceholder();
        Dock = DockStyle.Fill;
    }
    public override string Text { get { return Box == null ? "" : Box.Text; } set { if (Box != null) Box.Text = value; } }
    public string Placeholder { get { return placeholder; } set { placeholder = value ?? ""; ApplyPlaceholder(); } }
    void ApplyPlaceholder()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Box.IsHandleCreated) return;
        try { SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); } catch { }
    }
    public override Size GetPreferredSize(Size proposed)
    {
        int h = Box.Multiline ? Height : Box.PreferredHeight + Padding.Vertical + G.P(2);
        return new Size(proposed.Width > 0 ? proposed.Width : G.P(200), h);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (!Box.Multiline) Height = Box.PreferredHeight + Padding.Vertical + G.P(2);
        base.OnLayout(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g); g.Clear(G.SurfaceOf(this));
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        G.Fill(g, r, G.P(7), Theme.Input);
        G.Stroke(g, r, G.P(7), Box.Focused ? Theme.Accent : Theme.InputBorder, Box.Focused ? 1.6f : 1f);
    }
}

// ---------- 勾選框、單選鈕 ----------
public class TCheck : CheckBox
{
    public TCheck(string text, bool on = false)
    {
        Text = text; Checked = on;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true; Cursor = Cursors.Hand; Margin = new Padding(G.P(3), G.P(5), G.P(10), G.P(3));
    }
    public override Size GetPreferredSize(Size proposed)
    {
        var sz = TextRenderer.MeasureText(Text, Font);
        return new Size(sz.Width + G.P(30), Math.Max(sz.Height + G.P(6), G.P(24)));
    }
    public static void DrawBox(Graphics g, Rectangle box, bool on, bool enabled)
    {
        G.HQ(g);
        var r = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1);
        if (on)
        {
            G.Fill(g, r, G.P(4), enabled ? Theme.Accent : Theme.Border);
            using (var pen = new Pen(Theme.OnAccent, Math.Max(1.6f, 2f * G.S)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.24f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.43f, r.Y + r.Height * 0.70f), new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.32f) });
        }
        else
        {
            G.Fill(g, r, G.P(4), Theme.Input);
            G.Stroke(g, r, G.P(4), Theme.InputBorder, Math.Max(1f, 1.3f * G.S));
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(G.SurfaceOf(this));
        int s = G.P(18), y = (Height - s) / 2;
        DrawBox(g, new Rectangle(1, y, s, s), Checked, Enabled);
        var tr = new Rectangle(s + G.P(8), 0, Width - s - G.P(8), Height);
        G.Text(g, Text, Font, tr, Enabled ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

public class TRadio : RadioButton
{
    public TRadio(string text)
    {
        Text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true; Cursor = Cursors.Hand; Margin = new Padding(G.P(3), G.P(4), G.P(10), G.P(4));
    }
    public override Size GetPreferredSize(Size proposed)
    {
        var sz = TextRenderer.MeasureText(Text, Font);
        return new Size(sz.Width + G.P(30), Math.Max(sz.Height + G.P(6), G.P(24)));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(G.SurfaceOf(this)); G.HQ(g);
        int s = G.P(18), y = (Height - s) / 2;
        var r = new RectangleF(1.5f, y + 0.5f, s - 1, s - 1);
        using (var b = new SolidBrush(Theme.Input)) g.FillEllipse(b, r);
        using (var p = new Pen(Checked ? Theme.Accent : Theme.InputBorder, Checked ? 2f * G.S : 1.3f * G.S)) g.DrawEllipse(p, r);
        if (Checked) { var dot = RectangleF.Inflate(r, -s * 0.27f, -s * 0.27f); using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, dot); }
        var tr = new Rectangle(s + G.P(8), 0, Width - s - G.P(8), Height);
        G.Text(g, Text, Font, tr, Enabled ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

// ---------- 清單（標題＋小字說明），自己畫，不用系統的 ListBox ----------
public class NiceList : Control
{
    public readonly List<object> Items = new List<object>();
    public Func<object, string> TitleOf = o => o.ToString();
    public Func<object, string> SubOf = o => "";
    public Func<object, string> BadgeOf = o => "";
    public event EventHandler SelectedIndexChanged;
    int sel = -1, hot = -1;
    readonly VScrollBar vs = new VScrollBar { Dock = DockStyle.Right, Visible = false };

    public NiceList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Dock = DockStyle.Fill; Cursor = Cursors.Hand;
        Controls.Add(vs);
        vs.ValueChanged += (s, e) => Invalidate();
    }
    public void BeginUpdate() { }
    public void EndUpdate() { UpdateScroll(); Invalidate(); }
    int RowH { get { return TextRenderer.MeasureText("字", Font).Height * 2 + G.P(24); } }
    public int SelectedIndex
    {
        get { return sel; }
        set
        {
            int v = Items.Count == 0 ? -1 : Math.Max(-1, Math.Min(value, Items.Count - 1));
            if (v == sel) { Invalidate(); return; }
            sel = v; EnsureVisible(); Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }
    }
    public object SelectedItem { get { return sel >= 0 && sel < Items.Count ? Items[sel] : null; } }
    void UpdateScroll()
    {
        int total = Items.Count * RowH;
        vs.Visible = total > Height;
        if (vs.Visible) { vs.Maximum = total; vs.LargeChange = Math.Max(1, Height); vs.SmallChange = RowH; if (vs.Value > total - Height) vs.Value = Math.Max(0, total - Height); }
        else vs.Value = 0;
        Theme.StyleScrollBars(this);
    }
    void EnsureVisible()
    {
        if (!vs.Visible || sel < 0) return;
        int y = sel * RowH;
        if (y < vs.Value) vs.Value = y;
        else if (y + RowH > vs.Value + Height) vs.Value = Math.Min(vs.Maximum - vs.LargeChange + 1, y + RowH - Height);
    }
    int IndexAt(Point p) { int i = (p.Y + (vs.Visible ? vs.Value : 0)) / RowH; return i >= 0 && i < Items.Count ? i : -1; }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateScroll(); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); int i = IndexAt(e.Location); if (i >= 0) SelectedIndex = i; base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != hot) { hot = i; Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!vs.Visible) return;
        vs.Value = Math.Max(0, Math.Min(vs.Maximum - vs.LargeChange + 1, vs.Value - Math.Sign(e.Delta) * RowH));
    }
    protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && sel > 0) SelectedIndex = sel - 1;
        else if (e.KeyCode == Keys.Down && sel < Items.Count - 1) SelectedIndex = sel + 1;
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g); g.Clear(BackColor);
        int h = RowH, off = vs.Visible ? vs.Value : 0, w = Width - (vs.Visible ? vs.Width : 0);
        int line = TextRenderer.MeasureText("字", Font).Height;
        for (int i = 0; i < Items.Count; i++)
        {
            int y = i * h - off;
            if (y + h < 0 || y > Height) continue;
            var it = Items[i];
            var r = new RectangleF(G.P(2), y + G.P(2), w - G.P(4), h - G.P(4));
            if (i == sel)
            {
                G.Fill(g, r, G.P(7), Theme.AccentSoft);
                using (var p = G.Round(r, G.P(7))) { g.SetClip(p); using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y, G.P(4), r.Height); g.ResetClip(); }
            }
            else if (i == hot) G.Fill(g, r, G.P(7), Theme.CardAlt);
            string badge = BadgeOf(it); int bw = 0;
            if (!string.IsNullOrEmpty(badge))
            {
                var sz = TextRenderer.MeasureText(badge, Font);
                var br = new RectangleF(r.Right - sz.Width - G.P(18), r.Y + G.P(7), sz.Width + G.P(12), line + G.P(2));
                G.Fill(g, br, br.Height / 2, i == sel ? Theme.Card : Theme.CardAlt);
                G.Text(g, badge, Font, Rectangle.Round(br), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                bw = (int)br.Width + G.P(8);
            }
            string sub = SubOf(it); bool hasSub = !string.IsNullOrEmpty(sub);
            int top = (int)r.Y + (hasSub ? G.P(8) : (int)(r.Height - line) / 2);
            using (var bold = new Font(Font, FontStyle.Bold))
                G.Text(g, TitleOf(it), bold, new Rectangle((int)r.X + G.P(14), top, (int)r.Width - G.P(20) - bw, line + G.P(2)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (hasSub)
                G.Text(g, sub.Replace("\r", " ").Replace("\n", " "), Font, new Rectangle((int)r.X + G.P(14), top + line + G.P(3), (int)r.Width - G.P(24), line + G.P(2)), Theme.TextDim, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        if (Items.Count == 0)
            G.Text(g, "（空的）", Font, ClientRectangle, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// ---------- 色塊、模式方塊（外觀設定用） ----------
public class Tile : Control
{
    public string Title = "", Sub = "", Glyph = "";
    public Color? Swatch;
    public bool Selected;
    bool hover;
    public Tile()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand; Margin = new Padding(G.P(5));
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g); g.Clear(G.SurfaceOf(this));
        var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
        G.Fill(g, r, G.P(10), Selected ? Theme.AccentSoft : hover ? Theme.CardAlt : Theme.Card);
        G.Stroke(g, r, G.P(10), Selected ? Theme.Accent : Theme.Border, Selected ? 2f * G.S : 1f);
        int x = G.P(14);
        if (Swatch != null)
        {
            int d = G.P(30);
            var c = new RectangleF(x, (Height - d) / 2f, d, d);
            using (var b = new SolidBrush(Swatch.Value)) g.FillEllipse(b, c);
            using (var p = new Pen(Theme.Mix(Swatch.Value, Color.Black, 0.25f), 1)) g.DrawEllipse(p, c);
            if (Selected) using (var p = new Pen(Color.White, 2.2f * G.S)) g.DrawLines(p, new[] { new PointF(c.X + d * 0.28f, c.Y + d * 0.52f), new PointF(c.X + d * 0.44f, c.Y + d * 0.68f), new PointF(c.X + d * 0.74f, c.Y + d * 0.34f) });
            x += d + G.P(12);
        }
        else if (Glyph.Length > 0)
        {
            using (var f = new Font(Font.FontFamily, Font.Size + 6f))
                G.Text(g, Glyph, f, new Rectangle(x, 0, G.P(34), Height), Selected ? Theme.Accent : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            x += G.P(44);
        }
        int line = TextRenderer.MeasureText("字", Font).Height;
        bool hasSub = Sub.Length > 0;
        int top = hasSub ? (Height - line * 2 - G.P(4)) / 2 : (Height - line) / 2;
        using (var bold = new Font(Font, FontStyle.Bold))
            G.Text(g, Title, bold, new Rectangle(x, top, Width - x - G.P(8), line + 2), Theme.Text, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (hasSub) G.Text(g, Sub, Font, new Rectangle(x, top + line + G.P(4), Width - x - G.P(8), line + 2), Theme.TextDim, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

// ---------- 色環 ----------
public class ColorWheel : Control
{
    public float H, Sat = 1f, V = 1f;
    public event EventHandler ColorChanged;
    Bitmap wheel;
    bool drag;
    public ColorWheel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Cross;
    }
    public static Color FromHsv(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360;
        float c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }
    public static void ToHsv(Color c, out float h, out float s, out float v)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        h = 0;
        if (d > 0) { if (max == r) h = 60 * (((g - b) / d) % 6); else if (max == g) h = 60 * ((b - r) / d + 2); else h = 60 * ((r - g) / d + 4); }
        if (h < 0) h += 360;
        s = max == 0 ? 0 : d / max; v = max;
    }
    public Color Value { get { return FromHsv(H, Sat, V); } set { ToHsv(value, out H, out Sat, out V); Invalidate(); } }
    int D { get { return Math.Min(Width, Height) - 4; } }
    void Build()
    {
        int d = D; if (d <= 0) return;
        if (wheel != null && wheel.Width == d) return;
        if (wheel != null) wheel.Dispose();
        wheel = new Bitmap(d, d, PixelFormat.Format32bppArgb);
        var data = wheel.LockBits(new Rectangle(0, 0, d, d), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var px = new int[d * d]; float rad = d / 2f;
        for (int y = 0; y < d; y++)
            for (int x = 0; x < d; x++)
            {
                float dx = x - rad + 0.5f, dy = y - rad + 0.5f, dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist > rad) { px[y * d + x] = 0; continue; }
                float h = (float)(Math.Atan2(-dy, dx) * 180 / Math.PI); if (h < 0) h += 360;
                var c = FromHsv(h, Math.Min(1, dist / rad), 1);
                int a = dist > rad - 1 ? (int)(255 * (rad - dist)) : 255;
                px[y * d + x] = (a << 24) | (c.R << 16) | (c.G << 8) | c.B;
            }
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        wheel.UnlockBits(data);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g); g.Clear(G.SurfaceOf(this));
        Build(); if (wheel == null) return;
        int d = D, ox = (Width - d) / 2, oy = (Height - d) / 2;
        g.DrawImage(wheel, ox, oy, d, d);
        if (V < 1) using (var b = new SolidBrush(Color.FromArgb((int)((1 - V) * 255), 0, 0, 0))) g.FillEllipse(b, ox, oy, d, d);
        float rad = d / 2f, ang = (float)(H * Math.PI / 180);
        float mx = ox + rad + (float)Math.Cos(ang) * Sat * rad, my = oy + rad - (float)Math.Sin(ang) * Sat * rad;
        float k = G.P(8);
        using (var p = new Pen(Color.White, 2.5f * G.S)) g.DrawEllipse(p, mx - k, my - k, k * 2, k * 2);
        using (var p = new Pen(Color.FromArgb(120, 0, 0, 0), 1)) g.DrawEllipse(p, mx - k - 1.5f, my - k - 1.5f, k * 2 + 3, k * 2 + 3);
    }
    void Pick(Point pt)
    {
        int d = D; float rad = d / 2f, cx = Width / 2f, cy = Height / 2f;
        float dx = pt.X - cx, dy = pt.Y - cy;
        H = (float)(Math.Atan2(-dy, dx) * 180 / Math.PI); if (H < 0) H += 360;
        Sat = Math.Min(1f, (float)Math.Sqrt(dx * dx + dy * dy) / rad);
        Invalidate(); if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
    }
    protected override void OnMouseDown(MouseEventArgs e) { drag = true; Pick(e.Location); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) Pick(e.Location); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; }
}

// 亮度滑桿
public class ValueSlider : Control
{
    public ColorWheel Wheel;
    public event EventHandler ValueChanged;
    bool drag;
    public ValueSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; G.HQ(g); g.Clear(G.SurfaceOf(this));
        int pad = G.P(10);
        var r = new RectangleF(pad, Height / 2f - G.P(7), Width - pad * 2, G.P(14));
        using (var p = G.Round(r, r.Height / 2))
        using (var b = new LinearGradientBrush(new PointF(r.X - 1, 0), new PointF(r.Right + 1, 0), Color.Black, ColorWheel.FromHsv(Wheel.H, Wheel.Sat, 1)))
            g.FillPath(b, p);
        G.Stroke(g, r, r.Height / 2, Theme.Border, 1);
        float x = r.X + Wheel.V * r.Width, k = G.P(10);
        using (var b = new SolidBrush(Wheel.Value)) g.FillEllipse(b, x - k, Height / 2f - k, k * 2, k * 2);
        using (var p = new Pen(Color.White, 2.5f * G.S)) g.DrawEllipse(p, x - k, Height / 2f - k, k * 2, k * 2);
    }
    void Pick(int x)
    {
        int pad = G.P(10);
        Wheel.V = Math.Max(0, Math.Min(1, (x - pad) / (float)(Width - pad * 2)));
        Invalidate(); Wheel.Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
    }
    protected override void OnMouseDown(MouseEventArgs e) { drag = true; Pick(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) Pick(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; }
}

// ---------- 套用主題、表格樣式、對話框 ----------
public static class UI
{
    public static Font BaseFont = new Font("Microsoft JhengHei UI", 10f);

    public static void Apply(Control c) { Apply(c, Theme.Bg); }

    static void Apply(Control c, Color surface)
    {
        Color inner = surface;
        if (c is Form) { c.BackColor = Theme.Bg; c.ForeColor = Theme.Text; inner = Theme.Bg; Theme.StyleTitleBar((Form)c); }
        else if (c is Card) { c.BackColor = Theme.Card; inner = Theme.Card; }
        else if (c is InputBox)
        {
            var ib = (InputBox)c; ib.BackColor = surface; ib.Box.BackColor = Theme.Input; ib.Box.ForeColor = Theme.Text;
            if (ib.Box.Multiline) Theme.StyleScrollBars(ib.Box);
            ib.Invalidate(); return;
        }
        else if (c is TLabel) { var l = (TLabel)c; l.BackColor = surface; l.ForeColor = l.RoleColor(); }
        else if (c is Label) { c.BackColor = surface; c.ForeColor = Theme.Text; }
        else if (c is DataGridView) { StyleGrid((DataGridView)c); return; }
        else if (c is NiceList) { c.BackColor = Theme.Card; c.ForeColor = Theme.Text; Theme.StyleScrollBars(c); c.Invalidate(); return; }
        else if (c is ComboBox) { var cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Theme.Input; cb.ForeColor = Theme.Text; return; }
        else if (c is TextBox) { c.BackColor = Theme.Input; c.ForeColor = Theme.Text; return; }
        else if (c is FlatBtn || c is Chip || c is PillTabs || c is TCheck || c is TRadio || c is Tile || c is ColorWheel || c is ValueSlider)
        { c.BackColor = surface; c.ForeColor = Theme.Text; c.Invalidate(); }
        else if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel || c is UserControl)
        {
            string tag = c.Tag as string;
            if (tag == "header") inner = Theme.Header;
            else if (tag == "soft") inner = Theme.AccentSoft;
            else if (tag == "alt") inner = Theme.CardAlt;
            c.BackColor = inner; c.ForeColor = Theme.Text;
            if (c is ScrollableControl && ((ScrollableControl)c).AutoScroll) Theme.StyleScrollBars(c);
        }
        else { c.BackColor = surface; c.ForeColor = Theme.Text; }
        foreach (Control ch in c.Controls) Apply(ch, inner);
        c.Invalidate();
    }

    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Theme.Card; g.BorderStyle = BorderStyle.None; g.GridColor = Theme.GridLine;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = G.P(38);
        g.RowTemplate.Height = G.P(36);
        foreach (DataGridViewRow r in g.Rows) r.Height = G.P(36);
        var h = g.ColumnHeadersDefaultCellStyle;
        h.BackColor = Theme.Card; h.ForeColor = Theme.TextDim; h.SelectionBackColor = Theme.Card; h.SelectionForeColor = Theme.TextDim;
        h.Padding = new Padding(G.P(6), 0, G.P(6), 0); h.Font = new Font(BaseFont.FontFamily, BaseFont.Size - 0.5f, FontStyle.Bold);
        var d = g.DefaultCellStyle;
        d.BackColor = Theme.Card; d.ForeColor = Theme.Text; d.SelectionBackColor = Theme.Selection; d.SelectionForeColor = Theme.Text;
        d.Font = BaseFont; g.Font = BaseFont;
        d.Padding = new Padding(G.P(6), 0, G.P(6), 0);
        var rh = g.RowHeadersDefaultCellStyle;
        rh.BackColor = Theme.Card; rh.SelectionBackColor = Theme.Selection; rh.ForeColor = Theme.TextDim;
        g.RowHeadersWidth = G.P(24);
        g.ScrollBars = ScrollBars.Vertical;
        Theme.StyleScrollBars(g);
        if (!(g.Tag is string && (string)g.Tag == "styled"))
        {
            g.Tag = "styled";
            g.CellPainting += (s, e) => {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (!(g.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)) return;
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                int sz = G.P(18);
                var box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - sz) / 2, e.CellBounds.Y + (e.CellBounds.Height - sz) / 2, sz, sz);
                bool on = e.Value is bool && (bool)e.Value;
                TCheck.DrawBox(e.Graphics, box, on, true);
                e.Handled = true;
            };
        }
        g.Invalidate();
    }

    public static Form NewDialog(Form owner, string title)
    {
        var f = new Form {
            Text = title, Font = owner != null ? owner.Font : BaseFont, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(G.P(18)),
        };
        if (owner != null && owner.Icon != null) f.Icon = owner.Icon;
        f.HandleCreated += (s, e) => Theme.StyleTitleBar(f);
        return f;
    }

    // 有幾個按鈕可選的對話框，第一個按鈕是主要動作；傳回按下的是第幾個（關掉 = -1）
    public static int Choose(Form owner, string title, string message, params string[] buttons)
    {
        using (var f = NewDialog(owner, title))
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            var msg = new TLabel(message) { MaximumSize = new Size(G.P(720), 0), Margin = new Padding(G.P(3), G.P(3), G.P(3), G.P(16)) };
            var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            int chosen = -1;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int k = i;
                var b = new FlatBtn(buttons[i], i == 0 ? BtnKind.Primary : BtnKind.Soft);
                b.Click += (s, e) => { chosen = k; f.Close(); };
                flow.Controls.Add(b);
            }
            t.Controls.Add(msg, 0, 0); t.Controls.Add(flow, 0, 1);
            f.Controls.Add(t);
            Apply(f);
            f.ShowDialog(owner);
            return chosen;
        }
    }

    public static void Info(Form owner, string title, string message) { Choose(owner, title, message, "知道了"); }
    public static bool Confirm(Form owner, string title, string message, string yes = "確定", string no = "取消") { return Choose(owner, title, message, yes, no) == 0; }

    // 填幾個欄位的對話框；傳回 null 表示取消
    public static string[] Prompt(Form owner, string title, string intro, string[] labels, string[] values, bool[] multiline = null, Func<string[], string> validate = null)
    {
        using (var f = NewDialog(owner, title))
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, G.P(460)));
            int r = 0;
            if (!string.IsNullOrEmpty(intro))
            {
                var il = new TLabel(intro, LabelRole.Dim) { MaximumSize = new Size(G.P(560), 0), Margin = new Padding(G.P(3), G.P(3), G.P(3), G.P(12)) };
                t.Controls.Add(il, 0, r); t.SetColumnSpan(il, 2); r++;
            }
            var boxes = new InputBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                bool ml = multiline != null && multiline[i];
                boxes[i] = new InputBox(values[i] ?? "", ml);
                if (ml) boxes[i].Height = G.P(90);
                t.Controls.Add(new TLabel(labels[i]) { Margin = new Padding(G.P(3), G.P(10), G.P(10), G.P(3)) }, 0, r);
                t.Controls.Add(boxes[i], 1, r); r++;
            }
            var err = new TLabel("", LabelRole.Danger);
            t.Controls.Add(err, 1, r++);
            var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new FlatBtn("儲存", BtnKind.Primary); var cancel = new FlatBtn("取消");
            flow.Controls.Add(cancel); flow.Controls.Add(ok);
            t.Controls.Add(flow, 0, r); t.SetColumnSpan(flow, 2);
            f.Controls.Add(t);
            string[] result = null;
            ok.Click += (s, e) => {
                var vals = new string[boxes.Length];
                for (int i = 0; i < boxes.Length; i++) vals[i] = boxes[i].Text.Trim();
                string problem = validate == null ? null : validate(vals);
                if (problem != null) { err.Text = problem; return; }
                result = vals; f.Close();
            };
            cancel.Click += (s, e) => f.Close();
            f.CancelButton = cancel;
            Apply(f);
            f.Shown += (s, e) => { if (boxes.Length > 0) boxes[0].Box.Focus(); };
            f.ShowDialog(owner);
            return result;
        }
    }

    // 色環選色
    public static Color? PickColor(Form owner, Color start)
    {
        using (var f = NewDialog(owner, "自訂主題色"))
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            var wheel = new ColorWheel { Size = new Size(G.P(260), G.P(260)), Value = start, Margin = new Padding(G.P(3), G.P(3), G.P(20), G.P(3)) };
            var slider = new ValueSlider { Wheel = wheel, Size = new Size(G.P(260), G.P(32)) };
            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var preview = new Panel { Size = new Size(G.P(220), G.P(70)), Margin = new Padding(G.P(3), G.P(6), G.P(3), G.P(10)) };
            preview.Paint += (s, e) => {
                var g = e.Graphics; G.HQ(g); g.Clear(Theme.Bg);
                var half = preview.Width / 2f;
                G.Fill(g, new RectangleF(0, 0, half + G.P(8), preview.Height), G.P(10), start);
                G.Fill(g, new RectangleF(half, 0, half, preview.Height), G.P(10), wheel.Value);
                using (var b = new SolidBrush(wheel.Value)) g.FillRectangle(b, half, 0, G.P(10), preview.Height);
                G.Text(g, "原本", UI.BaseFont, new Rectangle(0, 0, (int)half, preview.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                G.Text(g, "新的", UI.BaseFont, new Rectangle((int)half, 0, (int)half, preview.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            var hex = new InputBox(Theme.Hex(start)) { Width = G.P(140), Dock = DockStyle.None };
            var swatches = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(G.P(240), 0) };
            foreach (var p in Theme.Palettes)
            {
                var pc = p.Accent;
                var sw = new Panel { Size = new Size(G.P(26), G.P(26)), Margin = new Padding(G.P(3)), Cursor = Cursors.Hand };
                sw.Paint += (s, e) => { e.Graphics.Clear(sw.Parent != null ? sw.Parent.BackColor : Theme.Bg); G.HQ(e.Graphics); using (var b = new SolidBrush(pc)) e.Graphics.FillEllipse(b, 1, 1, sw.Width - 3, sw.Height - 3); };
                sw.Click += (s, e) => { wheel.Value = pc; slider.Invalidate(); preview.Invalidate(); hex.Text = Theme.Hex(pc); };
                new ToolTip().SetToolTip(sw, p.Name);
                swatches.Controls.Add(sw);
            }
            right.Controls.Add(new TLabel("預覽", LabelRole.Dim)); right.Controls.Add(preview);
            right.Controls.Add(new TLabel("色碼（例如 #F07C4A）", LabelRole.Dim)); right.Controls.Add(hex);
            right.Controls.Add(new TLabel("快速選擇", LabelRole.Dim) { Margin = new Padding(G.P(3), G.P(12), G.P(3), G.P(3)) }); right.Controls.Add(swatches);
            var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            left.Controls.Add(wheel); left.Controls.Add(new TLabel("亮度", LabelRole.Dim)); left.Controls.Add(slider);
            t.Controls.Add(left, 0, 0); t.Controls.Add(right, 1, 0);
            var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new FlatBtn("使用這個顏色", BtnKind.Primary); var cancel = new FlatBtn("取消");
            flow.Controls.Add(cancel); flow.Controls.Add(ok);
            t.Controls.Add(flow, 0, 1); t.SetColumnSpan(flow, 2);
            f.Controls.Add(t);
            bool updating = false;
            EventHandler changed = (s, e) => { slider.Invalidate(); preview.Invalidate(); updating = true; hex.Text = Theme.Hex(wheel.Value); updating = false; };
            wheel.ColorChanged += changed; slider.ValueChanged += changed;
            hex.Box.TextChanged += (s, e) => { if (updating) return; var c = Theme.ParseHex(hex.Text); if (c != null) { wheel.Value = c.Value; slider.Invalidate(); preview.Invalidate(); } };
            Color? result = null;
            ok.Click += (s, e) => { result = wheel.Value; f.Close(); };
            cancel.Click += (s, e) => f.Close();
            f.CancelButton = cancel;
            Apply(f);
            f.ShowDialog(owner);
            return result;
        }
    }
}
