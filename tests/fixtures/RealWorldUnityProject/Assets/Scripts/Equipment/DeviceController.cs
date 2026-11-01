
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Threading.Tasks;

public class DeviceController : MonoBehaviour
{
    [Header("设备数据")]
    [SerializeField] private DeviceData deviceData;

    [Header("设备详情面板")]
    [SerializeField] private GameObject deviceDetailPanel;
    [Header("页面切换")]
    [SerializeField] private PanelSwitcher panelSwitcher;

    [Header("UI 文本")]
    [SerializeField] private TMP_Text deviceNameTitle;
    [SerializeField] private TMP_Text deviceTypeLabel;
    [SerializeField] private TMP_Text deviceStatusLabel;
    [SerializeField] private TMP_Text temperatureLabel;
    [SerializeField] private TMP_Text pressureLabel;
    [SerializeField] private TMP_Text speedLabel;
    [SerializeField] private TMP_Text runtimeLabel;

    [Header("故障模拟按钮")]
    [SerializeField] private UnityEngine.UI.Button temperatureFaultButton;
    [SerializeField] private UnityEngine.UI.Button pressureFaultButton;
    [SerializeField] private UnityEngine.UI.Button recoveryButton;

    [Header("离线按钮")]
    [SerializeField] private UnityEngine.UI.Button offlineButton;

    [Header("设备状态灯")]
    [SerializeField] private Renderer statusLightRenderer;

    // API 中该设备的 ID（设备 A=1, B=2, C=3）
    private int ApiDeviceId
    {
        get
        {
            if (deviceData == null) return 1;
            if (deviceData.deviceName == "设备 A") return 1;
            if (deviceData.deviceName == "设备 B") return 2;
            if (deviceData.deviceName == "设备 C") return 3;
            return 1;
        }
    }

    private bool isTemperatureFaultRunning = false;
    private bool isPressureFaultRunning = false;
    private bool temperatureAlarmAdded = false;
    private bool pressureAlarmAdded = false;
    /// <summary>手动触发故障后保持故障状态，禁止自动仿真恢复，仅点"恢复正常"才清除</summary>
    private bool manualFaultActive = false;

    [Header("自动阈值报警")]
    [Tooltip("是否启用超阈值自动报警：温度/压力达到阈值即自动触发报警，回到正常后自动恢复")]
    public bool enableAutoThresholdAlarm = true;

    [Header("温度阈值 (℃)")]
    public float tempWarnThreshold = 78f;
    public float tempFaultThreshold = 90f;

    [Header("压力阈值 (MPa)")]
    public float pressureWarnThreshold = 2.0f;
    public float pressureFaultThreshold = 2.3f;

    // ── 全局静态度量（解决共享按钮引用导致跨设备混触发） ──
    /// <summary>当前选中（详情面板上）的设备控制器</summary>
    private static DeviceController _selectedController = null;
    /// <summary>所有存活的 DeviceController 实例</summary>
    private static readonly List<DeviceController> _allControllers = new List<DeviceController>();

    /// <summary>中止所有设备上的故障协程并复位标记</summary>
    public static void StopAllFaults()
    {
        foreach (var c in _allControllers)
        {
            if (c == null) continue;
            c.StopAllCoroutines();
            c.isTemperatureFaultRunning = false;
            c.isPressureFaultRunning = false;
            c.temperatureAlarmAdded = false;
            c.pressureAlarmAdded = false;
        }
    }

    private void OnEnable()  { if (!_allControllers.Contains(this)) _allControllers.Add(this); }
    private void OnDisable() { _allControllers.Remove(this); }

    // 自动仿真的内部状态
    private bool simInitialized = false;
    private float apiSyncTimer = 2f;
    // 缓存材质引用，避免每帧 statusLightRenderer.material 创建新实例导致闪烁
    private Material cachedStatusLightMaterial = null;


    private void OnMouseDown()
    {
        Debug.Log($"点击了{deviceData.deviceName}");

        // 切换选中设备时，中止所有设备上正在运行的故障，防止残留协程冲突
        StopAllFaults();
        _selectedController = this;

        if (panelSwitcher != null)
        {
            panelSwitcher.ShowDeviceDetail();
        }

        deviceNameTitle.text = deviceData.deviceName;
        deviceTypeLabel.text = $"类型    {deviceData.deviceType}    车间    {deviceData.workshop}";
        deviceStatusLabel.text = $"状态    ● {deviceData.status}";
        temperatureLabel.text = $"温度    {deviceData.temperature:F1} ℃";
        pressureLabel.text = $"压力    {deviceData.pressure:F2} MPa";
        speedLabel.text = $"转速    {deviceData.rpm} RPM";
        runtimeLabel.text = $"运行时间    {deviceData.runtime:F1} h";

        UpdateStatusLight();

        temperatureFaultButton.onClick.RemoveAllListeners();
        temperatureFaultButton.onClick.AddListener(SimulateTemperatureFault);

        pressureFaultButton.onClick.RemoveAllListeners();
        pressureFaultButton.onClick.AddListener(SimulatePressureFault);

        recoveryButton.onClick.RemoveAllListeners();
        recoveryButton.onClick.AddListener(RecoverDevice);

        // 离线/上线按钮：动态绑定到当前选中设备
        if (offlineButton != null)
        {
            offlineButton.onClick.RemoveAllListeners();
            offlineButton.onClick.AddListener(ToggleOffline);
        }
    }

    private void Start()
    {
        // 初始化缓存材质（一次性，避免每帧创建实例导致闪烁）
        if (statusLightRenderer != null)
        {
            cachedStatusLightMaterial = statusLightRenderer.material;
        }
    }

    private void UpdateStatusLight()
    {
        if (statusLightRenderer == null)
        {
            return;
        }

        // 确保缓存材质已初始化
        if (cachedStatusLightMaterial == null)
        {
            cachedStatusLightMaterial = statusLightRenderer.material;
        }

        switch (deviceData.status)
        {
            case "正常":
                cachedStatusLightMaterial.color = Color.green;
                break;

            case "警告":
                cachedStatusLightMaterial.color = Color.yellow;
                break;

            case "故障":
                cachedStatusLightMaterial.color = Color.red;
                break;

            case "离线":
                // 离线用灰色，与其他状态区分
                cachedStatusLightMaterial.color = Color.gray;
                break;
        }
    }

    /// <summary>
    /// 切换设备的在线/离线状态（供"离线/上线"按钮调用）。
    /// 离线时状态灯变灰、概览"离线"计数 +1。
    /// </summary>
    public void ToggleOffline()
    {
        if (this != _selectedController)
        {
            Debug.Log($"[DeviceController] 跳过离线切换：{deviceData.deviceName} 非当前选中设备");
            return;
        }
        if (deviceData == null)
        {
            return;
        }

        if (deviceData.isOnline)
        {
            deviceData.SetOffline();
        }
        else
        {
            deviceData.SetOnline();
        }

        // 刷新详情面板显示
        if (deviceStatusLabel != null)
        {
            deviceStatusLabel.text = $"状态    ● {deviceData.status}";
        }

        UpdateStatusLight();

        // 通知概览刷新
        SystemEvents.RaiseDeviceStatusChanged();

        // 同步到 API
        _ = SyncToApiAsync();

        Debug.Log($"设备 {deviceData.deviceName} 状态：{deviceData.status}");
        ApiClient.Instance?.LogOperation("设备上下线", deviceData.deviceName, $"状态变为 {deviceData.status}");
    }

    private void SimulateTemperatureFault()
    {
        if (this != _selectedController)
        {
            Debug.Log($"[DeviceController] 跳过温度故障：{deviceData.deviceName} 非当前选中设备");
            return;
        }
        if (isTemperatureFaultRunning)
        {
            return;
        }

        isTemperatureFaultRunning = true;
        manualFaultActive = true;  // 手动故障后禁止自动恢复
        ApiClient.Instance?.LogOperation("故障模拟", deviceData.deviceName, "手动触发温度过高故障");
        StartCoroutine(TemperatureFaultRoutine());
    }

    private System.Collections.IEnumerator TemperatureFaultRoutine()
    {
        float[] temperatures = { 75f, 85f, 95f, 105f };

        foreach (float temperature in temperatures)
        {
            deviceData.temperature = temperature;

            if (temperature < 95f)
            {
                deviceData.status = "警告";
            }
            else
            {
                deviceData.status = "故障";

                if (!temperatureAlarmAdded && AlarmManager.Instance != null)
                {
                    temperatureAlarmAdded = true;

                    AlarmManager.Instance.AddAlarm(
                        deviceData.deviceName,
                        "温度过高",
                        "故障",
                        $"温度达到 {deviceData.temperature:F1} ℃"
                    );
                }
            }

            temperatureLabel.text =
                $"温度    {deviceData.temperature:F1} ℃";

            deviceStatusLabel.text =
                $"状态    ● {deviceData.status}";

            UpdateStatusLight();

            SystemEvents.RaiseDeviceStatusChanged();

            // 同步到 API
            _ = SyncToApiAsync();

            yield return new WaitForSeconds(1f);
        }

        isTemperatureFaultRunning = false;
    }

    private void SimulatePressureFault()
    {
        if (this != _selectedController)
        {
            Debug.Log($"[DeviceController] 跳过压力故障：{deviceData.deviceName} 非当前选中设备");
            return;
        }
        if (isPressureFaultRunning)
        {
            return;
        }

        isPressureFaultRunning = true;
        manualFaultActive = true;  // 手动故障后禁止自动恢复
        ApiClient.Instance?.LogOperation("故障模拟", deviceData.deviceName, "手动触发压力异常故障");
        StartCoroutine(PressureFaultRoutine());
    }

    private System.Collections.IEnumerator PressureFaultRoutine()
    {
        float[] pressures = { 1.9f, 2.1f, 2.3f, 2.5f };

        foreach (float pressure in pressures)
        {
            deviceData.pressure = pressure;

            if (pressure < 2.3f)
            {
                deviceData.status = "警告";
            }
            else
            {
                deviceData.status = "故障";

                if (!pressureAlarmAdded && AlarmManager.Instance != null)
                {
                    pressureAlarmAdded = true;

                    AlarmManager.Instance.AddAlarm(
                        deviceData.deviceName,
                        "压力异常",
                        "故障",
                        $"压力达到 {deviceData.pressure:F2} MPa"
                    );
                }
            }
            pressureLabel.text =
                $"压力    {deviceData.pressure:F2} MPa";

            deviceStatusLabel.text =
                $"状态    ● {deviceData.status}";

            UpdateStatusLight();

            SystemEvents.RaiseDeviceStatusChanged();

            yield return new WaitForSeconds(1f);
        }

        isPressureFaultRunning = false;
    }

    private void RecoverDevice()
    {
        if (this != _selectedController)
        {
            Debug.Log($"[DeviceController] 跳过恢复：{deviceData.deviceName} 非当前选中设备");
            return;
        }

        // 停止所有设备上的故障协程，确保"恢复正常"全局复位
        StopAllFaults();

        isTemperatureFaultRunning = false;
        isPressureFaultRunning = false;
        temperatureAlarmAdded = false;
        pressureAlarmAdded = false;

        deviceData.temperature = deviceData.initialTemperature;
        deviceData.pressure = deviceData.initialPressure;
        deviceData.rpm = deviceData.initialRpm;
        deviceData.runtime = deviceData.initialRuntime;
        deviceData.status = "正常";

        temperatureLabel.text =
            $"温度    {deviceData.temperature:F1} ℃";

        pressureLabel.text =
            $"压力    {deviceData.pressure:F2} MPa";

        speedLabel.text =
            $"转速    {deviceData.rpm} RPM";

        runtimeLabel.text =
            $"运行时间    {deviceData.runtime:F1} h";

        deviceStatusLabel.text =
            $"状态    ● {deviceData.status}";

        UpdateStatusLight();

        // 恢复该设备的报警记录
        if (AlarmManager.Instance != null)
        {
            AlarmManager.Instance.RecoverAlarm(deviceData.deviceName);
        }

        // 同步设备数据到 API
        _ = SyncToApiAsync();

        manualFaultActive = false;  // 清除手动故障标记，允许自动仿真继续继续平稳波动

        ApiClient.Instance?.LogOperation("设备恢复", deviceData.deviceName, "手动恢复正常");
    }

    private async Task SyncToApiAsync()
    {
        if (ApiClient.Instance == null || !ApiClient.Instance.IsConnected) return;

        string json = JsonUtility.ToJson(new DeviceUpdateJson
        {
            temperature = deviceData.temperature,
            pressure = deviceData.pressure,
            rpm = deviceData.rpm,
            runtime = deviceData.runtime,
            status = deviceData.status,
            workshop = deviceData.workshop
        });

        await ApiClient.Instance.PutAsync($"/api/devices/{ApiDeviceId}", json);
    }

    // ───────────────────────────────────────────────────────────
    // 自动仿真 + 阈值报警
    // ───────────────────────────────────────────────────────────

    private void EnsureSimInit()
    {
        if (simInitialized) return;
        simInitialized = true;
    }

    private void Update()
    {
        if (deviceData == null || !deviceData.isOnline) return;

        EnsureSimInit();
        TickSimulation(Time.deltaTime);

        if (enableAutoThresholdAlarm)
        {
            CheckThresholds();
        }

        // 节流（每 2s）把波动后的数值同步到后端，让设备表反映实时工况
        apiSyncTimer -= Time.deltaTime;
        if (apiSyncTimer <= 0f)
        {
            apiSyncTimer = 2f;
            _ = SyncToApiAsync();
        }
    }

    /// <summary>
    /// 自然仿真：温度/压力围绕初始值做小幅随机波动（让数据看起来是活的）。
    /// 超阈值事件仅由"手动故障模拟"按钮触发，仿真从不主动越线。
    /// 
    /// 注意：步长和回拉力的比例必须保证自然波动不会自行达到阈值，
    /// 否则会导致设备在未点击时频繁出现报警闪烁。
    /// </summary>
    private void TickSimulation(float dt)
    {
        // 手动故障激活时，仿真冻结数值，直到点"恢复正常"
        if (manualFaultActive) return;

        if (!isTemperatureFaultRunning)
        {
            deviceData.temperature += UnityEngine.Random.Range(-0.12f, 0.12f);
            deviceData.temperature = Mathf.Lerp(deviceData.temperature, deviceData.initialTemperature, dt * 2.0f);
            // 安全钳：自然波动不超过初始值 ±5°C，防止自行越线
            deviceData.temperature = Mathf.Clamp(deviceData.temperature,
                deviceData.initialTemperature - 5f,
                deviceData.initialTemperature + 5f);
        }
        if (!isPressureFaultRunning)
        {
            deviceData.pressure += UnityEngine.Random.Range(-0.01f, 0.01f);
            deviceData.pressure = Mathf.Lerp(deviceData.pressure, deviceData.initialPressure, dt * 2.0f);
            // 安全钳：自然波动不超过初始值 ±0.3 MPa
            deviceData.pressure = Mathf.Clamp(deviceData.pressure,
                deviceData.initialPressure - 0.3f,
                deviceData.initialPressure + 0.3f);
        }
    }

    private int StatusRank(string s)
    {
        if (s == "故障") return 3;
        if (s == "警告") return 2;
        return 1;
    }

    /// <summary>
    /// 根据当前温度/压力与阈值判定状态：超阈值自动设状态 + 自动报警（限频），
    /// 回到正常后自动恢复报警。
    /// </summary>
    private void CheckThresholds()
    {
        string target = "正常";

        if (deviceData.temperature >= tempFaultThreshold)
        {
            target = "故障";
            EnsureTempAlarm("故障", $"温度达到 {deviceData.temperature:F1} ℃");
        }
        else if (deviceData.temperature >= tempWarnThreshold)
        {
            target = StatusRank(target) >= 2 ? target : "警告";
            EnsureTempAlarm("警告", $"温度偏高 {deviceData.temperature:F1} ℃");
        }

        if (deviceData.pressure >= pressureFaultThreshold)
        {
            target = "故障";
            EnsurePressAlarm("故障", $"压力达到 {deviceData.pressure:F2} MPa");
        }
        else if (deviceData.pressure >= pressureWarnThreshold)
        {
            target = StatusRank(target) >= 2 ? target : "警告";
            EnsurePressAlarm("警告", $"压力偏高 {deviceData.pressure:F2} MPa");
        }

        // 全部回到正常 → 自动恢复该设备报警
        if (target == "正常")
        {
            if (temperatureAlarmAdded || pressureAlarmAdded)
            {
                if (AlarmManager.Instance != null)
                {
                    AlarmManager.Instance.RecoverAlarm(deviceData.deviceName);
                }
                temperatureAlarmAdded = false;
                pressureAlarmAdded = false;
            }
        }

        // 应用状态（离线状态不在此覆盖）
        if (target != deviceData.status && deviceData.status != "离线")
        {
            deviceData.status = target;
            ApplyStatusVisualAndSync();
        }
    }

    private void EnsureTempAlarm(string level, string msg)
    {
        if (!temperatureAlarmAdded && AlarmManager.Instance != null)
        {
            temperatureAlarmAdded = true;
            AlarmManager.Instance.AddAlarm(deviceData.deviceName, "温度过高", level, msg);
        }
    }

    private void EnsurePressAlarm(string level, string msg)
    {
        if (!pressureAlarmAdded && AlarmManager.Instance != null)
        {
            pressureAlarmAdded = true;
            AlarmManager.Instance.AddAlarm(deviceData.deviceName, "压力异常", level, msg);
        }
    }

    /// <summary>统一刷新状态灯 / 概览 / 后端同步（状态变化时调用）</summary>
    private void ApplyStatusVisualAndSync()
    {
        if (deviceStatusLabel != null)
        {
            deviceStatusLabel.text = $"状态    ● {deviceData.status}";
        }
        UpdateStatusLight();
        SystemEvents.RaiseDeviceStatusChanged();
        _ = SyncToApiAsync();
    }

    [System.Serializable]
    private class DeviceUpdateJson
    {
        public float temperature;
        public float pressure;
        public int rpm;
        public float runtime;
        public string status;
        public string workshop;
    }
}
