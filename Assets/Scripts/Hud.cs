using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Utils
{
    public static string FmtTime(float sec)
    {
        if (sec < 0 || float.IsNaN(sec)) sec = 0;
        int m = (int)(sec / 60);
        float s = sec - m * 60;
        return $"{m}:{s:00.00}";
    }
}

/// <summary>
/// HUD 全部用 IMGUI 画，零资源、零预制体。
/// 屏幕中下方的「节奏条」是这个原型最重要的 UI：它让玩家看见自己的节奏。
/// </summary>
public class Hud : MonoBehaviour
{
    public GameManager GM;
    public bool ShowTuning;

    Texture2D _white, _round;
    GUIStyle _big, _mid, _small, _tiny, _center, _box, _btn, _judge;
    float _scale = 1f;

    // 判定飘字
    struct Pop { public string Text; public Color Color; public float T; public float X; }
    readonly List<Pop> _pops = new List<Pop>();
    // 节奏历史（最近 N 次间隔）
    readonly List<(float iv, Judge j)> _history = new List<(float, Judge)>();
    // 播报
    readonly List<(string text, Color c, float t)> _feed = new List<(string, Color, float)>();
    string _toast; Color _toastColor; float _toastUntil, _toastStart;
    string _breathText; Color _breathColor; float _breathT = -99f;
    Vector2 _tuneScroll;
    string _copied; float _copiedT;

    void Awake()
    {
        _white = Texture2D.whiteTexture;
        _round = MakeRound(64);
    }

    static Texture2D MakeRound(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - size / 2f, dy = y + 0.5f - size / 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(size / 2f - d)));
        }
        t.Apply();
        return t;
    }

    void InitStyles()
    {
        float s = _scale;
        GUIStyle Make(int size, FontStyle fs = FontStyle.Bold, TextAnchor a = TextAnchor.UpperLeft)
        {
            var st = new GUIStyle(GUI.skin.label) { font = Art.Font, fontSize = Mathf.RoundToInt(size * s), fontStyle = fs, alignment = a, richText = true };
            st.normal.textColor = Color.white;
            st.wordWrap = false;
            return st;
        }
        _big = Make(64, FontStyle.Bold, TextAnchor.MiddleCenter);
        _mid = Make(26);
        _small = Make(19, FontStyle.Normal);
        _tiny = Make(15, FontStyle.Normal);
        _center = Make(22, FontStyle.Normal, TextAnchor.MiddleCenter);
        _judge = Make(34, FontStyle.Bold, TextAnchor.MiddleCenter);
        _box = new GUIStyle(GUI.skin.box);
        _btn = new GUIStyle(GUI.skin.button) { font = Art.Font, fontSize = Mathf.RoundToInt(15 * s) };
    }

    // ═══════════════ 外部调用 ═══════════════
    public void ResetFeed()
    {
        _pops.Clear(); _history.Clear(); _feed.Clear(); _toastUntil = 0; _breathT = -99f; _impacts.Clear(); _hitFlash = 0f;
    }

    public void Toast(string text, Color c, float dur)
    {
        _toast = text; _toastColor = c; _toastStart = Time.unscaledTime; _toastUntil = Time.unscaledTime + dur;
    }

    public void Feed(string text, Color c)
    {
        _feed.Add((text, c, Time.time));
        if (_feed.Count > 5) _feed.RemoveAt(0);
    }

    // 命中字：在被坑者头顶炸出一个大字
    struct ImpactPop { public string Text; public Color C; public float T; public Swimmer Target; }
    readonly List<ImpactPop> _impacts = new List<ImpactPop>();
    float _hitFlash;
    public void Impact(string text, Color c, Swimmer target)
    {
        _impacts.Add(new ImpactPop { Text = text, C = c, T = Time.unscaledTime, Target = target });
        if (_impacts.Count > 4) _impacts.RemoveAt(0);
    }
    public void HitFlash(float a) { _hitFlash = Mathf.Max(_hitFlash, a); }

    void DrawImpacts()
    {
        var cam = Camera.main;
        float h = Screen.height;
        for (int i = 0; i < _impacts.Count; i++)
        {
            var p = _impacts[i];
            float age = Time.unscaledTime - p.T;
            if (age > 0.9f || cam == null || p.Target == null) continue;
            var body = p.Target.GetComponent<SwimmerBody>();
            Vector3 sp = cam.WorldToScreenPoint(body.TorsoPos + new Vector3(0, 1.0f, 0));
            if (sp.z <= 0) continue;
            float sc = age < 0.08f ? Mathf.Lerp(2.4f, 1f, age / 0.08f) : 1f + Mathf.Sin(age * 30f) * 0.03f;
            float a = Mathf.Clamp01((0.9f - age) * 4f);
            var st = new GUIStyle(_big) { fontSize = (int)(S(54) * sc) };
            Label(new Rect(sp.x - S(250), h - sp.y - S(50) - age * S(50), S(500), S(100)), p.Text, st, TextAnchor.MiddleCenter, new Color(p.C.r, p.C.g, p.C.b, a));
        }
        if (_hitFlash > 0f)
        {
            Vignette(new Color(1f, 0.1f, 0.1f, _hitFlash));
            Rect(new Rect(0, 0, Screen.width, Screen.height), new Color(1f, 1f, 1f, _hitFlash * 0.25f));
            _hitFlash = Mathf.MoveTowards(_hitFlash, 0f, Time.unscaledDeltaTime * 1.2f);
        }
    }

    public void OnStroke(StrokeEvent e)
    {
        string txt; Color c;
        switch (e.Judge)
        {
            case Judge.Perfect: txt = "PERFECT"; c = Art.Hex("#ffd60a"); break;
            case Judge.Good: txt = "GOOD"; c = Art.Hex("#80ed99"); break;
            case Judge.Ok: txt = "OK"; c = Art.Hex("#a0c4ff"); break;
            case Judge.Slow: txt = "太慢"; c = Art.Hex("#adb5bd"); break;
            case Judge.Rush: txt = "太急"; c = Art.Hex("#ffa94d"); break;
            case Judge.Mash: txt = "乱划！"; c = Art.Hex("#ff6b6b"); break;
            case Judge.Fumble: txt = "同一只手！"; c = Art.Hex("#ff6b6b"); break;
            default: txt = "起划"; c = Color.white; break;
        }
        _pops.Add(new Pop { Text = txt, Color = c, T = Time.time, X = e.Hand == 0 ? -1 : 1 });
        if (_pops.Count > 6) _pops.RemoveAt(0);
        if (e.Interval > 0 && e.Judge != Judge.Fumble && e.Judge != Judge.Start)
        {
            _history.Add((e.Interval, e.Judge));
            if (_history.Count > 14) _history.RemoveAt(0);
        }
    }

    public void OnBreath(BreathResult r)
    {
        switch (r)
        {
            case BreathResult.Perfect: _breathText = "完美换气 +速"; _breathColor = Art.Hex("#ffd60a"); break;
            case BreathResult.Good: _breathText = "换气"; _breathColor = Art.Hex("#a0c4ff"); break;
            case BreathResult.Close: _breathText = "好险！"; _breathColor = Art.Hex("#ffa94d"); break;
            case BreathResult.Early: _breathText = "抢气了（太早）-速"; _breathColor = Art.Hex("#ffa94d"); break;
            case BreathResult.Recover: _breathText = "缓过来了"; _breathColor = Art.Hex("#80ed99"); break;
            case BreathResult.Choke: _breathText = "呛水！咳咳咳"; _breathColor = Art.Hex("#ff6b6b"); break;
        }
        _breathT = Time.time;
    }

    // ═══════════════ 绘制 ═══════════════
    void OnGUI()
    {
        _scale = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
        InitStyles();
        if (GM == null || GM.Player == null) return;

        switch (GM.State)
        {
            case GameState.Title: DrawTitle(); break;
            case GameState.Intro: DrawIntro(); break;
            case GameState.Countdown: DrawRaceHud(); DrawCountdown(); break;
            case GameState.Racing: DrawRaceHud(); break;
            case GameState.Results: DrawResults(); break;
        }

        DrawToast();
        if (GM.PauseOpen) DrawPause();
        if (ShowTuning) DrawTuning();
        else Label(new Rect(Screen.width - 260 * _scale, Screen.height - 30 * _scale, 250 * _scale, 24 * _scale), "<color=#ffffff99>F1 调参  ·  M 节拍器  ·  R 重开</color>", _tiny, TextAnchor.MiddleRight);
    }

    float S(float v) => v * _scale;

    void Rect(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old;
    }

    void Dot(Vector2 center, float radius, Color c)
    {
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), _round); GUI.color = old;
    }

    void Label(Rect r, string text, GUIStyle st, TextAnchor? a = null, Color? color = null, bool shadow = true)
    {
        var s = new GUIStyle(st);
        if (a.HasValue) s.alignment = a.Value;
        if (shadow)
        {
            var sh = new GUIStyle(s); sh.normal.textColor = new Color(0, 0, 0, 0.55f * (color?.a ?? 1));
            GUI.Label(new Rect(r.x + S(2), r.y + S(2), r.width, r.height), StripColor(text), sh);
        }
        if (color.HasValue) s.normal.textColor = color.Value;
        GUI.Label(r, text, s);
    }

    static string StripColor(string t) => System.Text.RegularExpressions.Regex.Replace(t, "<color=[^>]*>|</color>", "");

    void Panel(Rect r, float alpha = 0.55f) => Rect(r, new Color(0.03f, 0.08f, 0.16f, alpha));

    void Bar(Rect r, float v, Color fill, Color back, string label = null)
    {
        Rect(r, back);
        Rect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), fill);
        if (label != null) Label(r, label, _tiny, TextAnchor.MiddleCenter);
    }

    // ───── 标题 ─────
    void DrawTitle()
    {
        float w = Screen.width, h = Screen.height;
        Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.1f, 0.2f, 0.45f));
        Label(new Rect(0, h * 0.06f, w, S(90)), "泳 道 老 六", _big, TextAnchor.MiddleCenter, Art.Hex("#ffd60a"));
        Label(new Rect(0, h * 0.06f + S(76), w, S(36)), "选择玩法：  ← / A 竞速      → / D 互坑", _center, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.85f));

        float cw = S(560), ch = S(500), gap = S(40);
        float cy = h * 0.06f + S(130);
        var left = new Rect(w / 2 - gap / 2 - cw, cy, cw, ch);
        var right = new Rect(w / 2 + gap / 2, cy, cw, ch);
        if (GUI.Button(left, GUIContent.none, GUIStyle.none)) GM.SetMode(false);
        if (GUI.Button(right, GUIContent.none, GUIStyle.none)) GM.SetMode(true);
        DrawModeCard(left, false, "竞 速", "纯竞速 · 拼节奏和换气", Art.Hex("#4cc9f0"), new[]
        {
            "<color=#ffd60a>A / D</color>  左右手交替划水，<b>节奏就是速度</b>",
            "        同一只手连按 = 失衡；乱按 = 白费力气",
            "<color=#ffd60a>空格</color>  换气：气条见底前换，金色区 = 完美",
            "<color=#ff6b6b>不换气：脸变直 → 憋红挣扎 → 沉底判最后</color>",
            "<color=#ffd60a>Shift</color>  按住加速，消耗体力",
            "",
            "<color=#ffffff88>没有拽脚 / 蹬人，比的是谁手更稳</color>",
        });
        DrawModeCard(right, true, "互 坑", "拽脚、蹬人、一拽一蹬", Art.Hex("#ff4dd2"), new[]
        {
            "<color=#ffd60a>A / D</color>  划水   <color=#ffd60a>Shift</color>  加速   <color=#ffd60a>Q / E</color>  换泳道",
            "<color=#ffd60a>空格</color>  换气只是<b>加成</b>：按准短暂加速，不会淹死",
            "<color=#ffd60a>F</color>  对手在前 → <b>拽脚甩飞</b>；在后 → <b>蹬人</b>",
            "<color=#ffd60a>F F</color>  快速连按两次赌「<color=#ff4dd2>一拽一蹬</color>」",
            "<color=#ff6b6b>被甩飞</color>：照屏幕提示按 <b>3 个随机键</b> 爬回来",
            "        按错会呛水下沉，<b>乱按没用</b>",
            "<color=#ff4dd2>爬回来 = 复仇</color>：无敌 + 抓得更远、冷却减半",
            "<color=#ffffff88>落后越多游得越快，领头的会变慢、被全场盯上</color>",
        });

        float y = left.yMax + S(22);
        string[] diff = { "轻松", "普通", "高手" };
        string ds = "";
        for (int i = 0; i < 3; i++) ds += (i == GM.Difficulty ? $"<color=#ffd60a>[{i + 1} {diff[i]}]</color>" : $"<color=#ffffff88> {i + 1} {diff[i]} </color>") + "   ";
        Label(new Rect(0, y, w, S(34)), "对手水平：  " + ds + "     <color=#ffffff88>M 节拍器  ·  F1 调参</color>", _small, TextAnchor.MiddleCenter);

        float blink = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
        string modeName = Tuning.FoulRoom ? "<color=#ff4dd2>互坑</color>" : "<color=#4cc9f0>竞速</color>";
        Label(new Rect(0, y + S(46), w, S(40)), $"按 回车 / 空格 开始 {modeName}", _mid, TextAnchor.MiddleCenter, new Color(1, 1, 1, blink));
    }

    void DrawModeCard(Rect r, bool foul, string title, string sub, Color accent, string[] lines)
    {
        bool sel = Tuning.FoulRoom == foul;
        float lift = sel ? -S(8) : 0f;
        r.y += lift;
        Panel(r, sel ? 0.85f : 0.45f);
        float bw = sel ? S(5) : S(1);
        Color bc = sel ? accent : new Color(1, 1, 1, 0.15f);
        Rect(new Rect(r.x, r.y, r.width, bw), bc); Rect(new Rect(r.x, r.yMax - bw, r.width, bw), bc);
        Rect(new Rect(r.x, r.y, bw, r.height), bc); Rect(new Rect(r.xMax - bw, r.y, bw, r.height), bc);
        float a = sel ? 1f : 0.45f;
        Label(new Rect(r.x, r.y + S(16), r.width, S(70)), title, new GUIStyle(_big) { fontSize = (int)S(52) }, TextAnchor.MiddleCenter, new Color(accent.r, accent.g, accent.b, a));
        Label(new Rect(r.x, r.y + S(84), r.width, S(30)), sub, _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f * a));
        Rect(new Rect(r.x + S(30), r.y + S(124), r.width - S(60), S(1)), new Color(1, 1, 1, 0.2f));
        float y = r.y + S(140);
        foreach (var l in lines)
        {
            Label(new Rect(r.x + S(28), y, r.width - S(56), S(32)), sel ? l : $"<color=#ffffff77>{StripColor(l)}</color>", _small);
            y += S(46);
        }
        if (sel) Label(new Rect(r.x, r.yMax - S(4) - S(34), r.width, S(30)), "▲ 已选择", _small, TextAnchor.MiddleCenter, accent);
    }

    void DrawIntro()
    {
        float w = Screen.width, h = Screen.height;
        Label(new Rect(0, h * 0.14f, w, S(60)), Tuning.FoulRoom ? $"{Tuning.Distance:0} 米 · <color=#ff4dd2>互坑赛</color>" : $"{Tuning.Distance:0} 米自由泳 · <color=#4cc9f0>竞速决赛</color>", _mid, TextAnchor.MiddleCenter, Color.white);
        Label(new Rect(0, h * 0.14f + S(44), w, S(40)), $"第 {GM.Player.Lane + 1} 泳道 —— 你", _small, TextAnchor.MiddleCenter, GM.Player.CapColor);
        Label(new Rect(0, h * 0.14f + S(80), w, S(34)), Tuning.FoulRoom ? "靠近对手出现提示时按 F：前面拽脚，后面蹬人" : "稳住节奏，别忘了换气", _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.85f));
        Label(new Rect(0, h * 0.85f, w, S(30)), "回车跳过", _tiny, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.6f));
    }

    void DrawCountdown()
    {
        float left = GM.CountdownEnd - Time.time;
        int n = Mathf.CeilToInt(left);
        string t = n > 3 ? "各就各位" : n >= 1 ? n.ToString() : "GO!";
        float frac = left - Mathf.Floor(left);
        float sc = 1f + frac * 0.4f;
        var st = new GUIStyle(_big) { fontSize = Mathf.RoundToInt(_big.fontSize * sc * (n > 3 ? 0.6f : 1.4f)) };
        Label(new Rect(0, Screen.height * 0.22f, Screen.width, S(160)), t, st, TextAnchor.MiddleCenter, Art.Hex("#ffd60a"));
        Label(new Rect(0, Screen.height * 0.22f + S(140), Screen.width, S(30)), "别抢跳！哨响后再划", _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));
    }

    // ───── 比赛 HUD ─────
    void DrawRaceHud()
    {
        var P = GM.Player;
        float w = Screen.width, h = Screen.height;

        // 左上：名次 + 时间 + 距离
        var tl = new Rect(S(20), S(20), S(300), S(132));
        Panel(tl);
        int place = GM.PlayerPlace();
        Label(new Rect(tl.x + S(16), tl.y + S(6), S(140), S(80)), $"<size={(int)S(58)}>{place}</size><size={(int)S(24)}>/{GM.Swimmers.Count}</size>", _mid, null, place == 1 ? Art.Hex("#ffd60a") : Color.white);
        float rt = P.Finished ? P.FinishTime : P.RaceTime;
        Label(new Rect(tl.x + S(140), tl.y + S(14), S(150), S(30)), Utils.FmtTime(rt), _mid);
        Label(new Rect(tl.x + S(140), tl.y + S(50), S(150), S(30)), $"{P.Z:0.0} / {Tuning.Distance:0} m", _small);
        Label(new Rect(tl.x + S(16), tl.y + S(92), S(280), S(30)), $"速度 {P.V:0.00} m/s   ×{P.DebugMultiplier():0.00}", _tiny, null, new Color(1, 1, 1, 0.8f));
        string room = Tuning.FoulRoom ? "<color=#ff4dd2>● 允许互坑</color>" : "<color=#4cc9f0>● 纯竞速</color>";
        if (Tuning.FoulRoom)
        {
            room += $"  第{P.Lane + 1}道";
            float cu = P.CatchUp;
            if (cu > 0.005f) room += $"  <color=#80ed99>追赶 +{cu * 100f:0}%</color>";
        }
        Label(new Rect(tl.x, tl.yMax + S(2), S(300), S(22)), room, _tiny, TextAnchor.MiddleCenter);

        // 右上：赛道小地图
        DrawMinimap(new Rect(w - S(250), S(20), S(230), S(330)));

        // 播报
        float fy = S(170);
        for (int i = 0; i < _feed.Count; i++)
        {
            var f = _feed[i];
            float age = Time.time - f.t;
            if (age > 5f) continue;
            float a = Mathf.Clamp01(5f - age);
            Label(new Rect(S(22), fy, S(500), S(26)), f.text, _small, null, new Color(f.c.r, f.c.g, f.c.b, a));
            fy += S(28);
        }

        // 底部中：节奏条（最重要的 UI）
        DrawRhythm(new Rect(w / 2 - S(330), h - S(190), S(660), S(150)));

        // 左下：换气 & 体力
        DrawBreath(new Rect(S(30), h - S(190), S(80), S(160)));
        DrawStamina(new Rect(S(130), h - S(190), S(80), S(160)));

        // 判定飘字
        for (int i = 0; i < _pops.Count; i++)
        {
            var p = _pops[i];
            float age = Time.time - p.T;
            if (age > 0.7f) continue;
            float a = 1f - age / 0.7f;
            float sc = 1f + Mathf.Max(0, 0.15f - age) * 3f;
            var st = new GUIStyle(_judge) { fontSize = Mathf.RoundToInt(_judge.fontSize * sc) };
            Label(new Rect(w / 2 + p.X * S(170) - S(150), h - S(280) - age * S(60), S(300), S(50)), p.Text, st, TextAnchor.MiddleCenter, new Color(p.Color.r, p.Color.g, p.Color.b, a));
        }

        // 连击
        if (P.Combo >= 3)
        {
            float pulse = 1f + Mathf.Max(0, 0.2f - (Time.time - P.LastJudgeTime)) * 1.5f;
            var st = new GUIStyle(_mid) { fontSize = Mathf.RoundToInt(_mid.fontSize * pulse) };
            Label(new Rect(w / 2 - S(150), h - S(330), S(300), S(40)), $"{P.Combo} 连击", st, TextAnchor.MiddleCenter, Art.Hex("#ffd60a"));
        }

        // 互坑提示
        if (Tuning.FoulRoom && GM.State == GameState.Racing)
        {
            DrawMarked();
            if (P.Ragdolled) DrawRecovery(); else DrawFoulPrompt();
            DrawRubberBand();
        }
        DrawImpacts();
        // 追赶压迫感：身后有人逼近，边缘泛红随心跳脉动
        if (GM.ChasePressure > 0.05f && P.Active)
        {
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(4f, 8f, GM.ChasePressure))), 6f);
            Vignette(new Color(0.95f, 0.1f, 0.1f, GM.ChasePressure * (0.12f + 0.2f * beat)));
            if (GM.ChasePressure > 0.5f && GM.PromptTarget == null)
                Label(new Rect(0, S(150), w, S(30)), "身后有人逼近！", _small, TextAnchor.MiddleCenter, new Color(1f, 0.4f, 0.4f, GM.ChasePressure));
        }

        // 憋气警告：画面边缘发红
        if (P.Active && P.BreathTier > 0)
        {
            float a = P.BreathTier == 2 ? 0.35f + 0.2f * Mathf.Sin(Time.time * 14f) : 0.18f;
            Vignette(new Color(0.9f, 0.05f, 0.05f, a));
        }
        else if (P.Active && P.Breath < Tuning.BreathPerfectMax && P.Breath > 0)
        {
            Vignette(new Color(1f, 0.85f, 0.2f, 0.06f + 0.05f * Mathf.Sin(Time.time * 10f)));
        }
        if (P.Sprinting) Vignette(new Color(1, 1, 1, 0.06f));
        if (P.Drowned) Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.1f, 0.3f, Mathf.Clamp01((Time.time - P.DrownTime) * 0.4f) * 0.6f));

        if ((P.Finished || P.Drowned) && GM.State == GameState.Racing)
        {
            Label(new Rect(0, h * 0.62f, w, S(30)), "等待其他选手… 按回车直接看结果", _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));
        }
    }

    void DrawFoulPrompt()
    {
        var P = GM.Player;
        float w = Screen.width, h = Screen.height;

        // 被组合技蹬飞：自己在翻滚
        if (P.Tumbling)
        {
            float sh = Mathf.Sin(Time.time * 30f) * S(6);
            Label(new Rect(sh, h * 0.42f, w, S(50)), "被蹬飞了！翻滚中…", new GUIStyle(_mid) { fontSize = (int)S(34) }, TextAnchor.MiddleCenter, Art.Hex("#ff6b6b"));
            Vignette(new Color(1f, 0.2f, 0.6f, 0.25f));
            return;
        }

        // 第一下 F 已打出，等待第二下：给一个「快！」的暗示，但不暴露窗口
        if (GM.PendingCombo)
        {
            float a = 0.6f + 0.4f * Mathf.Sin(Time.time * 40f);
            Label(new Rect(0, h * 0.5f + S(40), w, S(40)), "再按 F ！", new GUIStyle(_mid) { fontSize = (int)S(30) }, TextAnchor.MiddleCenter, new Color(1f, 0.3f, 0.82f, a));
        }

        var t = GM.PromptTarget;
        if (t == null) { DrawLaneHint(); return; }
        var body = t.GetComponent<SwimmerBody>();
        var cam = Camera.main;
        bool ready = Foul.CanAct(P);
        string verb = GM.PromptAhead ? "拽脚" : "蹬人";
        Color col = !ready ? new Color(0.7f, 0.7f, 0.7f, 0.8f) : GM.PromptAhead ? Art.Hex("#ffd60a") : Art.Hex("#ff8c42");
        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 12f);
        float appear = Mathf.Clamp01((Time.time - GM.PromptSince) * 6f);

        // 1) 世界空间标记：挂在目标头顶
        if (cam != null && body != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(body.TorsoPos + new Vector3(0, 1.25f, 0));
            if (sp.z > 0)
            {
                Vector2 c = new Vector2(sp.x, h - sp.y);
                float r = S(34) * pulse * (2f - appear);
                Dot(c, r, new Color(col.r, col.g, col.b, 0.35f * appear));
                Dot(c, r * 0.7f, new Color(0, 0, 0, 0.55f * appear));
                Label(new Rect(c.x - S(40), c.y - S(22), S(80), S(44)), "F", new GUIStyle(_mid) { fontSize = (int)(S(34) * pulse) }, TextAnchor.MiddleCenter, col);
                Label(new Rect(c.x - S(100), c.y - r - S(38), S(200), S(36)), $"{verb}！", new GUIStyle(_mid) { fontSize = (int)S(28) }, TextAnchor.MiddleCenter, col);
                // 箭头：拽脚 ↓ 往回拉，蹬人 ↓ 往后蹬，都指向目标
                Label(new Rect(c.x - S(30), c.y + r * 0.6f, S(60), S(30)), "▼", _small, TextAnchor.MiddleCenter, col);
            }
        }

        // 2) 屏幕中下方横幅
        var bw = S(700); var bh = S(56);
        var br = new Rect((w - bw) / 2, h - S(400) - bh, bw, bh);
        Rect(br, new Color(0, 0, 0, 0.45f * appear));
        Rect(new Rect(br.x, br.y, S(6), br.height), col);
        Rect(new Rect(br.xMax - S(6), br.y, S(6), br.height), col);
        string who = t.DisplayName;
        string dir = GM.PromptAhead ? "在你前面" : "在你身后";
        string msg = ready
            ? $"<color=#ffffff>{who} {dir}</color>   按 <b>[F]</b> {verb}   <color=#ff4dd2>快速连按两次 = 赌组合技</color>"
            : (P.Stamina < Tuning.FoulCost ? $"{who} {dir} —— 体力不够 {verb}" : $"{who} {dir} —— 冷却中");
        var st = new GUIStyle(_small) { fontSize = (int)(S(20) * (ready ? pulse * 0.97f : 1f)) };
        Label(br, msg, st, TextAnchor.MiddleCenter, col);
    }

    // ═══════════════ 被甩飞：挣扎恢复提示 ═══════════════
    void DrawRecovery()
    {
        var P = GM.Player;
        float w = Screen.width, h = Screen.height, now = Time.time;
        var big = new GUIStyle(_big);

        if (P.Flung)
        {
            // 空中：失控，什么都按不了 —— 只管尖叫
            float sh = Mathf.Sin(now * 37f) * S(10);
            big.fontSize = (int)S(64);
            Label(new Rect(sh, h * 0.26f, w, S(90)), "啊啊啊啊啊——！！", big, TextAnchor.MiddleCenter, Art.Hex("#ff4dd2"));
            Label(new Rect(0, h * 0.26f + S(86), w, S(36)), "你被甩飞了…… 准备落水后挣扎！", _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.85f));
            Vignette(new Color(1f, 0.3f, 0.8f, 0.22f));
            return;
        }
        if (!P.RecoverPromptActive) return;

        bool choking = now < P.RecoverPenaltyUntil;
        float wf = GM.WrongFlash;
        if (choking) Vignette(new Color(0.1f, 0.3f, 0.9f, 0.4f + 0.15f * Mathf.Sin(now * 20f)));
        if (wf > 0f) Rect(new Rect(0, 0, w, h), new Color(1f, 0.1f, 0.1f, wf * 0.3f));

        // 标题
        string title = choking ? "咕噜咕噜…… 呛水了！" : "快！照着按，爬回来！";
        Color tc = choking ? Art.Hex("#74c0fc") : Art.Hex("#ffd60a");
        big.fontSize = (int)S(42);
        float tsh = choking ? Mathf.Sin(now * 25f) * S(8) : 0f;
        Label(new Rect(tsh, h * 0.22f, w, S(60)), title, big, TextAnchor.MiddleCenter, tc);

        // 当前要按的键（大）+ 后面两个预告（小、灰）
        float bs = S(128);
        float cx = w * 0.5f, cy = h * 0.22f + S(80) + bs * 0.5f;
        float life = Mathf.Clamp01((P.RecoverPromptUntil - now) / Mathf.Max(0.05f, Tuning.PromptLife));
        float pulse = choking ? 1f : 1f + 0.06f * Mathf.Sin(now * 18f);
        float shake = wf * Mathf.Sin(now * 70f) * S(18);
        var keyRect = new Rect(cx - bs * pulse * 0.5f + shake, cy - bs * pulse * 0.5f, bs * pulse, bs * pulse);
        Color kc = choking ? new Color(0.5f, 0.5f, 0.6f, 0.8f) : Art.Hex("#ffd60a");
        Rect(keyRect, new Color(0, 0, 0, 0.75f));
        float bw = S(6);
        Rect(new Rect(keyRect.x, keyRect.y, keyRect.width, bw), kc); Rect(new Rect(keyRect.x, keyRect.yMax - bw, keyRect.width, bw), kc);
        Rect(new Rect(keyRect.x, keyRect.y, bw, keyRect.height), kc); Rect(new Rect(keyRect.xMax - bw, keyRect.y, bw, keyRect.height), kc);
        var ks = new GUIStyle(_big) { fontSize = (int)(S(84) * pulse) };
        Label(keyRect, choking ? "…" : Swimmer.KeyName(P.RecoverKeys[0]), ks, TextAnchor.MiddleCenter, kc);
        // 倒计时条：键会过期换掉
        if (!choking) Rect(new Rect(keyRect.x, keyRect.yMax + S(8), keyRect.width * life, S(8)), Color.Lerp(Art.Hex("#ff6b6b"), Art.Hex("#ffd60a"), life));

        var ss = new GUIStyle(_mid) { fontSize = (int)S(38) };
        int left = Mathf.Max(0, P.RecoverNeed - P.RecoverStep - 1);
        for (int i = 1; i <= Mathf.Min(left, P.RecoverKeys.Length - 1); i++)
        {
            float sz = S(70);
            var r = new Rect(keyRect.xMax + S(26) + (i - 1) * (sz + S(16)), cy - sz * 0.5f, sz, sz);
            Rect(r, new Color(0, 0, 0, 0.45f));
            Label(r, "?", ss, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.35f));
        }

        // 进度：●●○
        string dots = "";
        for (int i = 0; i < P.RecoverNeed; i++) dots += i < P.RecoverStep ? "<color=#80ed99>●</color> " : "<color=#ffffff55>○</color> ";
        Label(new Rect(0, keyRect.yMax + S(24), w, S(44)), dots, new GUIStyle(_mid) { fontSize = (int)S(34) }, TextAnchor.MiddleCenter);

        float elapsed = now - P.RecoverStart;
        string tip = P.RecoverWrongs > 0 ? $"按错 {P.RecoverWrongs} 次 —— 键换了，看清楚再按！" : "按错 = 呛一口水、换个键；已按对的不会丢";
        Label(new Rect(0, keyRect.yMax + S(70), w, S(30)), tip, _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.7f));
        float maxLeft = P.BodyFreeUntil - now;
        if (maxLeft < 2.5f)
            Label(new Rect(0, keyRect.yMax + S(100), w, S(30)), $"再过 {maxLeft:0.0} 秒救生员会把你捞回去……", _small, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.6f, 0.8f));
        if (elapsed < 0f) { }
    }

    // ═══════════════ 众矢之的：领头的人头顶挂靶子 ═══════════════
    void DrawMarked()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float h = Screen.height, now = Time.time;
        foreach (var o in GM.Swimmers)
        {
            if (!o.Active || !o.IsMarked) continue;
            var body = o.GetComponent<SwimmerBody>();
            if (body == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(body.TorsoPos + new Vector3(0, 1.7f, 0));
            if (sp.z <= 0) continue;
            float bob = Mathf.Sin(now * 4f) * S(5);
            Vector2 c = new Vector2(sp.x, h - sp.y + bob);
            float r = S(22) * (1f + 0.1f * Mathf.Sin(now * 9f));
            Dot(c, r, new Color(1f, 0.15f, 0.15f, 0.85f));
            Dot(c, r * 0.68f, new Color(1f, 1f, 1f, 0.9f));
            Dot(c, r * 0.38f, new Color(1f, 0.15f, 0.15f, 0.95f));
            string label = o.IsPlayer ? "你是众矢之的！" : "众矢之的";
            Label(new Rect(c.x - S(110), c.y - r - S(30), S(220), S(28)), label, _small, TextAnchor.MiddleCenter, Art.Hex("#ff6b6b"));
        }
    }

    /// <summary>左侧小条：追赶加成 / 领头减速 / 复仇状态，让橡皮筋看得见。</summary>
    void DrawRubberBand()
    {
        var P = GM.Player;
        if (!P.Active && !P.Ragdolled) return;
        float h = Screen.height, now = Time.time;
        float x = S(24), y = h * 0.45f;
        if (P.Revenge)
        {
            float left = P.RevengeUntil - now;
            float a = 0.75f + 0.25f * Mathf.Sin(now * 12f);
            Panel(new Rect(x, y, S(260), S(64)));
            Label(new Rect(x + S(14), y + S(4), S(240), S(32)), "<b>复仇中！</b>", new GUIStyle(_mid) { fontSize = (int)S(26) }, TextAnchor.MiddleLeft, new Color(1f, 0.3f, 0.82f, a));
            Label(new Rect(x + S(14), y + S(34), S(240), S(24)), $"抓人范围×{Tuning.RevengeRangeMul:0.#}  冷却×{Tuning.RevengeCooldownMul:0.#}", _tiny, TextAnchor.MiddleLeft, Color.white);
            Rect(new Rect(x, y + S(62), S(260) * Mathf.Clamp01(left / Tuning.RevengeTime), S(4)), Art.Hex("#ff4dd2"));
            y += S(78);
        }
        if (now < P.ImmuneUntil && !P.Ragdolled)
        {
            Label(new Rect(x, y, S(260), S(26)), "无敌中", _small, TextAnchor.MiddleLeft, new Color(0.6f, 0.9f, 1f, 0.6f + 0.4f * Mathf.Sin(now * 15f)));
            y += S(30);
        }
        float cu = P.CatchUp;
        if (cu > 0.01f)
        {
            Label(new Rect(x, y, S(300), S(26)), $"追赶加成 +{cu * 100f:0}%", _small, TextAnchor.MiddleLeft, Art.Hex("#80ed99"));
            y += S(30);
        }
        if (P.LeaderSlowMul < 0.999f)
            Label(new Rect(x, y, S(300), S(26)), $"领头减速 -{(1f - P.LeaderSlowMul) * 100f:0}%", _small, TextAnchor.MiddleLeft, Art.Hex("#ff8787"));
    }

    /// <summary>没有可拽目标时：前方隔壁道有人快够着了 → 提示按 Q/E 换道过去。</summary>
    void DrawLaneHint()
    {
        var P = GM.Player;
        if (!P.Active || P.Shifting) return;
        Swimmer best = null; float bestD = 99f;
        foreach (var o in GM.Swimmers)
        {
            if (o == P || !o.Active) continue;
            int dl = o.Lane - P.Lane;
            if (Mathf.Abs(dl) < 2 || Mathf.Abs(dl) > 3) continue;   // 隔一道以内本来就够得着
            float dz = o.Z - P.Z;
            if (dz < 0.3f || dz > Tuning.FoulRange * 2.5f) continue;
            if (dz < bestD) { bestD = dz; best = o; }
        }
        if (best == null) return;
        bool left = best.Lane < P.Lane;
        float w = Screen.width, h = Screen.height;
        float a = 0.65f + 0.35f * Mathf.Sin(Time.time * 6f);
        string msg = left ? $"◀ 按 Q 换道，去拽 {best.DisplayName}" : $"按 E 换道，去拽 {best.DisplayName} ▶";
        Label(new Rect(0, h - S(400) - S(46), w, S(40)), msg, _small, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.04f, a));
    }

    void Vignette(Color c)
    {
        float w = Screen.width, h = Screen.height, t = S(60);
        for (int i = 0; i < 6; i++)
        {
            float k = 1f - i / 6f;
            var cc = new Color(c.r, c.g, c.b, c.a * k * k);
            float o = i * t / 6f, th = t / 6f;
            Rect(new Rect(0, o, w, th), cc); Rect(new Rect(0, h - o - th, w, th), cc);
            Rect(new Rect(o, 0, th, h), cc); Rect(new Rect(w - o - th, 0, th, h), cc);
        }
    }

    void DrawRhythm(Rect r)
    {
        var P = GM.Player;
        Panel(r);
        // 时间轴：从上一划开始，0 ~ 2×理想间隔。中间金色带就是 PERFECT 窗口。
        float ideal = Tuning.IdealInterval;
        float span = ideal * 2f;
        var track = new Rect(r.x + S(30), r.y + S(46), r.width - S(60), S(30));
        float X(float t) => track.x + Mathf.Clamp01(t / span) * track.width;

        Rect(track, new Color(1, 1, 1, 0.08f));
        // 乱划区
        Rect(new Rect(track.x, track.y, X(Tuning.MinInterval) - track.x, track.height), new Color(1f, 0.3f, 0.3f, 0.25f));
        // OK / GOOD / PERFECT
        Rect(new Rect(X(ideal - Tuning.OkWindow), track.y, X(ideal + Tuning.OkWindow) - X(ideal - Tuning.OkWindow), track.height), new Color(0.6f, 0.75f, 1f, 0.22f));
        Rect(new Rect(X(ideal - Tuning.GoodWindow), track.y, X(ideal + Tuning.GoodWindow) - X(ideal - Tuning.GoodWindow), track.height), new Color(0.5f, 0.93f, 0.6f, 0.35f));
        Rect(new Rect(X(ideal - Tuning.PerfectWindow), track.y - S(4), X(ideal + Tuning.PerfectWindow) - X(ideal - Tuning.PerfectWindow), track.height + S(8)), new Color(1f, 0.84f, 0.04f, 0.75f));

        // 下一只手提示
        int next = P.LastHand == 0 ? 1 : 0;
        string nextKey = P.LastHand < 0 ? "A 或 D" : next == 0 ? "← A 左手" : "D 右手 →";
        Label(new Rect(r.x, r.y + S(6), r.width, S(34)), $"节奏   下一划：<color=#ffd60a>{nextKey}</color>", _small, TextAnchor.MiddleCenter);

        // 游标：距上一划经过的时间
        if (P.LastStrokeTime > -50f && P.Active)
        {
            float since = Time.time - P.LastStrokeTime;
            if (since <= span)
            {
                float cx = X(since);
                bool inPerfect = Mathf.Abs(since - ideal) <= Tuning.PerfectWindow;
                Rect(new Rect(cx - S(3), track.y - S(10), S(6), track.height + S(20)), inPerfect ? Color.white : new Color(1, 1, 1, 0.85f));
            }
        }

        // 历史间隔（小点）
        for (int i = 0; i < _history.Count; i++)
        {
            var (iv, j) = _history[i];
            float age = (_history.Count - 1 - i) / 14f;
            Color c = j == Judge.Perfect ? Art.Hex("#ffd60a") : j == Judge.Good ? Art.Hex("#80ed99") : j == Judge.Ok ? Art.Hex("#a0c4ff") : Art.Hex("#ff6b6b");
            c.a = 1f - age * 0.8f;
            Dot(new Vector2(X(iv), track.yMax + S(16)), S(5), c);
        }

        // 流畅度条
        var fr = new Rect(r.x + S(30), r.yMax - S(34), r.width - S(60), S(16));
        Bar(fr, P.Flow / 100f, Color.Lerp(Art.Hex("#4cc9f0"), Art.Hex("#ffd60a"), P.Flow / 100f), new Color(1, 1, 1, 0.1f));
        Label(new Rect(fr.x, fr.y - S(2), fr.width, fr.height + S(4)), $"流畅度 {P.Flow:0}%  (+{P.Flow / 100f * Tuning.FlowSpeedBonus * 100f:0}% 速度)", _tiny, TextAnchor.MiddleCenter);
    }

    void DrawBreath(Rect r)
    {
        var P = GM.Player;
        Panel(r);
        var bar = new Rect(r.x + S(22), r.y + S(30), r.width - S(44), r.height - S(60));
        Rect(bar, new Color(1, 1, 1, 0.1f));
        // 完美区
        float y0 = bar.yMax - bar.height * Tuning.BreathPerfectMax / 100f;
        float y1 = bar.yMax - bar.height * Tuning.BreathPerfectMin / 100f;
        Rect(new Rect(bar.x - S(6), y0, bar.width + S(12), y1 - y0), new Color(1f, 0.84f, 0.04f, 0.35f));
        // 抢气区
        float ye = bar.yMax - bar.height * Tuning.BreathEarlyMax / 100f;
        Rect(new Rect(bar.x, bar.y, bar.width, ye - bar.y), new Color(1f, 0.6f, 0.2f, 0.15f));

        float v = P.Breath / 100f;
        Color c = P.Breath > Tuning.BreathPerfectMax ? Art.Hex("#4cc9f0") : Art.Hex("#ffd60a");
        if (P.Breath <= 0f && !Tuning.FoulRoom)
        {
            // 气空：显示憋气进度（红色向上涨）
            float k = Mathf.Clamp01(P.Airless / Tuning.BreathDrownAfter);
            Rect(new Rect(bar.x, bar.y, bar.width, bar.height * k), new Color(1f, 0.15f, 0.15f, 0.6f + 0.3f * Mathf.Sin(Time.time * 12f)));
        }
        else Rect(new Rect(bar.x, bar.yMax - bar.height * v, bar.width, bar.height * v), c);

        Label(new Rect(r.x, r.y + S(4), r.width, S(24)), "换气", _tiny, TextAnchor.MiddleCenter);
        string st = P.BreathTier == 2 ? "<color=#ff6b6b>脸红!</color>" : P.BreathTier == 1 ? "<color=#ffd43b>脸直</color>" : "空格";
        if (Tuning.FoulRoom) st = P.Breath <= 0f ? "<color=#adb5bd>憋着·慢</color>" : Time.time < P.PerfectBreathUntil ? "<color=#ffd60a>+速!</color>" : "空格";
        Label(new Rect(r.x, r.yMax - S(28), r.width, S(24)), st, _tiny, TextAnchor.MiddleCenter);

        if (Time.time - _breathT < 1.2f)
        {
            float a = 1f - (Time.time - _breathT) / 1.2f;
            Label(new Rect(r.x, r.y - S(36) - (1 - a) * S(20), S(300), S(30)), _breathText, _small, TextAnchor.MiddleLeft, new Color(_breathColor.r, _breathColor.g, _breathColor.b, a));
        }
    }

    void DrawStamina(Rect r)
    {
        var P = GM.Player;
        Panel(r);
        var bar = new Rect(r.x + S(22), r.y + S(30), r.width - S(44), r.height - S(60));
        Rect(bar, new Color(1, 1, 1, 0.1f));
        float v = P.Stamina / Tuning.StaminaMax;
        Color c = P.ExhaustLock ? Art.Hex("#ff6b6b") : P.Sprinting ? Art.Hex("#ffffff") : Art.Hex("#80ed99");
        Rect(new Rect(bar.x, bar.yMax - bar.height * v, bar.width, bar.height * v), c);
        float yr = bar.yMax - bar.height * Tuning.ExhaustRecover / Tuning.StaminaMax;
        if (P.ExhaustLock) Rect(new Rect(bar.x - S(6), yr - 1, bar.width + S(12), S(2)), Color.white);
        Label(new Rect(r.x, r.y + S(4), r.width, S(24)), "体力", _tiny, TextAnchor.MiddleCenter);
        Label(new Rect(r.x, r.yMax - S(28), r.width, S(24)), P.Sprinting ? "<color=#ffd60a>加速!</color>" : P.ExhaustLock ? "<color=#ff6b6b>力竭</color>" : "Shift", _tiny, TextAnchor.MiddleCenter);
    }

    void DrawMinimap(Rect r)
    {
        Panel(r);
        Label(new Rect(r.x, r.y + S(4), r.width, S(22)), "赛况", _tiny, TextAnchor.MiddleCenter);
        var pool = new Rect(r.x + S(14), r.y + S(30), r.width - S(28), r.height - S(44));
        Rect(pool, new Color(0.2f, 0.6f, 0.85f, 0.35f));
        int n = Tuning.LaneCount;
        float lw = pool.width / n;
        for (int i = 1; i < n; i++) Rect(new Rect(pool.x + i * lw, pool.y, 1, pool.height), new Color(1, 1, 1, 0.25f));
        Rect(new Rect(pool.x, pool.y, pool.width, S(3)), Art.Hex("#ffd60a"));
        foreach (var s in GM.Swimmers)
        {
            float t = s.Z / Tuning.Distance;
            var c = s.CapColor;
            if (s.Drowned) c = new Color(0.4f, 0.4f, 0.5f, 0.6f);
            float cx = pool.x + (s.X / Tuning.LaneWidth + (n - 1) * 0.5f + 0.5f) * lw;
            float cy = pool.yMax - t * pool.height;
            Dot(new Vector2(cx, cy), s.IsPlayer ? S(8) : S(6), c);
            if (s.IsPlayer) { Dot(new Vector2(cx, cy), S(3), Color.white); }
            if (s.Finished) Label(new Rect(cx - lw / 2, cy + S(4), lw, S(18)), (s.FinishOrder + 1).ToString(), _tiny, TextAnchor.MiddleCenter);
        }
    }

    void DrawToast()
    {
        if (Time.unscaledTime > _toastUntil || string.IsNullOrEmpty(_toast)) return;
        float age = Time.unscaledTime - _toastStart;
        float a = Mathf.Clamp01((_toastUntil - Time.unscaledTime) * 3f);
        float sc = 1f + Mathf.Max(0, 0.15f - age) * 2f;
        var st = new GUIStyle(_mid) { fontSize = Mathf.RoundToInt(_mid.fontSize * 1.3f * sc) };
        Label(new Rect(0, Screen.height * 0.3f, Screen.width, S(60)), _toast, st, TextAnchor.MiddleCenter, new Color(_toastColor.r, _toastColor.g, _toastColor.b, a));
    }

    // ───── 结算 ─────
    void DrawResults()
    {
        float w = Screen.width, h = Screen.height;
        Rect(new Rect(0, 0, w, h), new Color(0.02f, 0.06f, 0.14f, 0.55f));
        var P = GM.Player;
        float pw = S(980), ph = S(640);
        var p = new Rect((w - pw) / 2, (h - ph) / 2, pw, ph);
        Panel(p, 0.82f);

        int place = P.Drowned ? GM.Swimmers.Count : GM.Ranking.IndexOf(P) + 1;
        string title = P.Drowned ? "沉底了…（忘记换气判最后一名）" : place == 1 ? "冠 军 ！" : $"第 {place} 名";
        Label(new Rect(p.x, p.y + S(14), pw, S(60)), title, new GUIStyle(_big) { fontSize = (int)S(46) }, TextAnchor.MiddleCenter, place == 1 && !P.Drowned ? Art.Hex("#ffd60a") : Color.white);

        // 排名表
        float x = p.x + S(30), y = p.y + S(90), lh = S(34);
        Label(new Rect(x, y, S(460), lh), "<color=#ffffff88>名次   选手                 成绩</color>", _small); y += lh;
        for (int i = 0; i < GM.Ranking.Count; i++)
        {
            var s = GM.Ranking[i];
            string res = s.Finished ? Utils.FmtTime(s.FinishTime) : s.Drowned ? "沉底 DNF" : $"{s.Z:0.0}m";
            string hex = ColorUtility.ToHtmlStringRGB(s.CapColor);
            string row = $"  {i + 1,-4}  <color=#{hex}>●</color> {s.DisplayName}";
            if (s.IsPlayer) Rect(new Rect(x - S(8), y, S(470), lh), new Color(1, 0.84f, 0.04f, 0.18f));
            Label(new Rect(x, y, S(300), lh), row, _small);
            Label(new Rect(x + S(300), y, S(160), lh), res, _small, TextAnchor.MiddleRight);
            y += lh;
        }

        // 手感统计（这是原型要验证的东西）
        float sx = p.x + S(540), sy = p.y + S(90);
        Label(new Rect(sx, sy, S(420), lh), "<color=#ffd60a>你的划水报告</color>", _mid); sy += lh + S(8);
        int n = Mathf.Max(1, P.NStrokes);
        string[] stats =
        {
            $"划水次数　　{P.NStrokes}",
            $"PERFECT　　{P.NPerfect}  ({P.NPerfect * 100f / n:0}%)",
            $"GOOD　　　 {P.NGood}  ({P.NGood * 100f / n:0}%)",
            $"OK / 起划　 {P.NOk}",
            $"乱划 / 失误　{P.NBad}",
            $"最高连击　　{P.MaxCombo}",
            $"最高流畅度　{P.MaxFlow:0}%",
            $"最高速度　　{P.MaxSpeed:0.00} m/s",
            $"换气　　　　{P.NBreaths} 次（完美 {P.NBreathPerfect}）",
            $"脸变直 / 脸变红　{P.NTier1} / {P.NTier2}",
            $"加速时长　　{P.SprintSeconds:0.0} 秒",
        };
        if (Tuning.FoulRoom)
            stats = stats.Where(s => !s.StartsWith("脸变直")).Append($"拽/蹬 {P.NFoul}   一拽一蹬 <color=#ff4dd2>{P.NCombo}</color>   被坑 {P.NFouled}").ToArray();
        foreach (var st in stats) { Label(new Rect(sx, sy, S(420), lh), st, _small); sy += S(30); }

        // 一句评语
        float perfRate = P.NPerfect * 1f / n;
        string tip = P.Drowned ? "提示：气条见底前按空格；金色区换气还有加速。" :
            P.NBad > n * 0.25f ? "提示：别狂按！跟着节拍器「嗒」声交替按 A / D。" :
            perfRate > 0.6f ? "节奏非常稳。试试在最后 10 米把体力全砸进加速。" :
            P.NTier1 > 0 ? "提示：脸变直就已经在掉速了，早点换气。" :
            "提示：盯住节奏条，让白线落在金色带里再按。";
        Label(new Rect(p.x, p.yMax - S(90), pw, S(30)), tip, _small, TextAnchor.MiddleCenter, Art.Hex("#a0c4ff"));
        float blink = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
        Label(new Rect(p.x, p.yMax - S(50), pw, S(30)), "回车 再来一局   ·   Q 返回标题（换玩法）   ·   F1 调参", _small, TextAnchor.MiddleCenter, new Color(1, 1, 1, blink));
    }

    void DrawPause()
    {
        float w = Screen.width, h = Screen.height;
        Rect(new Rect(0, 0, w, h), new Color(0, 0, 0, 0.5f));
        Label(new Rect(0, h * 0.35f, w, S(80)), "暂停", _big, TextAnchor.MiddleCenter);
        Label(new Rect(0, h * 0.35f + S(90), w, S(40)), "Esc 继续   ·   R 重开   ·   Q 回标题", _mid, TextAnchor.MiddleCenter);
    }

    // ───── 调参面板 ─────
    void DrawTuning()
    {
        float pw = S(470);
        var p = new Rect(Screen.width - pw - S(10), S(10), pw, Screen.height - S(20));
        Panel(p, 0.9f);
        Label(new Rect(p.x + S(14), p.y + S(8), pw, S(32)), "调参面板（实时生效）", _mid);
        Label(new Rect(p.x + S(14), p.y + S(42), pw - S(28), S(22)), "F1 / Esc 关闭。调完点「复制参数」贴回 Tuning.cs", _tiny, null, new Color(1, 1, 1, 0.7f));

        if (GUI.Button(new Rect(p.x + S(14), p.y + S(70), S(130), S(30)), "复制参数", _btn))
        {
            GUIUtility.systemCopyBuffer = Tuning.Export();
            _copied = "已复制到剪贴板"; _copiedT = Time.unscaledTime;
        }
        if (GUI.Button(new Rect(p.x + S(154), p.y + S(70), S(130), S(30)), "恢复默认", _btn)) Tuning.ResetAll();
        if (GUI.Button(new Rect(p.x + S(294), p.y + S(70), S(160), S(30)), GM.Metronome ? "节拍器：开" : "节拍器：关", _btn)) GM.Metronome = !GM.Metronome;
        if (Time.unscaledTime - _copiedT < 2f) Label(new Rect(p.x + S(14), p.y + S(104), S(300), S(20)), _copied, _tiny, null, Art.Hex("#80ed99"));

        var view = new Rect(p.x + S(6), p.y + S(128), pw - S(12), p.height - S(136));
        float rowH = S(46);
        var entries = Tuning.Entries;
        int groups = entries.Select(e => e.Attr.Group).Distinct().Count();
        var content = new Rect(0, 0, view.width - S(20), entries.Count * rowH + groups * S(30));
        _tuneScroll = GUI.BeginScrollView(view, _tuneScroll, content);
        float y = 0;
        string lastGroup = null;
        foreach (var e in entries)
        {
            if (e.Attr.Group != lastGroup)
            {
                lastGroup = e.Attr.Group;
                Label(new Rect(S(8), y + S(4), content.width, S(24)), "■ " + lastGroup, _small, null, Art.Hex("#ffd60a"), false);
                y += S(30);
            }
            bool changed = !Mathf.Approximately(e.Value, e.Default);
            Label(new Rect(S(12), y, content.width - S(90), S(22)), e.Attr.Label, _tiny, null, changed ? Art.Hex("#80ed99") : Color.white, false);
            Label(new Rect(content.width - S(90), y, S(80), S(22)), e.Value.ToString(e.Attr.Max > 20 ? "0.0" : "0.000"), _tiny, TextAnchor.MiddleRight, null, false);
            float nv = GUI.HorizontalSlider(new Rect(S(12), y + S(24), content.width - S(24), S(16)), e.Value, e.Attr.Min, e.Attr.Max);
            if (!Mathf.Approximately(nv, e.Value)) e.Value = nv;
            y += rowH;
        }
        GUI.EndScrollView();
    }
}
