using System;

[Serializable]
public class AlarmRecord
{
    public string deviceName;
    public string alarmType;
    public string alarmLevel;
    public string alarmMessage;
    public string time;

    // 报警状态：未恢复 / 已恢复
    public string status = "未恢复";
    // 恢复时间（恢复时填写）
    public string recoverTime = "";
}
