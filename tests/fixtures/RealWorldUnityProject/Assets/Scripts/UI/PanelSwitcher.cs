using UnityEngine;

public class PanelSwitcher : MonoBehaviour
{
    [Header("页面面板")]
    [SerializeField] private GameObject deviceDetailPanel;
    [SerializeField] private GameObject alarmPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject historyPanel;

    private void Start()
    {
        // 启动时只显示默认概览（隐藏所有面板）
        ShowOverview();
    }

    /// <summary>点击设备 → 显示设备详情，隐藏其他面板</summary>
    public void ShowDeviceDetail()
    {
        SetActive(deviceDetailPanel, true);
        SetActive(alarmPanel, false);
        SetActive(settingsPanel, false);
        SetActive(historyPanel, false);
    }

    /// <summary>报警记录 → 显示报警面板，隐藏其他</summary>
    public void ShowAlarmPanel()
    {
        SetActive(deviceDetailPanel, false);
        SetActive(alarmPanel, true);
        SetActive(settingsPanel, false);
        SetActive(historyPanel, false);
    }

    /// <summary>系统设置 → 显示设置面板，隐藏其他</summary>
    public void ShowSettingsPanel()
    {
        SetActive(deviceDetailPanel, false);
        SetActive(alarmPanel, false);
        SetActive(settingsPanel, true);
        SetActive(historyPanel, false);
    }

    /// <summary>历史记录 → 显示历史数据面板（折线图），隐藏其他</summary>
    public void ShowHistoryPanel()
    {
        SetActive(deviceDetailPanel, false);
        SetActive(alarmPanel, false);
        SetActive(settingsPanel, false);
        SetActive(historyPanel, true);
    }

    /// <summary>回到概览 → 隐藏所有面板（3D 场景 + 右侧概览保持可见）</summary>
    public void ShowOverview()
    {
        SetActive(deviceDetailPanel, false);
        SetActive(alarmPanel, false);
        SetActive(settingsPanel, false);
        SetActive(historyPanel, false);
    }

    private void SetActive(GameObject obj, bool active)
    {
        if (obj != null)
        {
            obj.SetActive(active);
        }
    }
}