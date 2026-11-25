// 批量改名工具 — 編輯學到的規則
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public class RuleEditor : Form
{
    public Learned Result;

    readonly Learned orig;
    readonly Settings ctx;
    readonly string caseSig;      // 「只用在這種情況」代表的情況
    CheckBox cbEnabled;
    TextBox tbDoc, tbTpl, tbEx;
    RadioButton rbCase, rbAny;
    ComboBox cbToken;
    TLabel lblPreview, lblNote;

    // 可以插入的欄位：代碼, 說明
    static readonly string[,] Tokens = {
        { "公司", "公司" }, { "文件", "文件名稱（對照表換名後）" }, { "文件原名", "文件名稱（原本的寫法）" },
        { "頻率", "頻率，含括號，例如 (月)" }, { "頻率文字", "頻率，不含括號" },
        { "日期", "日期（依「檔名格式」頁的設定）" },
        { "年", "民國年" }, { "西元年", "西元年" }, { "月", "月，例如 5" }, { "月0", "月，兩位數，例如 05" },
        { "日", "日，例如 1" }, { "日0", "日，兩位數，例如 01" },
        { "迄年", "結束日的民國年" }, { "迄月", "結束日的月" }, { "迄月0", "結束日的月（兩位數）" },
        { "迄日", "結束日的日" }, { "迄日0", "結束日的日（兩位數）" },
        { "廠商1", "第一個廠商" }, { "廠商2", "第二個廠商" }, { "廠商", "全部廠商" },
        { "編號", "編號，含括號" }, { "編號文字", "編號，不含括號" },
        { "備註", "備註，含括號" }, { "備註文字", "備註，不含括號" },
    };
    static readonly HashSet<string> Known = new HashSet<string> { "名稱", "作業", "其他", "西元年", "迄西元年" };
    static readonly Regex FriendlyRx = new Regex(@"〔([^〔〕]*)〕");

    static RuleEditor() { for (int i = 0; i < Tokens.GetLength(0); i++) Known.Add(Tokens[i, 0]); }

    public static string ToTemplate(string friendly) { return FriendlyRx.Replace(friendly ?? "", m => "{" + m.Groups[1].Value.Trim() + "}"); }

    public RuleEditor(Learned rule, Settings settings, string exampleSig, Font font)
    {
        orig = rule; ctx = settings;
        caseSig = !Core.IsAnyCase(rule.Signature) ? rule.Signature : exampleSig;
        Text = "編輯規則";
        Font = font;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable; MinimizeBox = false; ShowInTaskbar = false;
        Size = new Size(G.P(900), G.P(640));
        MinimumSize = new Size(G.P(720), G.P(540));
        HandleCreated += (s, e) => Theme.StyleTitleBar(this);

        var card = new Card { Dock = DockStyle.Fill, AccentStrip = true, Padding = new Padding(G.P(22), G.P(14), G.P(16), G.P(14)) };
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(G.P(12)) };
        outer.Controls.Add(card); Controls.Add(outer);
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(t);
        int r = 0;
        Func<string, TLabel> L = text => new TLabel(text) { Margin = new Padding(G.P(3), G.P(10), G.P(12), G.P(3)) };
        Func<string, TLabel> Gray = text => new TLabel(text, LabelRole.Dim) { Margin = new Padding(G.P(3), G.P(2), G.P(3), G.P(10)), MaximumSize = new Size(G.P(700), 0) };

        var title = new TLabel("編輯規則", LabelRole.Title);
        t.Controls.Add(title, 0, r); t.SetColumnSpan(title, 2); r++;
        cbEnabled = new TCheck("使用這條規則", rule.Enabled);
        t.Controls.Add(cbEnabled, 1, r++);

        tbDoc = new TextBox(); var ibDoc = new InputBox(rule.DocKey); tbDoc = ibDoc.Box;
        t.Controls.Add(L("適用的文件名稱"), 0, r); t.Controls.Add(ibDoc, 1, r++);
        t.Controls.Add(Gray("檔名裡辨識出的文件名稱要和這裡「完全一樣」（用原本的寫法，不是對照表換名後的）才會套用。"), 1, r++);

        var casePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false };
        rbCase = new TRadio("只用在：" + (caseSig != null ? Core.DescribeSignature(caseSig) : "（範例看不懂，無法判斷）"));
        rbAny = new TRadio("任何情況都用");
        rbCase.Enabled = caseSig != null;
        if (Core.IsAnyCase(rule.Signature) || caseSig == null) rbAny.Checked = true; else rbCase.Checked = true;
        casePanel.Controls.AddRange(new Control[] { rbCase, rbAny });
        t.Controls.Add(L("適用情況"), 0, r); t.Controls.Add(casePanel, 1, r++);
        t.Controls.Add(Gray("同一個文件可以有好幾條規則，例如「日期是區間」用一種寫法、「日期是單一天」用另一種。程式會挑情況最符合的那一條。"), 1, r++);

        var ibTpl = new InputBox(Core.FriendlyTemplate(rule.Template)); tbTpl = ibTpl.Box;
        t.Controls.Add(L("新檔名的樣子"), 0, r); t.Controls.Add(ibTpl, 1, r++);

        var ins = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        cbToken = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = G.P(330), Margin = new Padding(G.P(3), G.P(7), G.P(3), G.P(3)) };
        for (int i = 0; i < Tokens.GetLength(0); i++) cbToken.Items.Add("〔" + Tokens[i, 0] + "〕　" + Tokens[i, 1]);
        cbToken.SelectedIndex = 0;
        var bIns = new FlatBtn("插入到游標位置", BtnKind.Soft);
        ins.Controls.AddRange(new Control[] { new TLabel("插入欄位", LabelRole.Dim) { Margin = new Padding(G.P(3), G.P(10), G.P(3), G.P(3)) }, cbToken, bIns });
        t.Controls.Add(ins, 1, r++);
        t.Controls.Add(Gray("〔 〕裡的是會自動帶入的欄位，其他文字（例如 -、_、固定的字）照樣保留。欄位沒有值時（例如沒有廠商），它前面的「-」「_」會自動拿掉。"), 1, r++);

        var ibEx = new InputBox(rule.ExampleFrom); tbEx = ibEx.Box;
        t.Controls.Add(L("用這個原檔名試算"), 0, r); t.Controls.Add(ibEx, 1, r++);
        lblPreview = new TLabel("", LabelRole.Success) { Margin = new Padding(G.P(3), G.P(10), G.P(3), G.P(3)), Font = new Font(font.FontFamily, font.Size + 1f, FontStyle.Bold) };
        lblNote = Gray("");
        t.Controls.Add(L("結果"), 0, r); t.Controls.Add(lblPreview, 1, r++);
        t.Controls.Add(lblNote, 1, r++);

        var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var bCancel = new FlatBtn("取消") { DialogResult = DialogResult.Cancel };
        var bSave = new FlatBtn("儲存", BtnKind.Primary);
        btns.Controls.AddRange(new Control[] { bCancel, bSave });
        t.Controls.Add(btns, 1, r++);
        CancelButton = bCancel;

        bIns.Click += (s, e) => {
            string tok = "〔" + Tokens[cbToken.SelectedIndex, 0] + "〕";
            int at = tbTpl.SelectionStart;
            tbTpl.Text = tbTpl.Text.Remove(at, tbTpl.SelectionLength).Insert(at, tok);
            tbTpl.SelectionStart = at + tok.Length; tbTpl.Focus();
        };
        foreach (var c in new Control[] { tbDoc, tbTpl, tbEx }) c.TextChanged += (s, e) => UpdatePreview();
        rbCase.CheckedChanged += (s, e) => UpdatePreview();
        cbEnabled.CheckedChanged += (s, e) => UpdatePreview();
        bSave.Click += (s, e) => Save();
        UI.Apply(this);
        foreach (Control c in t.Controls) { var tl = c as TLabel; if (tl != null && tl.Role != LabelRole.Success) tl.ApplyFont(font); }
        UpdatePreview();
    }

    Learned Build()
    {
        return new Learned {
            Enabled = cbEnabled.Checked, DocKey = tbDoc.Text.Trim(),
            Signature = rbCase.Checked && caseSig != null ? caseSig : Learned.AnyCase,
            Template = ToTemplate(tbTpl.Text), ExampleFrom = tbEx.Text.Trim(), ExampleTo = orig.ExampleTo,
        };
    }

    string Problem(Learned l)
    {
        if (l.DocKey.Length == 0) return "「適用的文件名稱」不能是空的。";
        var ms = Regex.Matches(l.Template, @"\{([^{}]*)\}");
        if (ms.Count == 0) return "「新檔名的樣子」裡至少要有一個〔欄位〕，不然每個檔案都會改成一樣的名字。";
        var fields = Core.FieldNames(ctx);
        foreach (Match m in ms) if (!Known.Contains(m.Groups[1].Value) && !fields.Contains(m.Groups[1].Value)) return "看不懂〔" + m.Groups[1].Value + "〕這個欄位，請用「插入欄位」選單裡的欄位。";
        if (tbTpl.Text.IndexOf('〔') >= 0 && FriendlyRx.Matches(tbTpl.Text).Count != Regex.Matches(tbTpl.Text, "〔").Count) return "有一個〔沒有對應的〕。";
        return null;
    }

    string preview = "";

    void UpdatePreview()
    {
        var l = Build();
        string err = Problem(l);
        if (err != null) { lblPreview.Text = "⚠ " + err; lblPreview.ForeColor = Theme.Danger; lblNote.Text = ""; preview = ""; return; }
        var c2 = ctx.Clone();
        var test = Build(); test.Enabled = true;
        c2.LearnedRules = new List<Learned> { test };
        var p = Core.Process(tbEx.Text.Trim(), c2, Core.BuildMap(ctx));
        if (!p.Ok) { lblPreview.Text = "無法辨識：" + p.Error; lblPreview.ForeColor = Theme.Danger; lblNote.Text = ""; preview = ""; return; }
        preview = p.Final;
        lblPreview.Text = p.Final;
        lblPreview.ForeColor = p.UsedRule != null ? Theme.Success : Theme.Warning;
        lblNote.Text = p.UsedRule != null ? "（套用了這條規則）"
            : "（這個檔名的文件名稱是「" + RawDocOf(tbEx.Text.Trim(), ctx) + "」，不符合這條規則，所以上面是用平常的檔名格式算的）";
    }

    static string RawDocOf(string stem, Settings c)
    {
        var r = Core.GetNewStem(Core.ApplyRules(stem, c.Rules, Rule.StageBefore, null), c, Core.BuildMap(c), false);
        return r.Ok ? r.RawDoc : "?";
    }

    void Save()
    {
        var l = Build();
        string err = Problem(l);
        if (err != null) { UI.Info(this, Text, err); return; }
        if (preview.Length > 0) l.ExampleTo = preview;
        Result = l;
        DialogResult = DialogResult.OK;
        Close();
    }
}
