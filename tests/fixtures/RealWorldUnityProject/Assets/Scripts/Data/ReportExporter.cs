using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 数据报表导出：把报警记录、设备状态导出为 CSV 文件。
/// 文件保存在「文档/IndustrialMonitor」目录下，可直接用 Excel 打开。
/// </summary>
public class ReportExporter : MonoBehaviour
{
    private const string ExportFolderName = "IndustrialMonitor";

    /// <summary>导出报警记录为 CSV（供按钮绑定）</summary>
    public void ExportAlarms()
    {
        if (AlarmManager.Instance == null)
        {
            Debug.LogWarning("ReportExporter：找不到 AlarmManager，无法导出");
            return;
        }

        List<AlarmRecord> records = AlarmManager.Instance.GetAlarmRecords();

        StringBuilder sb = new StringBuilder();

        // 加 BOM，让 Excel 正确识别 UTF-8（否则中文乱码）
        sb.Append('\uFEFF');
        sb.AppendLine("时间,设备,类型,等级,状态,恢复时间,描述");

        foreach (AlarmRecord r in records)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(r.time),
                EscapeCsv(r.deviceName),
                EscapeCsv(r.alarmType),
                EscapeCsv(r.alarmLevel),
                EscapeCsv(r.status),
                EscapeCsv(r.recoverTime),
                EscapeCsv(r.alarmMessage)
            ));
        }

        SaveAndOpen(sb.ToString(), "报警记录");

        Debug.Log($"ReportExporter：已导出 {records.Count} 条报警记录");
    }

    /// <summary>导出设备状态为 CSV（供按钮绑定）</summary>
    public void ExportDevices()
    {
        DeviceData[] devices = FindObjectsOfType<DeviceData>();

        StringBuilder sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("设备,类型,状态,在线,温度(℃),压力(MPa),转速(RPM),运行时长(h)");

        foreach (DeviceData d in devices)
        {
            if (d == null) continue;

            sb.AppendLine(string.Join(",",
                EscapeCsv(d.deviceName),
                EscapeCsv(d.deviceType),
                EscapeCsv(d.status),
                d.isOnline ? "在线" : "离线",
                d.temperature.ToString("F1"),
                d.pressure.ToString("F2"),
                d.rpm.ToString(),
                d.runtime.ToString("F1")
            ));
        }

        SaveAndOpen(sb.ToString(), "设备状态");

        Debug.Log($"ReportExporter：已导出 {devices.Length} 台设备状态");
    }

    /// <summary>CSV 字段转义：含逗号/引号/换行时用双引号包裹</summary>
    private string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return "";
        }

        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            // 双引号转义为两个双引号
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        return field;
    }

    private void SaveAndOpen(string content, string reportName)
    {
        string dir = GetExportDirectory();

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{reportName}_{timestamp}.csv";
        string fullPath = System.IO.Path.Combine(dir, fileName);

        // UTF-8 写入（含 BOM）
        System.IO.File.WriteAllText(fullPath, content, new UTF8Encoding(true));

        Debug.Log($"ReportExporter：文件已保存 → {fullPath}");

        // 打开所在文件夹
        try
        {
            Application.OpenURL("file://" + dir);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ReportExporter：无法自动打开文件夹 - {e.Message}");
        }
    }

    private string GetExportDirectory()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string dir = System.IO.Path.Combine(documents, ExportFolderName);

        if (!System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        return dir;
    }
}