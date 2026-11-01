using UnityEngine;
using TMPro;
using System.Threading.Tasks;

public class SettingsPanel : MonoBehaviour
{
    [Header("设置 UI")]
    [SerializeField] private TMP_InputField apiAddressInput;
    [SerializeField] private TMP_Text connectionStatusText;
    [SerializeField] private GameObject testConnectionButton;

    [Header("用户登录")]
    [SerializeField] private TMP_InputField loginUserInput;
    [SerializeField] private TMP_InputField loginPassInput;
    [SerializeField] private GameObject loginButton;
    [SerializeField] private TMP_Text currentUserText;
    [SerializeField] private TMP_Text loginStatusText;

    private const string ApiAddressPrefKey = "ApiAddress";

    private void Start()
    {
        // 读取已保存的 API 地址
        string savedAddress = PlayerPrefs.GetString(ApiAddressPrefKey, "http://localhost:5175");
        if (apiAddressInput != null)
        {
            apiAddressInput.text = savedAddress;
        }

        // 显示已登录用户（若已保存 token）
        if (ApiClient.Instance != null && !string.IsNullOrEmpty(ApiClient.Instance.AuthToken))
        {
            if (currentUserText != null)
            {
                currentUserText.text = "当前用户：" + ApiClient.Instance.CurrentUserName;
            }
            if (loginStatusText != null)
            {
                loginStatusText.text = "● 已登录";
                loginStatusText.color = Color.green;
            }
        }

        // 尝试自动连接
        TryAutoConnect(savedAddress);
    }

    private async void TryAutoConnect(string address)
    {
        await Task.Yield();

        if (ApiClient.Instance == null)
        {
            UpdateConnectionStatus(false);
            return;
        }

        ApiClient.Instance.BaseUrl = address;
        ApiClient.Instance.OnConnectionStatusChanged += OnApiConnectionChanged;

        bool connected = await ApiClient.Instance.TestConnection();
        UpdateConnectionStatus(connected);
    }

    /// <summary>点击"测试连接"按钮时调用</summary>
    public void OnTestConnection()
    {
        string address = apiAddressInput != null ? apiAddressInput.text : "http://localhost:5175";

        PlayerPrefs.SetString(ApiAddressPrefKey, address);
        PlayerPrefs.Save();

        if (connectionStatusText != null)
        {
            connectionStatusText.text = "● 测试中...";
            connectionStatusText.color = Color.yellow;
        }

        if (ApiClient.Instance == null)
        {
            Debug.LogWarning("SettingsPanel: ApiClient 未初始化");
            UpdateConnectionStatus(false);
            return;
        }

        ApiClient.Instance.BaseUrl = address;
        _ = TestConnectionAsync();
    }

    private async Task TestConnectionAsync()
    {
        bool connected = await ApiClient.Instance.TestConnection();
        UpdateConnectionStatus(connected);

        // 审计：测试连接操作（仅已登录时记录）
        if (connected)
        {
            ApiClient.Instance.LogOperation("测试连接", "API", $"连接到 {ApiClient.Instance.BaseUrl}");
        }
    }

    /// <summary>点击"登录"按钮时调用</summary>
    public void OnLogin()
    {
        if (ApiClient.Instance == null) return;

        string user = loginUserInput != null ? loginUserInput.text : "";
        string pass = loginPassInput != null ? loginPassInput.text : "";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            if (loginStatusText != null)
            {
                loginStatusText.text = "请输入用户名和密码";
                loginStatusText.color = Color.red;
            }
            return;
        }

        if (loginStatusText != null)
        {
            loginStatusText.text = "● 登录中...";
            loginStatusText.color = Color.yellow;
        }

        _ = DoLoginAsync(user, pass);
    }

    private async Task DoLoginAsync(string user, string pass)
    {
        bool ok = await ApiClient.Instance.LoginAsync(user, pass);

        if (ok)
        {
            if (currentUserText != null)
            {
                currentUserText.text = "当前用户：" + ApiClient.Instance.CurrentUserName;
            }
            if (loginStatusText != null)
            {
                loginStatusText.text = "● 登录成功";
                loginStatusText.color = Color.green;
            }
            ApiClient.Instance.LogOperation("登录", ApiClient.Instance.CurrentUserName, "用户登录系统");
        }
        else
        {
            if (loginStatusText != null)
            {
                loginStatusText.text = "● 登录失败（用户名或密码错误）";
                loginStatusText.color = Color.red;
            }
        }
    }

    private void OnApiConnectionChanged(bool isConnected)
    {
        UpdateConnectionStatus(isConnected);
    }

    /// <summary>供外部调用：更新连接状态显示</summary>
    public void UpdateConnectionStatus(bool isConnected)
    {
        if (connectionStatusText == null) return;

        if (isConnected)
        {
            connectionStatusText.text = "● 已连接";
            connectionStatusText.color = Color.green;
        }
        else
        {
            connectionStatusText.text = "● 未连接";
            connectionStatusText.color = Color.red;
        }
    }

    private void OnDestroy()
    {
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnConnectionStatusChanged -= OnApiConnectionChanged;
        }
    }
}
