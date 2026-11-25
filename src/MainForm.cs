// 批量改名工具 — 主視窗
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

public class MainForm : Form
{
    static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatchRename");
    static readonly string SettingsPath = Path.Combine(AppDir, "settings.xml");
    static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "批量改名紀錄");

    Settings cfg;
    readonly List<FileRow> rows = new List<FileRow>();
    readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly List<string> undoLogs = new List<string>();     // 「上一步」：這次開啟後做過的改名
    Timer timer, saveTimer;
    bool filling, loading;
    UpdateInfo pendingUpdate;

    // 共用
    PillTabs tabs;
    readonly List<Control> pages = new List<Control>();
    Panel updateBar; TLabel lblUpdate; FlatBtn btnUpdate, btnThemeToggle;
    InputBox tbTest; FlowLayoutPanel testParts; TLabel lblTestResult;

    // 改名頁
    DataGridView grid; Chip chipTotal, chipReady, chipBad; FlatBtn btnRun, btnUndo; TLabel lblEmpty;
    // 範例學習頁
    InputBox tbFrom, tbTo; TLabel lblLearn; DataGridView learnGrid, rulesGrid; Chip chipRules;
    // 名稱對照頁
    NiceList catList; DataGridView itemGrid; TLabel lblCatName, lblCatNote; FlatBtn btnNoteEdit, btnNoteDel;
    // 名單頁
    NiceList listList; DataGridView wordGrid; TLabel lblListName, lblListNote, lblListHint; Chip chipListKind;
    // 檔名格式頁
    NiceList fmtList; Panel fmtEditor; InputBox tbFmtTpl; FlowLayoutPanel fmtCats, fmtDateFlow, fmtCustomDates, fmtPreview;
    TLabel lblFmtTitle, lblFmtCatsHint; TCheck cbFmtMonth; InputBox[] fmtDateBoxes; bool fmtLoading;
    // 外觀頁
    FlowLayoutPanel modeTiles, colorTiles; TLabel lblAboutVer;

    public MainForm(string[] args)
    {
        Updater.CleanupOld(Application.ExecutablePath);
        cfg = LoadSettings();
        Theme.Load(cfg);
        using (var g = CreateGraphics()) G.S = g.DpiX / 96f;
        UI.BaseFont = new Font("Microsoft JhengHei UI", 10f);
        Font = UI.BaseFont;
        Text = "批量改名工具 v" + Updater.CurrentText;
        Size = new Size(G.P(1240), G.P(860));
        MinimumSize = new Size(G.P(980), G.P(660));
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true; KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        timer = new Timer { Interval = 300 };
        timer.Tick += (s, e) => { timer.Stop(); Safe(RefreshAll); };
        saveTimer = new Timer { Interval = 1500 };
        saveTimer.Tick += (s, e) => { saveTimer.Stop(); try { SaveSettings(cfg); } catch { } };

        loading = true;
        BuildUI();
        loading = false;
        UI.Apply(this);
        ApplyFonts(this);

        if (args != null && args.Length > 0) AddFiles(args);
        RefreshAll();
        HandleCreated += (s, e) => Theme.StyleTitleBar(this);
        Shown += (s, e) => { Theme.StyleTitleBar(this); UI.Apply(this); grid.ClearSelection(); CheckUpdate(false); };
        FormClosing += (s, e) => { try { SaveSettings(cfg); } catch { } };
        KeyDown += (s, e) => {
            if (e.Control && e.KeyCode == Keys.Z && !(ActiveControl is TextBoxBase) && !(ActiveControl is InputBox) && btnUndo.Enabled)
            { e.Handled = true; Safe(UndoRename); }
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) => {
            if (cfg.ThemeMode == "system" && Theme.SystemIsDark() != Theme.Dark) BeginInvoke(new Action(ApplyTheme));
        };
    }

    // ================= 小工具 =================
    static TLabel L(string t, LabelRole r = LabelRole.Text) { return new TLabel(t, r); }
    static FlatBtn B(string t, BtnKind k = BtnKind.Soft) { return new FlatBtn(t, k); }
    static FlowLayoutPanel Flow(bool rtl = false)
    {
        return new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, Margin = new Padding(0) };
    }
    static TableLayoutPanel Rows(params SizeType[] types)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = types.Length, Margin = new Padding(0), Padding = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 欄寬跟著外框，不會撐出去
        foreach (var st in types) t.RowStyles.Add(st == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        return t;
    }
    static TableLayoutPanel Cols(params float[] pct)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = pct.Length, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        foreach (var p in pct) t.ColumnStyles.Add(p <= 0 ? new ColumnStyle(SizeType.AutoSize) : new ColumnStyle(SizeType.Percent, p));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return t;
    }
    // 卡片的標題列：左邊標題（＋小標籤），右邊按鈕
    static TableLayoutPanel CardHeader(Control[] left, Control[] right)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, G.P(6)) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lf = Flow(); lf.WrapContents = false; lf.Controls.AddRange(left);
        var rf = Flow(true); rf.WrapContents = false; for (int i = right.Length - 1; i >= 0; i--) rf.Controls.Add(right[i]);
        t.Controls.Add(lf, 0, 0); t.Controls.Add(rf, 1, 0);
        return t;
    }
    static DataGridView NewGrid(bool editable)
    {
        var g = new DataGridView {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, Margin = new Padding(0),
        };
        if (!editable) g.ReadOnly = false;
        g.DataError += (s, e) => { e.ThrowException = false; };
        return g;
    }
    static DataGridViewTextBoxColumn TextCol(string name, string header, float weight, bool ro = true)
    {
        return new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = weight, ReadOnly = ro, SortMode = DataGridViewColumnSortMode.NotSortable };
    }
    void ApplyFonts(Control c)
    {
        var t = c as TLabel; if (t != null) t.ApplyFont(Font);
        foreach (Control ch in c.Controls) ApplyFonts(ch);
    }
    void Tip(Control c, string text) { new ToolTip().SetToolTip(c, text); }

    // ================= 整體版面 =================
    void BuildUI()
    {
        var root = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
        Controls.Add(root);

        // 頂部：標題、分頁、主題切換
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Tag = "header", Padding = new Padding(G.P(16), G.P(10), G.P(16), G.P(10)), Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = Flow(); brand.WrapContents = false;
        brand.Controls.Add(new TLabel("✦", LabelRole.Accent) { Margin = new Padding(0, G.P(4), G.P(4), 0) });
        brand.Controls.Add(L("批量改名工具", LabelRole.Title));
        brand.Controls.Add(new Chip("v" + Updater.CurrentText, ChipKind.Accent));
        tabs = new PillTabs { Margin = new Padding(G.P(24), 0, 0, 0), Font = UI.BaseFont };
        tabs.Items.AddRange(new[] { "改名", "範例學習", "名稱對照", "名單", "檔名格式", "外觀" });
        tabs.Size = tabs.GetPreferredSize(Size.Empty);
        btnThemeToggle = B(Theme.Dark ? "☀" : "☾", BtnKind.Ghost);
        Tip(btnThemeToggle, "切換亮色／暗色");
        btnThemeToggle.Click += (s, e) => { cfg.ThemeMode = Theme.Dark ? "light" : "dark"; ApplyTheme(); };
        header.Controls.Add(brand, 0, 0); header.Controls.Add(tabs, 1, 0); header.Controls.Add(btnThemeToggle, 2, 0);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildUpdateBar(), 0, 1);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(G.P(10), G.P(8), G.P(10), 0), Margin = new Padding(0) };
        pages.Add(BuildRenamePage()); pages.Add(BuildLearnPage()); pages.Add(BuildMapPage());
        pages.Add(BuildListsPage()); pages.Add(BuildFormatPage()); pages.Add(BuildLookPage());
        foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }
        pages[0].Visible = true;
        tabs.SelectedIndexChanged += (s, e) => {
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == tabs.SelectedIndex;
            if (tabs.SelectedIndex == 4) ShowFormat();
            Safe(RefreshAll);
        };
        root.Controls.Add(host, 0, 2);
        root.Controls.Add(BuildTestPanel(), 0, 3);
    }

    // ---------- 更新提示列 ----------
    Control BuildUpdateBar()
    {
        updateBar = new Panel { Dock = DockStyle.Fill, AutoSize = true, Visible = false, Tag = "soft", Padding = new Padding(G.P(16), G.P(4), G.P(16), G.P(4)), Margin = new Padding(0) };
        var f = Flow(); f.WrapContents = false;
        lblUpdate = L("", LabelRole.Accent);
        btnUpdate = B("立即更新", BtnKind.Primary);
        var later = B("稍後再說", BtnKind.Ghost);
        f.Controls.AddRange(new Control[] { lblUpdate, btnUpdate, later });
        updateBar.Controls.Add(f);
        btnUpdate.Click += (s, e) => DoUpdate();
        later.Click += (s, e) => updateBar.Visible = false;
        return updateBar;
    }

    // ---------- 下方「試試看」 ----------
    Control BuildTestPanel()
    {
        var wrap = new Panel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(G.P(10), G.P(4), G.P(10), G.P(10)), Margin = new Padding(0) };
        var card = new Card { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(G.P(16), G.P(10), G.P(16), G.P(10)) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tbTest = new InputBox("115.09月高空工作車(日)檢查表(甲公司)") { Placeholder = "輸入任何檔名，或在「改名」頁點一下某個檔案" };
        testParts = Flow(); testParts.WrapContents = true;
        lblTestResult = L("", LabelRole.Success);
        lblTestResult.Font = new Font(UI.BaseFont.FontFamily, 11f, FontStyle.Bold);
        t.Controls.Add(new TLabel("試試看", LabelRole.Dim) { Margin = new Padding(0, G.P(10), G.P(10), 0) }, 0, 0); t.Controls.Add(tbTest, 1, 0);
        t.Controls.Add(new TLabel("辨識出", LabelRole.Dim) { Margin = new Padding(0, G.P(9), G.P(10), 0) }, 0, 1); t.Controls.Add(testParts, 1, 1);
        t.Controls.Add(new TLabel("新檔名", LabelRole.Dim) { Margin = new Padding(0, G.P(6), G.P(10), 0) }, 0, 2); t.Controls.Add(lblTestResult, 1, 2);
        card.Controls.Add(t); wrap.Controls.Add(card);
        tbTest.Box.TextChanged += (s, e) => Safe(UpdateTest);
        return wrap;
    }

    void UpdateTest()
    {
        if (tbTest == null || loading) return;
        var p = Core.Process(tbTest.Text.Trim(), cfg, Core.BuildMap(cfg));
        testParts.SuspendLayout();
        testParts.Controls.Clear();
        if (p.Ok)
        {
            if (p.Category.Length > 0) testParts.Controls.Add(new Chip("分類 " + p.Category, ChipKind.Info));
            foreach (var kv in p.Parts)
                if (kv.Key != "廠商" && kv.Value.Length > 0) testParts.Controls.Add(new Chip(kv.Key + " " + kv.Value, ChipKind.Neutral));
            testParts.Controls.Add(new Chip(p.UsedRule != null ? "套用學到的規則" : "格式：" + p.Format.Name, ChipKind.Accent));
            lblTestResult.Text = p.Final; lblTestResult.Role = LabelRole.Success;
        }
        else { lblTestResult.Text = "無法辨識：" + p.Error; lblTestResult.Role = LabelRole.Danger; }
        if (p.RuleErrors.Count > 0) testParts.Controls.Add(new Chip("⚠ " + string.Join("；", p.RuleErrors.ToArray()), ChipKind.Warning));
        foreach (Control c in testParts.Controls) { c.Font = Font; c.BackColor = Theme.Card; }
        lblTestResult.ForeColor = lblTestResult.RoleColor();
        testParts.ResumeLayout();
    }

    // ================= 分頁 1：改名 =================
    Control BuildRenamePage()
    {
        var page = Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);

        var top = new Card { Dock = DockStyle.Fill, AutoSize = true };
        var topRows = Rows(SizeType.AutoSize, SizeType.AutoSize); topRows.AutoSize = true;
        chipTotal = new Chip("共 0 個"); chipReady = new Chip("將改名 0 個", ChipKind.Success); chipBad = new Chip("有問題 0 個", ChipKind.Danger);
        var bAdd = B("＋ 加入檔案", BtnKind.Primary); var bDir = B("加入資料夾", BtnKind.Outline);
        topRows.Controls.Add(CardHeader(new Control[] { L("檔案清單", LabelRole.Title), chipTotal, chipReady, chipBad }, new Control[] { bDir, bAdd }), 0, 0);
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var left = Flow();
        var bAll = B("全部勾選", BtnKind.Ghost); var bNone = B("全部取消", BtnKind.Ghost); var bRemove = B("移除選取", BtnKind.Ghost);
        var bClear = B("清空清單", BtnKind.Ghost); var bReset = B("恢復自動命名", BtnKind.Ghost); var bLearn = B("把手動改的學起來", BtnKind.Ghost);
        Tip(bReset, "選取的檔案如果手動改過新檔名，改回程式自動產生的名稱");
        Tip(bLearn, "在「新檔名」欄點兩下改成你要的樣子，再按這裡，程式就會學起來");
        left.Controls.AddRange(new Control[] { bAll, bNone, bRemove, bClear, bReset, bLearn });
        btnUndo = B("↶ 上一步", BtnKind.Outline); btnUndo.Enabled = false;
        Tip(btnUndo, "復原剛才的「執行改名」（Ctrl+Z）");
        bar.Controls.Add(left, 0, 0); bar.Controls.Add(btnUndo, 1, 0);
        topRows.Controls.Add(bar, 0, 1);
        top.Controls.Add(topRows);
        page.Controls.Add(top, 0, 0);

        var gridCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(G.P(8), G.P(6), G.P(8), G.P(8)) };
        grid = NewGrid(true); grid.AllowDrop = true;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "改", FillWeight = 5 });
        grid.Columns.Add(TextCol("Old", "原檔名", 37));
        grid.Columns.Add(TextCol("New", "新檔名（點兩下可直接修改）", 37, false));
        grid.Columns.Add(TextCol("Status", "狀態", 21));
        lblEmpty = new TLabel("把檔案或資料夾拖曳到這裡，或按上方「＋ 加入檔案」", LabelRole.Dim) { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        gridCard.Controls.Add(grid); gridCard.Controls.Add(lblEmpty); lblEmpty.BringToFront();
        lblEmpty.AllowDrop = true;
        page.Controls.Add(gridCard, 0, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(G.P(6), G.P(2), G.P(6), 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var bl = Flow();
        var bOld = B("還原以前的改名…", BtnKind.Ghost); var bLog = B("改名紀錄資料夾", BtnKind.Ghost);
        bl.Controls.AddRange(new Control[] { bOld, bLog });
        btnRun = B("執行改名  →", BtnKind.Primary);
        btnRun.Font = new Font(UI.BaseFont.FontFamily, 11f, FontStyle.Bold);
        bottom.Controls.Add(bl, 0, 0); bottom.Controls.Add(btnRun, 1, 0);
        page.Controls.Add(bottom, 0, 2);

        // 事件
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
        bClear.Click += (s, e) => {
            if (rows.Count > 0 && !UI.Confirm(this, "清空清單", "要把清單裡的 " + rows.Count + " 個檔案都移除嗎？（只是從清單移除，不會刪除檔案）", "清空", "取消")) return;
            rows.Clear(); paths.Clear(); FillGrid();
        };
        bAll.Click += (s, e) => { foreach (var r in rows) r.Excluded = false; FillGrid(); };
        bNone.Click += (s, e) => { foreach (var r in rows) r.Excluded = true; FillGrid(); };
        bReset.Click += (s, e) => { foreach (DataGridViewRow gr in grid.SelectedRows) ((FileRow)gr.Tag).Manual = false; RefreshAll(); };
        bLearn.Click += (s, e) => Safe(LearnFromManual);
        btnUndo.Click += (s, e) => Safe(UndoRename);
        btnRun.Click += (s, e) => Safe(RunRename);
        bOld.Click += (s, e) => Safe(RunUndoFromFile);
        bLog.Click += (s, e) => Safe(() => { Directory.CreateDirectory(LogDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + LogDir + "\""); });

        DragEventHandler enter = (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragEventHandler drop = (s, e) => Safe(() => { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); RefreshAll(); });
        foreach (Control c in new Control[] { grid, lblEmpty, this }) { c.DragEnter += enter; c.DragDrop += drop; }

        grid.CurrentCellDirtyStateChanged += (s, e) => {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellContentClick += (s, e) => { if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Apply") grid.EndEdit(); };
        grid.CellValueChanged += (s, e) => {
            if (filling || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Apply") return;
            var gr = grid.Rows[e.RowIndex]; var r = (FileRow)gr.Tag;
            bool v = gr.Cells["Apply"].Value is bool && (bool)gr.Cells["Apply"].Value;
            r.Excluded = !v;
            if (v && !r.Ok) RequestRefresh(); else UpdateCount();
        };
        grid.CellEndEdit += (s, e) => {
            if (grid.Columns[e.ColumnIndex].Name != "New") return;
            var gr = grid.Rows[e.RowIndex]; var r = (FileRow)gr.Tag;
            string txt = Convert.ToString(gr.Cells["New"].Value).Trim();
            if (r.Ext.Length > 0 && txt.EndsWith(r.Ext, StringComparison.OrdinalIgnoreCase)) txt = txt.Substring(0, txt.Length - r.Ext.Length);
            if (txt != r.NewStem) { r.NewStem = txt; r.Manual = true; }
            RequestRefresh();
        };
        grid.SelectionChanged += (s, e) => {
            if (filling || grid.CurrentRow == null || grid.CurrentRow.Tag == null) return;
            tbTest.Text = ((FileRow)grid.CurrentRow.Tag).Stem;
        };
        return page;
    }

    // ================= 分頁 2：範例學習 =================
    Control BuildLearnPage()
    {
        bool legacy = cfg.Rules != null && cfg.Rules.Count > 0;
        var page = legacy ? Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize) : Rows(SizeType.AutoSize, SizeType.Percent);

        var teach = new Card { Dock = DockStyle.Fill, AutoSize = true, AccentStrip = true };
        var tr = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize); tr.AutoSize = true;
        tr.Controls.Add(L("教程式一個新的改法", LabelRole.Title), 0, 0);
        tr.Controls.Add(new TLabel("遇到改得不對的檔名，輸入「原檔名」和「想要的結果」，按「學起來」。程式會自己找出規律，以後同一種文件都照這樣改。\r\n" +
                                   "如果和以前學過的規則重複或衝突，程式會先問你，不會直接蓋掉。", LabelRole.Dim) { MaximumSize = new Size(G.P(1100), 0) }, 0, 1);
        var inp = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, G.P(6), 0, 0) };
        inp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); inp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); inp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tbFrom = new InputBox() { Placeholder = "例如：甲公司-移動式起重機查驗表1150320(KEF-6390)" };
        tbTo = new InputBox() { Placeholder = "例如：甲公司-移動式起重機查驗表115年3月20-(KEF-6390)" };
        var bFill = B("帶入「試試看」的檔名", BtnKind.Ghost); var bLearn = B("學起來", BtnKind.Primary);
        inp.Controls.Add(L("原檔名") , 0, 0); inp.Controls.Add(tbFrom, 1, 0); inp.Controls.Add(bFill, 2, 0);
        inp.Controls.Add(L("想要的結果"), 0, 1); inp.Controls.Add(tbTo, 1, 1); inp.Controls.Add(bLearn, 2, 1);
        foreach (Control c in inp.Controls) if (c is TLabel) c.Margin = new Padding(0, G.P(10), G.P(10), 0);
        tr.Controls.Add(inp, 0, 2);
        lblLearn = L(""); lblLearn.MaximumSize = new Size(G.P(1100), 0);
        tr.Controls.Add(lblLearn, 0, 3);
        teach.Controls.Add(tr);
        page.Controls.Add(teach, 0, 0);

        var list = new Card { Dock = DockStyle.Fill };
        var lr = Rows(SizeType.AutoSize, SizeType.Percent);
        chipRules = new Chip("0 條");
        var bEdit = B("編輯", BtnKind.Soft); var bDel = B("刪除", BtnKind.Danger);
        lr.Controls.Add(CardHeader(new Control[] { L("已學會的規則", LabelRole.Title), chipRules, L("點兩下可以編輯，取消勾選＝暫停使用", LabelRole.Dim) }, new Control[] { bEdit, bDel }), 0, 0);
        learnGrid = NewGrid(true);
        learnGrid.MultiSelect = false;
        learnGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "使用", FillWeight = 6 });
        learnGrid.Columns.Add(TextCol("Doc", "文件", 13));
        learnGrid.Columns.Add(TextCol("Case", "適用情況", 16));
        learnGrid.Columns.Add(TextCol("Tpl", "新檔名的樣子", 31));
        learnGrid.Columns.Add(TextCol("From", "範例：原檔名", 17));
        learnGrid.Columns.Add(TextCol("To", "範例：結果", 17));
        lr.Controls.Add(learnGrid, 0, 1);
        list.Controls.Add(lr);
        page.Controls.Add(list, 0, 1);
        FillLearnGrid();

        if (legacy)
        {
            var old = new Card { Dock = DockStyle.Fill, AutoSize = true };
            var or = Rows(SizeType.AutoSize, SizeType.AutoSize); or.AutoSize = true;
            or.Controls.Add(L("舊版「尋找→取代」規則（之前設定的，仍然有效；全部刪掉後下次開啟就不再顯示）", LabelRole.Dim), 0, 0);
            rulesGrid = NewGrid(true); rulesGrid.AllowUserToAddRows = true; rulesGrid.AllowUserToDeleteRows = true; rulesGrid.Dock = DockStyle.Top; rulesGrid.Height = G.P(150);
            rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "啟用", FillWeight = 7 });
            var stageCol = new DataGridViewComboBoxColumn { Name = "Stage", HeaderText = "何時套用", FillWeight = 20, FlatStyle = FlatStyle.Flat };
            stageCol.Items.AddRange(Rule.StageBefore, Rule.StageAfter);
            rulesGrid.Columns.Add(stageCol);
            rulesGrid.Columns.Add(TextCol("Find", "尋找文字", 24, false));
            rulesGrid.Columns.Add(TextCol("Replace", "取代為", 24, false));
            rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Regex", HeaderText = "正規表示式", FillWeight = 9 });
            rulesGrid.Columns.Add(TextCol("Note", "備註", 16, false));
            foreach (var rule in cfg.Rules)
                rulesGrid.Rows.Add(rule.Enabled, rule.Stage == Rule.StageAfter ? Rule.StageAfter : Rule.StageBefore, rule.Find ?? "", rule.Replace ?? "", rule.UseRegex, rule.Note ?? "");
            rulesGrid.DefaultValuesNeeded += (s, e) => { e.Row.Cells["Enabled"].Value = true; e.Row.Cells["Stage"].Value = Rule.StageBefore; e.Row.Cells["Regex"].Value = false; };
            rulesGrid.CurrentCellDirtyStateChanged += (s, e) => { if (rulesGrid.IsCurrentCellDirty && !(rulesGrid.CurrentCell is DataGridViewTextBoxCell)) rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            rulesGrid.CellValueChanged += (s, e) => { if (!loading) { ReadLegacyRules(); Changed(); } };
            rulesGrid.RowsRemoved += (s, e) => { if (!loading) { ReadLegacyRules(); Changed(); } };
            or.Controls.Add(rulesGrid, 0, 1);
            old.Controls.Add(or);
            page.Controls.Add(old, 0, 2);
        }

        bFill.Click += (s, e) => { tbFrom.Text = tbTest.Text.Trim(); tbTo.Box.Focus(); };
        bLearn.Click += (s, e) => Safe(() => { LearnOne(tbFrom.Text, tbTo.Text, true); });
        learnGrid.CurrentCellDirtyStateChanged += (s, e) => {
            if (learnGrid.IsCurrentCellDirty && learnGrid.CurrentCell is DataGridViewCheckBoxCell) learnGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        learnGrid.CellContentClick += (s, e) => { if (e.RowIndex >= 0 && learnGrid.Columns[e.ColumnIndex].Name == "Enabled") learnGrid.EndEdit(); };
        learnGrid.CellValueChanged += (s, e) => {
            if (loading || e.RowIndex < 0 || learnGrid.Columns[e.ColumnIndex].Name != "Enabled") return;
            Safe(() => CheckEnableOverlap(learnGrid.Rows[e.RowIndex]));
            Changed();
        };
        learnGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && learnGrid.Columns[e.ColumnIndex].Name != "Enabled") Safe(() => EditRule((Learned)learnGrid.Rows[e.RowIndex].Tag)); };
        bEdit.Click += (s, e) => {
            if (learnGrid.CurrentRow == null) { UI.Info(this, "編輯規則", "請先在清單點一下要編輯的規則。"); return; }
            Safe(() => EditRule((Learned)learnGrid.CurrentRow.Tag));
        };
        bDel.Click += (s, e) => {
            if (learnGrid.CurrentRow == null) return;
            var l = (Learned)learnGrid.CurrentRow.Tag;
            if (!UI.Confirm(this, "刪除規則", "要刪除這條規則嗎？\r\n\r\n文件：" + l.DocKey + "\r\n情況：" + Core.DescribeSignature(l.Signature) +
                            "\r\n樣子：" + Core.FriendlyTemplate(l.Template), "刪除", "取消")) return;
            cfg.LearnedRules.Remove(l); FillLearnGrid(); Changed();
        };
        return page;
    }

    void ReadLegacyRules()
    {
        if (rulesGrid == null) return;
        var list = new List<Rule>();
        foreach (DataGridViewRow gr in rulesGrid.Rows)
        {
            if (gr.IsNewRow) continue;
            list.Add(new Rule {
                Enabled = gr.Cells["Enabled"].Value is bool && (bool)gr.Cells["Enabled"].Value, Stage = Convert.ToString(gr.Cells["Stage"].Value),
                Find = Convert.ToString(gr.Cells["Find"].Value) ?? "", Replace = Convert.ToString(gr.Cells["Replace"].Value) ?? "",
                UseRegex = gr.Cells["Regex"].Value is bool && (bool)gr.Cells["Regex"].Value, Note = Convert.ToString(gr.Cells["Note"].Value) ?? "",
            });
        }
        cfg.Rules = list;
    }

    void FillLearnGrid()
    {
        bool was = loading; loading = true;
        Learned cur = learnGrid.CurrentRow != null ? learnGrid.CurrentRow.Tag as Learned : null;
        learnGrid.Rows.Clear();
        foreach (var l in cfg.LearnedRules)
        {
            int i = learnGrid.Rows.Add(l.Enabled, l.DocKey, Core.DescribeSignature(l.Signature), Core.FriendlyTemplate(l.Template), l.ExampleFrom, l.ExampleTo);
            learnGrid.Rows[i].Tag = l;
            if (!l.Enabled) learnGrid.Rows[i].DefaultCellStyle.ForeColor = Theme.TextDim;
            if (l == cur) learnGrid.CurrentCell = learnGrid.Rows[i].Cells["Doc"];
        }
        chipRules.Text = cfg.LearnedRules.Count + " 條";
        UI.StyleGrid(learnGrid);
        loading = was;
    }

    // ================= 分頁 3：名稱對照（分類） =================
    Control BuildMapPage()
    {
        var page = Cols(32, 68);

        var left = new Card { Dock = DockStyle.Fill };
        var lr = Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
        var bAddCat = B("＋ 新增分類", BtnKind.Primary);
        lr.Controls.Add(CardHeader(new Control[] { L("分類", LabelRole.Title) }, new Control[] { bAddCat }), 0, 0);
        catList = new NiceList { Font = UI.BaseFont };
        catList.TitleOf = o => ((Category)o).Name;
        catList.SubOf = o => ((Category)o).Note;
        catList.BadgeOf = o => ((Category)o).Items.Count + " 項";
        lr.Controls.Add(catList, 0, 1);
        var lb = Flow();
        var bEditCat = B("編輯", BtnKind.Soft); var bDelCat = B("刪除", BtnKind.Danger);
        var bUp = B("↑", BtnKind.Ghost); var bDown = B("↓", BtnKind.Ghost);
        Tip(bUp, "往上移"); Tip(bDown, "往下移");
        lb.Controls.AddRange(new Control[] { bEditCat, bDelCat, bUp, bDown });
        lr.Controls.Add(lb, 0, 2);
        left.Controls.Add(lr);
        page.Controls.Add(left, 0, 0);

        var right = new Card { Dock = DockStyle.Fill, AccentStrip = true };
        var rr = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
        lblCatName = L("", LabelRole.Title);
        btnNoteEdit = B("＋ 新增註解", BtnKind.Ghost); btnNoteDel = B("刪除註解", BtnKind.Ghost);
        rr.Controls.Add(CardHeader(new Control[] { lblCatName }, new Control[] { btnNoteEdit, btnNoteDel }), 0, 0);
        lblCatNote = L("", LabelRole.Dim); lblCatNote.MaximumSize = new Size(G.P(760), 0);
        rr.Controls.Add(lblCatNote, 0, 1);
        var ib = Flow();
        var bAddItem = B("＋ 新增項目", BtnKind.Primary); var bEditItem = B("編輯", BtnKind.Soft); var bDelItem = B("刪除", BtnKind.Danger); var bMove = B("移到其他分類…", BtnKind.Ghost);
        ib.Controls.AddRange(new Control[] { bAddItem, bEditItem, bDelItem, bMove });
        rr.Controls.Add(ib, 0, 2);
        itemGrid = NewGrid(true);
        itemGrid.Columns.Add(TextCol("From", "原本的名稱（檔名裡的寫法）", 50, false));
        itemGrid.Columns.Add(TextCol("To", "改成（留空＝不換名，只歸類）", 50, false));
        rr.Controls.Add(itemGrid, 0, 3);
        rr.Controls.Add(new TLabel("辨識出的文件名稱和「原本的名稱」完全一樣時，就換成「改成」的寫法。項目也決定文件屬於哪個分類，「檔名格式」可以指定給分類。表格裡點兩下也能直接修改。", LabelRole.Dim) { MaximumSize = new Size(G.P(780), 0) }, 0, 4);
        right.Controls.Add(rr);
        page.Controls.Add(right, 1, 0);

        FillCatList(0);
        catList.SelectedIndexChanged += (s, e) => ShowCategory();
        catList.DoubleClick += (s, e) => EditCategory();
        bAddCat.Click += (s, e) => Safe(() => {
            var v = UI.Prompt(this, "新增分類", "例如「檢查表」「作業申請」。註解可以不填，有填才會顯示。", new[] { "分類名稱", "註解" }, new[] { "", "" },
                              new[] { false, true }, vals => ValidateCatName(vals[0], null));
            if (v == null) return;
            cfg.Categories.Add(new Category { Name = v[0], Note = v[1] });
            FillCatList(cfg.Categories.Count - 1); Changed();
        });
        bEditCat.Click += (s, e) => Safe(EditCategory);
        bDelCat.Click += (s, e) => Safe(() => {
            var c = CurCat(); if (c == null) return;
            if (!UI.Confirm(this, "刪除分類", "要刪除「" + c.Name + "」和裡面的 " + c.Items.Count + " 個項目嗎？" +
                            (FormatsUsing(c.Name).Count > 0 ? "\r\n指定給這個分類的格式會改回預設格式。" : ""), "刪除", "取消")) return;
            foreach (var f in cfg.Formats) f.Categories.Remove(c.Name);
            int i = cfg.Categories.IndexOf(c); cfg.Categories.Remove(c);
            FillCatList(Math.Min(i, cfg.Categories.Count - 1)); Changed();
        });
        EventHandler move = (s, e) => {
            var c = CurCat(); if (c == null) return;
            int i = cfg.Categories.IndexOf(c), j = i + (s == bUp ? -1 : 1);
            if (j < 0 || j >= cfg.Categories.Count) return;
            cfg.Categories[i] = cfg.Categories[j]; cfg.Categories[j] = c;
            FillCatList(j); Changed();
        };
        bUp.Click += move; bDown.Click += move;
        btnNoteEdit.Click += (s, e) => Safe(() => {
            var c = CurCat(); if (c == null) return;
            var v = UI.Prompt(this, c.Note.Length > 0 ? "編輯註解" : "新增註解", "「" + c.Name + "」的說明，會顯示在分類名稱下面。", new[] { "註解" }, new[] { c.Note }, new[] { true });
            if (v == null) return;
            c.Note = v[0]; FillCatList(cfg.Categories.IndexOf(c)); Changed();
        });
        btnNoteDel.Click += (s, e) => {
            var c = CurCat(); if (c == null || c.Note.Length == 0) return;
            if (!UI.Confirm(this, "刪除註解", "要刪除「" + c.Name + "」的註解嗎？", "刪除", "取消")) return;
            c.Note = ""; FillCatList(cfg.Categories.IndexOf(c)); Changed();
        };
        bAddItem.Click += (s, e) => Safe(() => {
            var c = CurCat(); if (c == null) { UI.Info(this, "新增項目", "請先新增或選一個分類。"); return; }
            var v = UI.Prompt(this, "新增項目到「" + c.Name + "」", "「原本的名稱」是檔名裡的寫法，例如「高空車檢查表」；「改成」留空表示不換名、只歸到這個分類。",
                              new[] { "原本的名稱", "改成" }, new[] { "", "" }, null, vals => ValidateItem(vals[0], null));
            if (v == null) return;
            c.Items.Add(new MapItem { From = v[0], To = v[1] });
            ShowCategory(); FillCatList(cfg.Categories.IndexOf(c)); Changed();
        });
        bEditItem.Click += (s, e) => Safe(EditItem);
        itemGrid.CellDoubleClick += (s, e) => { };   // 雙擊直接進入編輯（表格本身支援）
        bDelItem.Click += (s, e) => {
            var c = CurCat(); if (c == null || itemGrid.SelectedRows.Count == 0) return;
            var del = new List<MapItem>(); foreach (DataGridViewRow gr in itemGrid.SelectedRows) del.Add((MapItem)gr.Tag);
            if (!UI.Confirm(this, "刪除項目", "要刪除選取的 " + del.Count + " 個項目嗎？", "刪除", "取消")) return;
            foreach (var it in del) c.Items.Remove(it);
            ShowCategory(); FillCatList(cfg.Categories.IndexOf(c)); Changed();
        };
        bMove.Click += (s, e) => Safe(() => {
            var c = CurCat(); if (c == null || itemGrid.SelectedRows.Count == 0) return;
            var others = cfg.Categories.Where(x => x != c).ToList();
            if (others.Count == 0) { UI.Info(this, "移到其他分類", "目前只有一個分類。"); return; }
            int ch = UI.Choose(this, "移到其他分類", "要把選取的項目移到哪個分類？", others.Select(x => x.Name).Concat(new[] { "取消" }).ToArray());
            if (ch < 0 || ch >= others.Count) return;
            foreach (DataGridViewRow gr in itemGrid.SelectedRows) { var it = (MapItem)gr.Tag; c.Items.Remove(it); others[ch].Items.Add(it); }
            ShowCategory(); FillCatList(cfg.Categories.IndexOf(c)); Changed();
        });
        itemGrid.CellEndEdit += (s, e) => {
            var gr = itemGrid.Rows[e.RowIndex]; var it = (MapItem)gr.Tag; if (it == null) return;
            string v = Convert.ToString(gr.Cells[e.ColumnIndex].Value ?? "").Trim();
            if (itemGrid.Columns[e.ColumnIndex].Name == "From")
            {
                string err = ValidateItem(v, it);
                if (err != null) { UI.Info(this, "名稱對照", err); gr.Cells["From"].Value = it.From; return; }
                it.From = v;
            }
            else it.To = v;
            Changed();
        };
        return page;
    }

    Category CurCat() { return catList.SelectedItem as Category; }

    void FillCatList(int select)
    {
        catList.BeginUpdate(); catList.Items.Clear();
        foreach (var c in cfg.Categories) catList.Items.Add(c);
        catList.EndUpdate();
        if (cfg.Categories.Count > 0) catList.SelectedIndex = Math.Max(0, Math.Min(select, cfg.Categories.Count - 1));
        ShowCategory();
    }

    void ShowCategory()
    {
        var c = CurCat();
        lblCatName.Text = c == null ? "（還沒有分類，按左邊「＋ 新增分類」）" : c.Name;
        lblCatNote.Text = c == null ? "" : c.Note;
        lblCatNote.Visible = c != null && c.Note.Length > 0;
        btnNoteEdit.Text = c != null && c.Note.Length > 0 ? "編輯註解" : "＋ 新增註解";
        btnNoteEdit.Visible = c != null; btnNoteDel.Visible = c != null && c.Note.Length > 0;
        btnNoteEdit.Size = btnNoteEdit.GetPreferredSize(Size.Empty);
        itemGrid.Rows.Clear();
        if (c != null)
            foreach (var it in c.Items) { int i = itemGrid.Rows.Add(it.From, it.To); itemGrid.Rows[i].Tag = it; }
        UI.StyleGrid(itemGrid);
    }

    void EditCategory()
    {
        var c = CurCat(); if (c == null) return;
        var v = UI.Prompt(this, "編輯分類", "註解可以留空，留空就不會顯示。", new[] { "分類名稱", "註解" }, new[] { c.Name, c.Note },
                          new[] { false, true }, vals => ValidateCatName(vals[0], c));
        if (v == null) return;
        if (v[0] != c.Name) foreach (var f in cfg.Formats) { int k = f.Categories.IndexOf(c.Name); if (k >= 0) f.Categories[k] = v[0]; }
        c.Name = v[0]; c.Note = v[1];
        FillCatList(cfg.Categories.IndexOf(c)); Changed();
    }

    void EditItem()
    {
        var c = CurCat(); if (c == null || itemGrid.CurrentRow == null) return;
        var it = (MapItem)itemGrid.CurrentRow.Tag;
        var v = UI.Prompt(this, "編輯項目", "「改成」留空表示不換名、只歸到這個分類。", new[] { "原本的名稱", "改成" }, new[] { it.From, it.To }, null, vals => ValidateItem(vals[0], it));
        if (v == null) return;
        it.From = v[0]; it.To = v[1];
        ShowCategory(); Changed();
    }

    string ValidateCatName(string name, Category self)
    {
        if (name.Length == 0) return "分類名稱不能是空的。";
        foreach (var c in cfg.Categories) if (c != self && c.Name == name) return "已經有一個叫「" + name + "」的分類了。";
        return null;
    }

    string ValidateItem(string from, MapItem self)
    {
        if (from.Length == 0) return "「原本的名稱」不能是空的。";
        foreach (var c in cfg.Categories)
            foreach (var it in c.Items)
                if (it != self && it.From == from) return "「" + from + "」已經在「" + c.Name + "」分類裡了，不能重複。";
        return null;
    }

    List<NameFormat> FormatsUsing(string cat) { return cfg.Formats.Where(f => !f.IsDefault && f.Categories.Contains(cat)).ToList(); }

    // ================= 分頁 4：名單（可以自己開名單） =================
    static readonly string[][] ListKinds = {
        new[] { NameList.KindCompany, "公司／廠商", "檔名裡出現這些名稱，就當成公司或廠商。" },
        new[] { NameList.KindRemark, "備註用語", "檔名中含有這些字的部分當成備註，例如「END已退場」。" },
        new[] { NameList.KindRemove, "要刪掉的字", "改名時把這些字從檔名拿掉，例如「最終版」「(複本)」。" },
        new[] { NameList.KindField, "自訂欄位", "檔名裡出現清單中的字，就能在「檔名格式」用〔名單名稱〕把它放到任何位置，例如名單「工區」：A棟、B棟。" },
    };
    static readonly HashSet<string> ReservedTokens = new HashSet<string> {
        "公司", "文件", "文件原名", "頻率", "頻率文字", "日期", "廠商", "廠商1", "廠商2", "編號", "編號文字", "備註", "備註文字", "其他", "名稱", "作業",
        "年", "西元年", "月", "月0", "日", "日0", "迄年", "迄西元年", "迄月", "迄月0", "迄日", "迄日0" };

    Control BuildListsPage()
    {
        var page = Cols(32, 68);

        var left = new Card { Dock = DockStyle.Fill };
        var lr = Rows(SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
        var bAddList = B("＋ 新增名單", BtnKind.Primary);
        lr.Controls.Add(CardHeader(new Control[] { L("名單", LabelRole.Title) }, new Control[] { bAddList }), 0, 0);
        listList = new NiceList { Font = UI.BaseFont };
        listList.TitleOf = o => ((NameList)o).Name;
        listList.SubOf = o => { var l = (NameList)o; return NameList.KindName(l.Kind) + (l.Note.Length > 0 ? "・" + l.Note : ""); };
        listList.BadgeOf = o => ((NameList)o).Items.Count + " 個";
        lr.Controls.Add(listList, 0, 1);
        var lb = Flow();
        var bEditList = B("編輯", BtnKind.Soft); var bDelList = B("刪除", BtnKind.Danger);
        var bUp = B("↑", BtnKind.Ghost); var bDown = B("↓", BtnKind.Ghost);
        Tip(bUp, "往上移"); Tip(bDown, "往下移");
        lb.Controls.AddRange(new Control[] { bEditList, bDelList, bUp, bDown });
        lr.Controls.Add(lb, 0, 2);
        left.Controls.Add(lr);
        page.Controls.Add(left, 0, 0);

        var right = new Card { Dock = DockStyle.Fill, AccentStrip = true };
        var rr = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent);
        lblListName = L("", LabelRole.Title); chipListKind = new Chip("", ChipKind.Info);
        var bEditList2 = B("編輯名單", BtnKind.Ghost);
        rr.Controls.Add(CardHeader(new Control[] { lblListName, chipListKind }, new Control[] { bEditList2 }), 0, 0);
        lblListNote = L("", LabelRole.Dim); lblListNote.MaximumSize = new Size(G.P(760), 0);
        rr.Controls.Add(lblListNote, 0, 1);
        lblListHint = L("", LabelRole.Dim); lblListHint.MaximumSize = new Size(G.P(760), 0);
        rr.Controls.Add(lblListHint, 0, 2);
        var ib = Flow();
        var bAddWord = B("＋ 新增", BtnKind.Primary); var bEditWord = B("編輯", BtnKind.Soft); var bDelWord = B("刪除", BtnKind.Danger); var bMove = B("移到其他名單…", BtnKind.Ghost);
        ib.Controls.AddRange(new Control[] { bAddWord, bEditWord, bDelWord, bMove });
        rr.Controls.Add(ib, 0, 3);
        wordGrid = NewGrid(true);
        wordGrid.Columns.Add(TextCol("Word", "內容（點兩下可以直接修改）", 100, false));
        rr.Controls.Add(wordGrid, 0, 4);
        right.Controls.Add(rr);
        page.Controls.Add(right, 1, 0);

        FillListList(0);
        listList.SelectedIndexChanged += (s, e) => ShowList();
        listList.DoubleClick += (s, e) => Safe(EditList);
        bAddList.Click += (s, e) => Safe(() => {
            var l = ListDialog(null); if (l == null) return;
            cfg.Lists.Add(l); FillListList(cfg.Lists.Count - 1); Changed();
        });
        bEditList.Click += (s, e) => Safe(EditList);
        bEditList2.Click += (s, e) => Safe(EditList);
        bDelList.Click += (s, e) => Safe(() => {
            var l = CurList(); if (l == null) return;
            string warn = "";
            if (l.Kind == NameList.KindField && cfg.Formats.Exists(f => f.Template.Contains("{" + l.Name + "}")))
                warn = "\r\n有檔名格式用到〔" + l.Name + "〕，刪除後那個位置會變成空的。";
            if (!UI.Confirm(this, "刪除名單", "要刪除「" + l.Name + "」和裡面的 " + l.Items.Count + " 個內容嗎？" + warn, "刪除", "取消")) return;
            int i = cfg.Lists.IndexOf(l); cfg.Lists.Remove(l);
            FillListList(Math.Min(i, cfg.Lists.Count - 1)); Changed();
        });
        EventHandler move = (s, e) => {
            var l = CurList(); if (l == null) return;
            int i = cfg.Lists.IndexOf(l), j = i + (s == bUp ? -1 : 1);
            if (j < 0 || j >= cfg.Lists.Count) return;
            cfg.Lists[i] = cfg.Lists[j]; cfg.Lists[j] = l;
            FillListList(j); Changed();
        };
        bUp.Click += move; bDown.Click += move;
        bAddWord.Click += (s, e) => Safe(() => {
            var l = CurList(); if (l == null) { UI.Info(this, "新增", "請先新增或選一個名單。"); return; }
            var v = UI.Prompt(this, "新增到「" + l.Name + "」", "可以一次加很多個，一行一個。", new[] { "內容" }, new[] { "" }, new[] { true },
                              vals => vals[0].Trim().Length == 0 ? "內容不能是空的。" : null);
            if (v == null) return;
            var dup = new List<string>();
            foreach (var line in v[0].Split('\n'))
            {
                var w = line.Trim(); if (w.Length == 0) continue;
                if (l.Items.Contains(w)) { dup.Add(w); continue; }
                l.Items.Add(w);
            }
            ShowList(); listList.Invalidate(); Changed();
            if (dup.Count > 0) UI.Info(this, "新增", "這些已經在名單裡了，沒有重複加入：\r\n" + string.Join("、", dup.ToArray()));
        });
        bEditWord.Click += (s, e) => Safe(() => {
            var l = CurList(); if (l == null || wordGrid.CurrentRow == null) return;
            int i = wordGrid.CurrentRow.Index; string old = l.Items[i];
            var v = UI.Prompt(this, "編輯", null, new[] { "內容" }, new[] { old }, null,
                              vals => vals[0].Length == 0 ? "內容不能是空的。" : (vals[0] != old && l.Items.Contains(vals[0]) ? "「" + vals[0] + "」已經在名單裡了。" : null));
            if (v == null) return;
            l.Items[i] = v[0]; ShowList(); Changed();
        });
        bDelWord.Click += (s, e) => {
            var l = CurList(); if (l == null || wordGrid.SelectedRows.Count == 0) return;
            var del = new List<string>(); foreach (DataGridViewRow gr in wordGrid.SelectedRows) del.Add((string)gr.Tag);
            if (!UI.Confirm(this, "刪除", "要從「" + l.Name + "」刪除 " + string.Join("、", del.ToArray()) + " 嗎？", "刪除", "取消")) return;
            foreach (var w in del) l.Items.Remove(w);
            ShowList(); listList.Invalidate(); Changed();
        };
        bMove.Click += (s, e) => Safe(() => {
            var l = CurList(); if (l == null || wordGrid.SelectedRows.Count == 0) return;
            var others = cfg.Lists.Where(x => x != l).ToList();
            if (others.Count == 0) { UI.Info(this, "移到其他名單", "目前只有一個名單。"); return; }
            int ch = UI.Choose(this, "移到其他名單", "要把選取的內容移到哪個名單？", others.Select(x => x.Name + "（" + NameList.KindName(x.Kind) + "）").Concat(new[] { "取消" }).ToArray());
            if (ch < 0 || ch >= others.Count) return;
            foreach (DataGridViewRow gr in wordGrid.SelectedRows) { var w = (string)gr.Tag; l.Items.Remove(w); if (!others[ch].Items.Contains(w)) others[ch].Items.Add(w); }
            ShowList(); listList.Invalidate(); Changed();
        });
        wordGrid.CellEndEdit += (s, e) => {
            var l = CurList(); if (l == null) return;
            var gr = wordGrid.Rows[e.RowIndex]; string old = (string)gr.Tag;
            string v = Convert.ToString(gr.Cells[0].Value ?? "").Trim();
            if (v == old) return;
            if (v.Length == 0 || l.Items.Contains(v)) { gr.Cells[0].Value = old; if (v.Length > 0) UI.Info(this, "名單", "「" + v + "」已經在名單裡了。"); return; }
            l.Items[l.Items.IndexOf(old)] = v; gr.Tag = v; Changed();
        };
        return page;
    }

    NameList CurList() { return listList.SelectedItem as NameList; }

    void FillListList(int select)
    {
        listList.BeginUpdate(); listList.Items.Clear();
        foreach (var l in cfg.Lists) listList.Items.Add(l);
        listList.EndUpdate();
        if (cfg.Lists.Count > 0) listList.SelectedIndex = Math.Max(0, Math.Min(select, cfg.Lists.Count - 1));
        ShowList();
    }

    void ShowList()
    {
        var l = CurList();
        lblListName.Text = l == null ? "（還沒有名單，按左邊「＋ 新增名單」）" : l.Name;
        chipListKind.Text = l == null ? "" : NameList.KindName(l.Kind); chipListKind.Visible = l != null;
        lblListNote.Text = l == null ? "" : l.Note; lblListNote.Visible = l != null && l.Note.Length > 0;
        string hint = "";
        if (l != null)
        {
            foreach (var k in ListKinds) if (k[0] == l.Kind) hint = k[2];
            if (l.Kind == NameList.KindField)
            {
                bool used = cfg.Formats.Exists(f => f.Template.Contains("{" + l.Name + "}"));
                hint += used ? "\r\n✔ 已經有檔名格式用到〔" + l.Name + "〕。" : "\r\n⚠ 還沒有檔名格式用到〔" + l.Name + "〕，這些字目前會從新檔名消失。請到「檔名格式」點「更多…」插入〔" + l.Name + "〕。";
            }
        }
        lblListHint.Text = hint;
        wordGrid.Rows.Clear();
        if (l != null) foreach (var w in l.Items) { int i = wordGrid.Rows.Add(w); wordGrid.Rows[i].Tag = w; }
        UI.StyleGrid(wordGrid);
        if (chipListKind.Parent != null) { chipListKind.BackColor = Theme.Card; chipListKind.Invalidate(); }
    }

    void EditList()
    {
        var l = CurList(); if (l == null) return;
        string oldName = l.Name;
        var r = ListDialog(l); if (r == null) return;
        // 自訂欄位改名時，檔名格式和學到的規則裡的〔舊名〕也一起改
        if (oldName != r.Name && l.Kind == NameList.KindField)
        {
            foreach (var f in cfg.Formats) f.Template = f.Template.Replace("{" + oldName + "}", "{" + r.Name + "}");
            foreach (var x in cfg.LearnedRules) x.Template = x.Template.Replace("{" + oldName + "}", "{" + r.Name + "}");
        }
        l.Name = r.Name; l.Kind = r.Kind; l.Note = r.Note;
        FillListList(cfg.Lists.IndexOf(l)); FillLearnGrid(); Changed();
    }

    // 新增／編輯名單：名稱、用途、註解
    NameList ListDialog(NameList existing)
    {
        using (var f = UI.NewDialog(this, existing == null ? "新增名單" : "編輯名單"))
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, G.P(560)));
            int r = 0;
            var name = new InputBox(existing != null ? existing.Name : "");
            t.Controls.Add(new TLabel("名單名稱") { Margin = new Padding(G.P(3), G.P(10), G.P(12), G.P(3)) }, 0, r); t.Controls.Add(name, 1, r++);
            var kinds = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
            var radios = new List<TRadio>();
            foreach (var k in ListKinds)
            {
                var rb = new TRadio(k[1]) { Tag = k[0], Checked = (existing != null ? existing.Kind : NameList.KindCompany) == k[0] };
                radios.Add(rb); kinds.Controls.Add(rb);
                kinds.Controls.Add(new TLabel(k[2], LabelRole.Dim) { MaximumSize = new Size(G.P(520), 0), Margin = new Padding(G.P(32), 0, G.P(3), G.P(6)) });
            }
            t.Controls.Add(new TLabel("用途") { Margin = new Padding(G.P(3), G.P(10), G.P(12), G.P(3)) }, 0, r); t.Controls.Add(kinds, 1, r++);
            var note = new InputBox(existing != null ? existing.Note : "", true) { Height = G.P(80) };
            t.Controls.Add(new TLabel("註解") { Margin = new Padding(G.P(3), G.P(10), G.P(12), G.P(3)) }, 0, r); t.Controls.Add(note, 1, r++);
            t.Controls.Add(new TLabel("註解可以不填，有填才會顯示。", LabelRole.Dim), 1, r++);
            var err = new TLabel("", LabelRole.Danger); t.Controls.Add(err, 1, r++);
            var flow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new FlatBtn("儲存", BtnKind.Primary); var cancel = new FlatBtn("取消");
            flow.Controls.Add(cancel); flow.Controls.Add(ok);
            t.Controls.Add(flow, 0, r); t.SetColumnSpan(flow, 2);
            f.Controls.Add(t);
            NameList result = null;
            ok.Click += (s, e) => {
                string n = name.Text.Trim();
                string kind = radios.First(x => x.Checked).Tag as string;
                if (n.Length == 0) { err.Text = "名單名稱不能是空的。"; return; }
                if (cfg.Lists.Exists(x => x != existing && x.Name == n)) { err.Text = "已經有叫「" + n + "」的名單了。"; return; }
                if (kind == NameList.KindField && (ReservedTokens.Contains(n) || n.IndexOfAny(new[] { '{', '}', '〔', '〕' }) >= 0))
                { err.Text = "自訂欄位的名稱不能是「" + n + "」（和內建欄位重複或有括號），請換一個名字。"; return; }
                result = new NameList { Name = n, Kind = kind, Note = note.Text.Trim(), Items = existing != null ? existing.Items : new List<string>() };
                f.Close();
            };
            cancel.Click += (s, e) => f.Close();
            f.CancelButton = cancel;
            UI.Apply(f);
            foreach (Control c in t.Controls) { var tl = c as TLabel; if (tl != null) tl.ApplyFont(Font); }
            foreach (Control c in kinds.Controls) { var tl = c as TLabel; if (tl != null) tl.ApplyFont(Font); }
            f.Shown += (s, e) => name.Box.Focus();
            f.ShowDialog(this);
            return result;
        }
    }

    // ================= 分頁 5：檔名格式 =================
    static readonly string[] QuickTokens = { "公司", "文件", "頻率", "日期", "廠商1", "廠商2", "編號", "備註", "文件原名" };

    Control BuildFormatPage()
    {
        var page = Cols(30, 70);

        var left = new Card { Dock = DockStyle.Fill };
        var lr = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
        var bAdd = B("＋ 新增格式", BtnKind.Primary);
        lr.Controls.Add(CardHeader(new Control[] { L("格式", LabelRole.Title) }, new Control[] { bAdd }), 0, 0);
        lr.Controls.Add(new TLabel("每一組格式可以指定給「名稱對照」裡的分類，沒指定的文件用「預設格式」。", LabelRole.Dim) { MaximumSize = new Size(G.P(330), 0) }, 0, 1);
        fmtList = new NiceList { Font = UI.BaseFont };
        fmtList.TitleOf = o => ((NameFormat)o).Name;
        fmtList.BadgeOf = o => ((NameFormat)o).IsDefault ? "預設" : "";
        fmtList.SubOf = o => {
            var f = (NameFormat)o;
            if (f.IsDefault) return "沒有指定格式的文件都用這個";
            return f.Categories.Count == 0 ? "還沒指定分類（目前不會用到）" : "套用到：" + string.Join("、", f.Categories.ToArray());
        };
        lr.Controls.Add(fmtList, 0, 2);
        var lb = Flow();
        var bCopy = B("複製", BtnKind.Soft); var bRename = B("改名", BtnKind.Soft); var bDel = B("刪除", BtnKind.Danger);
        lb.Controls.AddRange(new Control[] { bCopy, bRename, bDel });
        lr.Controls.Add(lb, 0, 3);
        left.Controls.Add(lr);
        page.Controls.Add(left, 0, 0);

        var right = new Card { Dock = DockStyle.Fill, AccentStrip = true, Padding = new Padding(G.P(18), G.P(12), G.P(8), G.P(12)) };
        fmtEditor = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var ed = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, G.P(12), 0) };
        ed.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int r = 0;
        lblFmtTitle = L("", LabelRole.Title);
        ed.Controls.Add(lblFmtTitle, 0, r++);

        ed.Controls.Add(Section("① 套用到哪些分類"), 0, r++);
        fmtCats = Flow();
        ed.Controls.Add(fmtCats, 0, r++);
        lblFmtCatsHint = L("", LabelRole.Dim); lblFmtCatsHint.MaximumSize = new Size(G.P(760), 0);
        ed.Controls.Add(lblFmtCatsHint, 0, r++);

        ed.Controls.Add(Section("② 新檔名長怎樣"), 0, r++);
        tbFmtTpl = new InputBox(); tbFmtTpl.Dock = DockStyle.Top;
        ed.Controls.Add(tbFmtTpl, 0, r++);
        var tokens = Flow();
        tokens.Controls.Add(L("點一下插入：", LabelRole.Dim));
        foreach (var tk in QuickTokens)
        {
            var b = B("〔" + tk + "〕", BtnKind.Soft); string tok = "〔" + tk + "〕";
            b.Click += (s, e) => InsertToken(tbFmtTpl, tok);
            tokens.Controls.Add(b);
        }
        var more = B("更多…", BtnKind.Ghost);
        more.Click += (s, e) => {
            var menu = new ContextMenuStrip();
            foreach (var fn in Core.FieldNames(cfg))
            { string tok = "〔" + fn + "〕"; menu.Items.Add(tok + "　（自訂欄位）", null, (s2, e2) => InsertToken(tbFmtTpl, tok)); }
            if (Core.FieldNames(cfg).Count > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (var t in new[] { "廠商", "頻率文字", "編號文字", "備註文字", "年", "西元年", "月", "月0", "日", "日0", "迄月", "迄月0", "迄日", "迄日0" })
            { string tok = "〔" + t + "〕"; menu.Items.Add(tok, null, (s2, e2) => InsertToken(tbFmtTpl, tok)); }
            UI.StyleMenu(menu);
            menu.Show(more, new Point(0, more.Height));
        };
        tokens.Controls.Add(more);
        ed.Controls.Add(tokens, 0, r++);
        ed.Controls.Add(new TLabel("〔 〕裡的是會自動帶入的欄位，其他文字（例如 -、_、固定的字）照樣保留。欄位沒有值時（例如沒有廠商），它前面的「-」「_」會自動拿掉。", LabelRole.Dim) { MaximumSize = new Size(G.P(760), 0) }, 0, r++);

        ed.Controls.Add(Section("③ 日期寫法"), 0, r++);
        fmtDateFlow = Flow(); fmtDateFlow.FlowDirection = FlowDirection.TopDown; fmtDateFlow.WrapContents = false;
        foreach (var st in DateStyle.All)
        {
            var rb = new TRadio(st.Name + "　　（年月：" + Sample(st.Month) + "　單一天：" + Sample(st.Single) + "）") { Tag = st.Id };
            rb.CheckedChanged += (s, e) => { if (((TRadio)s).Checked) PickDateStyle((string)((TRadio)s).Tag); };
            fmtDateFlow.Controls.Add(rb);
        }
        var rbCustom = new TRadio("自訂（自己設定每一種日期的寫法）") { Tag = "custom" };
        rbCustom.CheckedChanged += (s, e) => { if (rbCustom.Checked) PickDateStyle("custom"); };
        fmtDateFlow.Controls.Add(rbCustom);
        ed.Controls.Add(fmtDateFlow, 0, r++);
        fmtCustomDates = Flow(); fmtCustomDates.FlowDirection = FlowDirection.TopDown; fmtCustomDates.WrapContents = false; fmtCustomDates.Visible = false;
        fmtDateBoxes = new InputBox[5];
        string[] dl = { "日期區間", "跨年區間", "年月", "單一天", "只有年" };
        for (int i = 0; i < 5; i++)
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = G.P(700), Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, G.P(90))); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, G.P(560)));
            fmtDateBoxes[i] = new InputBox();
            row.Controls.Add(L(dl[i]) , 0, 0); row.Controls.Add(fmtDateBoxes[i], 1, 0);
            fmtCustomDates.Controls.Add(row);
            int k = i; fmtDateBoxes[i].Box.TextChanged += (s, e) => { if (!fmtLoading) { var f = CurFmt(); if (f != null) { SetDatePart(f, k, fmtDateBoxes[k].Text); Changed(); } } };
        }
        fmtCustomDates.Controls.Add(new TLabel("日期代碼：〔年〕民國年　〔西元年〕　〔月〕5　〔月0〕05　〔日〕1　〔日0〕01；結束日前面加「迄」，例如〔迄月〕〔迄日0〕", LabelRole.Dim) { MaximumSize = new Size(G.P(760), 0) });
        ed.Controls.Add(fmtCustomDates, 0, r++);
        cbFmtMonth = new TCheck("日期區間剛好是整個月（1 號到月底）時，只寫年月");
        cbFmtMonth.CheckedChanged += (s, e) => { if (!fmtLoading) { var f = CurFmt(); if (f != null) { f.UseMonthFormat = cbFmtMonth.Checked; Changed(); } } };
        ed.Controls.Add(cbFmtMonth, 0, r++);

        ed.Controls.Add(Section("④ 預覽"), 0, r++);
        fmtPreview = Flow(); fmtPreview.FlowDirection = FlowDirection.TopDown; fmtPreview.WrapContents = false;
        ed.Controls.Add(fmtPreview, 0, r++);

        fmtEditor.Controls.Add(ed);
        right.Controls.Add(fmtEditor);
        page.Controls.Add(right, 1, 0);

        FillFmtList(0);
        fmtList.SelectedIndexChanged += (s, e) => ShowFormat();
        tbFmtTpl.Box.TextChanged += (s, e) => {
            if (fmtLoading) return; var f = CurFmt(); if (f == null) return;
            f.Template = RuleEditor.ToTemplate(tbFmtTpl.Text); Changed();
        };
        bAdd.Click += (s, e) => Safe(() => {
            var v = UI.Prompt(this, "新增格式", "取一個好認的名字，例如「檢查表格式」。建立後再勾選要套用的分類。", new[] { "格式名稱" }, new[] { "" }, null, vals => ValidateFmtName(vals[0], null));
            if (v == null) return;
            var f = cfg.DefaultFormat().Copy(); f.Name = v[0]; f.IsDefault = false; f.Categories.Clear();
            cfg.Formats.Add(f); FillFmtList(cfg.Formats.Count - 1); Changed();
        });
        bCopy.Click += (s, e) => Safe(() => {
            var cur = CurFmt(); if (cur == null) return;
            var v = UI.Prompt(this, "複製格式", "複製「" + cur.Name + "」成新的格式。", new[] { "新格式名稱" }, new[] { cur.Name + " 副本" }, null, vals => ValidateFmtName(vals[0], null));
            if (v == null) return;
            var f = cur.Copy(); f.Name = v[0]; f.IsDefault = false; f.Categories.Clear();
            cfg.Formats.Add(f); FillFmtList(cfg.Formats.Count - 1); Changed();
        });
        bRename.Click += (s, e) => Safe(() => {
            var cur = CurFmt(); if (cur == null) return;
            var v = UI.Prompt(this, "格式改名", null, new[] { "格式名稱" }, new[] { cur.Name }, null, vals => ValidateFmtName(vals[0], cur));
            if (v == null) return;
            cur.Name = v[0]; FillFmtList(cfg.Formats.IndexOf(cur)); Changed();
        });
        bDel.Click += (s, e) => {
            var cur = CurFmt(); if (cur == null) return;
            if (cur.IsDefault) { UI.Info(this, "刪除格式", "預設格式不能刪除，沒有指定格式的文件都會用它。"); return; }
            if (!UI.Confirm(this, "刪除格式", "要刪除「" + cur.Name + "」嗎？套用到它的分類會改用預設格式。", "刪除", "取消")) return;
            int i = cfg.Formats.IndexOf(cur); cfg.Formats.Remove(cur); FillFmtList(Math.Max(0, i - 1)); Changed();
        };
        return page;
    }

    static TLabel Section(string text)
    {
        return new TLabel(text, LabelRole.Accent) { Margin = new Padding(G.P(3), G.P(18), G.P(3), G.P(4)), Font = new Font(UI.BaseFont, FontStyle.Bold) };
    }
    static string Sample(string tpl)
    {
        return tpl.Replace("{年}", "115").Replace("{西元年}", "2026").Replace("{月}", "5").Replace("{月0}", "05").Replace("{日}", "1").Replace("{日0}", "01");
    }
    static void InsertToken(InputBox box, string tok)
    {
        int at = box.Box.SelectionStart;
        box.Box.Text = box.Box.Text.Remove(at, box.Box.SelectionLength).Insert(at, tok);
        box.Box.SelectionStart = at + tok.Length; box.Box.Focus();
    }
    static void SetDatePart(NameFormat f, int k, string friendly)
    {
        string v = RuleEditor.ToTemplate(friendly);
        switch (k) { case 0: f.RangeFormat = v; break; case 1: f.CrossYearFormat = v; break; case 2: f.MonthFormat = v; break; case 3: f.SingleFormat = v; break; default: f.YearFormat = v; break; }
    }
    string ValidateFmtName(string name, NameFormat self)
    {
        if (name.Length == 0) return "格式名稱不能是空的。";
        foreach (var f in cfg.Formats) if (f != self && f.Name == name) return "已經有叫「" + name + "」的格式了。";
        return null;
    }

    NameFormat CurFmt() { return fmtList.SelectedItem as NameFormat; }

    void FillFmtList(int select)
    {
        fmtList.BeginUpdate(); fmtList.Items.Clear();
        foreach (var f in cfg.Formats) fmtList.Items.Add(f);
        fmtList.EndUpdate();
        if (cfg.Formats.Count > 0) fmtList.SelectedIndex = Math.Max(0, Math.Min(select, cfg.Formats.Count - 1));
        ShowFormat();
    }

    void ShowFormat()
    {
        var f = CurFmt(); if (f == null || fmtCats == null) return;
        fmtLoading = true;
        lblFmtTitle.Text = f.Name + (f.IsDefault ? "（預設格式）" : "");
        fmtCats.Controls.Clear();
        if (f.IsDefault)
            lblFmtCatsHint.Text = "預設格式不用指定分類：沒有指定格式的分類，以及不在任何分類裡的文件，都會用這個格式。";
        else
        {
            foreach (var c in cfg.Categories)
            {
                var cb = new TCheck(c.Name, f.Categories.Contains(c.Name)) { Font = Font };
                string cat = c.Name;
                cb.CheckedChanged += (s, e) => {
                    if (fmtLoading) return;
                    if (cb.Checked)
                    {
                        foreach (var o in cfg.Formats) if (o != f) o.Categories.Remove(cat);   // 一個分類只能用一種格式
                        if (!f.Categories.Contains(cat)) f.Categories.Add(cat);
                    }
                    else f.Categories.Remove(cat);
                    fmtList.Invalidate(); ShowFormatHint(f); Changed();
                };
                fmtCats.Controls.Add(cb);
            }
            ShowFormatHint(f);
        }
        tbFmtTpl.Text = Core.FriendlyTemplate(f.Template);
        foreach (Control c in fmtDateFlow.Controls) { var rb = c as TRadio; if (rb != null) rb.Checked = (string)rb.Tag == (DateStyle.Find(f.DateStyleId) != null ? f.DateStyleId : "custom"); }
        string[] parts = { f.RangeFormat, f.CrossYearFormat, f.MonthFormat, f.SingleFormat, f.YearFormat };
        for (int i = 0; i < 5; i++) fmtDateBoxes[i].Text = Core.FriendlyTemplate(parts[i]);
        fmtCustomDates.Visible = DateStyle.Find(f.DateStyleId) == null;
        cbFmtMonth.Checked = f.UseMonthFormat;
        fmtLoading = false;
        UI.Apply(fmtEditor.Parent); ApplyFonts(fmtEditor);
        UpdateFormatPreview();
    }

    void ShowFormatHint(NameFormat f)
    {
        var taken = new List<string>();
        foreach (var c in cfg.Categories)
            foreach (var o in cfg.Formats) if (o != f && !o.IsDefault && o.Categories.Contains(c.Name)) taken.Add(c.Name + "→" + o.Name);
        lblFmtCatsHint.Text = (cfg.Categories.Count == 0 ? "還沒有分類，請先到「名稱對照」新增分類。" : "一個分類只能用一種格式，勾選後會從其他格式移過來。") +
                              (taken.Count > 0 ? "\r\n目前其他格式使用中：" + string.Join("　", taken.ToArray()) : "");
    }

    void PickDateStyle(string id)
    {
        if (fmtLoading) return;
        var f = CurFmt(); if (f == null) return;
        var st = DateStyle.Find(id);
        if (st != null) f.ApplyStyle(st); else f.DateStyleId = "custom";
        fmtCustomDates.Visible = st == null;
        fmtLoading = true;
        string[] parts = { f.RangeFormat, f.CrossYearFormat, f.MonthFormat, f.SingleFormat, f.YearFormat };
        for (int i = 0; i < 5; i++) fmtDateBoxes[i].Text = Core.FriendlyTemplate(parts[i]);
        fmtLoading = false;
        Changed();
    }

    void UpdateFormatPreview()
    {
        var f = CurFmt(); if (f == null || fmtPreview == null) return;
        var map = Core.BuildMap(cfg);
        var samples = new List<string>();
        foreach (var r in rows)
        {
            var p = Core.Process(r.Stem, cfg, map);
            if (p.Ok && p.UsedRule == null && p.Format == f) samples.Add(r.Stem);
            if (samples.Count >= 6) break;
        }
        bool fromList = samples.Count > 0;
        if (!fromList)
        {
            // 清單裡沒有這個格式的檔案，就用分類裡的名稱自己造幾個例子
            var docs = new List<string>();
            foreach (var c in cfg.Categories)
                if ((f.IsDefault && FormatsUsing(c.Name).Count == 0) || f.Categories.Contains(c.Name))
                    foreach (var it in c.Items) if (docs.Count < 3) docs.Add(it.From);
            if (docs.Count == 0) docs.Add(f.IsDefault ? "入場申請單" : "檢查表");
            string[] dates = { "1150501-0531", "1150510-0520", "1150320", "11504" };
            for (int i = 0; i < docs.Count; i++)
            {
                samples.Add("甲公司-" + docs[i] + dates[i % dates.Length] + "(乙公司)");
                if (i == 0) samples.Add("甲公司-" + docs[i] + "(月)" + dates[3]);
            }
        }
        var c2 = cfg.Clone(); c2.LearnedRules = new List<Learned>();
        fmtPreview.SuspendLayout(); fmtPreview.Controls.Clear();
        fmtPreview.Controls.Add(L(fromList ? "清單裡用這個格式的檔案：" : "範例（清單裡還沒有用這個格式的檔案）：", LabelRole.Dim));
        foreach (var s in samples)
        {
            var p = Core.Process(s, c2, map);
            var line = Flow(); line.WrapContents = false;
            line.Controls.Add(L(s, LabelRole.Dim));
            line.Controls.Add(L("→", LabelRole.Accent));
            line.Controls.Add(p.Ok ? (Control)L(p.Format == f ? p.Final : p.Final + "（這個文件用的是「" + p.Format.Name + "」）", LabelRole.Text) : L("無法辨識：" + p.Error, LabelRole.Danger));
            fmtPreview.Controls.Add(line);
        }
        fmtPreview.ResumeLayout();
        UI.Apply(fmtEditor.Parent); ApplyFonts(fmtPreview);
    }

    // ================= 分頁 6：外觀 =================
    Control BuildLookPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var mode = new Card { Dock = DockStyle.Top, AutoSize = true };
        var mr = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize); mr.AutoSize = true;
        mr.Controls.Add(L("顯示模式", LabelRole.Title), 0, 0);
        mr.Controls.Add(L("「跟隨系統」會照 Windows 設定裡的亮色／暗色自動切換。", LabelRole.Dim), 0, 1);
        modeTiles = Flow();
        mr.Controls.Add(modeTiles, 0, 2);
        mode.Controls.Add(mr);
        stack.Controls.Add(mode, 0, 0);

        var color = new Card { Dock = DockStyle.Top, AutoSize = true };
        var cr = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize); cr.AutoSize = true;
        cr.Controls.Add(L("主題色", LabelRole.Title), 0, 0);
        cr.Controls.Add(L("按鈕、選取的項目和重點文字會用這個顏色。也可以用色環自己挑。", LabelRole.Dim), 0, 1);
        colorTiles = Flow();
        cr.Controls.Add(colorTiles, 0, 2);
        color.Controls.Add(cr);
        stack.Controls.Add(color, 0, 1);

        var about = new Card { Dock = DockStyle.Top, AutoSize = true };
        var ar = Rows(SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize); ar.AutoSize = true;
        var bCheck = B("檢查更新", BtnKind.Outline);
        lblAboutVer = L("目前版本 v" + Updater.CurrentText, LabelRole.Dim);
        ar.Controls.Add(CardHeader(new Control[] { L("關於與更新紀錄", LabelRole.Title), lblAboutVer }, new Control[] { bCheck }), 0, 0);
        ar.Controls.Add(L("版本號是「大型.中型.小型」：介面或架構大改版時第一碼加 1，新增功能時第二碼加 1，修正或微調時第三碼加 1。", LabelRole.Dim), 0, 1);
        var log = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, G.P(8), 0, 0) };
        log.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); log.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); log.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int i = 0;
        foreach (var e in Changelog.Entries)
        {
            log.Controls.Add(new TLabel("v" + e[0], e[0] == Updater.CurrentText ? LabelRole.Accent : LabelRole.Text) { Font = new Font(UI.BaseFont, FontStyle.Bold) }, 0, i);
            log.Controls.Add(new Chip(e[1], e[1] == "大型" ? ChipKind.Accent : e[1] == "中型" ? ChipKind.Info : ChipKind.Neutral), 1, i);
            log.Controls.Add(new TLabel(e[2], LabelRole.Dim) { MaximumSize = new Size(G.P(880), 0) }, 2, i);
            i++;
        }
        ar.Controls.Add(log, 0, 2);
        about.Controls.Add(ar);
        stack.Controls.Add(about, 0, 2);
        bCheck.Click += (s, e) => CheckUpdate(true);

        page.Controls.Add(stack);
        BuildLookTiles();
        return page;
    }

    void BuildLookTiles()
    {
        modeTiles.Controls.Clear();
        foreach (var m in new[] { new[] { "system", "◐", "跟隨系統", "目前：" + (Theme.SystemIsDark() ? "暗色" : "亮色") }, new[] { "light", "☀", "亮色", "白色背景" }, new[] { "dark", "☾", "暗色", "深色背景" } })
        {
            var t = new Tile { Glyph = m[1], Title = m[2], Sub = m[3], Size = new Size(G.P(220), G.P(70)), Selected = cfg.ThemeMode == m[0], Font = Font };
            string id = m[0];
            t.Click += (s, e) => { cfg.ThemeMode = id; ApplyTheme(); };
            modeTiles.Controls.Add(t);
        }
        colorTiles.Controls.Clear();
        foreach (var p in Theme.Palettes)
        {
            var t = new Tile { Swatch = p.Accent, Title = p.Name, Size = new Size(G.P(170), G.P(58)), Selected = cfg.Accent == p.Name, Font = Font };
            string name = p.Name;
            t.Click += (s, e) => { cfg.Accent = name; ApplyTheme(); };
            colorTiles.Controls.Add(t);
        }
        var custom = Theme.ParseHex(cfg.CustomAccent);
        var ct = new Tile { Swatch = custom ?? Color.FromArgb(200, 200, 200), Title = "自訂顏色…", Sub = custom != null ? Theme.Hex(custom.Value) : "用色環挑選", Size = new Size(G.P(200), G.P(58)), Selected = cfg.Accent == "custom", Font = Font };
        ct.Click += (s, e) => {
            var c = UI.PickColor(this, Theme.Accent);
            if (c == null) return;
            cfg.CustomAccent = Theme.Hex(c.Value); cfg.Accent = "custom"; ApplyTheme();
        };
        colorTiles.Controls.Add(ct);
    }

    void ApplyTheme()
    {
        Theme.Load(cfg);
        btnThemeToggle.Text = Theme.Dark ? "☀" : "☾";
        SuspendLayout();
        BuildLookTiles();
        UI.Apply(this);
        ApplyFonts(this);
        ResumeLayout();
        FillGrid(); FillLearnGrid(); ShowCategory(); UpdateTest();
        Theme.StyleTitleBar(this);
        Invalidate(true);
        Changed();
    }

    // ================= 設定 =================
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
        bool fresh = c == null;
        if (c == null) c = new Settings();
        if (c.Rules == null) c.Rules = new List<Rule>();
        if (c.LearnedRules == null) c.LearnedRules = new List<Learned>();
        if (!fresh && c.Version < 2)
        {   // 很舊的版本：換成新的檔名格式，並補上新的對照
            c.NameTemplate = Settings.DefaultTemplate;
            c.WorkMap = MergeDefaultMap(c.WorkMap);
            if (string.IsNullOrEmpty(c.YearFormat)) c.YearFormat = "{年}年";
            c.Rules.RemoveAll(r => !r.Enabled && (r.Note ?? "").StartsWith("範例"));
            c.Version = 3;
        }
        else if (c.Version == 2)
        {   // 第 2 版：沒改過的檔名格式補上 {備註}
            if (c.NameTemplate == Settings.V2Template) c.NameTemplate = Settings.DefaultTemplate;
            if (string.IsNullOrEmpty(c.Remarks)) c.Remarks = Settings.DefaultRemarks;
            c.WorkMap = MergeDefaultMap(c.WorkMap);
            c.Version = 3;
        }
        if (c.Version < 4) Core.MigrateToV4(c);       // 第 4 版：分類、格式清單
        if (c.Version < 5) Core.MigrateToV5(c);       // 第 5 版：名單可以自己開
        foreach (var l in c.LearnedRules)            // 舊版學到的規則沒有記錄「情況」，用範例算出來
            if (string.IsNullOrEmpty(l.Signature)) { try { l.Signature = Core.SignatureOf(l.ExampleFrom, c) ?? Learned.AnyCase; } catch { l.Signature = Learned.AnyCase; } }
        return c;
    }
    static string MergeDefaultMap(string workMap)
    {
        var map = Core.ParseWorkMap(workMap);
        var sb = new StringBuilder(workMap ?? "");
        foreach (var kv in Core.ParseWorkMap(Settings.DefaultMap))
            if (!map.ContainsKey(kv.Key)) sb.Append('\n').Append(kv.Key).Append('=').Append(kv.Value);
        return sb.ToString();
    }
    static void SaveSettings(Settings c)
    {
        Directory.CreateDirectory(AppDir);
        using (var fs = File.Create(SettingsPath)) new XmlSerializer(typeof(Settings)).Serialize(fs, c);
    }

    // 設定有變：重新計算預覽、稍後存檔
    void Changed()
    {
        if (loading) return;
        RequestRefresh();
        saveTimer.Stop(); saveTimer.Start();
    }

    // ================= 動作 =================
    void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { UI.Info(this, "發生錯誤", "發生錯誤：\r\n" + ex.Message); }
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
        Core.UpdateRows(rows, cfg);
        FillGrid();
        UpdateTest();
        if (tabs.SelectedIndex == 4) { fmtList.Invalidate(); UpdateFormatPreview(); }
        if (tabs.SelectedIndex == 2) catList.Invalidate();
        if (tabs.SelectedIndex == 3) { listList.Invalidate(); ShowList(); }
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
            var st = gr.Cells["Status"].Style;
            if (r.Ok)
            {
                if (r.Manual) gr.DefaultCellStyle.BackColor = Theme.InfoSoft;
                st.ForeColor = r.Excluded ? Theme.TextDim : Theme.Success;
            }
            else if (r.Status == Core.StatusSame) { gr.DefaultCellStyle.ForeColor = Theme.TextDim; }
            else { gr.DefaultCellStyle.BackColor = Theme.DangerSoft; st.ForeColor = Theme.Danger; }
            st.SelectionForeColor = st.ForeColor.IsEmpty ? Theme.Text : st.ForeColor;
        }
        try { if (first >= 0 && first < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = first; } catch { }
        grid.ResumeLayout();
        grid.ClearSelection();
        UI.StyleGrid(grid);
        lblEmpty.Visible = rows.Count == 0;
        filling = false;
        UpdateCount();
    }

    void UpdateCount()
    {
        int ready = rows.FindAll(r => r.Ok && !r.Excluded).Count;
        int bad = rows.FindAll(r => !r.Ok && r.Status != Core.StatusSame).Count;
        chipTotal.Text = "共 " + rows.Count + " 個";
        chipReady.Text = "將改名 " + ready + " 個";
        chipBad.Text = "有問題 " + bad + " 個"; chipBad.Visible = bad > 0;
        btnRun.Enabled = ready > 0;
        btnRun.Text = ready > 0 ? "執行改名（" + ready + " 個）  →" : "執行改名  →";
        btnRun.Size = btnRun.GetPreferredSize(Size.Empty);
    }

    void RunRename()
    {
        Core.UpdateRows(rows, cfg);
        int n = rows.FindAll(r => r.Ok && !r.Excluded).Count;
        if (n == 0) { FillGrid(); return; }
        if (!UI.Confirm(this, "確認改名", "確定要將 " + n + " 個檔案改名嗎？\r\n改完之後可以按「↶ 上一步」復原。", "改名", "取消")) return;
        Core.RenameResult res;
        Cursor = Cursors.WaitCursor;
        try { res = Core.RenameAll(rows, LogDir); } finally { Cursor = Cursors.Default; }
        foreach (var r in rows.FindAll(x => x.Done)) { rows.Remove(r); paths.Remove(r.Path); }
        if (res.Ok > 0 && res.Log.Length > 0) { undoLogs.Add(res.Log); UpdateUndoButton(); }
        RefreshAll();
        string msg = "完成！成功改名 " + res.Ok + " 個檔案。";
        if (res.Fail > 0) msg += "\r\n有 " + res.Fail + " 個失敗（仍留在清單中，請看狀態欄）。";
        msg += "\r\n\r\n按錯了的話，按「↶ 上一步」就能全部改回來。";
        UI.Info(this, "改名完成", msg);
    }

    void UpdateUndoButton()
    {
        btnUndo.Enabled = undoLogs.Count > 0;
        if (undoLogs.Count > 0)
        {
            int n = Core.ReadLog(undoLogs[undoLogs.Count - 1]).Count;
            btnUndo.Text = "↶ 上一步（" + n + " 個）";
            Tip(btnUndo, "把剛才改名的 " + n + " 個檔案改回原本的名稱（Ctrl+Z）" + (undoLogs.Count > 1 ? "\r\n還可以再往前復原 " + (undoLogs.Count - 1) + " 次" : ""));
        }
        else btnUndo.Text = "↶ 上一步";
        btnUndo.Size = btnUndo.GetPreferredSize(Size.Empty);
    }

    // 上一步：復原最近一次「執行改名」，檔案回到清單裡
    void UndoRename()
    {
        if (undoLogs.Count == 0) return;
        string log = undoLogs[undoLogs.Count - 1];
        var items = Core.ReadLog(log);
        if (!UI.Confirm(this, "上一步", "要把剛才改名的 " + items.Count + " 個檔案改回原本的名稱嗎？", "改回去", "取消")) return;
        var res = Core.Undo(log);
        undoLogs.RemoveAt(undoLogs.Count - 1);
        var back = new List<string>();
        foreach (var it in items) if (File.Exists(it[0])) back.Add(it[0]);
        AddFiles(back);
        UpdateUndoButton();
        RefreshAll();
        string msg = "已改回 " + res[0] + " 個檔案，它們也回到清單裡了。";
        if (res[1] > 0) msg += "\r\n有 " + res[1] + " 個沒辦法改回（檔案已經被移動、改名，或原本的名字被佔用）。";
        UI.Info(this, "上一步", msg);
    }

    void RunUndoFromFile()
    {
        using (var d = new OpenFileDialog { Title = "選擇要還原的改名紀錄", Filter = "改名紀錄 (*.csv)|*.csv" })
        {
            if (Directory.Exists(LogDir)) d.InitialDirectory = LogDir;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (!UI.Confirm(this, "還原以前的改名", "要把這份紀錄裡的檔案全部改回原本的名稱嗎？\r\n" + d.FileName, "改回去", "取消")) return;
            var res = Core.Undo(d.FileName);
            undoLogs.Remove(d.FileName); UpdateUndoButton();
            string msg = "已還原 " + res[0] + " 個檔案。";
            if (res[1] > 0) msg += "\r\n跳過 " + res[1] + " 個（檔案已不存在、已被改動，或原檔名已被佔用）。";
            UI.Info(this, "還原", msg);
            RefreshAll();
        }
    }

    // ================= 範例學習 =================
    // 傳回 1 = 現在的規則會改成想要的結果；0 = 沒有學（保留舊規則）；-1 = 學不起來
    int LearnOne(string from, string to, bool showResult)
    {
        from = (from ?? "").Trim(); to = (to ?? "").Trim();
        string ext = Path.GetExtension(from);
        if (ext.Length > 1 && ext.Length <= 6 && to.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        { from = from.Substring(0, from.Length - ext.Length); to = to.Substring(0, to.Length - ext.Length); }

        var plan = Core.PlanLearn(from, to, cfg);
        var res = plan.Res;
        string msg;
        Action<string, LabelRole> show = (text, role) => {
            if (!showResult) return;
            lblLearn.Role = role; lblLearn.ForeColor = lblLearn.RoleColor(); lblLearn.Text = text;
        };
        switch (plan.Action)
        {
            case Core.LearnAction.Failed:
                show("✖ 學不起來：" + res.Error + "\r\n　 可以試試：在「名稱對照」或「名單」把公司或文件名稱加進去，或換一個比較完整的範例。", LabelRole.Danger);
                return -1;
            case Core.LearnAction.AlreadyWorks:
                show("✔ 不用再學：現在的設定本來就會改成「" + to + "」" +
                     (plan.Existing != null ? "（用的是「" + plan.Existing.DocKey + "／" + Core.DescribeSignature(plan.Existing.Signature) + "」這條規則）" : "（用的是檔名格式）"), LabelRole.Success);
                return 1;
            case Core.LearnAction.ReEnable:
                plan.Existing.Enabled = true;
                msg = "✔ 這條規則之前就學過了，只是暫停使用，已經重新啟用。";
                break;
            case Core.LearnAction.Conflict:
            {
                var old = plan.Existing;
                int ch = UI.Choose(this, "規則衝突",
                    "「" + old.DocKey + "」在同樣的情況（" + Core.DescribeSignature(old.Signature) + "）已經有一條規則，和這次的範例改法不一樣：\r\n\r\n" +
                    "【舊的】" + Core.FriendlyTemplate(old.Template) + "\r\n　　例：" + old.ExampleFrom + "  →  " + old.ExampleTo + "\r\n\r\n" +
                    "【新的】" + Core.FriendlyTemplate(res.Rule.Template) + "\r\n　　例：" + from + "  →  " + to + "\r\n\r\n要用哪一個？",
                    "用新的", "保留舊的", "兩個都留著（新的先暫停）");
                if (ch == 1 || ch < 0) { show("・保留原本的規則，這次的範例沒有學。", LabelRole.Dim); return 0; }
                int at = cfg.LearnedRules.IndexOf(old);
                if (ch == 0) { cfg.LearnedRules[at] = res.Rule; msg = "✔ 已經換成新的改法。"; }
                else
                {
                    res.Rule.Enabled = false; cfg.LearnedRules.Insert(at + 1, res.Rule);
                    show("✔ 兩條都保留了，新的那條目前暫停使用；之後可以在下面的清單切換要用哪一條。", LabelRole.Success);
                    FillLearnGrid(); Changed(); return 0;
                }
                break;
            }
            case Core.LearnAction.NewCase:
                cfg.LearnedRules.Insert(Math.Max(0, cfg.LearnedRules.IndexOf(plan.Existing)), res.Rule);   // 比較具體的情況放前面
                msg = "✔ 學會了！「" + res.Rule.DocKey + "」原本的規則保留，新規則只用在「" + Core.DescribeSignature(res.Rule.Signature) + "」的檔案：\r\n　 " + Core.FriendlyTemplate(res.Rule.Template);
                break;
            default:
                cfg.LearnedRules.Add(res.Rule);
                msg = "✔ 學會了！以後文件名稱是「" + res.Rule.DocKey + "」的檔案，新檔名會長這樣：\r\n　 " + Core.FriendlyTemplate(res.Rule.Template);
                break;
        }
        if (res.AddedCompany.Length > 0)
        {
            var cl = cfg.Lists.FirstOrDefault(x => x.Kind == NameList.KindCompany);
            if (cl == null) { cl = new NameList { Name = "公司名單", Kind = NameList.KindCompany }; cfg.Lists.Insert(0, cl); }
            if (!cl.Items.Contains(res.AddedCompany)) cl.Items.Add(res.AddedCompany);
            FillListList(cfg.Lists.IndexOf(cl));
            msg += "\r\n　 另外把「" + res.AddedCompany + "」加進了「" + cl.Name + "」名單。";
        }
        show(msg, LabelRole.Success);
        FillLearnGrid(); Changed();
        RefreshAll();
        return 1;
    }

    void LearnFromManual()
    {
        var targets = new List<FileRow>();
        foreach (DataGridViewRow gr in grid.SelectedRows) { var r = (FileRow)gr.Tag; if (r.Manual) targets.Add(r); }
        if (targets.Count == 0) targets = rows.FindAll(r => r.Manual);
        if (targets.Count == 0)
        {
            UI.Info(this, "把手動改的學起來", "清單裡沒有手動修改過的檔名。\r\n先在「新檔名」欄點兩下改成你要的樣子，再按這個按鈕。");
            return;
        }
        int ok = 0; var fails = new List<string>(); var kept = new List<string>();
        foreach (var r in targets)
        {
            int res = LearnOne(r.Stem, r.NewStem, false);
            if (res == 1) { ok++; r.Manual = false; }
            else if (res == 0) kept.Add(r.OldName);
            else fails.Add(r.OldName);
        }
        RefreshAll();
        string msg = "完成 " + ok + " 個（學會新規則，或現有規則本來就會這樣改），可以到「範例學習」頁查看。";
        if (kept.Count > 0) msg += "\r\n\r\n下面這些和舊規則衝突、你選了保留舊的，維持手動修改：\r\n" + string.Join("\r\n", kept.ToArray());
        if (fails.Count > 0) msg += "\r\n\r\n下面這些看不出規律，維持手動修改：\r\n" + string.Join("\r\n", fails.ToArray());
        UI.Info(this, "把手動改的學起來", msg);
    }

    void CheckEnableOverlap(DataGridViewRow gr)
    {
        var l = (Learned)gr.Tag;
        bool now = gr.Cells["Enabled"].Value is bool && (bool)gr.Cells["Enabled"].Value;
        if (now == l.Enabled) return;
        l.Enabled = now;
        if (l.Enabled)
            foreach (var o in cfg.LearnedRules)
            {
                if (o == l || !o.Enabled || o.DocKey != l.DocKey) continue;
                if (!(o.Signature == l.Signature || (Core.IsAnyCase(o.Signature) && Core.IsAnyCase(l.Signature)))) continue;
                int ch = UI.Choose(this, "規則重複",
                    "「" + l.DocKey + "」在「" + Core.DescribeSignature(l.Signature) + "」已經有另一條規則在使用：\r\n\r\n" +
                    "　正在使用：" + Core.FriendlyTemplate(o.Template) + "\r\n　你勾選的：" + Core.FriendlyTemplate(l.Template) + "\r\n\r\n同一種情況只能用一條，要用哪一條？",
                    "用我勾選的這條", "維持原本那條");
                if (ch == 0) o.Enabled = false; else l.Enabled = false;
                break;
            }
        BeginInvoke(new Action(FillLearnGrid));
    }

    void EditRule(Learned l)
    {
        string exampleSig = Core.SignatureOf(l.ExampleFrom, cfg);
        using (var dlg = new RuleEditor(l, cfg, exampleSig, Font))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var edited = dlg.Result;
            foreach (var o in cfg.LearnedRules)
            {
                if (o == l || !o.Enabled || !edited.Enabled || o.DocKey != edited.DocKey) continue;
                bool overlap = o.Signature == edited.Signature || (Core.IsAnyCase(o.Signature) && Core.IsAnyCase(edited.Signature));
                if (!overlap) continue;
                int ch = UI.Choose(this, "規則重複",
                    "已經有另一條規則也是用在「" + o.DocKey + "」、" + Core.DescribeSignature(o.Signature) + "：\r\n\r\n" +
                    "　另一條：" + Core.FriendlyTemplate(o.Template) + "\r\n　這一條：" + Core.FriendlyTemplate(edited.Template) + "\r\n\r\n同一種情況只能用一條，要怎麼處理？",
                    "用這一條（另一條暫停使用）", "回去再改", "取消");
                if (ch == 0) { o.Enabled = false; break; }
                if (ch == 1) EditRule(l);
                return;
            }
            l.Enabled = edited.Enabled; l.DocKey = edited.DocKey; l.Signature = edited.Signature; l.Template = edited.Template;
            l.ExampleFrom = edited.ExampleFrom; l.ExampleTo = edited.ExampleTo;
            FillLearnGrid(); Changed();
        }
    }

    // ================= 自動更新 =================
    void CheckUpdate(bool manual)
    {
        var bg = new System.ComponentModel.BackgroundWorker();
        UpdateInfo info = null; Exception err = null;
        bg.DoWork += (s, e) => { try { info = Updater.Check(); } catch (Exception ex) { err = ex; } };
        bg.RunWorkerCompleted += (s, e) => {
            if (err != null) { if (manual) UI.Info(this, "檢查更新", "無法檢查更新（可能沒有連上網路）：\r\n" + err.Message); return; }
            if (info == null) { if (manual) UI.Info(this, "檢查更新", "目前已經是最新版本（v" + Updater.CurrentText + "）。"); return; }
            pendingUpdate = info;
            lblUpdate.Text = "有新版本 v" + info.Version.ToString(3) + "（目前 v" + Updater.CurrentText + "）" + (info.Notes.Length > 0 ? "：" + info.Notes : "");
            lblUpdate.MaximumSize = new Size(Width - G.P(320), 0);
            updateBar.Visible = true; UI.Apply(updateBar);
            if (manual) DoUpdate();
        };
        bg.RunWorkerAsync();
    }

    void DoUpdate()
    {
        if (pendingUpdate == null) return;
        if (!UI.Confirm(this, "更新", "要更新到 v" + pendingUpdate.Version.ToString(3) + " 嗎？\r\n\r\n" +
                (pendingUpdate.Notes.Length > 0 ? "更新內容：" + pendingUpdate.Notes + "\r\n\r\n" : "") +
                "程式會自動下載、關閉並重新開啟。你的設定、學到的規則和改名紀錄都會保留。", "更新", "稍後")) return;
        try { SaveSettings(cfg); } catch { }
        btnUpdate.Enabled = false; btnUpdate.Text = "下載中…";
        Cursor = Cursors.WaitCursor;
        string exe = Application.ExecutablePath;
        var info = pendingUpdate;
        var bg = new System.ComponentModel.BackgroundWorker();
        string err = null;
        bg.DoWork += (s, e) => { err = Updater.DownloadAndReplace(info, exe); };
        bg.RunWorkerCompleted += (s, e) => {
            Cursor = Cursors.Default;
            if (err != null) { btnUpdate.Enabled = true; btnUpdate.Text = "立即更新"; UI.Info(this, "更新", err); return; }
            try { Updater.Restart(exe); } catch { }
            Application.Exit();
        };
        bg.RunWorkerAsync();
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
