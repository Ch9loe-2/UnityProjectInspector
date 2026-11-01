using UnityEngine;
using TMPro;

/// <summary>
/// 顶部状态栏控制器：让顶部状态跟随真实数据变化。
/// - API 状态：跟随 ApiClient 连接状态
/// - 系统状态：根据设备状态自动计算（正常/警告/异常/离线）
/// </summary>
public class TopBarController : MonoBehaviour
{
    [Header("状态文字")]
    [SerializeField] private TMP_Text systemStatusText;
    [SerializeField] private TMP_Text apiStatusText;

    private static readonly Color ColorNormal = new Color(0.4f, 1f, 0.4f);     // 绿
    private static readonly Color ColorWarning = new Color(1f, 0.85f, 0.2f);    // 黄
    private static readonly Color ColorFault = new Color(1f, 0.45f, 0.45f);     // 红
    private static readonly Color ColorOffline = new Color(0.7f, 0.7f, 0.7f);   // 灰

    private void OnEnable()
    {
        // 订阅 API 连接状态变化
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnConnectionStatusChanged -= OnApiStatusChanged;
            ApiClient.Instance.OnConnectionStatusChanged += OnApiStatusChanged;
        }

        // 订阅设备状态变化
        SystemEvents.DeviceStatusChanged -= UpdateSystemStatus;
        SystemEvents.DeviceStatusChanged += UpdateSystemStatus;

        // 初始化一次
        UpdateSystemStatus();
        UpdateApiStatus(ApiClient.Instance != null && ApiClient.Instance.IsConnected);
    }

    private void OnDisable()
    {
        if (ApiClient.Instance != null)
        {
            ApiClient.Instance.OnConnectionStatusChanged -= OnApiStatusChanged;
        }

        SystemEvents.DeviceStatusChanged -= UpdateSystemStatus;
    }

    private void OnApiStatusChanged(bool connected)
    {
        UpdateApiStatus(connected);
    }

    /// <summary>更新 API 连接状态显示</summary>
    private void UpdateApiStatus(bool connected)
    {
        if (apiStatusText == null)
        {
            return;
        }

        apiStatusText.text = connected ? "API：已连接" : "API：未连接";
        apiStatusText.color = connected ? ColorNormal : ColorOffline;
    }

    /// <summary>根据所有设备的状态计算系统整体状态</summary>
    private void UpdateSystemStatus()
    {
        if (systemStatusText == null)
        {
            return;
        }

        DeviceData[] devices = FindObjectsOfType<DeviceData>();

        int warning = 0;
        int fault = 0;
        int offline = 0;

        foreach (DeviceData device in devices)
        {
            if (device == null)
            {
                continue;
            }

            switch (device.status)
            {
                case "警告":
                    warning++;
                    break;

                case "故障":
                    fault++;
                    break;

                case "离线":
                    offline++;
                    break;
            }
        }

        // 优先级：故障 > 离线 > 警告 > 正常
        if (fault > 0)
        {
            systemStatusText.text = $"● 系统异常（故障 {fault}）";
            systemStatusText.color = ColorFault;
        }
        else if (offline > 0)
        {
            systemStatusText.text = $"● 设备离线（{offline}）";
            systemStatusText.color = ColorOffline;
        }
        else if (warning > 0)
        {
            systemStatusText.text = $"● 注意警告（{warning}）";
            systemStatusText.color = ColorWarning;
        }
        else
        {
            systemStatusText.text = "● 系统正常";
            systemStatusText.color = ColorNormal;
        }
    }
}