using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Threading.Tasks;

public class AlarmManager : MonoBehaviour
{
    public static AlarmManager Instance;

    // 报警变化事件：供系统概览、最近报警栏等订阅
    public delegate void AlarmChangedHandler();
    public event AlarmChangedHandler OnAlarmChanged;

    [Header("报警记录 UI")]
    [SerializeField] private Transform alarmList;
    [SerializeField] private GameObject alarmItemPrefab;

    [Header("最近报警栏")]
    [SerializeField] private TMP_Text recentAlarmText;
    [SerializeField] private GameObject recentAlarmPanel;

    [Header("报警 Item 设置")]
    [SerializeField] private float firstItemY = -66f;
    [SerializeField] private float itemSpacing = 100f;

    private List<AlarmRecord> alarmRecords = new List<AlarmRecord>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // 初始化最近报警栏空态
        RefreshRecentAlarmBar();
    }

    public void AddAlarm(
        string deviceName,
        string alarmType,
        string alarmLevel,
        string alarmMessage)
    {
        AlarmRecord record = new AlarmRecord();

        record.deviceName = deviceName;
        record.alarmType = alarmType;
        record.alarmLevel = alarmLevel;
        record.alarmMessage = alarmMessage;
        record.time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        record.status = "未恢复";

        alarmRecords.Add(record);

        Debug.Log(
            $"报警记录：{record.time} | " +
            $"{record.deviceName} | " +
            $"{record.alarmType} | " +
            $"{record.alarmLevel} | " +
            $"{record.alarmMessage}"
        );

        CreateAlarmItem(record);
        RefreshRecentAlarmBar();

        if (OnAlarmChanged != null)
        {
            OnAlarmChanged();
        }

        // 同步到 API（异步，不影响本地运行）
        _ = SyncAddAlarmAsync(record);
    }

    /// <summary>
    /// 恢复某台设备的报警：把所有"未恢复"且匹配该设备的记录置为"已恢复"。
    /// 供 DeviceController 的"恢复正常"按钮调用。
    /// </summary>
    public void RecoverAlarm(string deviceName)
    {
        bool changed = false;

        foreach (AlarmRecord record in alarmRecords)
        {
            if (record.deviceName == deviceName && record.status == "未恢复")
            {
                record.status = "已恢复";
                record.recoverTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                changed = true;
            }
        }

        if (changed)
        {
            // 刷新列表显示状态（简单起见：重建列表）
            RebuildAlarmList();
            RefreshRecentAlarmBar();

            if (OnAlarmChanged != null)
            {
                OnAlarmChanged();
            }

            // 同步到 API
            _ = SyncRecoverAlarmAsync(deviceName);
        }
    }

    private async Task SyncAddAlarmAsync(AlarmRecord record)
    {
        if (ApiClient.Instance == null || !ApiClient.Instance.IsConnected) return;

        string json = JsonUtility.ToJson(new AlarmJson
        {
            deviceName = record.deviceName,
            alarmType = record.alarmType,
            alarmLevel = record.alarmLevel,
            alarmMessage = record.alarmMessage
        });

        await ApiClient.Instance.PostAsync("/api/alarms", json);
        ApiClient.Instance?.LogOperation("报警产生", record.deviceName,
            $"{record.alarmType}({record.alarmLevel})：{record.alarmMessage}");
    }

    private async Task SyncRecoverAlarmAsync(string deviceName)
    {
        if (ApiClient.Instance == null || !ApiClient.Instance.IsConnected) return;

        string encoded = UnityEngine.Networking.UnityWebRequest.EscapeURL(deviceName);
        await ApiClient.Instance.PutAsync($"/api/alarms/recover/{encoded}", "{}");
        ApiClient.Instance?.LogOperation("报警恢复", deviceName, "该设备报警已恢复");
    }

    [System.Serializable]
    private class AlarmJson
    {
        public string deviceName;
        public string alarmType;
        public string alarmLevel;
        public string alarmMessage;
    }

    public List<AlarmRecord> GetAlarmRecords()
    {
        return alarmRecords;
    }

    private void RebuildAlarmList()
    {
        if (alarmList == null)
        {
            return;
        }

        // 清空现有条目
        foreach (Transform child in alarmList)
        {
            Destroy(child.gameObject);
        }

        // 重新生成
        int index = 0;
        foreach (AlarmRecord record in alarmRecords)
        {
            index++;
            SpawnAlarmItem(record, index);
        }
    }

    private void CreateAlarmItem(AlarmRecord record)
    {
        SpawnAlarmItem(record, alarmRecords.Count);
    }

    private void SpawnAlarmItem(AlarmRecord record, int index)
    {
        if (alarmList == null)
        {
            Debug.LogError("AlarmManager：Alarm List 没有设置！");
            return;
        }

        if (alarmItemPrefab == null)
        {
            Debug.LogError("AlarmManager：Alarm Item Prefab 没有设置！");
            return;
        }

        GameObject item = Instantiate(alarmItemPrefab, alarmList, false);
        item.name = "AlarmItem_" + index;

        RectTransform itemRect = item.GetComponent<RectTransform>();
        if (itemRect != null)
        {
            itemRect.anchorMin = new Vector2(0.5f, 1f);
            itemRect.anchorMax = new Vector2(0.5f, 1f);
            itemRect.pivot = new Vector2(0.5f, 0.5f);

            float y = firstItemY - (index - 1) * itemSpacing;
            itemRect.anchoredPosition = new Vector2(0f, y);
            itemRect.localScale = Vector3.one;
        }

        item.SetActive(true);

        TMP_Text text = item.GetComponent<TMP_Text>();
        if (text != null)
        {
            // 表格化显示：状态列显示"未恢复/已恢复"
            text.text =
                $"{record.deviceName}  {record.alarmType}  {record.alarmLevel}\n" +
                $"{record.time}\n" +
                $"{record.alarmMessage}  【{record.status}】";
        }
        else
        {
            Debug.LogWarning("AlarmManager：AlarmItem 根物体上没有 TMP_Text 组件！");
        }
    }

    private void RefreshRecentAlarmBar()
    {
        if (recentAlarmText == null)
        {
            return;
        }

        if (alarmRecords.Count == 0)
        {
            recentAlarmText.text = "✓ 当前无异常报警";
            return;
        }

        // 找最新一条"未恢复"报警（倒序遍历，因新报警追加在列表末尾）
        AlarmRecord latest = null;
        for (int i = alarmRecords.Count - 1; i >= 0; i--)
        {
            if (alarmRecords[i].status == "未恢复")
            {
                latest = alarmRecords[i];
                break;
            }
        }

        if (latest == null)
        {
            recentAlarmText.text = "✓ 当前无异常报警";
            return;
        }

        recentAlarmText.text =
            $"⚠ 最近报警  {latest.time}  {latest.deviceName}  {latest.alarmType}";
    }
}
