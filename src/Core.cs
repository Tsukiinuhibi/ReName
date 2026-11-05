// 批量改名工具 — 檔名辨識與改名核心
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public class Rule
{
    public const string StageBefore = "辨識前（改原檔名）";
    public const string StageAfter  = "改名後（改新檔名）";
    public bool   Enabled = true;
    public string Stage   = StageBefore;
    public string Find    = "";
    public string Replace = "";
    public bool   UseRegex;
    public string Note    = "";
}

// 從範例學到的規則：文件名稱（原本寫法）一樣的檔案，改用這個格式
public class Learned
{
    public bool   Enabled = true;
    public string DocKey = "";
    public string Template = "";
    public string ExampleFrom = "";
    public string ExampleTo = "";
}

public class LearnResult
{
    public bool Ok; public string Error = ""; public string AddedCompany = "";
    public Learned Rule; public string Got = "";
}

public class Settings
{
    public const int CurrentVersion = 3;
    public const string V2Template = "{公司}-{文件}{頻率}-{日期}-{廠商1}-{廠商2}-{編號}";
    public const string DefaultTemplate = "{公司}-{文件}{頻率}-{日期}-{廠商1}-{廠商2}-{編號}{備註}";
    public const string DefaultRemarks = "END\n退場\n作廢\n結案\n停用\n註銷\n補件\n暫停\n取消\n過期";
    public const string DefaultMap =
        "吊掛=吊掛作業申請\n" +
        "高空車檢查表=高空作業車檢查表\n" +
        "高空工作車檢查表=高空作業車檢查表\n" +
        "高空工作車=高空作業車檢查表";
    public const string DefaultCompanies = "";

    public int    Version;
    public string NameTemplate    = DefaultTemplate;
    public string RangeFormat     = "{年}年{月}月{日0}至{迄月}月{迄日0}";
    public string CrossYearFormat = "{年}年{月}月{日0}至{迄年}年{迄月}月{迄日0}";
    public string MonthFormat     = "{年}年{月}月";
    public string SingleFormat    = "{年}年{月}月{日0}";
    public string YearFormat      = "{年}年";
    public bool   UseMonthFormat  = true;
    public string WorkMap         = DefaultMap;
    public string Companies       = DefaultCompanies;
    public string Remarks         = DefaultRemarks;
    public List<Rule> Rules       = new List<Rule>();
    public List<Learned> LearnedRules = new List<Learned>();
    public Settings Clone() { return (Settings)MemberwiseClone(); }
    public bool   RulesSeeded;
}

public class ProcessResult
{
    public bool Ok; public string Pre = "", Parsed = "", Final = "", Error = "";
    public List<KeyValuePair<string, string>> Parts;
    public Learned UsedRule;
    public List<string> RuleErrors = new List<string>();
}

public class FileRow
{
    public string Path, Dir, Stem, Ext;
    public string NewStem = "", ParseError = "", Status = "";
    public bool Manual, Excluded, Ok, Done;
    public string OldName { get { return Stem + Ext; } }
    public string NewName { get { return NewStem + Ext; } }
}

public class StemResult
{
    public bool Ok; public string Name; public string Error;
    public List<KeyValuePair<string, string>> Parts;
    public List<KeyValuePair<string, string>> Vals;   // 所有可用代碼的值（給範例學習用）
    public string RawDoc = "";
    public Learned UsedRule;
}

enum DateKind { Year, Month, Day, Range }

public static class Core
{
    public const string StatusSame = "名稱沒有變化";
    static readonly char[] TrimChars = { ' ', '-', '_', '－', '　', '.' };

    // ---------- 括號 ----------
    static readonly Regex ParenRx = new Regex(@"[\(（]([^\(\)（）]*)[\)）]");
    static readonly Regex FreqRx  = new Regex(@"^(每)?(日|週|周|月|季|年|半年|雙週|半月)$");
    static readonly Regex CodeRx  = new Regex(@"[A-Za-z0-9０-９Ａ-Ｚａ-ｚ]");

    // ---------- 日期（依序嘗試，取最先出現的位置）----------
    //  1150501 / 1150501-0531 / 20260501 / 115.09.23 / 115年9月23日 / 115.09月 / 114年11月 / 11504 / 115
    static readonly Regex DateRx = new Regex(
        @"(?<![0-9])(?:" +
        @"(?<r1>(?:19|20)[0-9]{6}|1[0-9]{6})(?![0-9])(?:\s*[-－~～至]\s*(?<r2>(?:19|20)[0-9]{6}|1[0-9]{6}|[0-9]{4}|[0-9]{2})(?![0-9]))?" +
        @"|(?<fy>[0-9]{2,4})\s*[./年]\s*(?<fm>[0-9]{1,2})\s*[./月]\s*(?<fd>[0-9]{1,2})(?![0-9])\s*日?" +
        @"(?:\s*[至~～]\s*(?:(?<ey>[0-9]{2,4})\s*年\s*)?(?<em>[0-9]{1,2})\s*[./月]\s*(?<ed>[0-9]{1,2})(?![0-9])\s*日?)?" +
        @"|(?<my>[0-9]{2,4})\s*(?:[./]\s*(?<mm>[0-9]{1,2})(?![0-9])\s*月?|年\s*(?<mm>[0-9]{1,2})\s*月)" +
        @"|(?<ym>(?:19|20)[0-9]{4}|1[0-9]{4})(?![0-9])" +
        @"|(?<y>1[0-9]{2})(?![0-9])\s*年?" +
        @")");

    static readonly string[] DocKeywords = {
        "表", "單", "申請", "檢查", "查驗", "自主", "吊掛", "高架", "動火", "局限", "侷限", "空間",
        "車", "機", "作業", "施工", "許可", "紀錄", "記錄", "報告", "日誌", "照片", "計畫", "計劃" };

    static readonly Regex SegSplit = new Regex(@"[\s\-－_　]+");
    static readonly Regex MultiDash = new Regex(@"-{2,}");

    public static Dictionary<string, string> ParseWorkMap(string text)
    {
        var map = new Dictionary<string, string>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            int i = t.IndexOf('=');
            if (i < 1) continue;
            var k = t.Substring(0, i).Trim();
            if (k.Length > 0) map[k] = t.Substring(i + 1).Trim();
        }
        return map;
    }

    public static HashSet<string> ParseList(string text)
    {
        var set = new HashSet<string>();
        foreach (var line in (text ?? "").Split('\n')) { var t = line.Trim(); if (t.Length > 0) set.Add(t); }
        return set;
    }

    static int Year(string y) { int v = int.Parse(y); return y.Length == 4 ? v : v + 1911; }

    public static DateTime? ParseDate(string d, DateTime? refStart)
    {
        try
        {
            switch (d.Length)
            {
                case 8: return new DateTime(int.Parse(d.Substring(0, 4)), int.Parse(d.Substring(4, 2)), int.Parse(d.Substring(6, 2)));
                case 7: return new DateTime(int.Parse(d.Substring(0, 3)) + 1911, int.Parse(d.Substring(3, 2)), int.Parse(d.Substring(5, 2)));
                case 4:
                {
                    if (refStart == null) return null;
                    var s = refStart.Value;
                    int m = int.Parse(d.Substring(0, 2)), day = int.Parse(d.Substring(2, 2)), y = s.Year;
                    if (m * 100 + day < s.Month * 100 + s.Day) y++;   // 例如 1141215-0115 跨到隔年
                    return new DateTime(y, m, day);
                }
                case 2:
                    if (refStart == null) return null;
                    return new DateTime(refStart.Value.Year, refStart.Value.Month, int.Parse(d));
            }
        }
        catch (ArgumentException) { }
        catch (FormatException) { }
        return null;
    }

    // 找出日期；傳回 null 表示找不到。error 不為空表示找到但日期不合理
    static Match FindDate(string text, out DateKind kind, out DateTime start, out DateTime? end, out string error)
    {
        kind = DateKind.Day; start = DateTime.MinValue; end = null; error = "";
        var m = DateRx.Match(text);
        if (!m.Success) return null;
        try
        {
            if (m.Groups["r1"].Success)
            {
                var s = ParseDate(m.Groups["r1"].Value, null);
                if (s == null) { error = "日期不正確（" + m.Groups["r1"].Value + "）"; return m; }
                start = s.Value;
                if (m.Groups["r2"].Success)
                {
                    end = ParseDate(m.Groups["r2"].Value, s);
                    if (end == null) { error = "結束日期不正確（" + m.Groups["r2"].Value + "）"; return m; }
                    if (end.Value < start) { error = "結束日期比開始日期早"; return m; }
                    kind = end.Value == start ? DateKind.Day : DateKind.Range;
                }
                else kind = DateKind.Day;
            }
            else if (m.Groups["fy"].Success)
            {
                start = new DateTime(Year(m.Groups["fy"].Value), int.Parse(m.Groups["fm"].Value), int.Parse(m.Groups["fd"].Value));
                kind = DateKind.Day;
                if (m.Groups["ed"].Success)
                {   // 已整理過的區間，例如「115年2月09至2月28」
                    int ey = m.Groups["ey"].Success ? Year(m.Groups["ey"].Value) : start.Year;
                    end = new DateTime(ey, int.Parse(m.Groups["em"].Value), int.Parse(m.Groups["ed"].Value));
                    if (end.Value < start) { error = "結束日期比開始日期早"; return m; }
                    if (end.Value != start) kind = DateKind.Range;
                }
            }
            else if (m.Groups["my"].Success)
            {
                start = new DateTime(Year(m.Groups["my"].Value), int.Parse(m.Groups["mm"].Value), 1);
                kind = DateKind.Month;
            }
            else if (m.Groups["ym"].Success)
            {
                var v = m.Groups["ym"].Value;
                start = new DateTime(Year(v.Substring(0, v.Length - 2)), int.Parse(v.Substring(v.Length - 2)), 1);
                kind = DateKind.Month;
            }
            else
            {
                start = new DateTime(Year(m.Groups["y"].Value), 1, 1);
                kind = DateKind.Year;
            }
        }
        catch (ArgumentException) { error = "日期不正確（" + m.Value.Trim() + "）"; }
        return m;
    }

    static KeyValuePair<string, string> KV(string k, object v) { return new KeyValuePair<string, string>(k, Convert.ToString(v)); }

    static string FormatDate(DateKind kind, DateTime s, DateTime? eN, Settings c)
    {
        string tpl;
        var e = eN ?? s;
        switch (kind)
        {
            case DateKind.Year:  tpl = c.YearFormat; break;
            case DateKind.Month: tpl = c.MonthFormat; break;
            case DateKind.Day:   tpl = c.SingleFormat; break;
            default:
                if (c.UseMonthFormat && s.Day == 1 && e.Year == s.Year && e.Month == s.Month && e.Day == DateTime.DaysInMonth(e.Year, e.Month))
                    tpl = c.MonthFormat;
                else if (e.Year != s.Year) tpl = c.CrossYearFormat;
                else tpl = c.RangeFormat;
                break;
        }
        var v = new List<KeyValuePair<string, string>> {
            KV("年", s.Year - 1911), KV("西元年", s.Year),
            KV("月", s.Month), KV("月0", s.Month.ToString("00")),
            KV("日", s.Day),   KV("日0", s.Day.ToString("00")),
            KV("迄年", e.Year - 1911), KV("迄西元年", e.Year),
            KV("迄月", e.Month), KV("迄月0", e.Month.ToString("00")),
            KV("迄日", e.Day),   KV("迄日0", e.Day.ToString("00")),
        };
        string t = tpl ?? "";
        foreach (var kv in v) t = t.Replace("{" + kv.Key + "}", kv.Value);
        return t;
    }

    public static string FillTemplate(string tpl, IList<KeyValuePair<string, string>> vals)
    {
        string t = tpl ?? "";
        // 空的欄位連同前面的分隔符號一起拿掉（例如沒有廠商時不會多一個 -）
        foreach (var kv in vals)
            if (string.IsNullOrEmpty(kv.Value))
                t = Regex.Replace(t, @"[\s_\-－]*" + Regex.Escape("{" + kv.Key + "}"), "");
        foreach (var kv in vals) t = t.Replace("{" + kv.Key + "}", kv.Value ?? "");
        return MultiDash.Replace(t, "-").Trim(TrimChars);
    }

    // 備註從哪裡開始：最早出現的備註用語；前面若緊貼英文字母一起算進去
    static int RemarkIndex(string seg, List<string> words)
    {
        int best = -1;
        foreach (var w in words)
        {
            int i = seg.IndexOf(w, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (best < 0 || i < best)) best = i;
        }
        while (best > 0 && char.IsLetter(seg[best - 1]) && seg[best - 1] < 128) best--;
        return best;
    }

    static bool IsRemark(string seg, List<string> words)
    {
        foreach (var w in words) if (seg.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static bool IsDocLike(string seg, Dictionary<string, string> map)
    {
        if (map.ContainsKey(seg)) return true;
        foreach (var k in DocKeywords) if (seg.Contains(k)) return true;
        return false;
    }

    // 把檔名拆成：公司、文件、頻率、日期、廠商、編號
    public static StemResult GetNewStem(string stem, Settings c, Dictionary<string, string> map, bool useLearned = true)
    {
        var companies = ParseList(c.Companies);
        var remarkWords = new List<string>(ParseList(c.Remarks));

        // 1. 括號：頻率 (日)(月)(年)…、備註、編號（含英數字）、名稱（公司/廠商）
        var names = new List<string>(); var codes = new List<string>(); var freqs = new List<string>(); var remarks = new List<string>();
        string text = ParenRx.Replace(stem, x => {
            var v = x.Groups[1].Value.Trim();
            if (v.Length == 0) return "";
            if (FreqRx.IsMatch(v)) { freqs.Add("(" + v + ")"); return ""; }   // 頻率夾在文件名稱中間時直接接起來
            if (IsRemark(v, remarkWords)) remarks.Add(v);
            else if (CodeRx.IsMatch(v)) codes.Add("(" + v + ")");
            else if (!names.Contains(v)) names.Add(v);
            return "-";
        });

        // 2. 日期（可以沒有）
        DateKind kind; DateTime start; DateTime? end; string dErr;
        var dm = FindDate(text, out kind, out start, out end, out dErr);
        if (dm != null && dErr.Length > 0) return new StemResult { Error = dErr };
        string beforeDate = dm == null ? text : text.Substring(0, dm.Index);
        string afterDate  = dm == null ? "" : text.Substring(dm.Index + dm.Length);

        // 3. 剩下的文字用 - 切開，判斷哪段是公司、哪段是文件
        var segs = new List<string>();
        foreach (var p in SegSplit.Split(beforeDate)) { var t = p.Trim(TrimChars); if (t.Length > 0) segs.Add(t); }
        int firstAfter = segs.Count;
        foreach (var p in SegSplit.Split(afterDate)) { var t = p.Trim(TrimChars); if (t.Length > 0) segs.Add(t); }
        // 含有備註用語的段落（例如「END已退場」）→ 備註
        for (int i = segs.Count - 1; i >= 0; i--)
        {
            int at = RemarkIndex(segs[i], remarkWords);
            if (at < 0) continue;
            remarks.Insert(0, segs[i].Substring(at));
            string head = segs[i].Substring(0, at).Trim(TrimChars);
            if (head.Length > 0) segs[i] = head;       // 例如「入場申請單END已退場」→ 文件「入場申請單」+ 備註「END已退場」
            else { segs.RemoveAt(i); if (i < firstAfter) firstAfter--; }
        }
        int ci = -1;
        for (int i = 0; i < segs.Count && ci < 0; i++) if (companies.Contains(segs[i])) ci = i;
        for (int i = 0; i < segs.Count && ci < 0; i++) if (!IsDocLike(segs[i], map)) ci = i;
        if (ci < 0 && segs.Count > 1) ci = 0;
        string company = ci >= 0 ? segs[ci] : "";
        var docParts = new List<string>();
        var segVendors = new List<string>();
        bool seenDoc = false;
        for (int i = 0; i < segs.Count; i++)
        {
            if (i == ci) continue;
            bool docLike = IsDocLike(segs[i], map);
            // 公司名單裡的其他名稱 = 廠商（例如已整理過的「甲公司-高架-115年2月-乙公司」）
            // 日期後面、不像文件名稱的文字也當成廠商（例如「…114年12月15至115年1月15-乙公司」）
            // 文件名稱後面、不像文件的文字也當成廠商（例如「甲公司-入場申請單-乙公司」）
            bool vendorLike = (companies.Contains(segs[i]) || (!docLike && (i >= firstAfter || seenDoc))) && segs[i] != company;
            if (vendorLike) { if (!segVendors.Contains(segs[i])) segVendors.Add(segs[i]); }
            else { docParts.Add(segs[i]); if (docLike) seenDoc = true; }
        }
        foreach (var v in segVendors) if (!names.Contains(v)) names.Add(v);
        string doc = string.Join("-", docParts.ToArray());

        // 沒有寫在外面的公司，就用括號裡的名稱，例如「…檢查表(甲公司)」
        if (company.Length == 0 && names.Count > 0) { company = names[0]; names.RemoveAt(0); }
        if (doc.Length == 0 && company.Length == 0) return new StemResult { Error = "找不到公司或文件名稱" };
        // 沒有日期時要更謹慎：公司必須在公司名單裡，或有括號裡的廠商，避免亂改不相關的檔案
        if (dm == null && !(companies.Contains(company) || names.Count > 0 || segVendors.Count > 0))
            return new StemResult { Error = "找不到日期" };

        string rawDoc = doc;
        string mapped;
        if (map.TryGetValue(doc, out mapped)) doc = mapped;

        string freq = freqs.Count > 0 ? freqs[0] : "";
        string other = freqs.Count > 1 ? string.Join("", freqs.GetRange(1, freqs.Count - 1).ToArray()) : "";
        string date = dm == null ? "" : FormatDate(kind, start, end, c);

        var parts = new List<KeyValuePair<string, string>> {
            KV("公司", company), KV("文件", doc), KV("頻率", freq), KV("日期", date),
            KV("廠商", string.Join("-", names.ToArray())),
            KV("廠商1", names.Count > 0 ? names[0] : ""),
            KV("廠商2", names.Count > 1 ? string.Join("-", names.GetRange(1, names.Count - 1).ToArray()) : ""),
            KV("編號", string.Join("", codes.ToArray())),
            KV("備註", remarks.Count > 0 ? "(" + string.Join("-", remarks.ToArray()) + ")" : ""),
            KV("其他", other),
        };
        var vals = new List<KeyValuePair<string, string>>(parts);
        vals.Add(KV("名稱", company));        // 舊版代碼，仍可使用
        vals.Add(KV("作業", doc + freq));
        // 不含括號的版本
        vals.Add(KV("頻率文字", freq.Trim('(', ')')));
        vals.Add(KV("編號文字", string.Join("-", codes.ConvertAll(x => x.Trim('(', ')')).ToArray())));
        vals.Add(KV("備註文字", string.Join("-", remarks.ToArray())));
        // 日期的各個部分也可以直接放進檔名格式
        if (dm != null)
        {
            var e = end ?? start;
            vals.AddRange(new[] {
                KV("年", start.Year - 1911), KV("西元年", start.Year), KV("月", start.Month), KV("月0", start.Month.ToString("00")),
                KV("日", start.Day), KV("日0", start.Day.ToString("00")),
                KV("迄年", e.Year - 1911), KV("迄西元年", e.Year), KV("迄月", e.Month), KV("迄月0", e.Month.ToString("00")),
                KV("迄日", e.Day), KV("迄日0", e.Day.ToString("00")) });
        }
        else foreach (var k in new[] { "年", "西元年", "月", "月0", "日", "日0", "迄年", "迄西元年", "迄月", "迄月0", "迄日", "迄日0" }) vals.Add(KV(k, ""));

        Learned used = null;
        if (useLearned && c.LearnedRules != null)
            foreach (var l in c.LearnedRules) if (l.Enabled && l.DocKey == rawDoc && rawDoc.Length > 0) { used = l; break; }
        string tpl = used != null ? used.Template : c.NameTemplate;
        return new StemResult { Ok = true, Name = FillTemplate(tpl, vals), Parts = parts, Vals = vals, RawDoc = rawDoc, UsedRule = used };
    }

    // ---------- 進階規則 ----------
    public static string ApplyRules(string text, List<Rule> rules, string stage, List<string> errors)
    {
        if (rules == null) return text;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (!r.Enabled || r.Stage != stage || string.IsNullOrEmpty(r.Find)) continue;
            if (r.UseRegex)
            {
                try { text = Regex.Replace(text, r.Find, r.Replace ?? ""); }
                catch (ArgumentException) { if (errors != null) errors.Add("第 " + (i + 1) + " 條進階規則寫法有誤，已略過"); }
            }
            else text = text.Replace(r.Find, r.Replace ?? "");
        }
        return text;
    }

    public static ProcessResult Process(string stem, Settings c, Dictionary<string, string> map)
    {
        var p = new ProcessResult();
        p.Pre = ApplyRules(stem, c.Rules, Rule.StageBefore, p.RuleErrors);
        var res = GetNewStem(p.Pre, c, map);
        if (!res.Ok) { p.Error = res.Error; return p; }
        p.Parsed = res.Name; p.Parts = res.Parts; p.UsedRule = res.UsedRule;
        string fin = ApplyRules(res.Name, c.Rules, Rule.StageAfter, p.RuleErrors);
        if (fin != res.Name) fin = MultiDash.Replace(fin, "-").Trim(TrimChars);
        p.Final = fin; p.Ok = true;
        return p;
    }

    // ---------- 範例學習 ----------
    static readonly string[] DateCandidates = {
        "{日期}",
        "{年}年{月}月{日0}至{迄月}月{迄日0}", "{年}年{月}月{日}至{迄月}月{迄日}", "{年}年{月}月{日}日至{迄月}月{迄日}日",
        "{年}年{月0}月{日0}日至{迄月0}月{迄日0}日", "{年}{月0}{日0}-{迄月0}{迄日0}", "{年}.{月0}.{日0}-{迄月0}.{迄日0}",
        "{年}.{月0}.{日0}~{迄月0}.{迄日0}", "{年}{月0}{日0}~{迄月0}{迄日0}",
        "{年}年{月}月{日}日", "{年}年{月0}月{日0}日", "{年}年{月}月{日0}", "{年}年{月}月{日}", "{年}年{月0}月{日0}",
        "{年}.{月0}.{日0}", "{年}.{月}.{日}", "{年}/{月0}/{日0}", "{年}-{月0}-{日0}", "{年}{月0}{日0}",
        "{西元年}-{月0}-{日0}", "{西元年}.{月0}.{日0}", "{西元年}/{月0}/{日0}", "{西元年}{月0}{日0}",
        "{年}年{月0}月", "{年}年{月}月", "{年}.{月0}月", "{年}.{月0}", "{年}{月0}", "{西元年}-{月0}", "{西元年}{月0}",
        "{年}年", "{西元年}年", "{西元年}",
    };

    // 不同的代碼互相排斥（例如 {廠商} 和 {廠商1}）
    static string Group(string token)
    {
        if (token.StartsWith("{日期") || token.Contains("{年}") || token.Contains("{西元年}")) return "date";
        switch (token)
        {
            case "{頻率}": case "{頻率文字}": return "freq";
            case "{編號}": case "{編號文字}": return "code";
            case "{備註}": case "{備註文字}": return "remark";
            default: return token;
        }
    }

    public static string FriendlyTemplate(string tpl)
    {
        return Regex.Replace(tpl ?? "", @"\{([^{}]+)\}", m => "〔" + m.Groups[1].Value + "〕");
    }

    static readonly Regex TokenRx = new Regex(@"\{[^{}]+\}");
    static readonly Regex DatePieceRx = new Regex(@"\{迄?(西元年|年|月0?|日0?)\}");

    // 範例裡剛好沒有的部分（例如這個範例沒有廠商），依照平常檔名格式的順序補進學到的格式，
    // 以後遇到有廠商的檔案才不會漏掉。範例裡有、但結果刻意拿掉的部分不會補回去。
    static string FillMissing(string learned, string normal, List<KeyValuePair<string, string>> vals)
    {
        var empty = new HashSet<string>();
        foreach (var kv in vals) if (kv.Value.Length == 0) empty.Add("{" + kv.Key + "}");
        // 廠商有值卻被刻意拿掉時，其他廠商代碼也不要補
        bool vendorDropped = learned.Contains("{廠商}");
        foreach (var t in new[] { "{廠商1}", "{廠商2}" })
            if (!empty.Contains(t) && !learned.Contains(t)) vendorDropped = true;
        // 學到的格式如果用「_」分隔，補進去的也用「_」
        string lits = TokenRx.Replace(learned, "");
        bool underscore = lits.Contains("_") && !lits.Contains("-");
        var ms = TokenRx.Matches(normal ?? "");
        for (int i = 0; i < ms.Count; i++)
        {
            string tok = ms[i].Value;
            if (!empty.Contains(tok) || learned.Contains(tok)) continue;
            if (tok == "{日期}" && DatePieceRx.IsMatch(learned)) continue;
            if (vendorDropped && tok.StartsWith("{廠商")) continue;
            int prevEnd = i > 0 ? ms[i - 1].Index + ms[i - 1].Length : 0;
            string sep = normal.Substring(prevEnd, ms[i].Index - prevEnd);
            // 找前面最近一個已經在學到格式裡的代碼，插在它後面
            int insertAt = -1;
            for (int j = i - 1; j >= 0 && insertAt < 0; j--)
            {
                string p = ms[j].Value;
                int k = learned.LastIndexOf(p, StringComparison.Ordinal);
                if (k >= 0) insertAt = k + p.Length;
                else if (p == "{日期}")
                {
                    var dms = DatePieceRx.Matches(learned);
                    if (dms.Count > 0) insertAt = dms[dms.Count - 1].Index + dms[dms.Count - 1].Length;
                }
            }
            if (insertAt < 0) continue;
            if (underscore) sep = sep.Replace("-", "_");
            learned = learned.Insert(insertAt, sep + tok);
        }
        return learned;
    }

    public static LearnResult Learn(string src, string target, Settings c)
    {
        var lr = new LearnResult();
        src = (src ?? "").Trim(); target = (target ?? "").Trim();
        if (src.Length == 0 || target.Length == 0) { lr.Error = "原檔名和想要的結果都要填。"; return lr; }
        var map = ParseWorkMap(c.WorkMap);
        string pre = ApplyRules(src, c.Rules, Rule.StageBefore, null);
        var res = GetNewStem(pre, c, map, false);

        // 看不懂時，試著把結果的第一段當成公司（例如公司不在名單、又沒有日期）
        if (!res.Ok)
        {
            string first = target.Split('-')[0].Trim();
            if (first.Length > 0 && pre.Contains(first) && !ParseList(c.Companies).Contains(first))
            {
                var c2 = c.Clone(); c2.Companies = (c.Companies ?? "").TrimEnd('\n') + "\n" + first;
                var res2 = GetNewStem(pre, c2, map, false);
                if (res2.Ok) { res = res2; lr.AddedCompany = first; c = c2; }
            }
        }
        if (!res.Ok) { lr.Error = "程式看不懂這個原檔名（" + res.Error + "），所以學不起來。"; return lr; }
        if (res.RawDoc.Length == 0) { lr.Error = "原檔名裡找不到文件名稱，所以不知道要套用到哪一種檔案。"; return lr; }

        // 找出結果裡每一段是從原檔名的哪個部分來的
        var cands = new List<KeyValuePair<string, string>>();   // 代碼, 實際文字
        var skip = new HashSet<string> { "名稱", "作業", "其他" };
        foreach (var kv in res.Vals)
        {
            if (skip.Contains(kv.Key) || kv.Value.Length == 0) continue;
            if (kv.Key.Length <= 3 && "年西元年月月0日日0迄年迄西元年迄月迄月0迄日迄日0".Contains(kv.Key) && kv.Key != "日期") continue;
            cands.Add(new KeyValuePair<string, string>("{" + kv.Key + "}", kv.Value));
        }
        foreach (var d in DateCandidates)
        {
            if (d == "{日期}") continue;
            string v = FillTemplate(d, res.Vals);
            if (v.Length > 0 && !d.Contains("{迄") || (d.Contains("{迄") && res.Vals.Exists(x => x.Key == "迄日" && x.Value.Length > 0)))
                cands.Add(new KeyValuePair<string, string>(d, v));
        }
        // 長的先比對，避免短的名稱誤配到長字串裡
        cands.Sort((a, b) => {
            int x = b.Value.Length.CompareTo(a.Value.Length);
            return x != 0 ? x : (a.Key == "{日期}" ? -1 : b.Key == "{日期}" ? 1 : 0);
        });

        var used = new bool[target.Length];
        var hits = new List<KeyValuePair<int, KeyValuePair<string, int>>>();   // 位置, (代碼, 長度)
        var doneGroups = new HashSet<string>();
        foreach (var cd in cands)
        {
            string g = Group(cd.Key);
            if (doneGroups.Contains(g)) continue;
            if ((g == "{廠商1}" || g == "{廠商2}") && doneGroups.Contains("{廠商}")) continue;
            if (g == "{廠商}" && (doneGroups.Contains("{廠商1}") || doneGroups.Contains("{廠商2}"))) continue;
            int from = 0, idx;
            while ((idx = target.IndexOf(cd.Value, from, StringComparison.Ordinal)) >= 0)
            {
                bool free = true;
                for (int i = idx; i < idx + cd.Value.Length; i++) if (used[i]) { free = false; break; }
                if (free)
                {
                    for (int i = idx; i < idx + cd.Value.Length; i++) used[i] = true;
                    hits.Add(new KeyValuePair<int, KeyValuePair<string, int>>(idx, new KeyValuePair<string, int>(cd.Key, cd.Value.Length)));
                    doneGroups.Add(g);
                    break;
                }
                from = idx + 1;
            }
        }
        hits.Sort((a, b) => a.Key.CompareTo(b.Key));
        var sb = new StringBuilder(); int pos = 0;
        foreach (var h in hits)
        {
            sb.Append(target, pos, h.Key - pos);
            sb.Append(h.Value.Key);
            pos = h.Key + h.Value.Value;
        }
        sb.Append(target.Substring(pos));
        string tpl = sb.ToString();
        if (!tpl.Contains("{")) { lr.Error = "結果裡找不到任何來自原檔名的部分（公司、日期…），看不出規律。"; return lr; }

        tpl = FillMissing(tpl, c.NameTemplate, res.Vals);
        var rule = new Learned { DocKey = res.RawDoc, Template = tpl, ExampleFrom = src, ExampleTo = target };
        // 驗證：用學到的規則重跑一次原檔名，要和想要的結果一樣
        var c3 = c.Clone();
        c3.LearnedRules = new List<Learned> { rule };
        var check = Process(src, c3, map);
        lr.Got = check.Ok ? check.Final : "";
        if (!check.Ok || check.Final != target)
        { lr.Error = "學到的規則套回去得到的是「" + lr.Got + "」，和你要的不一樣。"; lr.Rule = rule; return lr; }
        lr.Ok = true; lr.Rule = rule;
        return lr;
    }

    // ---------- 檔案清單 ----------
    public static FileRow MakeRow(string path)
    {
        return new FileRow {
            Path = path,
            Dir  = System.IO.Path.GetDirectoryName(path),
            Stem = System.IO.Path.GetFileNameWithoutExtension(path),
            Ext  = System.IO.Path.GetExtension(path),
        };
    }

    public static void UpdateRows(List<FileRow> rows, Settings c)
    {
        var map = ParseWorkMap(c.WorkMap);
        foreach (var r in rows)
        {
            if (r.Manual) continue;
            var res = Process(r.Stem, c, map);
            if (res.Ok) { r.NewStem = res.Final; r.ParseError = ""; }
            else { r.NewStem = r.Stem; r.ParseError = res.Error; }
        }
        ValidateRows(rows);
    }

    static bool HasBadChar(string name)
    {
        foreach (char ch in name)
            if (ch < 32 || "\\/:*?\"<>|".IndexOf(ch) >= 0) return true;
        return false;
    }

    public static void ValidateRows(List<FileRow> rows)
    {
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            // 不會改名的檔案不算（它們若佔用同一個名稱，下面的「資料夾已有同名檔案」會擋下）
            if ((r.ParseError.Length > 0 && !r.Manual) || r.NewName == r.OldName) continue;
            var k = System.IO.Path.Combine(r.Dir, r.NewName);
            int n; count.TryGetValue(k, out n); count[k] = n + 1;
        }
        foreach (var r in rows)
        {
            r.Ok = false;
            string newName = r.NewName;
            if (r.ParseError.Length > 0 && !r.Manual) { r.Status = "無法辨識：" + r.ParseError; continue; }
            if (string.IsNullOrWhiteSpace(r.NewStem)) { r.Status = "錯誤：新檔名是空的"; continue; }
            if (HasBadChar(newName)) { r.Status = "錯誤：含有不能用的字元 \\ / : * ? \" < > |"; continue; }
            if (newName == r.OldName) { r.Status = StatusSame; continue; }
            string target = System.IO.Path.Combine(r.Dir, newName);
            if (count[target] > 1) { r.Status = "錯誤：和清單中其他檔案改名後重複"; continue; }
            if ((File.Exists(target) || Directory.Exists(target)) && !string.Equals(target, r.Path, StringComparison.OrdinalIgnoreCase))
            { r.Status = "錯誤：資料夾裡已有同名檔案"; continue; }
            r.Ok = true;
            r.Status = r.Manual ? "可改名（手動修改）" : "可改名";
        }
    }

    // ---------- 改名與還原 ----------
    static string Q(string s) { return "\"" + s.Replace("\"", "\"\"") + "\""; }

    public class RenameResult { public int Ok, Fail; public string Log = ""; }

    public static RenameResult RenameAll(List<FileRow> rows, string logDir)
    {
        var res = new RenameResult();
        var todo = rows.FindAll(r => r.Ok && !r.Excluded);
        if (todo.Count == 0) return res;
        Directory.CreateDirectory(logDir);
        res.Log = System.IO.Path.Combine(logDir, "改名紀錄_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        using (var sw = new StreamWriter(res.Log, false, new UTF8Encoding(true)))
        {
            sw.WriteLine("原檔案,新檔案");
            foreach (var r in todo)
            {
                string target = System.IO.Path.Combine(r.Dir, r.NewName);
                try
                {
                    if (string.Equals(target, r.Path, StringComparison.OrdinalIgnoreCase))
                    {   // 只有大小寫不同：先改成暫時名稱再改回來
                        string tmp = r.Path + ".renametmp";
                        File.Move(r.Path, tmp); File.Move(tmp, target);
                    }
                    else
                    {
                        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("目標檔名已存在");
                        File.Move(r.Path, target);
                    }
                    sw.WriteLine(Q(r.Path) + "," + Q(target)); sw.Flush();
                    r.Done = true; res.Ok++;
                }
                catch (Exception ex) { r.Ok = false; r.Status = "改名失敗：" + ex.Message; res.Fail++; }
            }
        }
        return res;
    }

    public static List<string[]> ReadLog(string path)
    {
        var list = new List<string[]>();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var fields = new List<string>(); var sb = new StringBuilder(); bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQ)
                {
                    if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else inQ = false; }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQ = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            if (fields.Count >= 2 && fields[0] != "原檔案") list.Add(new[] { fields[0], fields[1] });
        }
        return list;
    }

    public static int[] Undo(string logPath)
    {
        var items = ReadLog(logPath); items.Reverse();
        int ok = 0, skip = 0;
        foreach (var it in items)
        {
            string oldP = it[0], newP = it[1];
            try
            {
                bool caseOnly = string.Equals(oldP, newP, StringComparison.OrdinalIgnoreCase) && oldP != newP;
                if (caseOnly && File.Exists(newP)) { var tmp = newP + ".renametmp"; File.Move(newP, tmp); File.Move(tmp, oldP); ok++; }
                else if (File.Exists(newP) && !File.Exists(oldP)) { File.Move(newP, oldP); ok++; }
                else skip++;
            }
            catch { skip++; }
        }
        return new[] { ok, skip };
    }
}
