// 批量改名工具 — 主題（亮色／暗色／跟隨系統、配色）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class Palette
{
    public string Name; public Color Accent;
    public Palette(string n, Color c) { Name = n; Accent = c; }
}

public static class Theme
{
    public static readonly Palette[] Palettes = {
        new Palette("暖陽橘", Color.FromArgb(0xF0, 0x7C, 0x4A)),
        new Palette("海洋藍", Color.FromArgb(0x3B, 0x82, 0xF6)),
        new Palette("森林綠", Color.FromArgb(0x22, 0xA0, 0x6B)),
        new Palette("青瓷", Color.FromArgb(0x14, 0xA8, 0x9A)),
        new Palette("薰衣草紫", Color.FromArgb(0x8B, 0x5C, 0xF6)),
        new Palette("櫻花粉", Color.FromArgb(0xE8, 0x5D, 0x8E)),
        new Palette("琥珀黃", Color.FromArgb(0xD9, 0x9A, 0x06)),
        new Palette("石墨灰", Color.FromArgb(0x7A, 0x84, 0x94)),
    };

    public static bool Dark;
    public static Color Bg, Header, Card, CardAlt, Border, Text, TextDim, Input, InputBorder,
                        Accent, AccentHover, AccentSoft, OnAccent,
                        Success, SuccessSoft, Danger, DangerSoft, Warning, WarningSoft, Info, InfoSoft,
                        Selection, GridLine, Inverse, OnInverse;

    public static event Action Changed;

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
    static double Lum(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

    public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    public static Color? ParseHex(string s)
    {
        s = (s ?? "").Trim().TrimStart('#');
        if (s.Length != 6) return null;
        try { return Color.FromArgb(Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16)); }
        catch { return null; }
    }

    // Windows 的「應用程式模式」是不是深色
    public static bool SystemIsDark()
    {
        try
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                if (v is int) return (int)v == 0;
            }
        }
        catch { }
        return false;
    }

    // 每次換配色就加一，用來判斷某個畫面是不是已經套用過目前的配色（切分頁時不用每次重新上色）
    public static int Gen = 1;
    public static void Set(bool dark, Color accent)
    {
        Gen++;
        Dark = dark;
        if (dark)
        {
            Bg = Color.FromArgb(0x1E, 0x1F, 0x23); Header = Color.FromArgb(0x18, 0x19, 0x1C);
            Card = Color.FromArgb(0x2A, 0x2B, 0x30); CardAlt = Color.FromArgb(0x31, 0x32, 0x38);
            Border = Color.FromArgb(0x3C, 0x3E, 0x45); Text = Color.White; TextDim = Color.White;   // 暗色模式文字一律白色
            Input = Color.FromArgb(0x22, 0x23, 0x27); InputBorder = Color.FromArgb(0x45, 0x47, 0x4F);
            Success = Color.FromArgb(0x4A, 0xC2, 0x86); Danger = Color.FromArgb(0xEF, 0x6B, 0x6B);
            Warning = Color.FromArgb(0xE8, 0xB0, 0x4A); Info = Color.FromArgb(0x6C, 0xA8, 0xF0);
            GridLine = Color.FromArgb(0x35, 0x37, 0x3D);
            Inverse = Color.FromArgb(0xEC, 0xEC, 0xEE); OnInverse = Color.FromArgb(0x1E, 0x1F, 0x23);
        }
        else
        {
            Bg = Color.FromArgb(0xF3, 0xF4, 0xF6); Header = Color.FromArgb(0xFF, 0xFF, 0xFF);
            Card = Color.White; CardAlt = Color.FromArgb(0xF8, 0xF9, 0xFA);
            Border = Color.FromArgb(0xE2, 0xE4, 0xE8); Text = Color.FromArgb(0x1F, 0x23, 0x28); TextDim = Color.FromArgb(0x6B, 0x72, 0x80);
            Input = Color.White; InputBorder = Color.FromArgb(0xD5, 0xD8, 0xDE);
            Success = Color.FromArgb(0x16, 0x8A, 0x56); Danger = Color.FromArgb(0xD1, 0x43, 0x43);
            Warning = Color.FromArgb(0xB7, 0x79, 0x0F); Info = Color.FromArgb(0x25, 0x63, 0xEB);
            GridLine = Color.FromArgb(0xEE, 0xEF, 0xF2);
            Inverse = Color.FromArgb(0x1F, 0x23, 0x28); OnInverse = Color.White;
        }
        Accent = accent;
        AccentHover = dark ? Mix(accent, Color.White, 0.12) : Mix(accent, Color.Black, 0.10);
        AccentSoft = Mix(Card, accent, dark ? 0.22 : 0.12);
        OnAccent = Lum(accent) > 0.62 ? Color.FromArgb(0x1F, 0x23, 0x28) : Color.White;
        SuccessSoft = Mix(Card, Success, dark ? 0.20 : 0.12);
        DangerSoft = Mix(Card, Danger, dark ? 0.20 : 0.10);
        WarningSoft = Mix(Card, Warning, dark ? 0.20 : 0.12);
        InfoSoft = Mix(Card, Info, dark ? 0.20 : 0.10);
        Selection = Mix(Card, accent, dark ? 0.30 : 0.16);
        if (Changed != null) Changed();
    }

    public static void Load(Settings c)
    {
        bool dark = c.ThemeMode == "dark" || (c.ThemeMode != "light" && SystemIsDark());
        Set(dark, AccentOf(c));
    }

    public static Color AccentOf(Settings c)
    {
        if (c.Accent == "custom") { var h = ParseHex(c.CustomAccent); if (h != null) return h.Value; }
        foreach (var p in Palettes) if (p.Name == c.Accent) return p.Accent;
        return Palettes[0].Accent;
    }

    // ---------- Windows 原生外觀：深色標題列、深色捲軸 ----------
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

    static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

    public static void StyleTitleBar(Form f)
    {
        if (!IsWindows || !f.IsHandleCreated) return;
        try
        {
            int on = Dark ? 1 : 0;
            if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            int cap = Header.R | (Header.G << 8) | (Header.B << 16);   // Windows 11：標題列顏色
            DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);
            int txt = Text.R | (Text.G << 8) | (Text.B << 16);
            DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
        }
        catch { }
    }

    public static void StyleScrollBars(Control c)
    {
        if (!IsWindows) return;
        try
        {
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
            foreach (Control ch in c.Controls) if (ch is ScrollBar && ch.IsHandleCreated) SetWindowTheme(ch.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch { }
    }
}
