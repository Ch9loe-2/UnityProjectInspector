using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 折线图组件（纯 UI 实现）。
/// 继承 UnityEngine.UI.Graphic，重写 OnPopulateMesh 用顶点绘制折线，
/// 不依赖 LineRenderer，可直接挂在 Canvas 下的 UI 元素上。
/// </summary>
public class LineChart : Graphic
{
    [Header("折线样式")]
    [SerializeField] private float lineWidth = 2.5f;

    [Header("网格")]
    [SerializeField] private bool showGrid = true;
    [SerializeField] private int gridRows = 4;
    [SerializeField] private float gridWidth = 1f;

    private List<float> values = new List<float>();
    private float minValue = 0f;
    private float maxValue = 120f;

    /// <summary>
    /// 设置图表数据。
    /// </summary>
    /// <param name="data">数值序列</param>
    /// <param name="min">Y 轴最小值</param>
    /// <param name="max">Y 轴最大值</param>
    public void SetData(List<float> data, float min, float max)
    {
        values = (data != null) ? new List<float>(data) : new List<float>();
        minValue = min;
        maxValue = max;

        // 触发重新绘制
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();

        // 1. 画背景网格
        if (showGrid)
        {
            DrawGrid(vh, rect);
        }

        // 2. 画折线（至少需要 2 个点）
        if (values == null || values.Count < 2)
        {
            return;
        }

        float range = maxValue - minValue;
        if (range <= 0f)
        {
            range = 1f;
        }

        float xStep = rect.width / (values.Count - 1);

        for (int i = 0; i < values.Count - 1; i++)
        {
            float x1 = rect.xMin + i * xStep;
            float x2 = rect.xMin + (i + 1) * xStep;

            float y1 = rect.yMin + ((values[i] - minValue) / range) * rect.height;
            float y2 = rect.yMin + ((values[i + 1] - minValue) / range) * rect.height;

            DrawLine(vh, new Vector2(x1, y1), new Vector2(x2, y2), lineWidth, color);
        }
    }

    /// <summary>绘制一条有宽度的线段（用两个三角形拼成）</summary>
    private void DrawLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color col)
    {
        Vector2 dir = (b - a);
        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        dir.Normalize();

        // 法线方向（垂直于线段）
        Vector2 normal = new Vector2(-dir.y, dir.x) * width * 0.5f;

        int startIndex = vh.currentVertCount;

        vh.AddVert(a + normal, col, Vector2.zero);
        vh.AddVert(a - normal, col, Vector2.zero);
        vh.AddVert(b - normal, col, Vector2.zero);
        vh.AddVert(b + normal, col, Vector2.zero);

        vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
        vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
    }

    /// <summary>绘制横向网格线</summary>
    private void DrawGrid(VertexHelper vh, Rect rect)
    {
        Color gridColor = new Color(color.r, color.g, color.b, 0.15f);

        for (int i = 1; i < gridRows; i++)
        {
            float y = rect.yMin + (rect.height / gridRows) * i;
            DrawLine(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y),
                gridWidth, gridColor);
        }
    }
}