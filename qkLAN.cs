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
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows.Forms;
//增加断点续传和网络管道破裂（Broken pipe）导致异常处理
#nullable enable 
#pragma warning disable CS4014 // 屏蔽异步未 await 警告 兼容V1
#pragma warning disable CS8602 // 屏蔽解引用可能为空的引用警告
#pragma warning disable CS8600 // 屏蔽将 null 或可空类型转换为非可空类型警告 

public static void Exec(Quicker.Public.IStepContext context)
{
    
    string caozuo = context.GetVarValue("操作") as string ?? "";
    var port_Str = context.GetVarValue("端口号").ToString();//通常为 8088数字ToString，
    
    if(caozuo == "重置") {
        try {
        // 向可能存活的幽灵实例发送关闭指令
        using (var client = new System.Net.WebClient()) {
            client.DownloadString("http://127.0.0.1:"+port_Str+"/qkLANfile/shutdown");
        }
        MessageBox.Show("已通过网络指令成功强制关闭后台旧服务！", "重置成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    } 
    catch {
        // 如果报错，说明原本就没有服务占用 8088 端口，静默忽略即可
    }
    
    // 顺手清理当前上下文的新实例
    LocalImageServer.Instance.Reset();
    return;
    }
    
    var palb = context.GetVarValue("path_列表") as List<string>;
    Dictionary<string, object> Purlcd = new Dictionary<string, object>();
    
    bool IsInline = context.GetVarValue("开启浏览") != null ? Convert.ToBoolean(context.GetVarValue("开启浏览")) : true;
    string defaultUploadDir = context.GetVarValue("上传目录") as string ?? Path.GetTempPath();
    string filterFile = context.GetVarValue("筛选") as string ?? "";

    //if (palb == null)    {  Console.WriteLine("警告：未能获取到有效的 path_列表");   context.SetVarValue("pa_url_词典", Purlcd);    return;  }

    var server = LocalImageServer.Instance;
    
    server.Content_inline = IsInline;
    server.DefaultUploadDirectory = defaultUploadDir; 

    // --- 权限及配置更新      新增：信任列表传入 ---
    server.TrustList = context.GetVarValue("信任列表") as List<string> ?? new List<string>();

// ====== 新增：加载固定IP用户名词典 ======
    server.FixedUserNames = context.GetVarValue("固定IP用户名") as Dictionary<string, object>;

    string perm = context.GetVarValue("上传下载权限") as string ?? "上传下载";
    server.EnableUploading = perm.Contains("上传");
    server.EnableDownloads = perm.Contains("下载");

    var restrictDirs = context.GetVarValue("限定上传目录") as List<string>;
    server.AllowedUploadDirectories = restrictDirs?.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();

    server.AccessPassword = context.GetVarValue("访问密码") as string;
    server.CustomAlias = context.GetVarValue("自定义域名") as string ?? "";
    server.CustomIP = context.GetVarValue("自定义IP") as string;

// ====== 新增：加载表情常用语到内存 ======
    var emojiList = context.GetVarValue("表情常用语列表") as List<string>;
    server.UpdateEmojis(emojiList);

    server.Port = port_Str;

    if (server.IsRunning && !string.IsNullOrWhiteSpace(server.CustomAlias))
    {
        server.RegisterAlias(server.CustomAlias, port_Str);
    }

    if(filterFile !="old"){
    // ====== 前后端分离：C# 仅负责初始化文件映射模型 ======
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
    
    // ====== 前后端分离：C# 获取文本源码作为虚拟网页 ======
    // 获取 Quicker 中的文本源码
    string frontendHtmlContent = context.GetVarValue("网页源码") as string ?? "";
    server.VirtualHtmlContent = frontendHtmlContent; 

    string reportUrl = "";

    if (!string.IsNullOrWhiteSpace(server.CustomAlias))
    {
        // 走自定义域名逻辑
        string aliasClean = server.CustomAlias.Trim('/');
        reportUrl = $"http://{server.EffectiveIP}:{port_Str}/{aliasClean}/";
    }
    else
    {
        // 没配自定义域名时，我们为其设定一个固定的虚拟路由（例如 /qkLANfile/index）
        reportUrl = $"http://{server.EffectiveIP}:{port_Str}/qkLANfile/index";
    }

    // 将frontendHtmlPath[虚拟网页虚拟网页地址加入字典，键名可以自定义，方便前端或 Quicker 调用
    Purlcd["共享门户"] = reportUrl; 

    // 收集所有共享文件的 URL 供前端或 Quicker 使用
    foreach (var kvp in server.SharedFiles) 
    {
        if(File.Exists(kvp.Key) || Directory.Exists(kvp.Key)) 
        {
            if(filterFile =="fresh" && palb.Contains(kvp.Key)==false && Directory.Exists(kvp.Key) ){ continue;}
            Purlcd[kvp.Key] = kvp.Value.Url;
        }
    }

    // ====== 启动服务（若未运行） ======
    if (!server.IsRunning)
    {
        server.Start(port_Str); // 传入用户指定的端口
    }else
    {
        // 【关键修复】：如果服务正在运行（5分钟内追加运行），则刷新存活时间，顺延5分钟
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
// ====== 新增：记录固定IP主机名 和 缓存用户通过URL传入的名字 ======
    public Dictionary<string, object>? FixedUserNames { get; set; }
    private readonly ConcurrentDictionary<string, string> _userNames = new ConcurrentDictionary<string, string>();
    // 内存文件字典和表情JSON属性
    private readonly ConcurrentDictionary<string, byte[]> _memoryFiles = new ConcurrentDictionary<string, byte[]>();
    private readonly ConcurrentDictionary<string, string> _memoryFileMime = new ConcurrentDictionary<string, string>();
    public string EmojiJson { get; set; } = "[]";

    public bool Content_inline { get; set; }
    public string? DefaultUploadDirectory { get; set; }
    public string? VirtualHtmlContent { get; set; } // 【新增】用于存放纯文本网页源码
    // --- 权限与配置属性 ---
    public bool EnableUploading { get; set; } = true;
    public bool EnableDownloads { get; set; } = true;
    public List<string>? AllowedUploadDirectories { get; set; }
    public string? AccessPassword { get; set; }
    public List<string> TrustList { get; set; } = new List<string>(); // 新增信任列表
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

    // ====== 【新增】统一的动态文件映射仓库，用于提供 /api/files 数据 ======
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
    // ====== 【新增：聊天记录存储结构】 ======
    public class ChatMessage {
        public string Sender { get; set; } = "";
        public string SenderName { get; set; } = ""; // ====== 新增：包含昵称/设备等完整信息的字段
        public string Time { get; set; } = "";
        public string Target { get; set; } = "";
        public string Content { get; set; } = "";
// 增量获取必须依赖的绝对时间戳
        public long Timestamp { get; set; } = 0;
    }
    private readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();
    
    // ====== 【新增】追踪活跃的 action ping 主机与端口 ======
    private readonly ConcurrentDictionary<string, string> _activePingers = new ConcurrentDictionary<string, string>();

    public void Reset()
    {
        Stop();
        
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

    // ====== 【新增】刷新活跃时间，防止误判超时 ======
    public void RefreshActivity()
    {
        lock (_activityLock) 
        { 
            _lastActivityTime = DateTime.Now; 
        }
    }

    public void Start(string portStr) // 接收外部传入的端口
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
        // 动态提示具体的端口号
        MessageBox.Show($"本地服务启动失败！可能是 {portStr} 端口被其他程序占用，或者旧服务未彻底关闭。\n\n系统报错:\n{ex.Message}", "启动错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        
        throw new Exception("服务器启动失败，终止动作。");
    }
}

    public void RegisterAlias(string alias, string portStr)
{
    if (string.IsNullOrWhiteSpace(alias)) return;
    alias = alias.Trim('/');
    string prefix = $"http://+:{portStr}/{alias}/"; // 替换为动态端口
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
                // ====== 【修改】转交异步方法以支持长轮询不阻塞 ======
                _ = HandleRequestAsync(context);
            }
            catch { break; }
        }
    }

private string GetRichIdentity(HttpListenerRequest request, string remoteIp, string senderId)
    {
        List<string> tags = new List<string>();

        // a. 用户输入 (捕获 URL 参数 ?user=xxx 或 ?name=xxx，参考 inline 的写法)
        string? userInput = request.QueryString["user"] ?? request.QueryString["name"];
        if (!string.IsNullOrWhiteSpace(userInput)) 
        {
            _userNames[remoteIp] = userInput.Trim(); // 只要访问过一次带参数的URL，就根据IP永久记住该昵称
        }
        if (_userNames.TryGetValue(remoteIp, out string? savedName) && !string.IsNullOrWhiteSpace(savedName))
        {
            tags.Add(savedName);
        }

        // b. 主机设置词典 (通过最后一段 IP 匹配)
        if (FixedUserNames != null && FixedUserNames.TryGetValue(senderId, out object? dictNameObj) && dictNameObj != null)
        {
            string dictName = dictNameObj.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(dictName)) tags.Add(dictName);
        }

        // c. 设备信息 (通过 User-Agent 嗅探)
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

    // ====== 【重点修改】异步的 HTTP 处理方法 ======
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // ====== 【功能1：身份识别与信任列表】 仅需最后1位数字即可信任======
        string remoteIp = request.RemoteEndPoint.Address.ToString();
        string localIp = EffectiveIP;
        string senderId = remoteIp.Split('.').Last();
        
        // ====== 【新增：解析并生成多维身份标识】 ======
        string richSenderName = GetRichIdentity(request, remoteIp, senderId);
        // 判断是否本地，或者在信任列表中
        bool isTrusted = remoteIp == "127.0.0.1" || remoteIp == "::1" || remoteIp == localIp || (TrustList != null && (TrustList.Contains(remoteIp) || TrustList.Contains(senderId)));
 
        string urlPath = request.Url?.AbsolutePath ?? "";
        string[] segments = urlPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
// ====== 【修复：统一前缀判定】兼容原版前缀 和 自定义Alias别名（如 /QUICKER） ======
    bool isValidPrefix = segments.Length > 0 && (
        segments[0].Equals("qkLANfile", StringComparison.OrdinalIgnoreCase) || 
        (!string.IsNullOrEmpty(CustomAlias) && segments[0].Equals(CustomAlias.Trim('/'), StringComparison.OrdinalIgnoreCase))
    );

        // ====== 【保活ping逻辑区分刷新闲置倒计时,普通ping不刷新,新增：actionping】 ======

        // ====== 【功能4：actionping 解析】 ======
        if (segments.Length >= 2 && isValidPrefix)
        {
            var match = Regex.Match(segments[1], @"^action(\d+)ping$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string actionPort = match.Groups[1].Value;
                
                // 重点1 & 2：分开保存端口，actionping用户记录为 "IP:端口"，避免与普通聊天用户（仅记录IP）冲突
                string ipAndPort = $"{remoteIp}:{actionPort}";
                _activePingers[ipAndPort] = actionPort;

                // 重点3：返回剩余闲置时间（与普通ping一样，不刷新活跃时间）
                int remainSec = 0;
                lock (_activityLock) 
                { 
                    remainSec = (ShutdownDelayMs - (int)(DateTime.Now - _lastActivityTime).TotalMilliseconds) / 1000; 
                }
                
                response.StatusCode = 200;
                response.ContentType = "text/plain; charset=utf-8";
                byte[] actionMsg = Encoding.UTF8.GetBytes(remainSec.ToString());
                response.ContentLength64 = actionMsg.Length; // 必须加，防止挂起
                response.OutputStream.Write(actionMsg, 0, actionMsg.Length);
                response.Close();

                // 如果超时则触发停止
                if (remainSec <= 0) _ = Task.Run(() => this.Stop());
                return;
            }
        }

        // ====== 【修改】keepaliveping 返回 JSON 格式数据包含活跃端口 ======
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
                response.ContentLength64 = msg.Length; // ！！！必须加这一句，否则浏览器会挂起死等
                response.OutputStream.Write(msg, 0, msg.Length);
            }
            else 
            {
                byte[] msg = Encoding.UTF8.GetBytes(remainSec.ToString());
                response.ContentLength64 = msg.Length; // ！！！必须加这一句，否则浏览器会挂起死等
                response.OutputStream.Write(msg, 0, msg.Length);
            }
            response.Close();
            if (remainSec <= 0) _ = Task.Run(() => this.Stop());
            return;
        }
        // 非一般探测操作均刷新倒计时
        lock (_activityLock) _lastActivityTime = DateTime.Now;
            
            // ====== 【新增：幽灵实例自毁接口】 ======
if (segments.Length >= 2 && isValidPrefix && segments[1].Equals("shutdown", StringComparison.OrdinalIgnoreCase))
{
    SendError(response, HttpStatusCode.OK, "Server shutting down...");
    // 延迟 500 毫秒执行 Stop()，确保前面的 OK 响应能成功发给客户端
    response.Close(); // ！！！强制手动关闭连接，释放发起请求的客户端
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

// ====== 【核心修改】获取完整文件列表数据 API（支持上传权限与目录Token验证） ======
if (segments.Length >= 3 && isValidPrefix && segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) && segments[2].Equals("files", StringComparison.OrdinalIgnoreCase))
{
    var list = SharedFiles.Values.ToList();
    StringBuilder sbJson = new StringBuilder();
    sbJson.Append("[");
    for (int i = 0; i < list.Count; i++) {
        var f = list[i];
        string escPath = f.Path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        
        // 1. 确定当前行项目对应的上传目标目录（文件取其父目录，文件夹取自身）
        string targetDir = f.IsDir ? f.Path : (Path.GetDirectoryName(f.Path) ?? f.Path);
        
        // 2. 获取该目标目录对应的 Token（用于上传）
        string uploadToken = f.Token;
        foreach (var kv in _tokenToPathMap)
        {
            if (kv.Value.Equals(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                uploadToken = kv.Key;
                break;
            }
        }

        // 3. 权限验证：如果用户 IP 在信任列表，或者该目录在限定的上传范围内，则允许上传
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

// 在 HandleRequestAsync 里原 /api/files 下方，增加获取表情的 API：
if (segments.Length >= 3 && isValidPrefix && segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) && segments[2].Equals("emojis", StringComparison.OrdinalIgnoreCase))
{
    response.ContentType = "application/json; charset=utf-8";
    byte[] b = Encoding.UTF8.GetBytes(EmojiJson ?? "[]");
    response.ContentLength64 = b.Length;
    response.OutputStream.Write(b, 0, b.Length);
    return;
}

            // ====== 【功能3：长轮询与增量获取聊天接口】 ======
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
                        // 【功能0】：如果是长轮询挂起，期间不断刷新活跃时间，确保服务不被关闭！
                        RefreshActivity(); 
                        
                        bool hasNew = false;
                        lock (_chatHistory) { 
                            if (_chatHistory.Count > 0 && _chatHistory.Last().Timestamp > lastTime) hasNew = true; 
                        }
                        if (hasNew) break;
                    }
                }

                // 过滤出大于 lastTime 的消息返回
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
                string action = segments.Length > 2 ? segments[2].ToLower() : "";
                response.StatusCode = 200;
                response.ContentType = "text/plain; charset=utf-8";
                
                if (action == "get") {
                    string txt = "";
                    Thread t = new Thread(() => { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); });
                    t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
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
                    byte[] b = Encoding.UTF8.GetBytes(txt);
                    response.ContentLength64 = b.Length;
                    response.OutputStream.Write(b, 0, b.Length);
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
