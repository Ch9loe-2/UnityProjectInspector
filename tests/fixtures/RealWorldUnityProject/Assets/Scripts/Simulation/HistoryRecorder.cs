using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 历史数据采样器：定期采集各设备的运行数据，供历史记录页绘制趋势图，
/// 并在连接后端时批量上报，实现历史数据持久化。
/// 单例，场景启动时自动开始采样。
/// </summary>
public class HistoryRecorder : MonoBehaviour
{
    public static HistoryRecorder Instance { get; private set; }

    [Header("采样设置")]
    [SerializeField] private float sampleInterval = 2f;   // 采样间隔（秒）
    [SerializeField] private int maxPoints = 60;          // 本地最多保留点数
    [SerializeField] private float uploadInterval = 10f;  // 上报后端间隔（秒）

    // 设备名 → 温度序列（本地显示用）
    private Dictionary<string, List<float>> temperatureHistory
        = new Dictionary<string, List<float>>();

    // 设备名 → 压力序列（本地显示用）
    private Dictionary<string, List<float>> pressureHistory
        = new Dictionary<string, List<float>>();

    // 待上报缓冲：设备名 → 采样点列表
    private Dictionary<string, List<MetricBufferItem>> tempBuffer
        = new Dictionary<string, List<MetricBufferItem>>();

    private Dictionary<string, List<MetricBufferItem>> pressureBuffer
        = new Dictionary<string, List<MetricBufferItem>>();

    private float sampleTimer;
    private float uploadTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        sampleTimer += Time.deltaTime;
        if (sampleTimer >= sampleInterval)
        {
            sampleTimer = 0f;
            SampleAllDevices();
        }

        uploadTimer += Time.deltaTime;
        if (uploadTimer >= uploadInterval)
        {
            uploadTimer = 0f;
            FlushToBackend();
        }
    }

    /// <summary>采集所有设备的当前数据（本地 + 缓冲上报）</summary>
    private void SampleAllDevices()
    {
        DeviceData[] devices = FindObjectsOfType<DeviceData>();

        foreach (DeviceData device in devices)
        {
            if (device == null) continue;

            // 本地趋势图数据
            RecordValue(temperatureHistory, device.deviceName, device.temperature);
            RecordValue(pressureHistory, device.deviceName, device.pressure);

            // 待上报缓冲（带时间戳）
            BufferValue(tempBuffer, device.deviceName, device.temperature);
            BufferValue(pressureBuffer, device.deviceName, device.pressure);
        }
    }

    private void RecordValue(Dictionary<string, List<float>> dict, string deviceName, float value)
    {
        if (!dict.ContainsKey(deviceName))
        {
            dict[deviceName] = new List<float>();
        }

        List<float> list = dict[deviceName];
        list.Add(value);

        while (list.Count > maxPoints)
        {
            list.RemoveAt(0);
        }
    }

    private void BufferValue(
        Dictionary<string, List<MetricBufferItem>> dict,
        string deviceName,
        float value)
    {
        if (!dict.ContainsKey(deviceName))
        {
            dict[deviceName] = new List<MetricBufferItem>();
        }

        List<MetricBufferItem> list = dict[deviceName];
        list.Add(new MetricBufferItem
        {
            value = value,
            timestamp = DateTime.UtcNow.ToString("o")
        });

        // 缓冲封顶，避免离线期间无限增长
        while (list.Count > maxPoints)
        {
            list.RemoveAt(0);
        }
    }

    /// <summary>将缓冲的采样点批量上报到后端（仅连上时）</summary>
    private void FlushToBackend()
    {
        if (ApiClient.Instance == null || !ApiClient.Instance.IsConnected) return;

        _ = UploadMetricAsync(tempBuffer, "温度");
        _ = UploadMetricAsync(pressureBuffer, "压力");
    }

    private async Task UploadMetricAsync(
        Dictionary<string, List<MetricBufferItem>> liveBuffer,
        string metricName)
    {
        if (liveBuffer.Count == 0) return;

        // 取出快照并立即清空活动缓冲，避免慢网络下下一轮 flush 写入的数据被误清
        Dictionary<string, List<MetricBufferItem>> snapshot =
            new Dictionary<string, List<MetricBufferItem>>(liveBuffer);
        liveBuffer.Clear();

        foreach (var kvp in snapshot)
        {
            if (kvp.Value == null || kvp.Value.Count == 0) continue;

            MetricBatchJson batch = new MetricBatchJson
            {
                deviceName = kvp.Key,
                metricName = metricName,
                points = new List<MetricPointJson>()
            };

            foreach (MetricBufferItem item in kvp.Value)
            {
                batch.points.Add(new MetricPointJson
                {
                    value = item.value,
                    timestamp = item.timestamp
                });
            }

            string json = JsonUtility.ToJson(batch);
            await ApiClient.Instance.PostAsync("/api/history", json);
        }
    }

    /// <summary>获取某设备的温度历史（本地趋势图）</summary>
    public List<float> GetTemperatureHistory(string deviceName)
    {
        return GetHistory(temperatureHistory, deviceName);
    }

    /// <summary>获取某设备的压力历史（本地趋势图）</summary>
    public List<float> GetPressureHistory(string deviceName)
    {
        return GetHistory(pressureHistory, deviceName);
    }

    private List<float> GetHistory(Dictionary<string, List<float>> dict, string deviceName)
    {
        if (dict.ContainsKey(deviceName))
        {
            return dict[deviceName];
        }
        return new List<float>();
    }

    /// <summary>供外部手动记录（例如故障模拟时立即采样）</summary>
    public void RecordNow(DeviceData device)
    {
        if (device == null) return;

        RecordValue(temperatureHistory, device.deviceName, device.temperature);
        RecordValue(pressureHistory, device.deviceName, device.pressure);
        BufferValue(tempBuffer, device.deviceName, device.temperature);
        BufferValue(pressureBuffer, device.deviceName, device.pressure);
    }

    // ── 上报用 JSON 模型 ──────────────────────────────────────────────
    [System.Serializable]
    private class MetricPointJson
    {
        public double value;
        public string timestamp;
    }

    [System.Serializable]
    private class MetricBatchJson
    {
        public string deviceName;
        public string metricName;
        public List<MetricPointJson> points;
    }

    private class MetricBufferItem
    {
        public double value;
        public string timestamp;
    }
}
