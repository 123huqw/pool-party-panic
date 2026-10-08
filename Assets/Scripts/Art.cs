using System.Collections.Generic;
using UnityEngine;

/// <summary>运行时生成材质、贴图、字体、几何体的小工具。整个原型没有任何美术资源文件。</summary>
public static class Art
{
    static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
    static Shader _std;
    static Font _font;

    public static Shader Std
    {
        get
        {
            if (_std == null) _std = Shader.Find("Standard");
            return _std;
        }
    }

    /// <summary>共享的不透明材质（同色复用，开启 GPU Instancing）。</summary>
    public static Material Mat(Color c, float smooth = 0.3f, float metal = 0f)
    {
        string key = $"{c.r:0.00},{c.g:0.00},{c.b:0.00},{c.a:0.00}|{smooth:0.00}|{metal:0.00}";
        if (Cache.TryGetValue(key, out var m)) return m;
        m = NewMat(c, smooth, metal);
        Cache[key] = m;
        return m;
    }

    /// <summary>
    /// 材质模板放在 Resources 里（由编辑器脚本 SwimSetup 生成），
    /// 这样打包时 Standard 着色器和所需的关键字变体不会被裁掉。
    /// </summary>
    public static Material Template(string name)
    {
        var t = Resources.Load<Material>(name);
        if (t != null) return new Material(t);
        return new Material(Std);
    }

    /// <summary>独立材质（需要单独改颜色时用，比如会变红的脸）。</summary>
    public static Material NewMat(Color c, float smooth = 0.3f, float metal = 0f)
    {
        var m = Template("Mat_Opaque");
        m.color = c;
        m.enableInstancing = true;
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", metal);
        return m;
    }

    public static Font Font
    {
        get
        {
            if (_font == null)
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 48);
            return _font;
        }
    }

    public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat,
        Quaternion? rot = null, bool keepCollider = false, string name = null)
    {
        var go = GameObject.CreatePrimitive(type);
        if (name != null) go.name = name;
        if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot ?? Quaternion.identity;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        return go;
    }

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }

    /// <summary>泳池瓷砖：浅蓝底 + 白色勾缝。</summary>
    public static Texture2D TileTexture(Color tile, Color grout, int size = 128, int tiles = 4)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        int cell = size / tiles;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool g = (x % cell) < 2 || (y % cell) < 2;
            float n = (Mathf.PerlinNoise(x * 0.11f, y * 0.11f) - 0.5f) * 0.05f;
            var c = g ? grout : tile;
            px[y * size + x] = new Color(c.r + n, c.g + n, c.b + n, 1);
        }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>水面法线贴图：可平铺的多层正弦波，A=1 让内置 UnpackNormal 直接读 RGB。</summary>
    public static Texture2D WaterNormal(int size = 256)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        var h = new float[size * size];
        var rnd = new System.Random(7);
        var waves = new List<Vector4>(); // kx, ky, phase, amp
        for (int i = 0; i < 14; i++)
        {
            int kx = rnd.Next(-6, 7), ky = rnd.Next(-6, 7);
            if (kx == 0 && ky == 0) ky = 1;
            float amp = 1f / Mathf.Sqrt(kx * kx + ky * ky);
            waves.Add(new Vector4(kx, ky, (float)rnd.NextDouble() * Mathf.PI * 2, amp));
        }
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / size, v = (float)y / size, s = 0;
            foreach (var w in waves) s += Mathf.Sin((w.x * u + w.y * v) * Mathf.PI * 2 + w.z) * w.w;
            h[y * size + x] = s;
        }
        var px = new Color[size * size];
        const float strength = 2.2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float l = h[y * size + (x + size - 1) % size], r = h[y * size + (x + 1) % size];
            float d = h[((y + size - 1) % size) * size + x], u = h[((y + 1) % size) * size + x];
            var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
            px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>柔边圆点，用于水花粒子。</summary>
    public static Texture2D SoftDot(int size = 64)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
            float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
            px[y * size + x] = new Color(1, 1, 1, a * a);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    public static TextMesh Label(Transform parent, string text, Vector3 localPos, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = Font;
        tm.fontSize = 48;
        tm.characterSize = size / 10f;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.text = text;
        go.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
        return tm;
    }
}
