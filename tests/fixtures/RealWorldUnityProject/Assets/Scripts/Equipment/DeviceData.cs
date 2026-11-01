using UnityEngine;

public class DeviceData : MonoBehaviour
{
    [Header("设备信息")]
    public string deviceName = "设备 A";
    public string deviceType = "生产设备";
    public string workshop = "一号车间";

    [Header("运行数据")]
    public float temperature = 65.2f;
    public float pressure = 1.62f;
    public int rpm = 1450;
    public float runtime = 128.5f;

    [Header("设备状态")]
    public string status = "正常";

    [Header("在线状态")]
    public bool isOnline = true;

    /// <summary>离线前的状态，恢复上线时还原（避免丢失警告/故障状态）</summary>
    [HideInInspector] public string statusBeforeOffline = "正常";

    /// <summary>最后一次收到设备数据的时间（用于超时离线检测）</summary>
    [HideInInspector] public float lastHeartbeatTime;

    [Header("初始数据")]
    [HideInInspector] public float initialTemperature;
    [HideInInspector] public float initialPressure;
    [HideInInspector] public int initialRpm;
    [HideInInspector] public float initialRuntime;

    private void Awake()
    {
        initialTemperature = temperature;
        initialPressure = pressure;
        initialRpm = rpm;
        initialRuntime = runtime;
        statusBeforeOffline = status;
        lastHeartbeatTime = Time.time;

        // 按设备名自动归属车间（可在 Inspector 覆盖）
        if (deviceName == "设备 C")
        {
            workshop = "二号车间";
        }
        else
        {
            workshop = "一号车间";
        }
    }

    /// <summary>标记为离线（保留原状态，视图层显示为"离线"）</summary>
    public void SetOffline()
    {
        if (!isOnline) return;

        statusBeforeOffline = status;
        status = "离线";
        isOnline = false;
    }

    /// <summary>恢复上线（还原离线前的状态）</summary>
    public void SetOnline()
    {
        if (isOnline) return;

        status = statusBeforeOffline;
        isOnline = true;
        lastHeartbeatTime = Time.time;
    }

    /// <summary>记录一次心跳（设备上报数据）</summary>
    public void MarkHeartbeat()
    {
        lastHeartbeatTime = Time.time;
    }
}