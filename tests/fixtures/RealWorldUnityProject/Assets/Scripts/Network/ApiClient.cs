using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// API 客户端单例：管理 Unity 与后端 ASP.NET Core API 的 HTTP 通信。
/// 其他脚本通过 ApiClient.Instance 调用。
/// 支持登录鉴权（Bearer token）+ 操作日志上报（LogOperation）。
/// </summary>
public class ApiClient : MonoBehaviour
{
    private static ApiClient _instance;
    /// <summary>场景关闭/退出中的标记，防止 Instance getter 重建新对象导致残留</summary>
    private static bool _isShuttingDown = false;

    public static ApiClient Instance
    {
        get
        {
            // 场景关闭/退出中禁止重建，返回 null
            if (_isShuttingDown) return null;

            // 场景卸载/关闭时禁止重建，避免 DontDestroyOnLoad 引发的清理错误
            if (!Application.isPlaying)
            {
                return _instance;
            }

            if (_instance == null)
            {
                var go = new GameObject("ApiClient");
                _instance = go.AddComponent<ApiClient>();
            }
            return _instance;
        }
        private set { _instance = value; }
    }

    /// <summary>后端 API 基地址，由 SettingsPanel 设置</summary>
    public string BaseUrl { get; set; } = "http://localhost:5175";

    /// <summary>连接状态变化事件</summary>
    public event Action<bool> OnConnectionStatusChanged;

    /// <summary>当前是否已连接</summary>
    public bool IsConnected { get; private set; } = false;

    // ── 用户权限：登录后保存 token 与用户名（持久化到 PlayerPrefs）──
    public string AuthToken { get; private set; } = "";
    public string CurrentUserName { get; private set; } = "";

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // 读取已保存的登录态
        AuthToken = PlayerPrefs.GetString("AuthToken", "");
        CurrentUserName = PlayerPrefs.GetString("AuthUser", "");
    }

    private void OnDestroy()
    {
        _isShuttingDown = true;
        _instance = null;
    }

    private void OnApplicationQuit()
    {
        _isShuttingDown = true;
        _instance = null;
    }

    /// <summary>测试 API 连接（GET /api/devices）</summary>
    public async Task<bool> TestConnection()
    {
        try
        {
            using var request = UnityWebRequest.Get($"{BaseUrl}/api/devices");
            ApplyAuth(request);
            request.timeout = 5;

            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            bool success = request.result == UnityWebRequest.Result.Success;
            IsConnected = success;

            if (OnConnectionStatusChanged != null)
            {
                OnConnectionStatusChanged(success);
            }

            return success;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ApiClient: 连接测试失败 - {e.Message}");
            IsConnected = false;
            if (OnConnectionStatusChanged != null) OnConnectionStatusChanged(false);
            return false;
        }
    }

    /// <summary>GET 请求，返回 JSON 字符串</summary>
    public async Task<string> GetAsync(string endpoint)
    {
        try
        {
            using var request = UnityWebRequest.Get($"{BaseUrl}{endpoint}");
            ApplyAuth(request);
            request.timeout = 10;

            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                return request.downloadHandler.text;
            }

            Debug.LogWarning($"ApiClient GET {endpoint} 失败: {request.error}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ApiClient GET {endpoint} 异常: {e.Message}");
            return null;
        }
    }

    /// <summary>POST 请求，发送 JSON body</summary>
    public async Task<string> PostAsync(string endpoint, string jsonBody)
    {
        try
        {
            using var request = new UnityWebRequest($"{BaseUrl}{endpoint}", "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ApplyAuth(request);
            request.timeout = 10;

            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                return request.downloadHandler.text;
            }

            Debug.LogWarning($"ApiClient POST {endpoint} 失败: {request.error}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ApiClient POST {endpoint} 异常: {e.Message}");
            return null;
        }
    }

    /// <summary>PUT 请求，发送 JSON body</summary>
    public async Task<string> PutAsync(string endpoint, string jsonBody)
    {
        try
        {
            using var request = new UnityWebRequest($"{BaseUrl}{endpoint}", "PUT");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            ApplyAuth(request);
            request.timeout = 10;

            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
            {
                return request.downloadHandler.text;
            }

            Debug.LogWarning($"ApiClient PUT {endpoint} 失败: {request.error}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ApiClient PUT {endpoint} 异常: {e.Message}");
            return null;
        }
    }

    /// <summary>用户登录：POST /api/auth/login，成功则保存 token</summary>
    public async Task<bool> LoginAsync(string user, string pass)
    {
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) return false;

        string json = JsonUtility.ToJson(new LoginReqJson { username = user, password = pass });
        string resp = await PostAsync("/api/auth/login", json);
        if (string.IsNullOrEmpty(resp)) return false;

        LoginResp r = JsonUtility.FromJson<LoginResp>(resp);
        if (r == null || r.code != 200 || r.data == null) return false;

        AuthToken = r.data.token;
        CurrentUserName = r.data.userName;

        PlayerPrefs.SetString("AuthToken", AuthToken);
        PlayerPrefs.SetString("AuthUser", CurrentUserName);
        PlayerPrefs.Save();

        return true;
    }

    /// <summary>操作日志上报（即发即弃；未登录不记，由后端中间件要求 Bearer）</summary>
    public void LogOperation(string action, string target, string detail)
    {
        if (string.IsNullOrEmpty(AuthToken)) return;   // 未登录不审计
        if (!Application.isPlaying) return;

        string json = JsonUtility.ToJson(new LogReqJson
        {
            action = action,
            target = target,
            detail = detail
        });

        _ = PostAsync("/api/logs", json);
    }

    /// <summary>给请求附加 Authorization 头（已登录时）</summary>
    private void ApplyAuth(UnityWebRequest request)
    {
        if (!string.IsNullOrEmpty(AuthToken))
        {
            request.SetRequestHeader("Authorization", "Bearer " + AuthToken);
        }
    }

    // ── 登录 / 日志 JSON 模型（camelCase，与 JsonUtility 对齐）──
    [System.Serializable]
    private class LoginReqJson
    {
        public string username;
        public string password;
    }

    [System.Serializable]
    private class LoginResp
    {
        public int code = 0;
        public string message = null;
        public LoginData data = null;
    }

    [System.Serializable]
    private class LoginData
    {
        public string token = null;
        public string userName = null;
    }

    [System.Serializable]
    private class LogReqJson
    {
        public string action = null;
        public string target = null;
        public string detail = null;
    }
}
