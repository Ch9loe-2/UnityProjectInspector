using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

/// <summary>
/// 历史记录页面：展示设备运行数据的趋势折线图。
/// 支持切换设备与指标（温度 / 压力），并显示当前值、最高、最低统计。
/// 连上后端时，优先从 GET /api/history 拉取长期数据绘制；未连接则用本地采样。
/// </summary>
public class HistoryPanel : MonoBehaviour
{
    [Header("图表")]
    [SerializeField] private LineChart lineChart;

    [Header("信息显示")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statText;

    [Header("Y 轴范围")]
    [SerializeField] private float tempMin = 40f;
    [SerializeField] private float tempMax = 120f;
    [SerializeField] private float pressMin = 0f;
    [SerializeField] private float pressMax = 3f;

    [Header("后端查询范围（分钟）")]
    [SerializeField] private int historyRangeMinutes = 1440;  // 默认拉最近 24 小时

    private string currentDevice = "设备 A";
    private string currentMetric = "温度";

    private void OnEnable()
    {
        RefreshChart();
    }

    // ===== 供按钮绑定的无参数方法 =====

    public void SelectDeviceA() { SelectDevice("设备 A"); }
    public void SelectDeviceB() { SelectDevice("设备 B"); }
    public void SelectDeviceC() { SelectDevice("设备 C"); }

    public void SelectTemperature() { SelectMetric("温度"); }
    public void SelectPressure() { SelectMetric("压力"); }

    // ===== 车间筛选（供 UI 按钮绑定）=====
    public void SelectWorkshop1() { SelectWorkshop("一号车间"); }
    public void SelectWorkshop2() { SelectWorkshop("二号车间"); }

    /// <summary>选中某车间时，自动切换到该车间下的第一台设备并刷新图表</summary>
    private void SelectWorkshop(string workshop)
    {
        DeviceData[] all = FindObjectsOfType<DeviceData>();
        foreach (DeviceData d in all)
        {
            if (d != null && d.workshop == workshop)
            {
                SelectDevice(d.deviceName);
                return;
            }
        }
    }

    // ===== 核心逻辑 =====

    private void SelectDevice(string deviceName)
    {
        currentDevice = deviceName;
        RefreshChart();
    }

    private void SelectMetric(string metric)
    {
        currentMetric = metric;
        RefreshChart();
    }

    /// <summary>
    /// 刷新图表与统计信息：
    /// 1) 先用本地最近采样立即出图（无延迟）；
    /// 2) 连上后端时异步拉取长期数据覆盖。
    /// </summary>
    public void RefreshChart()
    {
        bool isPressure = (currentMetric == "压力");
        float min = isPressure ? pressMin : tempMin;
        float max = isPressure ? pressMax : tempMax;

        if (titleText != null)
        {
            titleText.text = $"{currentDevice} · {currentMetric}趋势";
        }

        // 本地数据立即显示
        List<float> local = GetLocalData(isPressure);
        if (lineChart != null)
        {
            lineChart.SetData(local, min, max);
        }
        UpdateStats(local, isPressure, isLocal: true);

        // 连上时拉后端长期数据覆盖
        if (ApiClient.Instance != null && ApiClient.Instance.IsConnected)
        {
            _ = LoadFromBackendAsync(isPressure, min, max);
        }
    }

    private List<float> GetLocalData(bool isPressure)
    {
        if (HistoryRecorder.Instance == null)
        {
            Debug.LogWarning("HistoryPanel：场景中找不到 HistoryRecorder！");
            return new List<float>();
        }

        List<float> data = isPressure
            ? HistoryRecorder.Instance.GetPressureHistory(currentDevice)
            : HistoryRecorder.Instance.GetTemperatureHistory(currentDevice);

        return data ?? new List<float>();
    }

    /// <summary>从后端拉取历史数据并绘制（异步，带切换防抖）</summary>
    private async Task LoadFromBackendAsync(bool isPressure, float min, float max)
    {
        // 捕获请求时的选择，避免异步返回时用户已切换
        string reqDevice = currentDevice;
        string reqMetric = currentMetric;

        string dev = UnityWebRequest.EscapeURL(reqDevice);
        string met = UnityWebRequest.EscapeURL(reqMetric);
        string endpoint = $"/api/history/{dev}/{met}?minutes={historyRangeMinutes}";

        string json = await ApiClient.Instance.GetAsync(endpoint);
        if (string.IsNullOrEmpty(json)) return;

        // 已切换设备/指标，丢弃这次结果
        if (reqDevice != currentDevice || reqMetric != currentMetric) return;

        HistoryResponse resp = JsonUtility.FromJson<HistoryResponse>(json);
        if (resp == null || resp.data == null || resp.data.Count == 0)
        {
            UpdateStats(new List<float>(), isPressure, isLocal: false);
            return;
        }

        List<float> values = new List<float>(resp.data.Count);
        foreach (HistoryItem item in resp.data)
        {
            values.Add(item.value);
        }

        if (lineChart != null)
        {
            lineChart.SetData(values, min, max);
        }
        UpdateStats(values, isPressure, isLocal: false);
    }

    private void UpdateStats(List<float> data, bool isPressure, bool isLocal)
    {
        if (statText == null) return;

        if (data == null || data.Count == 0)
        {
            statText.text = isLocal
                ? "暂无数据（等待采样中...）"
                : "服务器暂无该时段数据";
            return;
        }

        float current = data[data.Count - 1];
        float highest = data[0];
        float lowest = data[0];

        for (int i = 1; i < data.Count; i++)
        {
            if (data[i] > highest) highest = data[i];
            if (data[i] < lowest) lowest = data[i];
        }

        string unit = isPressure ? "MPa" : "℃";
        string format = isPressure ? "F2" : "F1";
        string source = isLocal ? "（本地）" : "（服务器）";

        statText.text =
            $"当前 {current.ToString(format)} {unit}    " +
            $"最高 {highest.ToString(format)} {unit}    " +
            $"最低 {lowest.ToString(format)} {unit}    " +
            $"采样 {data.Count} 点 {source}";
    }

    // ── 后端响应 JSON 模型（camelCase，与 JsonUtility 对齐）──
    [System.Serializable]
    private class HistoryResponse
    {
        public int code = 0;
        public string message = null;
        public List<HistoryItem> data = null;
    }

    [System.Serializable]
    private class HistoryItem
    {
        public int id = 0;
        public string deviceName = null;
        public string metricName = null;
        public float value = 0f;
        public string timestamp = null;
    }
}
