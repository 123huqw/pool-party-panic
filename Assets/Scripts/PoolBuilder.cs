using UnityEngine;

/// <summary>代码生成奥运标准泳池：8 道、100 米、浮标分道线、出发台、池底黑线、看台、灯光。</summary>
public static class PoolBuilder
{
    public static float LaneX(int lane) => (lane - (Tuning.LaneCount - 1) * 0.5f) * Tuning.LaneWidth;

    public static readonly Color[] CapColors =
    {
        Art.Hex("#ff4d4d"), Art.Hex("#ff9f1c"), Art.Hex("#ffd60a"), Art.Hex("#2ec4b6"),
        Art.Hex("#3a86ff"), Art.Hex("#8338ec"), Art.Hex("#ff006e"), Art.Hex("#80ed99"),
    };

    public static Material WaterMat;

    public static void Build(Transform root)
    {
        float W = Tuning.LaneCount * Tuning.LaneWidth;
        float L = Tuning.Distance;
        const float depth = 2.2f;
        const float deck = 6f;

        // ── 池底 & 池壁（瓷砖）
        var tile = Art.TileTexture(Art.Hex("#8fd3f4"), Art.Hex("#e6f6ff"));
        var floorMat = Art.NewMat(Color.white, 0.35f);
        floorMat.mainTexture = tile;
        floorMat.mainTextureScale = new Vector2(W / 1f, L / 1f);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -depth - 0.05f, L / 2), new Vector3(W, 0.1f, L), floorMat, name: "PoolFloor");

        var wallMatSide = Art.NewMat(Color.white, 0.35f); wallMatSide.mainTexture = tile; wallMatSide.mainTextureScale = new Vector2(L, depth);
        var wallMatEnd = Art.NewMat(Color.white, 0.35f); wallMatEnd.mainTexture = tile; wallMatEnd.mainTextureScale = new Vector2(W, depth);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(-W / 2 - 0.05f, -depth / 2, L / 2), new Vector3(0.1f, depth, L), wallMatSide);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(W / 2 + 0.05f, -depth / 2, L / 2), new Vector3(0.1f, depth, L), wallMatSide);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -depth / 2, -0.05f), new Vector3(W, depth, 0.1f), wallMatEnd);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -depth / 2, L + 0.05f), new Vector3(W, depth, 0.1f), wallMatEnd);

        // ── 池底泳道黑线（T 字收尾）
        var lineMat = Art.Mat(Art.Hex("#1b2a4a"), 0.2f);
        for (int i = 0; i < Tuning.LaneCount; i++)
        {
            float x = LaneX(i);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, -depth + 0.01f, L / 2), new Vector3(0.25f, 0.02f, L - 4f), lineMat);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, -depth + 0.01f, 2f), new Vector3(1f, 0.02f, 0.25f), lineMat);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, -depth + 0.01f, L - 2f), new Vector3(1f, 0.02f, 0.25f), lineMat);
            // 池壁目标十字
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, -depth * 0.5f, L - 0.005f), new Vector3(0.25f, depth * 0.8f, 0.02f), lineMat);
        }

        // ── 水面
        WaterMat = Art.Template("Mat_Water");
        WaterMat.color = new Color(0.15f, 0.62f, 0.85f, 0.42f);
        WaterMat.SetFloat("_Glossiness", 0.96f);
        MakeTransparent(WaterMat);
        var nrm = Art.WaterNormal();
        WaterMat.EnableKeyword("_NORMALMAP");
        WaterMat.SetTexture("_BumpMap", nrm);
        WaterMat.SetFloat("_BumpScale", 0.55f);
        WaterMat.mainTextureScale = new Vector2(W / 4f, L / 4f);
        var water = Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -0.01f, L / 2), new Vector3(W, 0.02f, L), WaterMat, name: "Water");
        water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        water.AddComponent<WaterAnim>();

        // ── 分道线浮标（红-白-蓝，两端 5 米红色）
        var red = Art.Mat(Art.Hex("#e63946"), 0.6f);
        var white = Art.Mat(Art.Hex("#f1faee"), 0.6f);
        var blue = Art.Mat(Art.Hex("#1d3fbb"), 0.6f);
        var yellow = Art.Mat(Art.Hex("#ffd60a"), 0.6f);
        const float spacing = 0.33f;
        for (int k = 0; k <= Tuning.LaneCount; k++)
        {
            float x = (k - Tuning.LaneCount * 0.5f) * Tuning.LaneWidth;
            if (k == 0 || k == Tuning.LaneCount) x += k == 0 ? 0.15f : -0.15f;
            var rope = new GameObject("LaneRope" + k).transform;
            rope.SetParent(root, false);
            int n = Mathf.FloorToInt(L / spacing);
            for (int i = 0; i <= n; i++)
            {
                float z = i * spacing;
                Material m;
                if (z < 5f || z > L - 5f) m = red;
                else if (Mathf.Abs(z - L / 2) < 1.2f) m = yellow;
                else m = (i % 4 < 2) ? (k % 2 == 0 ? blue : white) : (k % 2 == 0 ? white : blue);
                var d = Art.Prim(PrimitiveType.Sphere, rope, new Vector3(x, 0.03f, z), new Vector3(0.16f, 0.13f, 0.3f), m);
                d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ── 池岸
        var deckMat = Art.Mat(Art.Hex("#f3efe6"), 0.25f);
        var deckEdge = Art.Mat(Art.Hex("#d9e4ea"), 0.3f);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(-W / 2 - deck / 2, 0.1f, L / 2), new Vector3(deck, 0.2f, L + deck * 2), deckMat);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(W / 2 + deck / 2, 0.1f, L / 2), new Vector3(deck, 0.2f, L + deck * 2), deckMat);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 0.1f, -deck / 2), new Vector3(W, 0.2f, deck), deckMat);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 0.1f, L + deck / 2), new Vector3(W, 0.2f, deck), deckMat);
        // 池沿压条
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 0.12f, -0.15f), new Vector3(W + 0.6f, 0.24f, 0.3f), deckEdge);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 0.12f, L + 0.15f), new Vector3(W + 0.6f, 0.24f, 0.3f), deckEdge);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(-W / 2 - 0.15f, 0.12f, L / 2), new Vector3(0.3f, 0.24f, L), deckEdge);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(W / 2 + 0.15f, 0.12f, L / 2), new Vector3(0.3f, 0.24f, L), deckEdge);

        // ── 出发台 + 终点触板
        var blockMat = Art.Mat(Art.Hex("#e9ecef"), 0.5f);
        var blockTop = Art.Mat(Art.Hex("#2b2d42"), 0.2f);
        var touchMat = Art.Mat(Art.Hex("#ffd60a"), 0.6f);
        for (int i = 0; i < Tuning.LaneCount; i++)
        {
            float x = LaneX(i);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, 0.4f, -0.55f), new Vector3(0.6f, 0.6f, 0.6f), blockMat);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, 0.72f, -0.5f), new Vector3(0.6f, 0.05f, 0.75f), blockTop, Quaternion.Euler(-8, 0, 0));
            Art.Label(root, (i + 1).ToString(), new Vector3(x, 0.45f, -0.24f), 0.5f, PoolBuilder.CapColors[i]).transform.rotation = Quaternion.Euler(0, 180, 0);
            // 终点触板
            Art.Prim(PrimitiveType.Cube, root, new Vector3(x, -0.3f, L - 0.02f), new Vector3(Tuning.LaneWidth * 0.86f, 0.9f, 0.04f), touchMat);
            Art.Label(root, (i + 1).ToString(), new Vector3(x, 0.45f, L + 0.4f), 0.5f, Color.white).transform.rotation = Quaternion.Euler(0, 0, 0);
        }

        // ── 15 米 / 25 米标记旗 & 地面标记
        var flagMat = Art.Mat(Art.Hex("#ff006e"), 0.4f);
        var poleMat = Art.Mat(Art.Hex("#adb5bd"), 0.6f, 0.6f);
        foreach (float fz in new[] { 5f, L - 5f })
        {
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(-W / 2 - 1.2f, 1.1f, fz), new Vector3(0.08f, 1.1f, 0.08f), poleMat);
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(W / 2 + 1.2f, 1.1f, fz), new Vector3(0.08f, 1.1f, 0.08f), poleMat);
            for (float x = -W / 2 - 1.2f; x <= W / 2 + 1.2f; x += 0.5f)
            {
                var f = Art.Prim(PrimitiveType.Cube, root, new Vector3(x, 1.95f, fz), new Vector3(0.3f, 0.3f, 0.01f), flagMat, Quaternion.Euler(0, 0, 45));
                f.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        for (int m = 10; m < L; m += 10)
        {
            Art.Label(root, m + "m", new Vector3(-W / 2 - 0.9f, 0.21f, m), 0.6f, Art.Hex("#495057")).transform.rotation = Quaternion.Euler(90, 0, 0);
            Art.Label(root, m + "m", new Vector3(W / 2 + 0.9f, 0.21f, m), 0.6f, Art.Hex("#495057")).transform.rotation = Quaternion.Euler(90, 0, 0);
        }

        // ── 看台 & 观众（几何方块）
        var standMat = Art.Mat(Art.Hex("#3d5a80"), 0.2f);
        var seatMats = new[] { Art.Mat(Art.Hex("#ee6c4d")), Art.Mat(Art.Hex("#98c1d9")), Art.Mat(Art.Hex("#e0fbfc")), Art.Mat(Art.Hex("#ffd166")), Art.Mat(Art.Hex("#06d6a0")) };
        var crowdRoot = new GameObject("Crowd").transform;
        crowdRoot.SetParent(root, false);
        var rnd = new System.Random(11);
        for (int side = -1; side <= 1; side += 2)
        {
            for (int row = 0; row < 6; row++)
            {
                float x = side * (W / 2 + deck + 0.8f + row * 1.1f);
                float y = 0.5f + row * 0.75f;
                Art.Prim(PrimitiveType.Cube, root, new Vector3(x, y / 2, L / 2), new Vector3(1.1f, y, L + 10f), standMat);
                for (float z = -3f; z < L + 3f; z += 0.9f)
                {
                    if (rnd.NextDouble() < 0.25) continue;
                    var p = Art.Prim(PrimitiveType.Capsule, crowdRoot, new Vector3(x, y + 0.4f, z + (float)rnd.NextDouble() * 0.3f),
                        new Vector3(0.45f, 0.4f, 0.45f), seatMats[rnd.Next(seatMats.Length)]);
                    p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }
        crowdRoot.gameObject.AddComponent<CrowdBounce>();

        // ── 场馆外墙 & 屋顶（简易）
        var hallMat = Art.Mat(Art.Hex("#1f2b44"), 0.1f);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 6f, L + 12f), new Vector3(W + 40f, 12f, 0.5f), hallMat);
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 6f, -14f), new Vector3(W + 40f, 12f, 0.5f), hallMat);

        // 终点大屏
        var screen = Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 6.5f, L + 11.6f), new Vector3(14f, 4.5f, 0.2f), Art.Mat(Art.Hex("#0b132b"), 0.8f));
        Art.Label(root, "SWIM PARTY · 100m", new Vector3(0, 7.6f, L + 11.4f), 1.1f, Art.Hex("#ffd60a")).transform.rotation = Quaternion.Euler(0, 180, 0);
        GameObject.Destroy(screen.GetComponent<Collider>());

        // ── 灯光
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Art.Hex("#fff4e0");
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.6f;
        sun.transform.rotation = Quaternion.Euler(55, -30, 0);
        sun.transform.SetParent(root, false);

        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = Art.Hex("#9ad1ff");
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(30, 150, 0);
        fill.transform.SetParent(root, false);

        // 水下焦散感：几盏蓝色点光
        for (int i = 0; i < 5; i++)
        {
            var pl = new GameObject("PoolLight").AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = Art.Hex("#59c3ff");
            pl.range = 14f;
            pl.intensity = 1.2f;
            pl.transform.SetParent(root, false);
            pl.transform.localPosition = new Vector3(0, -1.6f, 5 + i * (L - 10) / 4f);
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Art.Hex("#bcd9f2");
        RenderSettings.ambientEquatorColor = Art.Hex("#7aa7c7");
        RenderSettings.ambientGroundColor = Art.Hex("#3b6e8f");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Art.Hex("#9ec9e6");
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 140f;
    }

    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Mode", 3);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }
}

/// <summary>水面法线滚动。</summary>
public class WaterAnim : MonoBehaviour
{
    Material _m;
    void Start() { _m = GetComponent<Renderer>().sharedMaterial; }
    void Update()
    {
        if (_m == null) return;
        float t = Time.time;
        _m.SetTextureOffset("_BumpMap", new Vector2(t * 0.018f, t * 0.031f));
    }
}

/// <summary>观众跟着比赛气氛蹦。</summary>
public class CrowdBounce : MonoBehaviour
{
    public static float Hype;   // 0~1，由 GameManager 设置
    Transform[] _kids;
    float[] _base, _ph;
    void Start()
    {
        int n = transform.childCount;
        _kids = new Transform[n]; _base = new float[n]; _ph = new float[n];
        for (int i = 0; i < n; i++)
        {
            _kids[i] = transform.GetChild(i);
            _base[i] = _kids[i].localPosition.y;
            _ph[i] = Random.value * 10f;
        }
    }
    void Update()
    {
        float t = Time.time;
        float amp = 0.03f + Hype * 0.28f;
        float spd = 4f + Hype * 8f;
        for (int i = 0; i < _kids.Length; i++)
        {
            var p = _kids[i].localPosition;
            p.y = _base[i] + Mathf.Max(0, Mathf.Sin(t * spd + _ph[i])) * amp;
            _kids[i].localPosition = p;
        }
    }
}
