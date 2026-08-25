// 引用必要的命名空间
//css_ref System.Net.HttpListener.dll
using System;
using System.IO;
using System.Drawing;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Collections.Concurrent; 
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;

#nullable enable 
#pragma warning disable CS4014 // 兼容V1
#pragma warning disable CS8602
#pragma warning disable CS8600

public static void Exec(Quicker.Public.IStepContext context)
{
    
    string caozuo = context.GetVarValue("操作") as string ?? "";
    var port_Str = context.GetVarValue("端口号").ToString();//通常为 8088数字ToString
    
    if(caozuo == "重置") {
        try {
        
        using (var client = new System.Net.WebClient()) {
            client.DownloadString("http://127.0.0.1:"+port_Str+"/qkLANfile/shutdown");
        }
        MessageBox.Show("已通过网络指令成功强制关闭后台旧服务！", "重置成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    } 
    catch {
        // 如果报错，说明原本就没有服务占用 8088 端口，静默忽略即可
    }
    LocalImageServer.Instance.Reset();
    return;
    }
    
    var palb = context.GetVarValue("path_列表") as List<string>;
    Dictionary<string, object> Purlcd = new Dictionary<string, object>();
    
    bool IsInline = context.GetVarValue("开启浏览") != null ? Convert.ToBoolean(context.GetVarValue("开启浏览")) : true;
    string defaultUploadDir = context.GetVarValue("上传目录") as string ?? Path.GetTempPath();
    string filterFile = context.GetVarValue("筛选") as string ?? "";


    var server = LocalImageServer.Instance;

    // 传递 Quicker 上下文给服务器实例，以便其内部触发截图子程序 ======
    server.QuickerContext = context;
    server.Content_inline = IsInline;
    server.DefaultUploadDirectory = defaultUploadDir; 

    // --- 权限及配置更新    信任列表传入 ---
    server.TrustList = context.GetVarValue("信任列表") as List<string> ?? new List<string>();

// ======加载固定IP用户名词典 ======
    server.FixedUserNames = context.GetVarValue("固定IP用户名") as Dictionary<string, object>;

    string perm = context.GetVarValue("上传下载权限") as string ?? "上传下载";
    server.EnableUploading = perm.Contains("上传");
    server.EnableDownloads = perm.Contains("下载");

    var restrictDirs = context.GetVarValue("限定上传目录") as List<string>;
    server.AllowedUploadDirectories = restrictDirs?.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();

    server.AccessPassword = context.GetVarValue("访问密码") as string;
    server.CustomAlias = context.GetVarValue("自定义域名") as string ?? "";
    server.CustomIP = context.GetVarValue("自定义IP") as string;

// ====== 加载表情常用语到内存 ======
    var emojiList = context.GetVarValue("表情常用语列表") as List<string>;
    server.UpdateEmojis(emojiList);

    server.Port = port_Str;

    if (server.IsRunning && !string.IsNullOrWhiteSpace(server.CustomAlias))
    {
        server.RegisterAlias(server.CustomAlias, port_Str);
    }

    if(filterFile !="old"){


    foreach (string kpa in palb)
    {
        try
        {
            if (File.Exists(kpa) || Directory.Exists(kpa)) 
            {
                server.SetImageAndGetUrl(kpa);
            }
        }
        catch { }
    }
    }
    

    // 获取 加载 共享门户 SC 屏幕控制网页源码
    string frontendHtmlContent = context.GetVarValue("网页源码") as string ?? "";
    server.VirtualHtmlContent = frontendHtmlContent; 
    string scHtmlContent = context.GetVarValue("网页源码_sc") as string ?? "";
    server.ScreenControlHtmlContent = scHtmlContent;

    string reportUrl = "";

    if (!string.IsNullOrWhiteSpace(server.CustomAlias))
    {
        // 走自定义域名逻辑，或固定的虚拟路由index
        string aliasClean = server.CustomAlias.Trim('/');
        reportUrl = $"http://{server.EffectiveIP}:{port_Str}/{aliasClean}/";
    }
    else
    {
        reportUrl = $"http://{server.EffectiveIP}:{port_Str}/qkLANfile/index";
    }

    Purlcd["共享门户"] = reportUrl; 

    // 收集所有共享文件的 URL 供前端或 Quicker 使用
    foreach (var kvp in server.SharedFiles) 
    {
        if(File.Exists(kvp.Key) || Directory.Exists(kvp.Key)) 
        {
            if(filterFile =="fresh" &&( palb.Contains(kvp.Key)==false || Directory.Exists(kvp.Key)) ){ continue;}
            Purlcd[kvp.Key] = kvp.Value.Url;
        }
    }

    if (!server.IsRunning)
    {
        server.Start(port_Str); 
    }else
    {
        server.RefreshActivity();
    }
    context.SetVarValue("pa_url_词典", Purlcd);
}



public class LocalImageServer
{

public string? CustomIP { get; set; }

// 优先使用自定义IP（如二级网关/公网映射IP），未指定时自动获取本地IPv4
public string EffectiveIP => !string.IsNullOrWhiteSpace(CustomIP) ? CustomIP.Trim() : NetworkHelper.GetLocalIPv4();
    public string Port { get; set; } = "8088";
    public Dictionary<string, object>? FixedUserNames { get; set; }
    private readonly ConcurrentDictionary<string, string> _userNames = new ConcurrentDictionary<string, string>();
    private readonly ConcurrentDictionary<string, byte[]> _memoryFiles = new ConcurrentDictionary<string, byte[]>();
    private readonly ConcurrentDictionary<string, string> _memoryFileMime = new ConcurrentDictionary<string, string>();
    public string EmojiJson { get; set; } = "[]";
    public bool Content_inline { get; set; }
    public string? DefaultUploadDirectory { get; set; }
    public string? VirtualHtmlContent { get; set; } 
    public string? ScreenControlHtmlContent { get; set; }
    private string? copyHtmlMemoryCache;

    // ====== 截图内存与长轮询控制参数 ======
    public Quicker.Public.IStepContext? QuickerContext { get; set; }
    private readonly object _screenshotLock = new object();
    private byte[]? _screenshotMemory;
    private string? _screenshotFileName;
    private DateTime _lastScreenshotCapturedTime = DateTime.MinValue;
    private DateTime _lastScreenshotRequestTime = DateTime.MinValue;
    private bool _isScreenshotLoopActive = false;
    private const int ScreenshotCacheTtlMs = 80; // 两次截图的最小硬间隔（毫秒）
    private static readonly SemaphoreSlim _screenshotSemaphore = new SemaphoreSlim(1, 1);
    private TaskCompletionSource<bool>? _currentScreenshotTcs;
    // ====== TTL 缓存控制属性 ======
    private bool _isScreenshotLooping = false;
    private TaskCompletionSource<byte[]>? _screenshotTcs;
    private TaskCompletionSource<bool> _screenshotRequestedTcs = new TaskCompletionSource<bool>();

    // --- 权限与配置属性 ---
    public bool EnableUploading { get; set; } = true;
    public bool EnableDownloads { get; set; } = true;
    public List<string>? AllowedUploadDirectories { get; set; }
    public string? AccessPassword { get; set; }
    public List<string> TrustList { get; set; } = new List<string>(); 
    public string? CustomAlias { get; set; }
    private static LocalImageServer? _instance;
    private static readonly object _instanceLock = new object();

    public static LocalImageServer Instance
    {
        get
        {
            lock (_instanceLock) {
                if (_instance == null) _instance = new LocalImageServer();
                return _instance;
            }
        }
    }
    
    public string BaseUrl { get; set; } = "http://+:8088/qkLANfile/";
    private HttpListener? _listener; 
    
    private readonly ConcurrentDictionary<string, string> _tokenToPathMap = new ConcurrentDictionary<string, string>();
    private readonly ConcurrentDictionary<string, string> _pathToTokenMap = new ConcurrentDictionary<string, string>();

    // ====== 动态文件映射仓库，提供 /api/files 数据 ======
    public class SharedFileInfo {
        public string Path { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsDir { get; set; }
        public long Size { get; set; }
        public long DateTicks { get; set; }
        public string Token { get; set; } = "";
    }
    public ConcurrentDictionary<string, SharedFileInfo> SharedFiles = new ConcurrentDictionary<string, SharedFileInfo>();
    
    public string? CurrentReportToken { get; set; }
    public string? CurrentReportPath { get; set; }
    public bool IsRunning => _isRunning;
    private volatile bool _isRunning = false;

    private System.Threading.Timer? _shutdownTimer;
    private const int ShutdownDelayMs = 300000; 
    private DateTime _lastActivityTime = DateTime.Now; 
    private readonly object _activityLock = new object();
    // ====== 聊天记录存储结构 ======
    public class ChatMessage {
        public string Sender { get; set; } = "";
        public string SenderName { get; set; } = ""; 
        public string Time { get; set; } = "";
        public string Target { get; set; } = "";
        public string Content { get; set; } = "";
        public long Timestamp { get; set; } = 0;
    }
    private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();
    private readonly ConcurrentDictionary<string, string> _activePingers = new ConcurrentDictionary<string, string>();

    public void Reset()
    {
        Stop();
        _screenshotMemory = null;
        _screenshotFileName = null; 
        _tokenToPathMap.Clear();
        _pathToTokenMap.Clear();
SharedFiles.Clear();
        _chatHistory.Clear();
        CurrentReportToken = null;
        CurrentReportPath = null;
        lock (_activityLock) { _lastActivityTime = DateTime.Now; }
        _memoryFiles.Clear();
        _memoryFileMime.Clear();
        _activePingers.Clear();
        EmojiJson = "[]";
        Console.WriteLine("服务器已彻底重置并强制清理所有映射表及监听器。");
    }

    // ======刷新活跃时间 ======
    public void RefreshActivity()
    {
        lock (_activityLock) 
        { 
            _lastActivityTime = DateTime.Now; 
        }
    }

    public void Start(string portStr) 
{
    if (_listener != null) return;
    if (!HttpListener.IsSupported) return;

    // 动态设置当前端口的 BaseUrl 和别名监听前缀
    BaseUrl = $"http://+:{portStr}/qkLANfile/";

    _listener = new HttpListener();
    _listener.Prefixes.Add(BaseUrl);
    if (!string.IsNullOrWhiteSpace(CustomAlias)) _listener.Prefixes.Add($"http://+:{portStr}/{CustomAlias.Trim('/')}/");
    try
    {
        _listener.Start();
        _isRunning = true;
        RefreshActivity();
        Console.WriteLine($"本地服务已启动，监听于 {BaseUrl}...");
        _shutdownTimer = new System.Threading.Timer(CheckForIdleShutdown, null, 0, 120000); 
        Task.Run(() => ProcessRequests());
    }
    catch (Exception ex)
    {
        _listener = null;
        _isRunning = false;
        MessageBox.Show($"本地服务启动失败！可能是 {portStr} 端口被其他程序占用，或者旧服务未彻底关闭。\n\n系统报错:\n{ex.Message}", "启动错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        throw new Exception("服务器启动失败，终止动作。");
    }
}

    public void RegisterAlias(string alias, string portStr)
{
    if (string.IsNullOrWhiteSpace(alias)) return;
    alias = alias.Trim('/');
    string prefix = $"http://+:{portStr}/{alias}/"; 
    if (_listener != null && _listener.IsListening)
    {
        if (!_listener.Prefixes.Contains(prefix))
        {
            try { _listener.Prefixes.Add(prefix); } catch { Console.WriteLine($"绑定前缀 {prefix} 失败，可能需管理员权限。"); }
        }
    }
}

    private async Task ProcessRequests()
    {
        var listener = _listener;
        if (listener == null) return;

        while (_isRunning)
        {
            try
            {
                HttpListenerContext context = await listener.GetContextAsync(); 
                _ = HandleRequestAsync(context);
            }
            catch { break; }
        }
    }

private string GetRichIdentity(HttpListenerRequest request, string remoteIp, string senderId)
    {
        List<string> tags = new List<string>();
        // a. 用户输入  ?name=xxx
        string? userInput = request.QueryString["user"] ?? request.QueryString["name"];
        if (!string.IsNullOrWhiteSpace(userInput)) 
        {
            _userNames[remoteIp] = userInput.Trim(); 
        }
        if (_userNames.TryGetValue(remoteIp, out string? savedName) && !string.IsNullOrWhiteSpace(savedName))
        {
            tags.Add(savedName);
        }

        if (FixedUserNames != null && FixedUserNames.TryGetValue(senderId, out object? dictNameObj) && dictNameObj != null)
        {
            string dictName = dictNameObj.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(dictName)) tags.Add(dictName);
        }
        string ua = request.Headers["User-Agent"] ?? "";
        string device = "";
        if (ua.Contains("Windows NT")) device = "🖥️PC";
        else if (ua.Contains("Android")) device = "📱Android";
        else if (ua.Contains("iPhone")) device = "📱iPhone";
        else if (ua.Contains("iPad")) device = "📱iPad";
        else if (ua.Contains("Mac OS X")) device = "💻Mac";
        else if (ua.Contains("Linux")) device = "🐧Linux";
        if (!string.IsNullOrEmpty(device)) tags.Add(device);
        // 将组装好的标签用斜杠拼接，例如：12 (刘一/🖥️PC)
        if (tags.Count > 0)
            return $"{senderId} ({string.Join("/", tags)})";
        return senderId;
    }

    // ====== 异步的 HTTP 处理方法 ======
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // ====== 身份识别与本地，信任列表 仅需最后1位数字即可信任======
        string remoteIp = request.RemoteEndPoint.Address.ToString();
        string localIp = EffectiveIP;
        string senderId = remoteIp.Split('.').Last();
        
        string richSenderName = GetRichIdentity(request, remoteIp, senderId);
        bool isTrusted = remoteIp == "127.0.0.1" || remoteIp == "::1" || remoteIp == localIp || (TrustList != null && (TrustList.Contains(remoteIp) || TrustList.Contains(senderId)));
 
        string urlPath = request.Url?.AbsolutePath ?? "";
        string[] segments = urlPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    bool isValidPrefix = segments.Length > 0 && (
        segments[0].Equals("qkLANfile", StringComparison.OrdinalIgnoreCase) || 
        (!string.IsNullOrEmpty(CustomAlias) && segments[0].Equals(CustomAlias.Trim('/'), StringComparison.OrdinalIgnoreCase))
    );

        // ====== 保活keepaliveping刷新闲置倒计时,普通ping actionping不刷新======
        if (segments.Length >= 2 && isValidPrefix)
        {
            var match = Regex.Match(segments[1], @"^action(\d+)ping$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string actionPort = match.Groups[1].Value;
                // actionping用户记录为 "IP:端口"
                string ipAndPort = $"{remoteIp}:{actionPort}";
                _activePingers[ipAndPort] = actionPort;
                int remainSec = 0;
                lock (_activityLock) 
                { 
                    remainSec = (ShutdownDelayMs - (int)(DateTime.Now - _lastActivityTime).TotalMilliseconds) / 1000; 
                }
                
                response.StatusCode = 200;
                response.ContentType = "text/plain; charset=utf-8";
                byte[] actionMsg = Encoding.UTF8.GetBytes(remainSec.ToString());
                response.ContentLength64 = actionMsg.Length; 
                response.OutputStream.Write(actionMsg, 0, actionMsg.Length);
                response.Close();

                if (remainSec <= 0) _ = Task.Run(() => this.Stop());
                return;
            }
        }

        if (request.HttpMethod == "GET" && urlPath.EndsWith("ping", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 200;
            if (urlPath.IndexOf("keepaliveping", StringComparison.OrdinalIgnoreCase) >= 0)
                lock (_activityLock) _lastActivityTime = DateTime.Now;
 
            int remainSec = 0;
            lock (_activityLock) { remainSec = (ShutdownDelayMs - (int)(DateTime.Now - _lastActivityTime).TotalMilliseconds) / 1000; }
            
            if (urlPath.IndexOf("keepaliveping", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                response.ContentType = "application/json; charset=utf-8";
                string pingJson = "{" + string.Join(",", _activePingers.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
                byte[] msg = Encoding.UTF8.GetBytes($"{{\"remain\":{remainSec}, \"actionPings\":{pingJson}}}");
                response.ContentLength64 = msg.Length; 
                response.OutputStream.Write(msg, 0, msg.Length);
            }
            else 
            {
                byte[] msg = Encoding.UTF8.GetBytes(remainSec.ToString());
                response.ContentLength64 = msg.Length; 
                response.OutputStream.Write(msg, 0, msg.Length);
            }
            response.Close();
            if (remainSec <= 0) _ = Task.Run(() => this.Stop());
            return;
        }
        // 非一般探测操作均刷新倒计时
        lock (_activityLock) _lastActivityTime = DateTime.Now;

            // ====== 幽灵实例关闭接口 ======
if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("shutdown", StringComparison.OrdinalIgnoreCase))
{
    SendError(response, HttpStatusCode.OK, "Server shutting down...");
    response.Close(); 
    _ = Task.Run(async () => { 
        await Task.Delay(500); 
        this.Stop(); 
    });
    return;
}

        try
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, X-File-Name, X-Chat-Target, X-Chat-Wait, Authorization");
 
            if (request.HttpMethod == "OPTIONS") { response.StatusCode = 204; return; }

            // --- 密码门户验证 (Basic Auth +信任列表绕过) ---
            if (!isTrusted && !string.IsNullOrEmpty(AccessPassword))
            {
                string? authHeader = request.Headers["Authorization"];
                bool authorized = false;
                if (authHeader != null && authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Substring(6).Trim()));
 
                        string[] parts = decoded.Split(new[] { ':' }, 2);
                        if (parts.Length == 2 && parts[1] == AccessPassword) authorized = true;
                    }
                    catch { }
                }
 
                if (!authorized)
                {
                    response.StatusCode = 401;
                    response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure LAN File Portal\"");
                    return;
                }
            }

// ====== 获取完整文件列表数据 API（支持上传权限与目录Token验证） ======
if (segments.Length >= 3 && isValidPrefix && segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) && segments[2].Equals("files", StringComparison.OrdinalIgnoreCase))
{
    var list = SharedFiles.Values.ToList();
    StringBuilder sbJson = new StringBuilder();
    sbJson.Append("[");
    for (int i = 0; i < list.Count; i++) {
        var f = list[i];
        string escPath = f.Path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string targetDir = f.IsDir ? f.Path : (Path.GetDirectoryName(f.Path) ?? f.Path);
        string uploadToken = f.Token;
        foreach (var kv in _tokenToPathMap)
        {
            if (kv.Value.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                uploadToken = kv.Key;
                break;
            }
        }

        bool canUpload = isTrusted || IsPathAllowedForUpload(targetDir,AllowedUploadDirectories);

        sbJson.Append($"{{\"name\":\"{Path.GetFileName(f.Path)}\",\"path\":\"{escPath}\",\"url\":\"{f.Url}\",\"isDir\":{f.IsDir.ToString().ToLower()},\"size\":{f.Size},\"date\":{f.DateTicks},\"token\":\"{f.Token}\",\"uploadToken\":\"{uploadToken}\",\"canUpload\":{canUpload.ToString().ToLower()}}}");
        if (i < list.Count - 1) sbJson.Append(",");
    }
    sbJson.Append("]");
    response.ContentType = "application/json; charset=utf-8";
    byte[] b = Encoding.UTF8.GetBytes(sbJson.ToString());
    response.ContentLength64 = b.Length;
    response.OutputStream.Write(b, 0, b.Length);
    return;
}

// 获取表情的 API：
if (segments.Length >= 3 && isValidPrefix && segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) && segments[2].Equals("emojis", StringComparison.OrdinalIgnoreCase))
{
    response.ContentType = "application/json; charset=utf-8";
    byte[] b = Encoding.UTF8.GetBytes(EmojiJson ?? "[]");
    response.ContentLength64 = b.Length;
    response.OutputStream.Write(b, 0, b.Length);
    return;
}

// ====== 获取截图 API (支持长轮询触发)】 ======
if (segments.Length >= 3 && isValidPrefix && segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) && segments[2].Equals("screenshot", StringComparison.OrdinalIgnoreCase))
{
    if (!isTrusted) {
        SendError(response, HttpStatusCode.Forbidden, "Untrusted IP for screenshot.");
        return;
    }

    //刷新活跃时间戳，并确保后台预抓取流水线正高效运转
    lock (_screenshotLock) {
        _lastScreenshotRequestTime = DateTime.Now;
    }
    EnsureBackgroundCaptureRunning();

    byte[]? targetBytes = null;
    string? targetName = null;

    // 1. 判断当前内存图片是否过期（例如超过 1 秒则视为过期旧图）
    bool isExpired = (DateTime.Now - _lastScreenshotCapturedTime).TotalMilliseconds > 1000;
 
    // 2. 仅在缓存未过期时直接复用内存
    if (!isExpired) {
        lock (_screenshotLock) {
            targetBytes = _screenshotMemory;
            targetName = _screenshotFileName;
        }
    }
 
    // 3. 若无有效缓存或已过期，等待后台最新一帧抓取完成
    if (targetBytes == null) {
        TaskCompletionSource<bool>? tcs;
        lock (_screenshotLock) { tcs = _currentScreenshotTcs; }
        if (tcs != null) {
            await Task.WhenAny(tcs.Task, Task.Delay(2000));
        }
        lock (_screenshotLock) {
            targetBytes = _screenshotMemory;
            targetName = _screenshotFileName;
        }
    }

    // 4. 立即输出图片（不清空内存，供该 80ms 时间窗口内所有并发用户复用）
    if (targetBytes != null) {
        string shotName = targetName ?? "screenshot.jpg";
        response.ContentType = "image/jpeg";
        response.AddHeader("Content-Disposition", $"inline; filename*=UTF-8''{Uri.EscapeDataString(shotName)}");
        response.AddHeader("X-File-Name", Uri.EscapeDataString(shotName));
        response.ContentLength64 = targetBytes.Length;
        await response.OutputStream.WriteAsync(targetBytes, 0, targetBytes.Length);
    } else {
        response.StatusCode = 204;
    }
    return;
}

            // ====== 长轮询与增量获取聊天接口 ======
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                // 核心修复：任何请求聊天接口的用户（包括只打开未发言的）均记录在线 IP
                _activePingers[remoteIp] = senderId;

                string action = segments.Length > 2 ? segments[2].ToLower() : "";
                
                // 获取最后一条记录的时间，用于过滤增量数据
                long lastTime = 0;
                long.TryParse(request.QueryString["last"] ?? "0", out lastTime);
                
                // 等候时间，长轮询模式
                int.TryParse(request.QueryString["c"] ?? request.Headers["X-Chat-Wait"] ?? "0", out int waitSec);
                if (waitSec > 60) waitSec = 60;

                // POST /chat/send
                if (action == "send" && request.HttpMethod == "POST")
                {
                    string target = request.QueryString["a"] ?? Uri.UnescapeDataString(request.Headers["X-Chat-Target"] ?? "");
                    string content = "";
                    
                    // 【修改点】：增加编码兜底 Encoding.UTF8，防止 ContentEncoding 为 null 导致报错
                    System.Text.Encoding enc = request.ContentEncoding ?? System.Text.Encoding.UTF8;
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(request.InputStream, enc)) { 
                        content = reader.ReadToEnd(); 
                    }
                    
                    if (!string.IsNullOrEmpty(content))
                    {
                        lock (_chatHistory) {
                            _chatHistory.Add(new ChatMessage {
                                Sender = senderId, 
                                SenderName = richSenderName, // ====== 新增：记录完整富文本身份 ======
                                Time = DateTime.Now.ToString("HH:mm:ss"), 
                                Target = target, 
                                Content = content,
                                Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                            });
                        }
                    }
                }

                // 长轮询等待逻辑 (Send后或者Get时都可以等待新消息)
                if (waitSec > 0)
                {
                    int elapsed = 0;
                    while (elapsed < waitSec * 5) // 每200ms检查一次
                    {
                        await Task.Delay(200);
                        elapsed++;
                        RefreshActivity(); 
                        bool hasNew = false;
                        lock (_chatHistory) { 
                            if (_chatHistory.Count > 0 && _chatHistory.Last().Timestamp > lastTime) hasNew = true; 
                        }
                        if (hasNew) break;
                    }
                }
                string jsonResult = GetVisibleChatMessagesJson(senderId, lastTime);
                response.ContentType = "application/json; charset=utf-8";
                byte[] b = Encoding.UTF8.GetBytes(jsonResult);
                response.ContentLength64 = b.Length;
                response.OutputStream.Write(b, 0, b.Length);
                return;
            }


            // ====== 【新增：信任列表前端状态接口 (用于解锁被隐藏的UI)】 ======
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("truststatus", StringComparison.OrdinalIgnoreCase))
{
    response.StatusCode = 200;
    response.ContentType = "text/plain; charset=utf-8";
    // 如果在信任列表，返回 true
    byte[] b = Encoding.UTF8.GetBytes(isTrusted ? "true" : "false");
    response.ContentLength64 = b.Length;
    response.OutputStream.Write(b, 0, b.Length);
    return;
}
            // ====== 【新增：获取自身ID的后台接口 (解决大家都是主机ID的问题)】 ======
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("whoami", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 200;
                response.ContentType = "application/json; charset=utf-8"; // 改为返回 JSON
                // ID 用于底层权限判断，Name 用于前端展示
                string jsonRes = $"{{\"id\":\"{senderId}\", \"name\":\"{richSenderName}\"}}";
                byte[] b = Encoding.UTF8.GetBytes(jsonRes);
                response.ContentLength64 = b.Length;
                response.OutputStream.Write(b, 0, b.Length);
                return;
            }
// ====== 【功能3：服务器新增剪贴板后台指令】 ======
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("clip", StringComparison.OrdinalIgnoreCase))
            {
                if (!isTrusted) {
                return;}
                string action = segments.Length > 2 ? segments[2].ToLower() : "";
                response.StatusCode = 200;
                
                if (action == "get") {
                    string txt = "";
                    Thread t = new Thread(() => { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); });
                    t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
                response.ContentType = "text/plain; charset=utf-8";
                    byte[] b = Encoding.UTF8.GetBytes(txt);
                    response.ContentLength64 = b.Length;
                    response.OutputStream.Write(b, 0, b.Length);
                }
                else if (action == "getsel") {
                    SendKeys.SendWait("^c");
                    Thread.Sleep(150); // 略作延时等待系统写入
                    string txt = "";
                    Thread t = new Thread(() => { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); });
                    t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
                response.ContentType = "text/plain; charset=utf-8";
                    byte[] b = Encoding.UTF8.GetBytes(txt);
                    response.ContentLength64 = b.Length;
                    response.OutputStream.Write(b, 0, b.Length);
                }
// ===== 【新增：getfiledrop 获取剪贴板文件列表映射词典】 =====
                else if (action == "getfiledrop") {
                    // 参数1解析（是否发送Ctrl+C）：默认 true
                    string? p1Str = request.QueryString["p1"] ?? request.QueryString["sendKey"] ?? request.QueryString["copy"];
                    bool sendKey = true;
                    if (!string.IsNullOrWhiteSpace(p1Str)) {
                        p1Str = p1Str.Trim().ToLower();
                        sendKey = !(p1Str == "false" || p1Str == "0" || p1Str == "no");
                    }

                    // 参数2解析（是否仅筛选文件）：默认 true
                    string? p2Str = request.QueryString["p2"] ?? request.QueryString["fileOnly"] ?? request.QueryString["onlyFile"];
                    bool fileOnly = true;
                    if (!string.IsNullOrWhiteSpace(p2Str)) {
                        p2Str = p2Str.Trim().ToLower();
                        fileOnly = !(p2Str == "false" || p2Str == "0" || p2Str == "no");
                    }

                    // 1. 如果需要发送快捷键，先执行复制
                    if (sendKey) {
                        SendKeys.SendWait("^c");
                        Thread.Sleep(150); // 延时等待系统将文件路径写入剪贴板
                    }

                    // 2. 从 STA 线程读取剪贴板中的文件列表
                    List<string> rawPaths = new List<string>();
                    Thread t = new Thread(() => {
                        if (Clipboard.ContainsFileDropList()) {
                            var files = Clipboard.GetFileDropList();
                            if (files != null) {
                                foreach (string? f in files) {
                                    if (!string.IsNullOrEmpty(f)) rawPaths.Add(f);
                                }
                            }
                        }
                    });
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                    t.Join();

                    // 3. 根据参数2按文件/目录条件筛选
                    IEnumerable<string> filteredPaths = fileOnly 
                        ? rawPaths.Where(p => File.Exists(p)) 
                        : rawPaths.Where(p => File.Exists(p) || Directory.Exists(p));

                    // 4. 优先复用 _pathToTokenMap，没有则生成新 Token 并加入映射字典
                    Dictionary<string, string> resultDict = new Dictionary<string, string>();
                    foreach (string p in filteredPaths) {
                        // SetImageAndGetUrl 内部会自动判断 _pathToTokenMap 是否已存在该路径对应的 Token，
                        // 存在则复用现有 Token，不存在则生成新 Token 并写入字典与 SharedFiles
                        string url = SetImageAndGetUrl(p);
                        resultDict[p] = url;
                    }

                    // 5. 序列化为 JSON 字典返回
                    StringBuilder sbJson = new StringBuilder();
                    sbJson.Append("{");
                    int count = 0;
                    foreach (var kvp in resultDict) {
                        if (count > 0) sbJson.Append(",");
                        string escKey = kvp.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string escVal = kvp.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        sbJson.Append($"\"{escKey}\":\"{escVal}\"");
                        count++;
                    }
                    sbJson.Append("}");

                    response.StatusCode = 200;
                    response.ContentType = "application/json; charset=utf-8";
                    byte[] b = Encoding.UTF8.GetBytes(sbJson.ToString());
                    response.ContentLength64 = b.Length;
                    response.OutputStream.Write(b, 0, b.Length);
                }
                else if (action == "getselhtml")
{
    string text = "";
    string url = "";
    string htmlContent = "";

    try
    {
        // 1. 模拟 Ctrl+C 复制当前选中内容（同 getsel）
        SendKeys.SendWait("^c");
        Thread.Sleep(150); // 等待剪贴板写入完成

        // 2. 获取剪贴板纯文本、HTML
        Thread clipboardThread = new Thread(() =>
{
    try
    {
        // 1. 获取纯文本：无参重载所有旧框架都支持，兼容ANSI/Unicode所有纯文本格式
        if (System.Windows.Forms.Clipboard.ContainsText())
        {
            text = System.Windows.Forms.Clipboard.GetText();
        }

        // 2. 获取HTML内容：旧框架WinForms原生支持TextDataFormat.Html枚举和对应重载
        if (System.Windows.Forms.Clipboard.ContainsText(System.Windows.Forms.TextDataFormat.Html))
        {
            htmlContent = System.Windows.Forms.Clipboard.GetText(System.Windows.Forms.TextDataFormat.Html);
        }
    }
    catch (Exception ex)
    {
        // 剪贴板是全局资源，可能被其他进程占用，这里可以做重试或异常处理
    }
});
clipboardThread.SetApartmentState(ApartmentState.STA);
clipboardThread.Start();
clipboardThread.Join(); // 同步等待剪贴板操作完成，拿到结果

        // 4. 如果提取到了 HTML 内容，将其映射到内存/HTTP 服务中
        if (!string.IsNullOrEmpty(htmlContent))
        {
            // 生成唯一标识符string htmlId = Guid.NewGuid().ToString("N");
            
            // 补全 HTML 页面结构与沙盒防御样式（确保图片自适应、布局不溢出）
            string fullPageHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>剪贴板 HTML 预览</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background-color: #fff;
            padding: 16px;
            color: #333;
            line-height: 1.6;
            word-break: break-all;
        }}
        img, video, iframe {{
            max-width: 100% !important;
            height: auto !important;
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
            margin: 10px 0;
        }}
        th, td {{
            border: 1px solid #cbd5e0;
            padding: 8px;
            text-align: left;
        }}
    </style>
</head>
<body>
    {htmlContent}
</body>
</html>";

            // 将 HTML 内容存入内存字典或静态缓存（供 HTTP 服务端路由读取）
            copyHtmlMemoryCache=fullPageHtml;

            // 5. 生成对应的访问 URL
            string htmlId = Guid.NewGuid().ToString("N");
            url = $"http://{EffectiveIP}:{Port}/qkLANfile/copyview/{htmlId}";
        }else{ copyHtmlMemoryCache=null;}
    }
    catch (Exception ex)
    {
        // 异常容错处理
        if (string.IsNullOrEmpty(text)) text = "异常";
        url = "";
copyHtmlMemoryCache=null;
    }
// 6. 返回 JSON 格式的数据
var responseObj = new { text, url };
                    string responsetxt = JsonConvert.SerializeObject(responseObj);
                response.ContentType = "application/json; charset=utf-8";
    byte[] copyHtmljsonmsg = Encoding.UTF8.GetBytes(responsetxt);
    response.ContentLength64 = copyHtmljsonmsg.Length; 
    response.OutputStream.Write(copyHtmljsonmsg, 0, copyHtmljsonmsg.Length);
    return;
}
                else if (action == "set" && request.HttpMethod == "POST") {
                    using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string txt = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(txt)) {
                            Thread t = new Thread(() => { Clipboard.SetText(txt); });
                            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
                        }
                    }
                    byte[] b = Encoding.UTF8.GetBytes("OK");
                response.ContentLength64 = b.Length;
                    response.OutputStream.Write(b, 0, b.Length);
                }
                // ===== 【新增：setpas 写入剪贴板并模拟 Ctrl+V 粘贴】 =====
                else if (action == "setpas" && request.HttpMethod == "POST") {
                    using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string txt = reader.ReadToEnd();
                        if (!string.IsNullOrEmpty(txt)) {
                            Thread t = new Thread(() => { Clipboard.SetText(txt); });
                            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
                            Thread.Sleep(100); // 留出写入缓冲时间
                            SendKeys.SendWait("^v"); // 模拟 Ctrl+V 粘贴
                        }
                    }
                    byte[] b = Encoding.UTF8.GetBytes("OK");
                    response.ContentLength64 = b.Length;
                    response.OutputStream.Write(b, 0, b.Length);
                }
                return;
            }

// ====== 【新增：服务器鼠标控制指令接口】 ======
if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("mouse", StringComparison.OrdinalIgnoreCase))
{
    if (!isTrusted) return;

    if (request.HttpMethod == "POST")
    {
        using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            string scriptText = reader.ReadToEnd();
            if (!string.IsNullOrEmpty(scriptText))
            {
                ExecuteMouseScript(scriptText);
            }
        }
        byte[] b = Encoding.UTF8.GetBytes("OK");
        response.StatusCode = 200;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = b.Length;
        response.OutputStream.Write(b, 0, b.Length);
    }
    return;
}

            // --- 自定义域名/别名路由拦截 ---
            if (!string.IsNullOrEmpty(CustomAlias) && segments.Length > 0 && segments[0].Equals(CustomAlias.Trim('/'), StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1)
                {
                    // 【优先响应内存虚拟网页】
                    if (!string.IsNullOrEmpty(VirtualHtmlContent)) 
                    {
                        response.ContentType = "text/html; charset=utf-8";
                        byte[] htmlBytes = Encoding.UTF8.GetBytes(VirtualHtmlContent);
                        response.ContentLength64 = htmlBytes.Length;
                        response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                        return;
                    }
                    // 兼容原本的物理文件逻辑（如果源码为空）
                    else if (!string.IsNullOrEmpty(CurrentReportPath) && File.Exists(CurrentReportPath))
                    {
                        response.ContentType = "text/html; charset=utf-8";
                        using (FileStream fs = new FileStream(CurrentReportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            response.ContentLength64 = fs.Length;
                            fs.CopyTo(response.OutputStream);
                        }
                        return;
                    }
                    else 
                    {
                        SendError(response, HttpStatusCode.NotFound, "Report not found.");
                        return;
                    }
                }
            }

            // 修改了对上传请求的判定结构，确保被信任用户或者携带有效请求的方法被稳定捕捉
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("upload", StringComparison.OrdinalIgnoreCase))
            {
                if (request.HttpMethod == "POST") HandleUpload(request, response, segments, isTrusted);
                return; // 强行阻断防止掉入底部的 404
            }


// ====== 【新增：虚拟网页内存路由 (无自定义别名时使用)】 ======
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("index", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(VirtualHtmlContent))
                {
                    response.ContentType = "text/html; charset=utf-8";
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(VirtualHtmlContent);
                    response.ContentLength64 = htmlBytes.Length;
                    response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                    return;
                }
            }

// 处理请求路由 /qkLANfile/copyview
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("copyview", StringComparison.OrdinalIgnoreCase))
{
    
    if (!string.IsNullOrEmpty(copyHtmlMemoryCache))
    {
        byte[] buffer = Encoding.UTF8.GetBytes(copyHtmlMemoryCache);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
copyHtmlMemoryCache=null;
    }
    else
    {
        response.StatusCode = 404;
    }
}

// ====== 【新增：Screen Control (SC) 屏幕控制网页路由】 ======
            if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("sc", StringComparison.OrdinalIgnoreCase))
            {
                // 1. 验证信任列表，只有 isTrusted 为 true 才能访问
                if (!isTrusted) 
                {
                    SendError(response, HttpStatusCode.Forbidden, "Access Denied: 您当前的 IP 不在信任列表中，无法访问屏幕控制面板。");
                    return;
                }

                // 2. 检查并返回 SC 网页源码
                if (!string.IsNullOrEmpty(ScreenControlHtmlContent))
                {
                    response.ContentType = "text/html; charset=utf-8";
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(ScreenControlHtmlContent);
                    response.ContentLength64 = htmlBytes.Length;
                    response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                }
                else
                {
                    SendError(response, HttpStatusCode.NotFound, "SC Web page source not configured.");
                }
                return; // 结束处理
            }




            // 文件下载与获取流的路由逻辑提取 （注意：如果 URL 带了查询参数，AbsolutePath 不包含 Query，因此 segments 里的 token 不受影响）
string token = "";
if (segments.Length >= 2)
{
    token = segments[1];
}

// 如果包含查询参数，token 可能是带有 ? 后缀的，需要安全剥离干净
int qIndex = token.IndexOf('?');
if (qIndex >= 0) token = token.Substring(0, qIndex);

if (_memoryFiles.TryGetValue(token, out byte[]? memBytes))
{
    try {
        string fileName = segments.Length >= 3 ? Uri.UnescapeDataString(segments[2]) : "image.png";
        int fqIndex = fileName.IndexOf('?');
        if (fqIndex >= 0) fileName = fileName.Substring(0, fqIndex);
        response.ContentType = _memoryFileMime.TryGetValue(token, out string? mime) && mime != null ? mime : "application/octet-stream";
        bool useInline = Content_inline;
        string qInline = request.QueryString["inline"];
        string qDownload = request.QueryString["download"];
        if (!string.IsNullOrEmpty(qDownload) && (qDownload.Equals("true", StringComparison.OrdinalIgnoreCase) || qDownload == "1")) useInline = false;
        else if (!string.IsNullOrEmpty(qInline)) bool.TryParse(qInline, out useInline);
        string dispositionType = useInline ? "inline" : "attachment";
        response.AddHeader("Content-Disposition", $"{dispositionType}; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");
        
        // ====== 【新增：内存文件的断点续传支持】 ======
        response.AddHeader("Accept-Ranges", "bytes"); 
        
        long fileLength = memBytes.Length;
        long startByte = 0;
        long endByte = fileLength - 1;
        bool isPartial = false;

        string rangeHeader = request.Headers["Range"];
        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            string[] range = rangeHeader.Substring(6).Split('-');
            if (range.Length >= 1 && long.TryParse(range[0], out long start)) startByte = start;
            if (range.Length >= 2 && long.TryParse(range[1], out long end)) endByte = end;
            isPartial = true;
        }

        if (startByte > endByte || startByte >= fileLength)
        {
            response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
            response.AddHeader("Content-Range", $"bytes */{fileLength}");
            return;
        }

        if (isPartial)
        {
            response.StatusCode = (int)HttpStatusCode.PartialContent;
            response.AddHeader("Content-Range", $"bytes {startByte}-{endByte}/{fileLength}");
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.OK;
        }

        // ====== 【修改后】：内存文件传输（增加断连捕获与异步写入） ======
long contentLength = endByte - startByte + 1;
response.ContentLength64 = contentLength;

try
{
    // 使用 WriteAsync 代替 Write，捕获客户端中途断开
    await response.OutputStream.WriteAsync(memBytes, (int)startByte, (int)contentLength);
}
catch (HttpListenerException) { /* 客户端主动断开连接，忽略即可 */ }
catch (IOException) { /* 管道破裂 Broken Pipe，忽略即可 */ }
catch (Exception) { /* 其他未知传输异常 */ }
return;
    } catch { }
}
else if (_tokenToPathMap.TryGetValue(token, out string? imagePath) && imagePath != null && File.Exists(imagePath))
{
                // 下载权限拦截 (信任列表绕过)
                if (!isTrusted && !EnableDownloads)
                {
                    SendError(response, HttpStatusCode.Forbidden, "服务器当前已禁用下载功能。");
                    return;
                }

    try
    {
        // ====== 【核心行为控制逻辑】 ======
        bool useInline = Content_inline; // 默认采用全局保底配置
        
        string qInline = request.QueryString["inline"];
        string qDownload = request.QueryString["download"];

        if (!string.IsNullOrEmpty(qDownload) && (qDownload.Equals("true", StringComparison.OrdinalIgnoreCase) || qDownload == "1"))
        {
            useInline = false; // 强制下载
        }
        else if (!string.IsNullOrEmpty(qInline))
        {
            bool.TryParse(qInline, out useInline); // 显式指定 inline 状态
        }

        string dispositionType = useInline ? "inline" : "attachment";
        string fileName = Path.GetFileName(imagePath);
        
        // 设置响应头控制浏览器行为
        response.ContentType = GetMimeType(imagePath);
        response.AddHeader("Content-Disposition", $"{dispositionType}; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");

        // ====== 【新增：支持视频/音频的断点续传 (Range 请求)】 ======
        response.AddHeader("Accept-Ranges", "bytes"); // 声明支持断点续传

        long fileLength = new FileInfo(imagePath).Length;
        long startByte = 0;
        long endByte = fileLength - 1;
        bool isPartial = false;

        // 解析浏览器传来的 Range 请求头
        string rangeHeader = request.Headers["Range"];
        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
        {
            string[] range = rangeHeader.Substring(6).Split('-');
            if (range.Length >= 1 && long.TryParse(range[0], out long start)) startByte = start;
            if (range.Length >= 2 && long.TryParse(range[1], out long end)) endByte = end;
            isPartial = true;
        }

        // 处理超出范围的请求
        if (startByte > endByte || startByte >= fileLength)
        {
            response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable; // 416
            response.AddHeader("Content-Range", $"bytes */{fileLength}");
            return;
        }

        // 根据是否为部分请求设置不同的状态码和响应头
        if (isPartial)
        {
            response.StatusCode = (int)HttpStatusCode.PartialContent; // 206
            response.AddHeader("Content-Range", $"bytes {startByte}-{endByte}/{fileLength}");
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.OK; // 200
        }

        // ====== 【修改后】：磁盘文件传输（改用异步+断连精准 break 释放线程） ======
long contentLength = endByte - startByte + 1;
response.ContentLength64 = contentLength;

// 1. 使用 FileShare.ReadWrite 防止文件被占用时读取失败
using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
{
    if (startByte > 0) fs.Seek(startByte, SeekOrigin.Begin);
    
    byte[] buffer = new byte[64 * 1024]; // 64KB 缓冲区
    long bytesRemaining = contentLength;
    
    while (bytesRemaining > 0)
    {
        int bytesToRead = (int)Math.Min(buffer.Length, bytesRemaining);
        int read = await fs.ReadAsync(buffer, 0, bytesToRead);
        if (read == 0) break;
        
        try
        {
            // 2. 异步写入客户端
            await response.OutputStream.WriteAsync(buffer, 0, read);
            await response.OutputStream.FlushAsync();
        }
        catch (HttpListenerException)
        {
            // 3. 【核心修复】：客户端（浏览器切歌/拖进度条）强行掐断了连接
            // 此时必须立即 break 跳出循环，结束当前 HTTP 请求处理线程
            break; 
        }
        catch (IOException)
        {
            // 客户端连接重置 / Broken Pipe
            break;
        }
        catch (ObjectDisposedException)
        {
            // OutputStream 已被关闭
            break;
        }
        
        bytesRemaining -= read;
    }
}
return;
    }
    catch { /* 异常处理 */ }
}
            else SendError(response, HttpStatusCode.NotFound, "Resource not found.");
        }
        catch { }
        finally
        {
            try { response.OutputStream.Close(); } catch { }
            try { response.Close(); } catch { } 
        }
    }

/// <summary>
/// 从系统剪贴板 HTML 字符串中提取真实的 HTML 片段
/// </summary>
private string ExtractHtmlFragment(string rawHtml)
{
    if (string.IsNullOrEmpty(rawHtml)) return "";

    int startIdx = rawHtml.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
    int endIdx = rawHtml.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);

    if (startIdx != -1 && endIdx != -1 && endIdx > startIdx)
    {
        startIdx += "<!--StartFragment-->".Length;
        return rawHtml.Substring(startIdx, endIdx - startIdx).Trim();
    }

    // 备用解析：如果没有 Fragment 标记，查找 <html 或 <body 标签
    int bodyIdx = rawHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
    if (bodyIdx != -1)
    {
        return rawHtml.Substring(bodyIdx);
    }

    return rawHtml;
}

// ====== 【新增：维持后台预抓取循环】 ======
private void EnsureBackgroundCaptureRunning()
{
    lock (_screenshotLock) {
        if (_isScreenshotLoopActive) return;
        _isScreenshotLoopActive = true;
    }

    Task.Run(async () => {
        try {
            while (true) {
                // 1. 无人观看不浪费资源：若超过 2 秒没有收到任何截图请求，自动停止后台循环
                lock (_screenshotLock) {
                    if ((DateTime.Now - _lastScreenshotRequestTime).TotalSeconds > 2) {
                        _isScreenshotLoopActive = false;
                        _screenshotMemory = null;
                        break;
                    }
                }

                // 2. 频次安全阀：确保距上次截图完成的间隔不低于 ScreenshotCacheTtlMs
                double elapsedMs = (DateTime.Now - _lastScreenshotCapturedTime).TotalMilliseconds;
                int waitMs = (int)(ScreenshotCacheTtlMs - elapsedMs);
                if (waitMs > 0) {
                    await Task.Delay(waitMs);
                }

                // 3. 在后台预先发起下一次截图操作
                await TriggerScreenshotAsync();
            }
        } finally {
            lock (_screenshotLock) {
                _isScreenshotLoopActive = false;
            }
        }
    });
}

// ====== 【 Quicker 调用的单例触发器】 ======
private async Task TriggerScreenshotAsync()
{
    if (!await _screenshotSemaphore.WaitAsync(0)) return;

    TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_screenshotLock) {
        _currentScreenshotTcs = tcs;
    }

    try {
        if (QuickerContext != null) {
            var myInputs = new Dictionary<string, object> {
                { "IP", EffectiveIP },
                { "Port", Port }
            };
            
            var runTask = QuickerContext.RunSpAsync("截图子程序", myInputs);
            if (await Task.WhenAny(runTask, Task.Delay(2500)) == runTask) {
                await runTask;
            }
        }
    } catch { 
        /* 忽略单次异常 */ 
    } finally {
        _screenshotSemaphore.Release();
        tcs.TrySetResult(true);
    }
}

private string GetVisibleChatMessagesJson(string senderId, long lastTime)
    {
        List<ChatMessage> listToReturn;
        lock (_chatHistory)
        {
            var query = _chatHistory.Where(m => {
                if (m.Timestamp <= lastTime) return false; // 基于时间戳过滤旧消息
                if (string.IsNullOrWhiteSpace(m.Target)) return true;
                if (m.Sender == senderId) return true;
                string[] parts = m.Target.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts) {
                    if (p.Contains("-")) {
                        var range = p.Split('-');
                        if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end) && int.TryParse(senderId, out int myNum))
                            if (myNum >= start && myNum <= end) return true;
                    } else {
                        if (p.Trim() == senderId) return true;
                    }
                }
                return false;
            });
            
            // 首次请求（lastTime=0）只返回最后30条避免刷屏，增量请求则.TakeLast(60)
            // 修改后（兼容V1旧版本 C# Skip）
            int takeCount = (lastTime == 0) ? 30 : 60;
            int totalCount = query.Count();
            listToReturn = query.Skip(Math.Max(0, totalCount - takeCount)).ToList();
        }

        StringBuilder sbJson = new StringBuilder();
        sbJson.Append("[");
        for (int i = 0; i < listToReturn.Count; i++) {
            var m = listToReturn[i];
            string escContent = m.Content.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            sbJson.Append($"{{\"Sender\":\"{m.Sender}\",\"SenderName\":\"{m.SenderName}\",\"Time\":\"{m.Time}\",\"Target\":\"{m.Target}\",\"Content\":\"{escContent}\",\"Timestamp\":{m.Timestamp}}}");
            if (i < listToReturn.Count - 1) sbJson.Append(",");
        }
        sbJson.Append("]");
        return sbJson.ToString();
    }

    private void HandleUpload(HttpListenerRequest request, HttpListenerResponse response, string[] segments, bool isTrusted)
    {
    try
    {
        // 【修复】如果没传第三个参数(例如只请求了 /upload)，强制当做 default 处理
        string tokenOrAction = (segments.Length > 2 && !string.IsNullOrWhiteSpace(segments[2])) ? segments[2] : "default";
        
        string targetDir = "";
            bool isMemory = false;

        // ====== 【新增：截图上传专属通道 (存入独立内存)】 ======
        if (tokenOrAction == "screenshot")
        {
            if (!isTrusted) { SendError(response, HttpStatusCode.Forbidden, "Untrusted IP."); return; }

            // 【修改】获取请求头中的文件名，若未携带则使用默认名称
            string shotfileName = Uri.UnescapeDataString(request.Headers["X-File-Name"] ?? ("screenshot_" + DateTime.Now.ToString("HHmmss") + ".jpg"));

            using (MemoryStream ms = new MemoryStream()) {
                request.InputStream.CopyTo(ms);
                byte[] fileBytes = ms.ToArray();
                
                lock (_screenshotLock) {
                _screenshotMemory = fileBytes;
                _screenshotFileName = shotfileName;
                _lastScreenshotCapturedTime = DateTime.Now; // 【关键】记录最新的截图完成时刻
                  }
                
                string shotUrl = $"http://{EffectiveIP}:{Port}/qkLANfile/api/screenshot"; 
                string shotPath = "Memory_Screenshot";
                long shotSize = fileBytes.Length;
                long shotTicks = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                // 返回与普通上传一致格式的 JSON，满足子程序运行完成后的合法性验证要求
                byte[] shotMsg = Encoding.UTF8.GetBytes($"{{\"{shotPath}\":\"{shotUrl}\", \"status\":\"ok\", \"file\":\"{shotfileName}\", \"url\":\"{shotUrl}\", \"size\":{shotSize}, \"date\":{shotTicks}, \"fullPath\":\"{shotPath}\"}}");
                response.ContentType = "application/json";
                response.StatusCode = 200;
                response.ContentLength64 = shotMsg.Length;
                response.OutputStream.Write(shotMsg, 0, shotMsg.Length);
            }
            return; // 结束处理，避免混入普通文件共享队列
        }

        if (tokenOrAction == "default")
        {
            targetDir = DefaultUploadDirectory ?? Path.GetTempPath();
        }
        else if (tokenOrAction == "chat")
            {
                // 聊天图片存放至系统 Users 的 Temp 临时文件夹改为存入内存字典
                isMemory = true;
            }
            else if (tokenOrAction == "chatfile")
            {
                // 要求 2：聊天室发送文件存入子文件夹且不进SharedFiles
                targetDir = Path.Combine(DefaultUploadDirectory ?? Path.GetTempPath(), "chatfiles");
            }
            else if (_tokenToPathMap.TryGetValue(tokenOrAction, out string? sourcePath))
            {
            targetDir = Directory.Exists(sourcePath) ? sourcePath : Path.GetDirectoryName(sourcePath) ?? "";
        }
        
        string fileName = Uri.UnescapeDataString(request.Headers["X-File-Name"] ?? "upload_" + DateTime.Now.ToString("HHmmss") + ".dat");
            string newUrl = "";
            string escapedPath = "";
            long size = 0;
            long dateTicks = 0;

            if (isMemory)
            {
                using (MemoryStream ms = new MemoryStream()) {
                    request.InputStream.CopyTo(ms);
                    byte[] fileBytes = ms.ToArray();
                    string token = Guid.NewGuid().ToString("N");
                    _memoryFiles[token] = fileBytes;
                    _memoryFileMime[token] = GetMimeType(fileName);
                    
                    string localIp = EffectiveIP;
                    newUrl = $"http://{localIp}:{Port}/qkLANfile/{token}/{Uri.EscapeDataString(fileName)}";
                    escapedPath = "Memory";
                    size = fileBytes.Length;
                    dateTicks = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                }
            }
            else
            {
                if (string.IsNullOrEmpty(targetDir)) 
                {
                    SendError(response, HttpStatusCode.BadRequest, "Upload directory not found or mapping invalid.");
                    return; 
                }

                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                string savePath = Path.Combine(targetDir, fileName);
                using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    request.InputStream.CopyTo(fs);
                }

                if (tokenOrAction == "chatfile")
                {
                    newUrl = RegisterTokenOnly(savePath); // 只注册Token，不加入SharedFiles
                    FileInfo fi = new FileInfo(savePath);
                    size = fi.Length;
                    dateTicks = ((DateTimeOffset)fi.LastWriteTime).ToUnixTimeMilliseconds();
                }
                else
                {
                    newUrl = SetImageAndGetUrl(savePath);
                }
                escapedPath = savePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            }

            // 返回更多属性以供前端构建气泡框
            byte[] resMsg = Encoding.UTF8.GetBytes($"{{\"{escapedPath}\":\"{newUrl}\", \"status\":\"ok\", \"file\":\"{fileName}\", \"url\":\"{newUrl}\", \"size\":{size}, \"date\":{dateTicks}, \"fullPath\":\"{escapedPath}\"}}");
            response.ContentType = "application/json";
            response.StatusCode = 200;
            response.ContentLength64 = resMsg.Length;
            response.OutputStream.Write(resMsg, 0, resMsg.Length);
        }
        catch (Exception ex) 
        { 
            SendError(response, HttpStatusCode.InternalServerError, "Upload error: " + ex.Message);
        }
    }

    private void CheckForIdleShutdown(object? state)
    {
        lock (_activityLock)
        {
            if (!_isRunning)
            {
                _shutdownTimer?.Dispose();
                _shutdownTimer = null;
                return;
            }
            if ((DateTime.Now - _lastActivityTime).TotalMilliseconds >= ShutdownDelayMs) Stop(); 
        }
    }

    // ====== 【新增：截图触发长轮询机制核心循环】 ======
    private void StartScreenshotLoopIfNeeded()
    {
        lock (_screenshotLock)
        {
            if (_isScreenshotLooping) return;
            _isScreenshotLooping = true;
        }

        Task.Run(async () => {
            try {
                while (true)
                {
                    lock (_screenshotLock) {
                        // 如果没有再收到长轮询请求2秒之后清空截图内存并结束循环
                        if ((DateTime.Now - _lastScreenshotCapturedTime).TotalMilliseconds > 2000) {
                            _screenshotMemory = null;
                            _screenshotFileName = null; // 【新增】清空文件名
                            break; 
                        }
                    }

                    _screenshotTcs = new TaskCompletionSource<byte[]>();

                    // 1. 准备给子程序的东西 (输入参数)
                    if (QuickerContext != null)
                    {
                        var myInputs = new Dictionary<string, object> {
                            { "IP", EffectiveIP },
                            { "Port", Port }
                        };
                        
                        try {
                            // 2. 呼叫子程序触发截图
                            var resultTask = QuickerContext.RunSpAsync("截图子程序", myInputs);
                            
                            // 等待截图产生（此时子程序会往 /upload/screenshot 发请求）
                            var uploadTask = _screenshotTcs.Task;
                            await Task.WhenAny(resultTask, uploadTask, Task.Delay(3000));
                            
                            if (resultTask.IsCompleted && !resultTask.IsFaulted) {
                                var result = resultTask.Result;
                                // 3. 子程序返回上传结果，提取 output验证结果
                                if (result != null && result.ContainsKey("output")) 
                                {
                                    var outputValue = result["output"]?.ToString();
                                    if (string.IsNullOrWhiteSpace(outputValue)) {
                                        // 子程序运行完了，但没返回 output 变量:终止截图
                                        break;
                                    }
                                }
                                else {
                                    // 没返回 output 变量:终止截图
                                    break;
                                }
                            }
                        } 
                        catch {
                            // 出错了:终止截图
                            break;
                        }
                    }

                    // 替换掉原本的 await Task.Delay(100);
                    // 只要前端一发请求，这里会瞬间通行，进入下一次截图！只要 _screenshotRequestedTcs 没被触发，循环就会在这里安静等待，不消耗任何 CPU。 (加入 WhenAny 2000ms 的目的是：如果没有请求，也能每 2 秒醒来一次判断是否该自动退出了)
                    await Task.WhenAny(_screenshotRequestedTcs.Task, Task.Delay(2000));
                    
                    // 重置请求信号旗，为下一帧的等待做准备
                    _screenshotRequestedTcs = new TaskCompletionSource<bool>();
                }
            }
            finally {
                lock (_screenshotLock) {
                    _isScreenshotLooping = false;
                }
            }
        });
    }

    public void Stop()
    {
        if (_isRunning)
        {
            _isRunning = false;
            _shutdownTimer?.Dispose();
            _shutdownTimer = null;
            if (_listener != null)
            {
                try { _listener.Abort(); } catch { } 
                _listener = null; 
            }
            // 【超时彻底销毁】：清理所有映射与内存数据，实现零残留
            _tokenToPathMap.Clear();
            _pathToTokenMap.Clear();
            SharedFiles.Clear();
            _chatHistory.Clear();
            CurrentReportToken = null;
            CurrentReportPath = null;
            _memoryFiles.Clear();
            _memoryFileMime.Clear();
            _activePingers.Clear();
            EmojiJson = "[]";
            _screenshotMemory = null;
            _screenshotFileName = null; // 【新增】
        }
    }

public void UpdateEmojis(List<string>? list)
    {
        if (list == null || list.Count == 0) return;
        List<string> jsonItems = new List<string>();
        foreach (string item in list)
        {
            if (File.Exists(item))
            {
                try {
                    byte[] bytes = File.ReadAllBytes(item);
                    string token = Guid.NewGuid().ToString("N");
                    _memoryFiles[token] = bytes;
                    _memoryFileMime[token] = GetMimeType(item);
                    string localIp = EffectiveIP;
                    string url = $"http://{localIp}:{Port}/qkLANfile/{token}/{Uri.EscapeDataString(Path.GetFileName(item))}";
                    jsonItems.Add($"{{\"type\":\"image\", \"url\":\"{url}\", \"shortStr\":\"[图]\"}}");
                } catch { }
            }
            else
            {
                string escaped = item.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                string shortStr = item.Length > 2 ? item.Substring(0, 2) : item;
                string escShort = shortStr.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                jsonItems.Add($"{{\"type\":\"text\", \"text\":\"{escaped}\", \"shortStr\":\"{escShort}\"}}");
            }
        }
        EmojiJson = "[" + string.Join(",", jsonItems) + "]";
    }

    public string SetImageAndGetUrl(string localPath)
    {
        string normalizedPath = Path.GetFullPath(localPath).ToLowerInvariant();
        string token = _pathToTokenMap.TryGetValue(normalizedPath, out string? existingToken) && existingToken != null ? existingToken : Guid.NewGuid().ToString("N");
        
        _tokenToPathMap[token] = localPath;
        _pathToTokenMap[normalizedPath] = token;

        string localIp = EffectiveIP;
        string url = $"http://{localIp}:{Port}/qkLANfile/{token}/{Uri.EscapeDataString(Path.GetFileName(localPath))}";
        
        // 自动同步到供 API 查询的字典
        bool isDir = Directory.Exists(localPath);
        FileInfo fi = isDir ? null : new FileInfo(localPath);
        DirectoryInfo di = isDir ? new DirectoryInfo(localPath) : null;
    // 【修复2】将 .NET Ticks 转换为 JavaScript 可识别的 Unix 毫秒时间戳
    DateTime fileTime = isDir ? di!.LastWriteTime : fi!.LastWriteTime;
    long unixMs = ((DateTimeOffset)fileTime).ToUnixTimeMilliseconds();
        SharedFiles[localPath] = new SharedFileInfo {
            Path = localPath, Url = url, IsDir = isDir, Token = token,
            Size = isDir ? 0 : fi!.Length,
            DateTicks = unixMs // 传入毫秒数，前端用 new Date(item.date) 即可完美显示正常时间
        };

        return url;
    }

    // 仅注册 Token 映射，但不加入 SharedFiles 共享列表（专供聊天临时图片使用）
    public string RegisterTokenOnly(string localPath)
    {
        string normalizedPath = Path.GetFullPath(localPath).ToLowerInvariant();
        string token = _pathToTokenMap.TryGetValue(normalizedPath, out string? existingToken) && existingToken != null ? existingToken : Guid.NewGuid().ToString("N");
        
        _tokenToPathMap[token] = localPath;
        _pathToTokenMap[normalizedPath] = token;
 
        string localIp = EffectiveIP;
        string url = $"http://{localIp}:{Port}/qkLANfile/{token}/{Uri.EscapeDataString(Path.GetFileName(localPath))}";
        return url;
    }

    public Dictionary<string, string> GetAllMappings()
    {
        var dict = new Dictionary<string, string>();
        foreach (var kvp in _tokenToPathMap)
        {
            dict[kvp.Value] = kvp.Key; 
        }
        return dict;
    }

    public void RemoveTokenAndFile(string token)
    {
        if (_tokenToPathMap.TryRemove(token, out string? path))
        {
            string normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
            _pathToTokenMap.TryRemove(normalizedPath, out _);
            
            if (File.Exists(path) && path.EndsWith(".html") && path.Contains("Temp"))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
    
    private void SendError(HttpListenerResponse response, HttpStatusCode statusCode, string message)
{
    try
    {
        response.StatusCode = (int)statusCode;
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = buffer.Length;
        // 使用 try-catch 保护错误信息的写入
        response.OutputStream.Write(buffer, 0, buffer.Length);
    }
    catch 
    { 
        // 客户端已断开时，忽略发送失败
    }
}
    
    private string GetMimeType(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "application/octet-stream";
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            // 图片
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif",
            ".bmp" => "image/bmp", ".svg" => "image/svg+xml", ".webp" => "image/webp",
            // 视频和音频 (新增)
            ".mp4" => "video/mp4", ".webm" => "video/webm", ".ogg" => "video/ogg",
            ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".m4a" => "audio/mp4", ".flac" => "audio/flac",
            // 文档与其他
            ".pdf" => "application/pdf", ".txt" => "text/plain", ".html" or ".htm" => "text/html",
            ".json" => "application/json", ".xml" => "application/xml", ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed", _ => "application/octet-stream"
        };
    }
    
    public class NetworkHelper
    {
        public static string GetLocalIPv4()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint?.Address.ToString() ?? "127.0.0.1";
                }
            }
            catch { return "127.0.0.1"; }
        }
    }

    // ====== 【新增：Win32 鼠标 API 声明与解析执行方法】 ======
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

// 鼠标事件标志常量
    private const int MOUSEEVENTF_MOVE = 0x0001; // ===== 【新增：鼠标移动消息】 =====
    private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const int MOUSEEVENTF_LEFTUP = 0x0004;
    private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const int MOUSEEVENTF_RIGHTUP = 0x0010;
    private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const int MOUSEEVENTF_WHEEL = 0x0800;
    private const int MOUSEEVENTF_HWHEEL = 0x01000;

    public static void ExecuteMouseScript(string rawScript)
    {
        if (string.IsNullOrEmpty(rawScript)) return;

        string[] scriptLines = rawScript.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in scriptLines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

            var parts = line.Split(new[] { ':' }, 2);
            string cmd = parts[0].ToUpper();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "DL": // 等待时间 (毫秒)
                    if (int.TryParse(arg, out int ms))
                    {
                        Thread.Sleep(ms);
                    }
                    break;

                case "MVP": // 鼠标移动到指定坐标
                    var coords = arg.Split(',');
                    if (coords.Length == 2 && int.TryParse(coords[0], out int x) && int.TryParse(coords[1], out int y))
                    {
                        SetCursorPos(x, y);
                        // 【核心修复】：SetCursorPos 不会触发 WM_MOUSEMOVE 消息，网页和 Excel 拖选必须依赖此事件！
                        mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, 0);
                    }
                    break;

                case "MD": // 鼠标按下
                    HandleMouseAction(arg, "DOWN");
                    break;

                case "MU": // 鼠标抬起
                    HandleMouseAction(arg, "UP");
                    break;

                case "MC": // 鼠标点击
                    HandleMouseAction(arg, "CLICK");
                    break;

                case "MW": // 垂直滚轮滚动
                    if (int.TryParse(arg, out int vScroll))
                    {
                        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, vScroll * 4, 0);
                    }
                    break;

                case "MH": // 水平滚轮滚动
                    if (int.TryParse(arg, out int hScroll))
                    {
                        mouse_event(MOUSEEVENTF_HWHEEL, 0, 0, hScroll * 4, 0);
                    }
                    break;
            }
        }
    }

    private static void HandleMouseAction(string buttonType, string action)
    {
        if (string.IsNullOrEmpty(buttonType)) buttonType = "Left";

        int downFlag = MOUSEEVENTF_LEFTDOWN;
        int upFlag = MOUSEEVENTF_LEFTUP;

        string bt = buttonType.ToLower();
        if (bt == "right")
        {
            downFlag = MOUSEEVENTF_RIGHTDOWN;
            upFlag = MOUSEEVENTF_RIGHTUP;
        }
        else if (bt == "middle")
        {
            downFlag = MOUSEEVENTF_MIDDLEDOWN;
            upFlag = MOUSEEVENTF_MIDDLEUP;
        }

        if (action == "DOWN")
        {
            mouse_event(downFlag, 0, 0, 0, 0);
            // 【核心修复】：按下后暂停 30ms，给 Excel/网页 UI 线程建立鼠标捕获(Mouse Capture)的时间
            Thread.Sleep(30);
        }
        else if (action == "UP")
        {
            mouse_event(upFlag, 0, 0, 0, 0);
            Thread.Sleep(30);
        }
        else if (action == "CLICK")
        {
            mouse_event(downFlag, 0, 0, 0, 0);
            Thread.Sleep(30);
            mouse_event(upFlag, 0, 0, 0, 0);
        }
    }
//====鼠标 API END==========

}


// =============================================================
// 辅助目录检测方法
private static bool IsPathAllowedForUpload(string targetDir, List<string>? allowedDirs)
{
    if (allowedDirs == null || allowedDirs.Count == 0) return true;
    if (string.IsNullOrWhiteSpace(targetDir)) return false;

    try
    {
        string targetFull = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var dir in allowedDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            string allowedFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (targetFull.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase)) return true;
        }
    }
    catch { return false; }
    return false;
}
