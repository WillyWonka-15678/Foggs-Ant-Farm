using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>全项目统一配色。面板颜色 = 它需要的那块木块的颜色。</summary>
public static class Palette
{
    public static readonly Color Notify   = Hex("#D85A30");
    public static readonly Color Bonus    = Hex("#EF9F27");
    public static readonly Color Assist   = Hex("#1D9E75");
    public static readonly Color Gray     = Hex("#5F5E5A");
    public static readonly Color Panel    = Hex("#2C2C2A", 0.92f);
    public static readonly Color BarBg    = Hex("#444441");
    public static readonly Color BarLight = Hex("#D3D1C7");
    public static readonly Color Warn     = Hex("#F09595");
    public static readonly Color Yellow   = Hex("#FAC775");
    public static readonly Color Dim      = Hex("#B4B2A9");
    public static readonly Color IdleLight = Hex("#F0997B");
    public static readonly Color IdleDeep  = Hex("#E24B4A");

    public static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        c.a = alpha;
        return c;
    }

    public static Color ForBlock(BlockType b)
    {
        switch (b)
        {
            case BlockType.Notify: return Notify;
            case BlockType.Bonus:  return Bonus;
            default:               return Assist;
        }
    }

    /// <summary>小人当前状态需要哪块木块</summary>
    public static BlockType NeededBlock(WorkerState s)
    {
        switch (s)
        {
            case WorkerState.Working: return BlockType.Bonus;
            case WorkerState.Idle:    return BlockType.Notify;
            default:                  return BlockType.Assist;
        }
    }
}

/// <summary>一根进度条：长度 0–1，颜色可变</summary>
public class UIBar
{
    public readonly RectTransform root;
    readonly Image fill;

    public UIBar(RectTransform root, Image fill) { this.root = root; this.fill = fill; }

    public void Set(float v)
    {
        var a = fill.rectTransform.anchorMax;
        a.x = Mathf.Clamp01(v);
        fill.rectTransform.anchorMax = a;
    }

    public void SetColor(Color c) => fill.color = c;
}

/// <summary>用代码搭 UI 的小工具。坐标以面板左上角为原点，单位像素，向下为正。</summary>
public static class HUDFactory
{
    static bool warnedFont;

    public static RectTransform CreateCanvas(Transform parent, string name, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(parent, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(w, h);
        rt.pivot = new Vector2(0.5f, 0f);          // 底边中点对齐挂点
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        return rt;
    }

    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color c)
    {
        var img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    public static TextMeshProUGUI Label(Transform parent, string name, float x, float y, float w, float h,
                                       float size, Color c,
                                       TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
                                       bool wrap = false, bool bold = false)
    {
        var t = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        if (t.font == null && !warnedFont)
        {
            warnedFont = true;
            Debug.LogWarning("TextMeshPro 没有默认字体：请执行 Window > TextMeshPro > Import TMP Essential Resources");
        }
        t.text = "";
        t.fontSize = size;
        t.color = c;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.richText = true;
        if (bold) t.fontStyle = FontStyles.Bold;
        return t;
    }

    public static UIBar Bar(Transform parent, string name, float x, float y, float w, float h, Color fillColor)
    {
        var root = Rect(parent, name, x, y, w, h);
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = Palette.BarBg;
        bg.raycastTarget = false;

        var f = new GameObject("Fill", typeof(RectTransform));
        var frt = (RectTransform)f.transform;
        frt.SetParent(root, false);
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = new Vector2(0f, 1f);
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        var fill = f.AddComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;

        return new UIBar(root, fill);
    }

    public static Image Icon(Transform parent, string name, float x, float y, float w, float h)
    {
        var img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.enabled = false;
        return img;
    }

    /// <summary>始终正面朝向主相机（观众的头）</summary>
    public static void FaceCamera(Transform t)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 dir = t.position - cam.transform.position;
        if (dir.sqrMagnitude < 1e-8f) return;
        t.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    public static string ClockUp(float sec)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(sec));
        return $"{s / 60:00}:{s % 60:00}";
    }

    public static string ClockDown(float sec)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(sec));
        return $"{s / 60:00}:{s % 60:00}";
    }
}
