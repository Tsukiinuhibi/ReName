// 批量改名工具 — 視窗介面
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

public class MainForm : Form
{
    static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatchRename");
    static readonly string SettingsPath = Path.Combine(AppDir, "settings.xml");
    static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "批量改名紀錄");

    readonly List<FileRow> rows = new List<FileRow>();
    readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    TextBox tbName, tbRange, tbCross, tbMonth, tbSingle, tbYear, tbTest;
    CheckBox cbMonth;
    DataGridView grid, mapGrid, compGrid, remGrid, rulesGrid, learnGrid;
    TextBox tbFrom, tbTo;
    Label lblLearn;
    TabControl tabs;
    TabPage pageLearn;
    Label lblCount, lblTestParts, lblTestResult;
    Button btnRun, btnUpdate;
    Panel updateBar;
    Label lblUpdate;
    UpdateInfo pendingUpdate;
    Timer timer;
    bool filling, loading;

    static readonly Color ColOk = Color.White, ColManual = Color.FromArgb(225, 238, 255),
                          ColSame = Color.FromArgb(238, 238, 238), ColErr = Color.FromArgb(255, 225, 225);

    public MainForm(string[] args)
    {
        Text = "批量改名工具 v" + Updater.CurrentText;
        Updater.CleanupOld(Application.ExecutablePath);
        Font = new Font("Microsoft JhengHei UI", 10f);
        float scale = 1f;
        using (var g = CreateGraphics()) scale = g.DpiX / 96f;
        Size = new Size((int)(1200 * scale), (int)(820 * scale));
        MinimumSize = new Size((int)(900 * scale), (int)(600 * scale));
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        timer = new Timer { Interval = 350 };
        timer.Tick += (s, e) => { timer.Stop(); Safe(RefreshAll); };

        loading = true;
        BuildUI(LoadSettings());
        loading = false;

        if (args != null && args.Length > 0) AddFiles(args);
        RefreshAll();
        Shown += (s, e) => { grid.CurrentCell = null; grid.ClearSelection(); CheckUpdate(false); };
        FormClosing += (s, e) => { try { SaveSettings(GetUISettings()); } catch { } };
    }

    // ================= 小工具 =================
    static Button Btn(string text) { return new Button { Text = text, AutoSize = true, Padding = new Padding(8, 3, 8, 3) }; }
    static Label Lbl(string text, bool gray = false)
    {
        var l = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
        if (gray) l.ForeColor = Color.DimGray;
        return l;
    }
    static TextBox Tb(string text) { return new TextBox { Text = text, Dock = DockStyle.Fill }; }
    static bool B(object o) { return o is bool && (bool)o; }
    static string S(object o) { return o == null ? "" : o.ToString().Trim(); }
    static TableLayoutPanel Rows(int n, int fillRow)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = n, Padding = new Padding(6) };
        for (int i = 0; i < n; i++) t.RowStyles.Add(i == fillRow ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        return t;
    }
    static DataGridView NewGrid(bool userAdd)
    {
        var g = new DataGridView {
            Dock = DockStyle.Fill, AllowUserToAddRows = userAdd, AllowUserToDeleteRows = userAdd, AllowUserToResizeRows = false,
            RowHeadersVisible = userAdd, RowHeadersWidth = 30, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
        };
        g.DataError += (s, e) => { e.ThrowException = false; };
        return g;
    }
    static string Crlf(string s) { return (s ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"); }

    // ================= 介面 =================
    void BuildUI(Settings c)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(BuildUpdateBar(), 0, 0);

        tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildMainTab());
        tabs.TabPages.Add(pageLearn = BuildLearnTab(c));
        tabs.TabPages.Add(BuildMapTab(c));
        tabs.TabPages.Add(BuildFormatTab(c));
        tabs.SelectedIndexChanged += (s, e) => Safe(RefreshAll);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(BuildTestPanel(), 0, 2);
    }

    // ---------- 分頁 1：改名 ----------
    TabPage BuildMainTab()
    {
        var page = new TabPage("改名");
        var t = Rows(3, 1);
        page.Controls.Add(t);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var bAdd = Btn("加入檔案…"); var bDir = Btn("加入整個資料夾…"); var bRemove = Btn("移除選取的列");
        var bClear = Btn("清空清單"); var bAll = Btn("全部勾選"); var bNone = Btn("全部取消");
        var bReset = Btn("選取的列恢復自動命名");
        var bLearnManual = Btn("把手動改的檔名學起來");
        bLearnManual.ForeColor = Color.FromArgb(20, 90, 160);
        bar.Controls.AddRange(new Control[] { bAdd, bDir, bRemove, bClear, bAll, bNone, bReset, bLearnManual, Lbl("（也可以把檔案或資料夾直接拖曳到下方清單）", true) });
        t.Controls.Add(bar, 0, 0);

        grid = NewGrid(false);
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AllowDrop = true;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "改", FillWeight = 6 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Old", HeaderText = "原檔名", ReadOnly = true, FillWeight = 38 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "New", HeaderText = "新檔名（點兩下可直接修改）", FillWeight = 38 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "狀態", ReadOnly = true, FillWeight = 24 });
        t.Controls.Add(grid, 0, 1);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        btnRun = Btn("執行改名"); btnRun.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        var bUndo = Btn("還原之前的改名…"); var bLog = Btn("開啟改名紀錄資料夾");
        var bCheck = Btn("檢查更新");
        lblCount = Lbl("");
        bottom.Controls.AddRange(new Control[] { btnRun, bUndo, bLog, bCheck, lblCount });
        bCheck.Click += (s, e) => CheckUpdate(true);
        t.Controls.Add(bottom, 0, 2);

        bAdd.Click += (s, e) => {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "選擇要改名的檔案（可按 Ctrl 或 Shift 多選）", Filter = "所有檔案 (*.*)|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) Safe(() => { AddFiles(d.FileNames); RefreshAll(); });
        };
        bDir.Click += (s, e) => {
            using (var d = new FolderBrowserDialog { Description = "選擇資料夾（會加入資料夾內的所有檔案，不含子資料夾）" })
                if (d.ShowDialog(this) == DialogResult.OK) Safe(() => { AddFiles(new[] { d.SelectedPath }); RefreshAll(); });
        };
        bRemove.Click += (s, e) => {
            foreach (DataGridViewRow gr in grid.SelectedRows) { var r = (FileRow)gr.Tag; rows.Remove(r); paths.Remove(r.Path); }
            RefreshAll();
        };
        bClear.Click += (s, e) => { rows.Clear(); paths.Clear(); FillGrid(); };
        bAll.Click  += (s, e) => { foreach (var r in rows) r.Excluded = false; FillGrid(); };
        bNone.Click += (s, e) => { foreach (var r in rows) r.Excluded = true; FillGrid(); };
        bReset.Click += (s, e) => { foreach (DataGridViewRow gr in grid.SelectedRows) ((FileRow)gr.Tag).Manual = false; RefreshAll(); };
        bLearnManual.Click += (s, e) => Safe(LearnFromManual);

        DragEventHandler enter = (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragEventHandler drop = (s, e) => Safe(() => { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); RefreshAll(); });
        grid.DragEnter += enter; grid.DragDrop += drop; DragEnter += enter; DragDrop += drop;

        grid.CurrentCellDirtyStateChanged += (s, e) => {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (s, e) => {
            if (filling || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Apply") return;
            var gr = grid.Rows[e.RowIndex]; var r = (FileRow)gr.Tag;
            bool v = B(gr.Cells["Apply"].Value);
            r.Excluded = !v;
            if (v && !r.Ok) RequestRefresh(); else UpdateCount();
        };
        grid.CellEndEdit += (s, e) => {
            if (grid.Columns[e.ColumnIndex].Name != "New") return;
            var gr = grid.Rows[e.RowIndex]; var r = (FileRow)gr.Tag;
            string txt = S(gr.Cells["New"].Value);
            if (r.Ext.Length > 0 && txt.EndsWith(r.Ext, StringComparison.OrdinalIgnoreCase)) txt = txt.Substring(0, txt.Length - r.Ext.Length);
            if (txt != r.NewStem) { r.NewStem = txt; r.Manual = true; }
            RequestRefresh();
        };
        // 點一下某一列，下方測試列就顯示這個檔案的辨識結果
        grid.SelectionChanged += (s, e) => {
            if (filling || grid.CurrentRow == null || grid.CurrentRow.Tag == null) return;
            tbTest.Text = ((FileRow)grid.CurrentRow.Tag).Stem;
        };

        btnRun.Click += (s, e) => Safe(RunRename);
        bUndo.Click += (s, e) => Safe(RunUndo);
        bLog.Click += (s, e) => Safe(() => { Directory.CreateDirectory(LogDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + LogDir + "\""); });
        return page;
    }

    // ---------- 分頁 2：名稱對照與公司 ----------
    TabPage BuildMapTab(Settings c)
    {
        var page = new TabPage("名稱對照與名單");
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(t);

        var h1 = Lbl("【文件名稱對照】\r\n辨識出的文件名稱和左欄「完全一樣」時，換成右欄的寫法。\r\n" +
                     "（頻率 (日)(月)(年) 會先拿掉再比對，例如「高空工作車(日)檢查表」比對的是「高空工作車檢查表」）");
        var h2 = Lbl("【公司名單】\r\n列在這裡的名稱一定會被當成公司或廠商。沒列到的程式也會自己猜，猜錯時再加進來。");
        var h3 = Lbl("【備註用語】\r\n檔名中含有這些字的部分會當成備註，放進 {備註}，例如「END已退場」。");
        h1.MaximumSize = new Size(560, 0); h2.MaximumSize = new Size(280, 0); h3.MaximumSize = new Size(280, 0);
        t.Controls.Add(h1, 0, 0); t.Controls.Add(h2, 1, 0); t.Controls.Add(h3, 2, 0);

        mapGrid = NewGrid(true);
        mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "From", HeaderText = "原本的名稱（要完全一樣）" });
        mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "To", HeaderText = "改成" });
        foreach (var kv in Core.ParseWorkMap(c.WorkMap)) mapGrid.Rows.Add(kv.Key, kv.Value);
        t.Controls.Add(mapGrid, 0, 1);

        compGrid = NewGrid(true);
        compGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "公司／廠商名稱" });
        foreach (var n in (c.Companies ?? "").Split('\n')) if (n.Trim().Length > 0) compGrid.Rows.Add(n.Trim());
        t.Controls.Add(compGrid, 1, 1);

        remGrid = NewGrid(true);
        remGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "備註用語（含有這些字就算）" });
        foreach (var n in (c.Remarks ?? "").Split('\n')) if (n.Trim().Length > 0) remGrid.Rows.Add(n.Trim());
        t.Controls.Add(remGrid, 2, 1);

        var tip = Lbl("在最下面的空白列直接輸入就能新增；點選左邊的灰色小格選取整列後，按鍵盤 Delete 鍵可以刪除。", true);
        t.Controls.Add(tip, 0, 2); t.SetColumnSpan(tip, 3);

        foreach (var g in new[] { mapGrid, compGrid, remGrid })
        {
            g.CellValueChanged += (s, e) => { if (!loading) RequestRefresh(); };
            g.RowsRemoved += (s, e) => { if (!loading) RequestRefresh(); };
        }
        return page;
    }

    // ---------- 分頁 3：格式設定 ----------
    TabPage BuildFormatTab(Settings c)
    {
        var page = new TabPage("檔名格式");
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(6), AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(t);

        tbName = Tb(c.NameTemplate); tbRange = Tb(c.RangeFormat); tbCross = Tb(c.CrossYearFormat);
        tbMonth = Tb(c.MonthFormat); tbSingle = Tb(c.SingleFormat); tbYear = Tb(c.YearFormat);
        cbMonth = new CheckBox { Text = "日期區間剛好是整個月（1 號到月底）時，改用「只有年月」的格式", AutoSize = true, Checked = c.UseMonthFormat };

        int r = 0;
        Action<string, Control> add = (label, ctl) => { t.Controls.Add(Lbl(label), 0, r); t.Controls.Add(ctl, 1, r); r++; };
        add("新檔名格式", tbName);
        t.Controls.Add(Lbl("可用代碼：{公司}　{文件}　{頻率}=(日)(月)(年)　{日期}　{廠商1}　{廠商2}　{廠商}=全部廠商　{編號}=括號裡的編號　{備註}=例如(END已退場)　{其他}\r\n" +
                           "欄位是空的時候（例如沒有廠商、沒有編號），前面的「-」會自動拿掉。", true), 1, r++);
        t.Controls.Add(Lbl(" "), 1, r++);
        add("只有年", tbYear);
        add("年月", tbMonth);
        add("單一日期", tbSingle);
        add("日期區間", tbRange);
        add("跨年區間", tbCross);
        t.Controls.Add(cbMonth, 1, r++);
        t.Controls.Add(Lbl("日期代碼：{年}=民國年　{西元年}　{月}=5　{月0}=05　{日}=1　{日0}=01\r\n" +
                           "結束日前面加「迄」：{迄年}　{迄月}　{迄月0}　{迄日}　{迄日0}", true), 1, r++);
        var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var bSave = Btn("儲存設定"); var bDefault = Btn("恢復預設格式");
        btns.Controls.AddRange(new Control[] { bSave, bDefault });
        t.Controls.Add(btns, 1, r++);

        foreach (var tb in new[] { tbName, tbRange, tbCross, tbMonth, tbSingle, tbYear }) tb.TextChanged += (s, e) => { if (!loading) RequestRefresh(); };
        cbMonth.CheckedChanged += (s, e) => { if (!loading) RequestRefresh(); };
        bSave.Click += (s, e) => Safe(() => { SaveSettings(GetUISettings()); MessageBox.Show(this, "設定已儲存，下次開啟會自動套用。", Text); });
        bDefault.Click += (s, e) => {
            var d = new Settings();
            loading = true;
            tbName.Text = d.NameTemplate; tbRange.Text = d.RangeFormat; tbCross.Text = d.CrossYearFormat;
            tbMonth.Text = d.MonthFormat; tbSingle.Text = d.SingleFormat; tbYear.Text = d.YearFormat; cbMonth.Checked = d.UseMonthFormat;
            loading = false;
            RefreshAll();
        };
        return page;
    }

    // ---------- 分頁 2：範例學習 ----------
    TabPage BuildLearnTab(Settings c)
    {
        var page = new TabPage("範例學習");
        bool legacy = c.Rules != null && c.Rules.Count > 0;
        var t = Rows(legacy ? 7 : 5, 4);
        page.Controls.Add(t);

        var intro = Lbl("遇到程式改得不對的檔名，不用自己寫規則：在下面輸入「原檔名」和「想要的結果」，按「學起來」。\r\n" +
                        "程式會自己找出規律，以後同一種文件（文件名稱相同）的檔案都會照這個樣子改。\r\n" +
                        "小技巧：在「改名」頁直接點兩下修改新檔名，再按「把手動改的檔名學起來」，效果一樣。");
        t.Controls.Add(intro, 0, 0);

        var inp = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        inp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tbFrom = Tb(""); tbTo = Tb("");
        var bFill = Btn("帶入下方「試試看」的檔名");
        var bLearn = Btn("學起來"); bLearn.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        inp.Controls.Add(Lbl("原檔名"), 0, 0); inp.Controls.Add(tbFrom, 1, 0); inp.Controls.Add(bFill, 2, 0);
        inp.Controls.Add(Lbl("想要的結果"), 0, 1); inp.Controls.Add(tbTo, 1, 1); inp.Controls.Add(bLearn, 2, 1);
        t.Controls.Add(inp, 0, 1);

        lblLearn = Lbl("");
        t.Controls.Add(lblLearn, 0, 2);
        t.Controls.Add(Lbl("已學會的規則（取消打勾＝暫停使用；點選左邊灰色小格選取整列後按 Delete 鍵可刪除）", true), 0, 3);

        learnGrid = NewGrid(false);
        learnGrid.AllowUserToDeleteRows = true; learnGrid.RowHeadersVisible = true;
        learnGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "使用", FillWeight = 6 });
        learnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Doc", HeaderText = "適用的文件名稱", ReadOnly = true, FillWeight = 18 });
        learnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tpl", HeaderText = "新檔名的樣子", ReadOnly = true, FillWeight = 34 });
        learnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "From", HeaderText = "範例：原檔名", ReadOnly = true, FillWeight = 21 });
        learnGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "To", HeaderText = "範例：結果", ReadOnly = true, FillWeight = 21 });
        foreach (var l in c.LearnedRules ?? new List<Learned>()) AddLearnRow(l);
        t.Controls.Add(learnGrid, 0, 4);
        learnGrid.CurrentCellDirtyStateChanged += (s, e) => {
            if (learnGrid.IsCurrentCellDirty && learnGrid.CurrentCell is DataGridViewCheckBoxCell) learnGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        learnGrid.CellValueChanged += (s, e) => { if (!loading) RequestRefresh(); };
        learnGrid.RowsRemoved += (s, e) => { if (!loading) RequestRefresh(); };

        // 舊版的尋找取代規則：只有曾經設定過才顯示
        rulesGrid = NewGrid(true);
        rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "啟用", FillWeight = 7 });
        var stageCol = new DataGridViewComboBoxColumn { Name = "Stage", HeaderText = "何時套用", FillWeight = 20, FlatStyle = FlatStyle.Flat };
        stageCol.Items.AddRange(Rule.StageBefore, Rule.StageAfter);
        rulesGrid.Columns.Add(stageCol);
        rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Find", HeaderText = "尋找文字", FillWeight = 24 });
        rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Replace", HeaderText = "取代為", FillWeight = 24 });
        rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Regex", HeaderText = "正規表示式", FillWeight = 9 });
        rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "備註", FillWeight = 16 });
        rulesGrid.DefaultValuesNeeded += (s, e) => { e.Row.Cells["Enabled"].Value = true; e.Row.Cells["Stage"].Value = Rule.StageBefore; e.Row.Cells["Regex"].Value = false; };
        foreach (var rule in c.Rules ?? new List<Rule>())
            rulesGrid.Rows.Add(rule.Enabled, rule.Stage == Rule.StageAfter ? Rule.StageAfter : Rule.StageBefore, rule.Find ?? "", rule.Replace ?? "", rule.UseRegex, rule.Note ?? "");
        rulesGrid.CurrentCellDirtyStateChanged += (s, e) => {
            if (rulesGrid.IsCurrentCellDirty && !(rulesGrid.CurrentCell is DataGridViewTextBoxCell)) rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        rulesGrid.CellValueChanged += (s, e) => { if (!loading) RequestRefresh(); };
        rulesGrid.RowsRemoved += (s, e) => { if (!loading) RequestRefresh(); };
        if (legacy)
        {
            t.Controls.Add(Lbl("舊版「尋找→取代」規則（之前設定的，仍然有效；全部刪掉後下次開啟就不再顯示）", true), 0, 5);
            rulesGrid.Dock = DockStyle.Top; rulesGrid.Height = 130;
            t.Controls.Add(rulesGrid, 0, 6);
        }

        bFill.Click += (s, e) => { tbFrom.Text = tbTest.Text.Trim(); tbTo.Focus(); };
        bLearn.Click += (s, e) => Safe(() => LearnOne(tbFrom.Text, tbTo.Text, true));
        return page;
    }

    void AddLearnRow(Learned l)
    {
        int i = learnGrid.Rows.Add(l.Enabled, l.DocKey, Core.FriendlyTemplate(l.Template), l.ExampleFrom, l.ExampleTo);
        learnGrid.Rows[i].Tag = l;
    }

    // 學一個範例；showResult=true 時把結果顯示在範例學習頁
    bool LearnOne(string from, string to, bool showResult)
    {
        from = (from ?? "").Trim(); to = (to ?? "").Trim();
        // 使用者可能連副檔名一起貼上
        string ext = Path.GetExtension(from);
        if (ext.Length > 1 && ext.Length <= 6 && to.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        { from = from.Substring(0, from.Length - ext.Length); to = to.Substring(0, to.Length - ext.Length); }

        var res = Core.Learn(from, to, GetUISettings());
        if (!res.Ok)
        {
            if (showResult)
            {
                lblLearn.ForeColor = Color.Firebrick;
                lblLearn.Text = "✖ 學不起來：" + res.Error + "\r\n　 可以試試：在「名稱對照與名單」把公司或文件名稱加進去，或換一個比較完整的範例。";
            }
            return false;
        }
        loading = true;
        bool replaced = false;
        for (int i = learnGrid.Rows.Count - 1; i >= 0; i--)
            if (((Learned)learnGrid.Rows[i].Tag).DocKey == res.Rule.DocKey) { learnGrid.Rows.RemoveAt(i); replaced = true; }
        AddLearnRow(res.Rule);
        if (res.AddedCompany.Length > 0) compGrid.Rows.Add(res.AddedCompany);
        loading = false;
        if (showResult)
        {
            lblLearn.ForeColor = Color.FromArgb(20, 110, 50);
            lblLearn.Text = "✔ 學會了！以後文件名稱是「" + res.Rule.DocKey + "」的檔案，新檔名會長這樣：\r\n　 " +
                            Core.FriendlyTemplate(res.Rule.Template) +
                            (res.AddedCompany.Length > 0 ? "\r\n　 另外把「" + res.AddedCompany + "」加進了公司名單。" : "") +
                            (replaced ? "\r\n　 （這個文件之前學過的規則已經被取代）" : "");
        }
        RefreshAll();
        return true;
    }

    void LearnFromManual()
    {
        var targets = new List<FileRow>();
        foreach (DataGridViewRow gr in grid.SelectedRows) { var r = (FileRow)gr.Tag; if (r.Manual) targets.Add(r); }
        if (targets.Count == 0) targets = rows.FindAll(r => r.Manual);
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "清單裡沒有手動修改過的檔名。\r\n先在「新檔名」欄點兩下改成你要的樣子，再按這個按鈕。", Text);
            return;
        }
        int ok = 0; var fails = new List<string>();
        foreach (var r in targets)
        {
            if (LearnOne(r.Stem, r.NewStem, false)) { ok++; }
            else fails.Add(r.OldName);
        }
        // 學會之後，這些檔案改回自動命名；結果應該和手動改的一樣
        foreach (var r in targets) if (!fails.Contains(r.OldName)) r.Manual = false;
        RefreshAll();
        string msg = "學會了 " + ok + " 個範例，可以到「範例學習」頁查看。";
        if (fails.Count > 0) msg += "\r\n\r\n下面這些看不出規律，維持手動修改：\r\n" + string.Join("\r\n", fails.ToArray());
        MessageBox.Show(this, msg, Text);
    }

    // ---------- 上方更新提示列 ----------
    Control BuildUpdateBar()
    {
        updateBar = new Panel { Dock = DockStyle.Fill, AutoSize = true, Visible = false, BackColor = Color.FromArgb(255, 244, 200), Padding = new Padding(8, 4, 8, 4) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        lblUpdate = Lbl("");
        btnUpdate = Btn("立即更新"); btnUpdate.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        var bLater = Btn("稍後再說");
        flow.Controls.AddRange(new Control[] { lblUpdate, btnUpdate, bLater });
        updateBar.Controls.Add(flow);
        btnUpdate.Click += (s, e) => DoUpdate();
        bLater.Click += (s, e) => updateBar.Visible = false;
        return updateBar;
    }

    // manual=true：使用者按了「檢查更新」，結果都要告訴他；false：啟動時默默檢查，有新版才提示
    void CheckUpdate(bool manual)
    {
        var bg = new System.ComponentModel.BackgroundWorker();
        UpdateInfo info = null; Exception err = null;
        bg.DoWork += (s, e) => { try { info = Updater.Check(); } catch (Exception ex) { err = ex; } };
        bg.RunWorkerCompleted += (s, e) => {
            if (err != null)
            {
                if (manual) MessageBox.Show(this, "無法檢查更新（可能沒有連上網路）：\r\n" + err.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (info == null)
            {
                if (manual) MessageBox.Show(this, "目前已經是最新版本（v" + Updater.CurrentText + "）。", Text);
                return;
            }
            pendingUpdate = info;
            lblUpdate.Text = "有新版本 v" + info.Version.ToString(3) + "（目前 v" + Updater.CurrentText + "）" +
                             (info.Notes.Length > 0 ? "：" + info.Notes : "");
            updateBar.Visible = true;
            if (manual) DoUpdate();
        };
        bg.RunWorkerAsync();
    }

    void DoUpdate()
    {
        if (pendingUpdate == null) return;
        if (MessageBox.Show(this, "要更新到 v" + pendingUpdate.Version.ToString(3) + " 嗎？\r\n\r\n" +
                (pendingUpdate.Notes.Length > 0 ? "更新內容：" + pendingUpdate.Notes + "\r\n\r\n" : "") +
                "程式會自動下載、關閉並重新開啟。你的設定、學到的規則和改名紀錄都會保留。",
                "更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { SaveSettings(GetUISettings()); } catch { }
        btnUpdate.Enabled = false; btnUpdate.Text = "下載中…";
        Cursor = Cursors.WaitCursor;
        string exe = Application.ExecutablePath;
        var info = pendingUpdate;
        var bg = new System.ComponentModel.BackgroundWorker();
        string err = null;
        bg.DoWork += (s, e) => { err = Updater.DownloadAndReplace(info, exe); };
        bg.RunWorkerCompleted += (s, e) => {
            Cursor = Cursors.Default;
            if (err != null)
            {
                btnUpdate.Enabled = true; btnUpdate.Text = "立即更新";
                MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { Updater.Restart(exe); } catch { }
            Application.Exit();
        };
        bg.RunWorkerAsync();
    }

    // ---------- 下方常駐測試列 ----------
    Control BuildTestPanel()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 2, 8, 6),
                                       BackColor = Color.FromArgb(246, 248, 252), CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tbTest = Tb("115.09月高空工作車(日)檢查表(甲公司)");
        lblTestParts = Lbl(""); lblTestParts.ForeColor = Color.DimGray;
        lblTestResult = Lbl(""); lblTestResult.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        var title = Lbl("試試看：輸入任何檔名，或在「改名」頁點一下某個檔案，就能看到程式怎麼辨識", true);
        t.Controls.Add(title, 0, 0); t.SetColumnSpan(title, 2);
        t.Controls.Add(Lbl("原檔名"), 0, 1); t.Controls.Add(tbTest, 1, 1);
        t.Controls.Add(Lbl("辨識出"), 0, 2); t.Controls.Add(lblTestParts, 1, 2);
        t.Controls.Add(Lbl("新檔名"), 0, 3); t.Controls.Add(lblTestResult, 1, 3);
        tbTest.TextChanged += (s, e) => Safe(UpdateTest);
        return t;
    }

    void UpdateTest()
    {
        if (tbTest == null || loading) return;
        var c = GetUISettings();
        var p = Core.Process(tbTest.Text.Trim(), c, Core.ParseWorkMap(c.WorkMap));
        if (p.Ok)
        {
            var sb = new StringBuilder();
            foreach (var kv in p.Parts)
            {
                if (kv.Key == "廠商" || kv.Value.Length == 0) continue;
                sb.Append(kv.Key).Append("＝").Append(kv.Value).Append("　　");
            }
            if (p.UsedRule != null) sb.Append("（套用「範例學習」學到的規則）");
            lblTestParts.Text = sb.ToString();
            lblTestResult.Text = p.Final;
            lblTestResult.ForeColor = Color.FromArgb(20, 110, 50);
        }
        else
        {
            lblTestParts.Text = "";
            lblTestResult.Text = "無法辨識：" + p.Error;
            lblTestResult.ForeColor = Color.Firebrick;
        }
        if (p.RuleErrors.Count > 0) lblTestParts.Text += "\r\n⚠ " + string.Join("；", p.RuleErrors.ToArray());
    }

    // ================= 設定 =================
    Settings GetUISettings()
    {
        var mapText = new StringBuilder();
        foreach (DataGridViewRow gr in mapGrid.Rows)
        {
            if (gr.IsNewRow) continue;
            string f = S(gr.Cells["From"].Value), to = S(gr.Cells["To"].Value);
            if (f.Length > 0) mapText.Append(f).Append('=').Append(to).Append('\n');
        }
        var comp = new StringBuilder();
        foreach (DataGridViewRow gr in compGrid.Rows)
        {
            if (gr.IsNewRow) continue;
            string n = S(gr.Cells["Name"].Value);
            if (n.Length > 0) comp.Append(n).Append('\n');
        }
        var rem = new StringBuilder();
        foreach (DataGridViewRow gr in remGrid.Rows)
        {
            if (gr.IsNewRow) continue;
            string n = S(gr.Cells["Name"].Value);
            if (n.Length > 0) rem.Append(n).Append('\n');
        }
        var rules = new List<Rule>();
        foreach (DataGridViewRow gr in rulesGrid.Rows)
        {
            if (gr.IsNewRow) continue;
            rules.Add(new Rule {
                Enabled = B(gr.Cells["Enabled"].Value), Stage = S(gr.Cells["Stage"].Value),
                Find = Convert.ToString(gr.Cells["Find"].Value) ?? "", Replace = Convert.ToString(gr.Cells["Replace"].Value) ?? "",
                UseRegex = B(gr.Cells["Regex"].Value), Note = S(gr.Cells["Note"].Value),
            });
        }
        var learned = new List<Learned>();
        foreach (DataGridViewRow gr in learnGrid.Rows)
        {
            var l = (Learned)gr.Tag;
            if (l == null) continue;
            l.Enabled = B(gr.Cells["Enabled"].Value);
            learned.Add(l);
        }
        return new Settings {
            Version = Settings.CurrentVersion, LearnedRules = learned,
            NameTemplate = tbName.Text, RangeFormat = tbRange.Text, CrossYearFormat = tbCross.Text,
            MonthFormat = tbMonth.Text, SingleFormat = tbSingle.Text, YearFormat = tbYear.Text, UseMonthFormat = cbMonth.Checked,
            WorkMap = mapText.ToString(), Companies = comp.ToString(), Remarks = rem.ToString(), Rules = rules, RulesSeeded = true,
        };
    }

    static Settings LoadSettings()
    {
        Settings c = null;
        try
        {
            if (File.Exists(SettingsPath))
                using (var fs = File.OpenRead(SettingsPath))
                    c = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(fs);
        }
        catch { }
        if (c == null) c = new Settings();
        if (c.Rules == null) c.Rules = new List<Rule>();
        if (c.LearnedRules == null) c.LearnedRules = new List<Learned>();
        if (c.Version == 2)
        {   // 第 2 版升級：沒改過的檔名格式補上 {備註}
            if (c.NameTemplate == Settings.V2Template) c.NameTemplate = Settings.DefaultTemplate;
            if (string.IsNullOrEmpty(c.Remarks)) c.Remarks = Settings.DefaultRemarks;
            var map2 = Core.ParseWorkMap(c.WorkMap);
            var sb2 = new StringBuilder(c.WorkMap ?? "");
            foreach (var kv in Core.ParseWorkMap(Settings.DefaultMap))
                if (!map2.ContainsKey(kv.Key)) sb2.Append('\n').Append(kv.Key).Append('=').Append(kv.Value);
            c.WorkMap = sb2.ToString();
            c.Version = Settings.CurrentVersion;
        }
        if (c.Version < Settings.CurrentVersion)
        {   // 從更舊的版本升級：換成新的檔名格式，並補上新的對照
            c.NameTemplate = Settings.DefaultTemplate;
            var map = Core.ParseWorkMap(c.WorkMap);
            var sb = new StringBuilder(c.WorkMap ?? "");
            foreach (var kv in Core.ParseWorkMap(Settings.DefaultMap))
                if (!map.ContainsKey(kv.Key)) sb.Append('\n').Append(kv.Key).Append('=').Append(kv.Value);
            c.WorkMap = sb.ToString();
            if (string.IsNullOrEmpty(c.YearFormat)) c.YearFormat = "{年}年";
            if (string.IsNullOrEmpty(c.Companies)) c.Companies = Settings.DefaultCompanies;
            // 舊版的範例規則（未啟用）直接移除，避免混淆
            c.Rules.RemoveAll(r => !r.Enabled && (r.Note ?? "").StartsWith("範例"));
            c.Version = Settings.CurrentVersion;
        }
        return c;
    }
    static void SaveSettings(Settings c)
    {
        Directory.CreateDirectory(AppDir);
        using (var fs = File.Create(SettingsPath)) new XmlSerializer(typeof(Settings)).Serialize(fs, c);
    }

    // ================= 動作 =================
    void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { MessageBox.Show(this, "發生錯誤：\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void RequestRefresh() { timer.Stop(); timer.Start(); }

    void AddFiles(IEnumerable<string> list)
    {
        foreach (var p in list)
        {
            if (string.IsNullOrEmpty(p)) continue;
            IEnumerable<string> files;
            if (Directory.Exists(p)) { var arr = Directory.GetFiles(p); Array.Sort(arr, StringComparer.CurrentCulture); files = arr; }
            else if (File.Exists(p)) files = new[] { p };
            else continue;
            foreach (var f in files) if (paths.Add(f)) rows.Add(Core.MakeRow(f));
        }
    }

    void RefreshAll()
    {
        if (loading) return;
        Core.UpdateRows(rows, GetUISettings());
        FillGrid();
        UpdateTest();
    }

    void FillGrid()
    {
        filling = true;
        int first = grid.FirstDisplayedScrollingRowIndex;
        grid.SuspendLayout();
        grid.Rows.Clear();
        foreach (var r in rows)
        {
            int i = grid.Rows.Add(r.Ok && !r.Excluded, r.OldName, r.NewName, r.Status);
            var gr = grid.Rows[i];
            gr.Tag = r;
            gr.DefaultCellStyle.BackColor = r.Ok ? (r.Manual ? ColManual : ColOk) : (r.Status == Core.StatusSame ? ColSame : ColErr);
        }
        try { if (first >= 0 && first < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = first; } catch { }
        grid.ResumeLayout();
        grid.ClearSelection();
        filling = false;
        UpdateCount();
    }

    void UpdateCount()
    {
        int ready = rows.FindAll(r => r.Ok && !r.Excluded).Count;
        int bad = rows.FindAll(r => !r.Ok && r.Status != Core.StatusSame).Count;
        lblCount.Text = "共 " + rows.Count + " 個檔案　｜　將改名 " + ready + " 個　｜　有問題 " + bad + " 個（紅色列不會被改名）";
        btnRun.Enabled = ready > 0;
    }

    void RunRename()
    {
        Core.UpdateRows(rows, GetUISettings());
        int n = rows.FindAll(r => r.Ok && !r.Excluded).Count;
        if (n == 0) { FillGrid(); return; }
        if (MessageBox.Show(this, "確定要將 " + n + " 個檔案改名嗎？\r\n（會留下改名紀錄，之後可以還原）", "確認改名",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Core.RenameResult res;
        Cursor = Cursors.WaitCursor;
        try { res = Core.RenameAll(rows, LogDir); } finally { Cursor = Cursors.Default; }
        foreach (var r in rows.FindAll(x => x.Done)) { rows.Remove(r); paths.Remove(r.Path); }
        RefreshAll();
        string msg = "完成！成功改名 " + res.Ok + " 個檔案。";
        if (res.Fail > 0) msg += "\r\n有 " + res.Fail + " 個失敗（仍留在清單中，請看狀態欄）。";
        if (res.Log.Length > 0) msg += "\r\n\r\n改名紀錄：\r\n" + res.Log;
        MessageBox.Show(this, msg, Text);
    }

    void RunUndo()
    {
        using (var d = new OpenFileDialog { Title = "選擇要還原的改名紀錄", Filter = "改名紀錄 (*.csv)|*.csv" })
        {
            if (Directory.Exists(LogDir)) d.InitialDirectory = LogDir;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, "要把這份紀錄裡的檔案全部改回原本的名稱嗎？\r\n" + d.FileName, "確認還原",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var res = Core.Undo(d.FileName);
            string msg = "已還原 " + res[0] + " 個檔案。";
            if (res[1] > 0) msg += "\r\n跳過 " + res[1] + " 個（檔案已不存在、已被改動，或原檔名已被佔用）。";
            MessageBox.Show(this, msg, Text);
            RefreshAll();
        }
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (s, e) => MessageBox.Show("發生錯誤：\r\n" + e.Exception.Message, "批量改名工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm(args));
    }
}
