// 批量改名工具 — 自動更新
// 從 GitHub 讀取 version.json，有新版時下載新的 exe、比對 SHA-256，
// 再把正在執行的 exe 改名成 .old、新版放到原位置後重新啟動。
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

[assembly: AssemblyTitle("批量改名工具")]
[assembly: AssemblyProduct("批量改名工具")]
[assembly: AssemblyVersion("3.4.0.0")]
[assembly: AssemblyFileVersion("3.4.0.0")]

public class UpdateInfo
{
    public Version Version; public string Url = "", Sha256 = "", Notes = "";
}

public static class Updater
{
    public const string VersionUrl =
        "https://forgejo.taiyopen.com/tsukiinuhibi/ReName/raw/branch/main/release/version.json";

    public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
    public static string CurrentText { get { var v = Current; return v.Major + "." + v.Minor + "." + v.Build; } }

    static WebClient NewClient()
    {
        // .NET Framework 預設可能沒開 TLS 1.2，GitHub 需要
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        var wc = new WebClient { Encoding = Encoding.UTF8 };
        wc.Headers[HttpRequestHeader.UserAgent] = "BatchRename-Updater";
        wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
        return wc;
    }

    static string JsonString(string json, string key)
    {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!m.Success) return "";
        return Regex.Unescape(m.Groups[1].Value);
    }

    public static UpdateInfo Parse(string json)
    {
        var info = new UpdateInfo {
            Url = JsonString(json, "url"), Sha256 = JsonString(json, "sha256").ToLowerInvariant(), Notes = JsonString(json, "notes"),
        };
        Version v;
        if (!Version.TryParse(JsonString(json, "version"), out v)) return null;
        info.Version = v;
        return info;
    }

    // 有新版就回傳資訊；沒有新版回傳 null；連不上網路會丟出例外
    public static UpdateInfo Check()
    {
        using (var wc = NewClient())
        {
            string json = wc.DownloadString(VersionUrl + "?t=" + DateTime.UtcNow.Ticks);
            var info = Parse(json);
            if (info == null || info.Url.Length == 0) throw new Exception("更新資訊格式不正確");
            var cur = Current;
            var curN = new Version(cur.Major, cur.Minor, Math.Max(cur.Build, 0));
            var newN = new Version(info.Version.Major, info.Version.Minor, Math.Max(info.Version.Build, 0));
            return newN > curN ? info : null;
        }
    }

    public static string Sha256Of(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(path))
        {
            var sb = new StringBuilder();
            foreach (var b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    // 下載並替換；成功時回傳 null，失敗回傳原因
    public static string DownloadAndReplace(UpdateInfo info, string exePath)
    {
        string newPath = exePath + ".new", oldPath = exePath + ".old";
        try
        {
            if (File.Exists(newPath)) File.Delete(newPath);
            using (var wc = NewClient()) wc.DownloadFile(info.Url + "?t=" + DateTime.UtcNow.Ticks, newPath);
            if (info.Sha256.Length > 0 && Sha256Of(newPath) != info.Sha256)
            {
                File.Delete(newPath);
                return "下載的檔案不完整（驗證碼不符），已取消更新，原本的程式沒有被改動。請稍後再試。";
            }
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);              // 執行中的 exe 不能覆蓋，但可以改名
            try { File.Move(newPath, exePath); }
            catch { File.Move(oldPath, exePath); throw; }   // 放不進去就改回原狀
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(newPath);
            return "沒有權限修改程式所在的資料夾（例如放在 Program Files）。\r\n請把程式移到「文件」或「桌面」等資料夾再更新。";
        }
        catch (Exception ex)
        {
            TryDelete(newPath);
            return "更新失敗：" + ex.Message + "\r\n原本的程式沒有被改動。";
        }
    }

    public static void Restart(string exePath)
    {
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) });
    }

    // 啟動時清掉上次更新留下的舊檔
    public static void CleanupOld(string exePath)
    {
        TryDelete(exePath + ".old");
        TryDelete(exePath + ".new");
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
}
