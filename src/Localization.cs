using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Xml;

namespace GoggleCast
{
    public static partial class I18n
    {
        private static readonly Dictionary<string, Dictionary<string, string>> messages = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private static string selected = "auto";
        private static string language = Normalize(CultureInfo.CurrentUICulture.Name);
        public static string SelectedLanguage { get { return selected; } }
        public static string Language { get { return language; } }
        public static readonly string[] SupportedLanguages = { "zh-CN", "zh-TW", "en", "ja", "ko", "es", "fr", "de" };

        static I18n() { AddWestern(); AddAsian(); }

        public static void SetLanguage(string code)
        {
            selected = String.IsNullOrWhiteSpace(code) || code == "auto" ? "auto" : Normalize(code);
            language = selected == "auto" ? Normalize(CultureInfo.CurrentUICulture.Name) : selected;
        }

        private static string Normalize(string code)
        {
            string name = (code ?? "").ToLowerInvariant();
            if (name.StartsWith("zh")) return name.Contains("tw") || name.Contains("hk") || name.Contains("mo") || name.Contains("hant") ? "zh-TW" : "zh-CN";
            foreach (string item in new[] { "en", "ja", "ko", "es", "fr", "de" }) if (name == item || name.StartsWith(item + "-")) return item;
            return "en";
        }

        private static void Add(string key, string locale, string text)
        {
            Dictionary<string, string> row;
            if (!messages.TryGetValue(key, out row)) { row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); messages.Add(key, row); }
            if (row.ContainsKey(locale)) throw new InvalidOperationException("Duplicate locale entry: " + locale + " / " + key);
            row.Add(locale, text);
        }

        private static void W(string key, string en, string es, string fr, string de)
        { Add(key, "en", en); Add(key, "es", es); Add(key, "fr", fr); Add(key, "de", de); }

        public static bool HasTranslation(string key, string locale)
        { Dictionary<string, string> row; return locale == "zh-CN" || (messages.TryGetValue(key, out row) && row.ContainsKey(locale)); }

        public static string T(string key, params object[] args)
        {
            if (key == null) return "";
            string value = key;
            Dictionary<string, string> row;
            if (language != "zh-CN" && messages.TryGetValue(key, out row))
                if (!row.TryGetValue(language, out value) && !row.TryGetValue("en", out value)) value = key;
            if (args == null || args.Length == 0) return value;
            try { return String.Format(CultureInfo.GetCultureInfo(language), value, args); }
            catch (FormatException) { return String.Format(CultureInfo.InvariantCulture, key, args); }
        }
    }

    public static class ErrorText
    {
        // Technical error details remain in the log. The operation surface gives a
        // translated, actionable explanation instead of leaking a different UI language.
        public static string Translate(Exception error)
        {
            string message = error == null ? "" : error.Message;
            string key = "操作未完成。请展开日志查看详细信息。";
            if (error is TimeoutException || error is OperationCanceledException)
                key = "连接超时或已取消。请检查设备后重试。";
            else if (error is FileNotFoundException || error is DirectoryNotFoundException)
                key = "找不到所需文件。请重新选择视频或 FFmpeg。";
            else if (error is UnauthorizedAccessException || message.Contains("Windows 拒绝读取"))
                key = "无法访问文件或桌面。请检查权限，并保持桌面已登录且解锁。";
            else if (message.Contains("被设备拒绝"))
                key = "设备拒绝播放请求。请打开无线投屏，或尝试另一传输格式。";
            else if (message.Contains("没有已连接的 IPv4") || message.Contains("未找到通向眼镜"))
                key = "未找到可用的局域网连接。请将电脑与眼镜连接到同一网络。";
            else if (message.Contains("首版仅支持 IPv4"))
                key = "此版本仅支持 IPv4，请输入设备的 IPv4 地址。";
            else if (message.Contains("没有响应标准 UDP") || message.Contains("无法发送局域网发现"))
                key = "设备未响应搜索。请检查无线投屏、同一网络及防火墙。";
            else if (message.Contains("AVTransport"))
                key = "设备未提供可用的 DLNA 视频接收服务。";
            else if (message.Contains("没有找到 FFmpeg"))
                key = "找不到所需文件。请重新选择视频或 FFmpeg。";
            else if (message.Contains("系统声音") || message.Contains("音频"))
                key = "系统声音采集不可用。请检查默认播放设备，或关闭桌面系统声音后重试。";
            else if (error is ArgumentException && (message.Contains("地址") || message.Contains("URL") || message.Contains("IPv4")))
                key = "请输入有效的 IPv4 地址或完整 HTTP 设备描述地址。";
            else if (message.Contains("编码") || message.Contains("FFmpeg"))
                key = "编码器未能输出视频。请检查文件，或切换为软件编码后重试。";
            else if (error is XmlException || message.Contains("XML") || message.Contains("无法识别的响应"))
                key = "设备返回了无效响应。请重新开启无线投屏后重试。";
            else if (error is WebException || error is SocketException || error is IOException || message.Contains("无法连接设备"))
                key = "无法与设备通信。请检查网络、地址及防火墙，然后重试。";
            return I18n.T(key);
        }
    }
}
