// 批量改名工具 — 效能紀錄
// 切換分頁、切換配色等動作如果花太久（含之後畫面重畫的時間），就把每個步驟花的時間
// 記到 %AppData%\BatchRename\效能紀錄.txt，方便找出在使用者電腦上到底是哪裡慢。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

public static class Perf
{
    public static int SlowMs = 400;                 // 超過這個時間才記錄
    public static Func<string> Context;             // 主視窗提供：檔案數、控制項數等
    static readonly Stopwatch sw = new Stopwatch();
    static string name;
    static readonly List<string> marks = new List<string>();
    static long lastMark;
    static int depth;
    static bool waitingIdle;

    sealed class Scope : IDisposable
    {
        readonly bool outer; readonly string inner;
        public Scope(bool outer, string inner) { this.outer = outer; this.inner = inner; }
        public void Dispose()
        {
            depth--;
            if (!outer) { Mark(inner); return; }
            Mark("（" + name + " 本身）");
            if (waitingIdle) return;
            waitingIdle = true;
            Application.Idle += OnIdle;   // 等畫面排版、重畫完（程式閒下來）再算總時間
        }
    }

    public static IDisposable Begin(string what)
    {
        depth++;
        if (depth > 1) return new Scope(false, what);
        if (waitingIdle) { name += "，接著" + what; return new Scope(false, what); }   // 畫面還沒更新完又做了別的動作，記在同一筆
        name = what; marks.Clear(); lastMark = 0; sw.Restart();
        return new Scope(true, null);
    }

    public static void Mark(string step)
    {
        if (!sw.IsRunning) return;
        long now = sw.ElapsedMilliseconds;
        marks.Add(step + " " + (now - lastMark) + "ms");
        lastMark = now;
    }

    static void OnIdle(object s, EventArgs e)
    {
        Application.Idle -= OnIdle;
        waitingIdle = false;
        long total = sw.ElapsedMilliseconds;
        sw.Stop();
        if (total < SlowMs) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatchRename");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "效能紀錄.txt");
            if (File.Exists(path) && new FileInfo(path).Length > 300 * 1024) File.Delete(path);
            string ctx = "";
            try { if (Context != null) ctx = Context(); } catch { }
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + Updater.CurrentText + "  " + name + "：總共 " + total + "ms（其中排版與重畫 " + (total - lastMark) + "ms）\r\n" +
                "    " + string.Join("、", marks.ToArray()) + "\r\n    " + ctx + "\r\n", Encoding.UTF8);
        }
        catch { }
    }
}
