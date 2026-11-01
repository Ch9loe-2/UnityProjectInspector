using UnityEngine;
using TMPro;

public class MonitoringOverview : MonoBehaviour
{
    [Header("系统概览")]
    [SerializeField] private TMP_Text totalDevicesValue;
    [SerializeField] private TMP_Text normalDevicesValue;
    [SerializeField] private TMP_Text warningDevicesValue;
    [SerializeField] private TMP_Text faultDevicesValue;
    [SerializeField] private TMP_Text offlineDevicesValue;

    [Header("监控设备")]
    [SerializeField] private DeviceData[] devices;

    [Header("车间分组统计")]
    [SerializeField] private TMP_Text workshopSummaryText;

    private void OnEnable()
    {
        SystemEvents.DeviceStatusChanged += UpdateOverview;
        UpdateOverview();
    }

    private void OnDisable()
    {
        SystemEvents.DeviceStatusChanged -= UpdateOverview;
    }

    public void UpdateOverview()
    {
        if (devices == null)
        {
            return;
        }

        int total = devices.Length;
        int normal = 0;
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
                case "正常":
                    normal++;
                    break;

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

        totalDevicesValue.text = total.ToString();
        normalDevicesValue.text = normal.ToString();
        warningDevicesValue.text = warning.ToString();
        faultDevicesValue.text = fault.ToString();

        if (offlineDevicesValue != null)
        {
            offlineDevicesValue.text = offline.ToString();
        }

        // 按车间分组统计（各车间设备数 / 异常数）
        if (workshopSummaryText != null)
        {
            System.Collections.Generic.Dictionary<string, int[]> stat =
                new System.Collections.Generic.Dictionary<string, int[]>();

            foreach (DeviceData device in devices)
            {
                if (device == null) continue;

                if (!stat.ContainsKey(device.workshop))
                {
                    stat[device.workshop] = new int[2]; // [0]=总数, [1]=异常数
                }

                stat[device.workshop][0]++;

                if (device.status != "正常")
                {
                    stat[device.workshop][1]++;
                }
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (var kvp in stat)
            {
                if (sb.Length > 0) sb.Append("    ");
                sb.Append($"{kvp.Key}: {kvp.Value[0]}台 异常{kvp.Value[1]}");
            }

            workshopSummaryText.text = sb.ToString();
        }
    }
}
