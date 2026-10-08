using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum GameState { Title, Intro, Countdown, Racing, Results }

/// <summary>
/// 比赛流程：标题 → 开场镜头 → 倒计时（抢跳会被罚）→ 比赛 → 结算。
/// 场景里不需要任何东西：RuntimeInitializeOnLoadMethod 会自动创建一切。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager I;

    public GameState State = GameState.Title;
    public readonly List<Swimmer> Swimmers = new List<Swimmer>();
    public Swimmer Player;
    public SwimmerBody PlayerBody;
    public ChaseCam Cam;
    public Hud Hud;
    public int Difficulty = 1;       // 0 轻松 1 普通 2 高手
    public bool Metronome = true;    // 节拍器（练节奏用）
    public bool PauseOpen;

    public float StateTime;          // 进入当前状态的时刻
    public float CountdownEnd;
    public int FinishCounter;
    public float PlayerDoneAt = -1f;
    public float FalseStartPenaltyUntil = -1f;
    public readonly List<Swimmer> Ranking = new List<Swimmer>();

    AudioSource _sfx, _ambience;
    int _lastCountBeep;
    Transform _world;
    float _nextTick;
    int _tickHand;

    static readonly string[] NpcNames = { "浪里白条", "咸鱼翻身", "狗刨大师", "水猴子", "蛙王", "憋气怪", "海绵宝宝", "浮漂", "秤砣", "鲨鲨", "大白鹅", "八爪鱼" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<GameManager>() != null) return;
        new GameObject("GameManager").AddComponent<GameManager>();
    }

    void Awake()
    {
        I = this;
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 0;
        Time.fixedDeltaTime = 1f / 100f;
        Physics.defaultSolverIterations = 12;
        Physics.IgnoreLayerCollision(SwimmerBody.BodyLayer, SwimmerBody.BodyLayer, true);
        Sfx.Init();

        // 删掉场景里默认的相机/灯（如果有）
        foreach (var c in FindObjectsByType<Camera>()) Destroy(c.gameObject);
        foreach (var l in FindObjectsByType<Light>()) Destroy(l.gameObject);

        _world = new GameObject("World").transform;
        PoolBuilder.Build(_world);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Art.Hex("#9ec9e6");
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        cam.fieldOfView = 58f;
        cam.allowMSAA = true;
        camGo.AddComponent<AudioListener>();
        Cam = camGo.AddComponent<ChaseCam>();
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadowDistance = 45f;

        _sfx = camGo.AddComponent<AudioSource>();
        _ambience = camGo.AddComponent<AudioSource>();
        _ambience.clip = Sfx.Crowd; _ambience.loop = true; _ambience.volume = 0.12f; _ambience.Play();

        Hud = gameObject.AddComponent<Hud>();
        Hud.GM = this;

        SpawnSwimmers();
        Foul.All = Swimmers;
        Foul.OnFoul = OnFoulEvent;
        SwimmerBody.OnSplashDown = OnSplashDown;
        GoTitle();
    }

    // ═══════════════ 生成 ═══════════════
    void SpawnSwimmers()
    {
        foreach (var s in Swimmers) if (s != null) Destroy(s.gameObject);
        Swimmers.Clear();

        var names = NpcNames.OrderBy(_ => Random.value).ToList();
        var skins = new[] { Art.Hex("#f2c9a0"), Art.Hex("#e0ac69"), Art.Hex("#c68642"), Art.Hex("#8d5524"), Art.Hex("#ffdbac") };
        var suits = new[] { Art.Hex("#14213d"), Art.Hex("#1d3557"), Art.Hex("#2b2d42"), Art.Hex("#3c1642"), Art.Hex("#0b525b") };
        int ni = 0;
        for (int lane = 0; lane < Tuning.LaneCount; lane++)
        {
            bool isPlayer = lane == Tuning.PlayerLane;
            var go = new GameObject(isPlayer ? "Player" : "NPC_" + lane);
            var s = go.AddComponent<Swimmer>();
            s.Lane = lane; s.HomeLane = lane;
            s.IsPlayer = isPlayer;
            s.DisplayName = isPlayer ? "你" : names[ni++ % names.Count];
            s.CapColor = PoolBuilder.CapColors[lane];
            s.ResetState();

            var body = go.AddComponent<SwimmerBody>();
            body.Build(s, PoolBuilder.LaneX(lane), s.CapColor, suits[lane % suits.Length], isPlayer ? skins[0] : skins[Random.Range(0, skins.Length)]);

            if (isPlayer)
            {
                Player = s; PlayerBody = body;
                s.OnStroke += OnPlayerStroke;
                s.OnBreath += OnPlayerBreath;
                s.OnStatus += OnPlayerStatus;
            }
            else
            {
                go.AddComponent<NpcBrain>();
            }
            s.OnStatus += st => OnAnyStatus(s, st);
            Swimmers.Add(s);
        }
        Cam.Target = Player;
        Cam.Body = PlayerBody;
    }

    public void SetMode(bool foul)
    {
        if (Tuning.FoulRoom == foul) return;
        Tuning.FoulRoom = foul;
        Sfx.Play(_sfx, Sfx.Tick, foul ? 1.3f : 1f, 0.8f);
    }

    void ResetRace()
    {
        Time.timeScale = 1f; _hitStopUntil = 0f;
        _heartNext = 0f; ChasePressure = 0f;
        FinishCounter = 0;
        PlayerDoneAt = -1f;
        FalseStartPenaltyUntil = -1f;
        Ranking.Clear();
        float[] skillByDiff = { 0f, 0f, 0f };
        foreach (var s in Swimmers)
        {
            s.ResetState();
            var body = s.GetComponent<SwimmerBody>();
            body.PlaceAll(true);
            var npc = s.GetComponent<NpcBrain>();
            if (npc != null)
            {
                float lo = Tuning.NpcSkillMin, hi = Tuning.NpcSkillMax;
                float shift = Difficulty == 0 ? -0.25f : Difficulty == 2 ? 0.12f : 0f;
                npc.Init(s, Mathf.Clamp01(Random.Range(lo, hi) + shift));
            }
        }
        Hud.ResetFeed();
        PromptTarget = null; _pendingTarget = null; LastFPressInterval = -1f;
    }

    // ═══════════════ 状态切换 ═══════════════
    void GoTitle()
    {
        State = GameState.Title;
        StateTime = Time.time;
        ResetRace();
        Cam.StartIntro();
    }

    public void StartRace()
    {
        ResetRace();
        State = GameState.Intro;
        StateTime = Time.time;
        Cam.StartIntro();
        _lastCountBeep = 99;
    }

    void GoCountdown()
    {
        State = GameState.Countdown;
        StateTime = Time.time;
        CountdownEnd = Time.time + Tuning.CountdownTime;
        Cam.Intro = false;
    }

    void GoRacing()
    {
        State = GameState.Racing;
        StateTime = Time.time;
        float now = Time.time;
        foreach (var s in Swimmers)
        {
            s.Begin(now);
            if (s.IsPlayer && FalseStartPenaltyUntil > now)
            {
                s.V = 0f;
                s.SlowUntil = FalseStartPenaltyUntil;
                s.SlowMul = 0.3f;
            }
        }
        Sfx.Play(_sfx, Sfx.Whistle, 1f, 0.8f);
        Sfx.Play(_sfx, Sfx.BeepHigh, 1f, 0.6f);
        _nextTick = -1f;
        Hud.Toast("出发！", Art.Hex("#ffd60a"), 1.2f);
    }

    void GoResults()
    {
        State = GameState.Results;
        StateTime = Time.time;
        // 没游完的按距离排，沉底的永远最后
        var unfinished = Swimmers.Where(s => !s.Finished && !s.Drowned).OrderByDescending(s => s.Z);
        var drowned = Swimmers.Where(s => s.Drowned).OrderByDescending(s => s.DrownTime);
        foreach (var s in unfinished) if (!Ranking.Contains(s)) Ranking.Add(s);
        foreach (var s in drowned) if (!Ranking.Contains(s)) Ranking.Add(s);
        foreach (var s in Swimmers) s.SprintHeld = false;
        Sfx.Play(_sfx, Sfx.Crowd, 1f, 0.7f);
    }

    // ═══════════════ 事件 ═══════════════
    void OnAnyStatus(Swimmer s, string st)
    {
        if (st == "finish")
        {
            s.FinishOrder = FinishCounter++;
            Ranking.Add(s);
            if (s.FinishOrder == 0) Hud.Feed($"{s.DisplayName} 第一个触壁！ {Utils.FmtTime(s.FinishTime)}", s.CapColor);
        }
        else if (st == "drown")
        {
            Hud.Feed($"{s.DisplayName} 忘记换气，沉底了…", Art.Hex("#ff6b6b"));
        }
        else if (st == "tier2" && !s.IsPlayer)
        {
            Hud.Feed($"{s.DisplayName} 憋得满脸通红！", Art.Hex("#ffa94d"));
        }
        else if (st == "recover" && !s.IsPlayer)
        {
            Hud.Feed($"{s.DisplayName} 手忙脚乱爬了回来…", Art.Hex("#ced4da"));
        }
    }

    void OnPlayerStroke(StrokeEvent e)
    {
        Hud.OnStroke(e);
        if (e.Judge == Judge.Perfect) { Cam.Pulse(1f); Sfx.Play(_sfx, Sfx.Perfect, 1f + Mathf.Min(Player.Combo, 20) * 0.02f, 0.5f); }
        if (e.Judge == Judge.Fumble) Cam.Shake(0.5f);
    }

    void OnPlayerBreath(BreathResult r)
    {
        Hud.OnBreath(r);
        if (r == BreathResult.Choke) Cam.Shake(1f);
    }

    void OnPlayerStatus(string st)
    {
        switch (st)
        {
            case "tier1": Hud.Toast("脸变直了！快换气 [空格]", Art.Hex("#ffd43b"), 1.2f); break;
            case "tier2": Hud.Toast("脸憋红了！！再不换气要沉了！", Art.Hex("#ff6b6b"), 1.5f); Cam.Shake(0.6f); break;
            case "drown": Hud.Toast("你沉底了…… 判定最后一名", Art.Hex("#ff6b6b"), 3f); Cam.Shake(1.2f); PlayerDoneAt = Time.time; break;
            case "exhaust": Hud.Toast("力竭！体力恢复前无法加速", Art.Hex("#ffa94d"), 1.2f); break;
            case "recover":
                Hud.Toast("爬回来了！复仇时间 —— 逮谁拽谁！", Art.Hex("#ff4dd2"), 1.6f);
                Hud.Feed("你从水里爬了起来，全身冒火（短暂无敌 + 抓人更远更快）", Art.Hex("#ff4dd2"));
                Cam.Shake(0.7f);
                Sfx.Play(_sfx, Sfx.Perfect, 0.7f, 0.8f);
                break;
            case "finish":
                PlayerDoneAt = Time.time;
                int place = Player.FinishOrder + 1;
                Hud.Toast(place == 1 ? "冠军！！！" : $"第 {place} 名触壁！", place == 1 ? Art.Hex("#ffd60a") : Color.white, 3f);
                Sfx.Play(_sfx, Sfx.BeepHigh, 1f, 0.7f);
                break;
        }
    }

    // ═══════════════ 主循环 ═══════════════
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) Hud.ShowTuning = !Hud.ShowTuning;
        UpdateHitStop();
        if (Input.GetKeyDown(KeyCode.M)) { Metronome = !Metronome; Hud.Toast(Metronome ? "节拍器：开" : "节拍器：关", Color.white, 0.8f); }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (Hud.ShowTuning) Hud.ShowTuning = false;
            else if (State == GameState.Title) { }
            else
            {
                PauseOpen = !PauseOpen;
                Time.timeScale = PauseOpen ? 0f : 1f;
            }
        }
        if (PauseOpen)
        {
            if (Input.GetKeyDown(KeyCode.R)) { PauseOpen = false; Time.timeScale = 1f; StartRace(); }
            if (Input.GetKeyDown(KeyCode.Q)) { PauseOpen = false; Time.timeScale = 1f; GoTitle(); }
            return;
        }

        if (Input.GetKeyDown(KeyCode.R) && State != GameState.Title) { StartRace(); return; }

        CrowdBounce.Hype = State == GameState.Racing ? Mathf.Clamp01(0.3f + Player.Z / Tuning.Distance * 0.7f) : State == GameState.Results ? 1f : 0.15f;
        _ambience.volume = 0.08f + CrowdBounce.Hype * 0.18f;

        switch (State)
        {
            case GameState.Title:
                if (Input.GetKeyDown(KeyCode.Alpha1)) Difficulty = 0;
                if (Input.GetKeyDown(KeyCode.Alpha2)) Difficulty = 1;
                if (Input.GetKeyDown(KeyCode.Alpha3)) Difficulty = 2;
                // 模式选择：← / A = 竞速，→ / D = 互坑，Tab 切换
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) SetMode(false);
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) SetMode(true);
                if (Input.GetKeyDown(KeyCode.Tab)) SetMode(!Tuning.FoulRoom);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) StartRace();
                break;

            case GameState.Intro:
                if (Time.time - StateTime > 2.6f || Input.GetKeyDown(KeyCode.Return)) GoCountdown();
                break;

            case GameState.Countdown:
                {
                    float left = CountdownEnd - Time.time;
                    int n = Mathf.CeilToInt(left);
                    if (n != _lastCountBeep && n >= 1 && n <= 3) { _lastCountBeep = n; Sfx.Play(_sfx, Sfx.Beep); }
                    // 抢跳：倒计时期间按划水键 → 出发时被罚 1 秒
                    if (StrokeKeyDown(out _) && FalseStartPenaltyUntil < 0f)
                    {
                        FalseStartPenaltyUntil = CountdownEnd + 1.0f;
                        Hud.Toast("抢跳！出发罚停 1 秒", Art.Hex("#ff6b6b"), 1.2f);
                        Cam.Shake(0.4f);
                    }
                    if (left <= 0f) GoRacing();
                    break;
                }

            case GameState.Racing:
                {
                    float lz = 0f, l2 = 0f;
                    foreach (var s in Swimmers)
                    {
                        if (s.Drowned) continue;
                        if (s.Z > lz) { l2 = lz; lz = s.Z; }
                        else if (s.Z > l2) l2 = s.Z;
                    }
                    Swimmer.LeaderZ = lz;
                    Swimmer.SecondZ = l2;
                }
                PlayerControls();
                MetronomeTick();
                ChasePressureTick();
                bool allDone = Swimmers.All(s => s.Finished || s.Drowned);
                if (allDone && Time.time - Swimmers.Max(s => s.Finished ? s.RaceStart + s.FinishTime : s.DrownTime) > 1.5f) GoResults();
                else if (PlayerDoneAt > 0f && Time.time - PlayerDoneAt > 10f) GoResults();
                else if (PlayerDoneAt > 0f && Input.GetKeyDown(KeyCode.Return)) GoResults();
                break;

            case GameState.Results:
                if (Time.time - StateTime > 0.6f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))) StartRace();
                if (Input.GetKeyDown(KeyCode.Q)) GoTitle();
                break;
        }
    }

    static bool StrokeKeyDown(out int hand)
    {
        hand = -1;
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.J)) hand = 0;
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.K)) hand = 1;
        return hand >= 0;
    }

    // ═══════════════ 互坑：F 键 ═══════════════
    // 提示：对手在前 → 「拽脚」；在后 → 「蹬人」。
    // 按 1 下 F = 立即打出普通拽/蹬。
    // 在 ComboMaxWait 内按第 2 下，且两下的间隔落进「隐藏随机窗口」→ 升级成一拽一蹬组合技。
    // 隐藏窗口在每次出现新提示时重新随机，玩家永远背不出固定节奏 —— 这就是运气。
    public Swimmer PromptTarget;      // 当前提示对象（null = 无提示）
    public bool PromptAhead;
    public float PromptSince;
    public float HiddenWinCenter, HiddenWinHalf;
    Swimmer _pendingTarget;           // 第一下 F 打中的人，等待第二下
    float _pendingTime = -99f;
    int _pendingPresses;
    public float LastFPressInterval = -1f;   // 调试显示用（结算后可见）
    public bool PendingCombo => _pendingTarget != null && Time.time - _pendingTime <= Tuning.ComboMaxWait;

    void RollHiddenWindow()
    {
        float lo = Mathf.Min(Tuning.ComboWinMin, Tuning.ComboWinMax), hi = Mathf.Max(Tuning.ComboWinMin, Tuning.ComboWinMax);
        HiddenWinCenter = Random.Range(lo, hi);
        HiddenWinHalf = Tuning.ComboWinWidth * 0.5f;
    }

    void PlayerFoulControls()
    {
        // 更新提示对象
        var t = Foul.FindTarget(Player, out bool ahead);
        if (t != PromptTarget || ahead != PromptAhead)
        {
            if (t != null) { RollHiddenWindow(); PromptSince = Time.time; }
            PromptTarget = t; PromptAhead = ahead;
        }

        // 第二下的等待超时
        if (_pendingTarget != null && Time.time - _pendingTime > Tuning.ComboMaxWait) _pendingTarget = null;

        if (!Input.GetKeyDown(KeyCode.F)) return;

        if (_pendingTarget != null)
        {
            _pendingPresses++;
            float iv = Time.time - _pendingTime;
            LastFPressInterval = iv;
            if (_pendingPresses == 2 && Mathf.Abs(iv - HiddenWinCenter) <= HiddenWinHalf && _pendingTarget.Active)
            {
                Foul.Combo(Player, _pendingTarget, false);
                _pendingTarget = null;
            }
            else if (_pendingPresses >= 3) _pendingTarget = null;   // 乱按：到此为止，就是普通的一下
            return;
        }

        if (PromptTarget == null)
        {
            // 抓空：没人可抓还按 F —— 用力过猛在水里翻一圈（GDD：滑稽空抓）
            if (Tuning.FoulRoom && Foul.CanAct(Player))
            {
                Foul.Whiff(Player);
                Hud.Toast("抓空了！用力过猛…", Art.Hex("#adb5bd"), 0.9f);
                Cam.Shake(0.5f);
                Sfx.Play(_sfx, Sfx.Splash, 0.6f, 1f);
            }
            return;
        }
        if (!Foul.CanAct(Player))
        {
            Hud.Toast(Player.Stamina < Tuning.FoulCost ? "体力不够！" : "还在冷却…", Art.Hex("#adb5bd"), 0.6f);
            return;
        }
        var target = PromptTarget;
        // 用「出手那一刻」的窗口判定这次组合技；之后立刻重随，下一次提示又不一样
        float c = HiddenWinCenter, h = HiddenWinHalf;
        Foul.Single(Player, target, PromptAhead);
        HiddenWinCenter = c; HiddenWinHalf = h;
        _pendingTarget = target;
        _pendingTime = Time.time;
        _pendingPresses = 1;
        PromptTarget = null;   // 下一帧重新找；被坑的人有无敌时间，不会立刻再出提示
    }

    void OnFoulEvent(Swimmer s, Swimmer t, FoulKind k)
    {
        bool mine = s.IsPlayer, onMe = t.IsPlayer;
        // 所有人的拽/蹬都在被坑者身上冒一大团水花（身体脚本里做），这里只处理玩家视角的反馈
        if (mine || onMe) HitStop(k == FoulKind.Combo ? Tuning.HitStop * 2.5f : Tuning.HitStop);
        string verb = k == FoulKind.Combo ? "一拽一蹬！把" : k == FoulKind.Grab ? (t.Flung ? "抓住脚踝甩飞了" : "拽住了") : "一脚蹬开了";
        string tail = k == FoulKind.Combo ? "甩到身后蹬飞了！" : "";
        Color c = k == FoulKind.Combo ? Art.Hex("#ff4dd2") : mine ? Art.Hex("#80ed99") : onMe ? Art.Hex("#ff6b6b") : Art.Hex("#ced4da");
        string who = mine ? "你" : s.DisplayName, whom = onMe ? "你" : t.DisplayName;
        Hud.Feed(k == FoulKind.Combo ? $"{who} {verb} {whom} {tail}" : $"{who} {verb} {whom}", c);

        if (k == FoulKind.Grab && t.Flung)
        {
            // 拽脚甩飞：全场都看得见的大场面
            if (mine)
            {
                HitStop(Tuning.FlingHitStop);
                Hud.Toast("甩出去了！！！", Art.Hex("#ff4dd2"), 1.5f);
                Hud.Impact("甩飞!", Art.Hex("#ff4dd2"), t);
                Cam.Shake(1.5f); Cam.Pulse(3f);
                Sfx.Play(_sfx, Sfx.Scream, 1.1f, 1f);
                Sfx.Play(_sfx, Sfx.Crowd, 1.3f, 0.9f);
            }
            else if (onMe)
            {
                HitStop(Tuning.FlingHitStop * 1.6f);
                Hud.Toast($"被 {s.DisplayName} 拽住脚甩飞了！！！", Art.Hex("#ff6b6b"), 2f);
                Hud.HitFlash(0.7f);
                Cam.Shake(1.8f);
            }
            else if (Mathf.Abs(t.Z - Player.Z) < 14f)
            {
                HitStop(Tuning.FlingHitStop * 0.7f);
                Sfx.Play(_sfx, Sfx.Scream, Random.Range(0.85f, 1.2f), 0.55f);
            }
            return;
        }
        if (mine)
        {
            if (k == FoulKind.Combo)
            {
                Hud.Toast("一拽一蹬！！！", Art.Hex("#ff4dd2"), 1.6f);
                Hud.Impact("一拽一蹬!!", Art.Hex("#ff4dd2"), t);
                Cam.Shake(1.4f); Cam.Pulse(3f);
                Sfx.Play(_sfx, Sfx.Thump, 0.7f, 1f);
                Sfx.Play(_sfx, Sfx.Perfect, 0.8f, 0.9f);
                Sfx.Play(_sfx, Sfx.Crowd, 1.2f, 0.8f);
            }
            else
            {
                Hud.Toast(k == FoulKind.Grab ? "拽脚！" : "蹬人！", Art.Hex("#80ed99"), 0.9f);
                Hud.Impact((k == FoulKind.Grab ? "拽!" : "蹬!") + $"  -{Tuning.FoulTargetShove / Tuning.ShoveDecay + 0.5f:0}m", Art.Hex("#ffd60a"), t);
                Cam.Shake(0.8f); Cam.Pulse(1.2f);
                Sfx.Play(_sfx, Sfx.Thump, k == FoulKind.Grab ? 0.9f : 1.1f, 1f);
            }
            Sfx.Play(_sfx, Sfx.Splash, 0.7f, 1f);
        }
        else if (onMe)
        {
            Hud.Toast(k == FoulKind.Combo ? $"被 {s.DisplayName} 一拽一蹬，蹬飞了！" : k == FoulKind.Grab ? $"被 {s.DisplayName} 拽回去 {Tuning.FoulTargetShove / Tuning.ShoveDecay + 0.5f:0} 米！" : $"被 {s.DisplayName} 蹬退 {Tuning.FoulTargetShove / Tuning.ShoveDecay + 0.5f:0} 米！",
                Art.Hex("#ff6b6b"), k == FoulKind.Combo ? 1.6f : 0.9f);
            Cam.Shake(k == FoulKind.Combo ? 1.6f : 1f);
            Hud.HitFlash(k == FoulKind.Combo ? 0.55f : 0.35f);
            Sfx.Play(_sfx, Sfx.Thump, 0.8f, 1f);
            Sfx.Play(_sfx, Sfx.Choke, k == FoulKind.Combo ? 0.8f : 1.1f, 0.8f);
        }
        else
        {
            // NPC 互坑：近处的才有声音
            if (Mathf.Abs(t.Z - Player.Z) < 8f) Sfx.Play(_sfx, Sfx.Thump, 1.2f, 0.35f);
        }
    }

    // 命中顿帧：短暂把时间放慢，让「拽住」「蹬中」有打击感
    float _hitStopUntil;
    void OnSplashDown(Swimmer who, Swimmer hit)
    {
        bool me = who == null || who.IsPlayer;
        Color c = Art.Hex("#ffd43b");
        if (hit != null)
        {
            string who2 = who == null ? "有人" : who.IsPlayer ? "你" : who.DisplayName;
            string whom = hit.IsPlayer ? "你" : hit.DisplayName;
            Hud.Feed($"{who2} 从天而降，把 {whom} 砸进水里！", Art.Hex("#ff922b"));
            if (who != null && who.IsPlayer) Hud.Toast("连砸带撞！", Art.Hex("#ff922b"), 1f);
            c = Art.Hex("#ff922b");
        }
        if (who != null && who.IsPlayer)
        {
            Hud.Impact("噗通!!", c, who);
            Cam.Shake(1.2f);
            _wrongFlashUntil = 0f;
        }
        if (Mathf.Abs((who == null ? Player.Z : who.Z) - Player.Z) < 16f)
            Sfx.Play(_sfx, Sfx.Splat, Random.Range(0.9f, 1.1f), 0.8f);
    }

    void HitStop(float dur)
    {
        if (dur <= 0f || PauseOpen) return;
        _hitStopUntil = Time.unscaledTime + dur;
        Time.timeScale = 0.08f;
    }
    void UpdateHitStop()
    {
        if (PauseOpen || _hitStopUntil <= 0f) return;
        if (Time.unscaledTime >= _hitStopUntil) { _hitStopUntil = 0f; Time.timeScale = 1f; }
    }

    void PlayerControls()
    {
        if (!Player.Active) { Player.SprintHeld = false; return; }
        // 被甩飞：所有操作都被挣扎提示接管，游泳键全部失效
        if (Player.Ragdolled) { RecoverControls(); Player.SprintHeld = false; return; }
        if (Time.time < FalseStartPenaltyUntil) return;

        // 同一帧按下两个键：按 Unity 无法区分先后，就按"和上一手相反的先"处理
        bool l = Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.J);
        bool r = Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.K);
        if (l && r)
        {
            int first = Player.LastHand == 0 ? 1 : 0;
            Player.Stroke(first);
            Player.Stroke(1 - first);
        }
        else if (l) Player.Stroke(0);
        else if (r) Player.Stroke(1);

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) Player.Breathe();
        Player.SprintHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.L);
        // 换泳道（互坑房）：Q 左 / E 右
        if (Tuning.FoulRoom)
        {
            int dir = Input.GetKeyDown(KeyCode.Q) ? -1 : Input.GetKeyDown(KeyCode.E) ? 1 : 0;
            if (dir != 0)
            {
                if (Player.ShiftLane(dir)) Sfx.Play(_sfx, Sfx.Splash, 0.8f, 0.5f);
                else if (Player.Stamina < Tuning.LaneShiftCost) Hud.Toast("体力不够换道", Art.Hex("#adb5bd"), 0.5f);
            }
        }
        PlayerFoulControls();
    }

    // ═══════════════ 挣扎：被甩飞后按提示键爬回来 ═══════════════
    float _wrongFlashUntil;
    public float WrongFlash => Mathf.Clamp01((_wrongFlashUntil - Time.time) / 0.35f);
    void RecoverControls()
    {
        if (!Player.RecoverPromptActive || Player.Flung) return;   // 飞在空中那一秒什么都按不了
        // 按对 3 个随机键才能翻回来；乱按一个键没用（键是随机换的，背不出来）
        for (int i = 0; i < Swimmer.PromptPool.Length; i++)
        {
            if (!Input.GetKeyDown(Swimmer.PromptPool[i])) continue;
            int r = Player.RecoverPress(Swimmer.PromptPool[i], Time.time);
            if (r == 2) { }
            else if (r == 1) { Sfx.Play(_sfx, Sfx.BeepHigh, 1.25f, 0.45f); Cam.Pulse(0.6f); }
            else if (r == -1)
            {
                _wrongFlashUntil = Time.time + 0.35f;
                Sfx.Play(_sfx, Sfx.Choke, 1.3f, 0.9f);
                Hud.Toast("按错了！呛了一大口水…", Art.Hex("#ff6b6b"), 0.7f);
                Cam.Shake(0.8f);
            }
            break;
        }
    }

    // ═══════════════ 追赶压迫感（互坑模式）═══════════════
    // 身后有人逼近到可拽脚距离附近：屏幕边缘泛红 + 心跳声，越近越快。
    public float ChasePressure;     // 0~1，HUD 读取
    float _heartNext;
    void ChasePressureTick()
    {
        float p = 0f;
        if (Tuning.FoulRoom && Player.Active)
        {
            foreach (var s in Swimmers)
            {
                if (s == Player || !s.Active || Mathf.Abs(s.Lane - Player.Lane) > Tuning.FoulLaneReach) continue;
                float behind = Player.Z - s.Z;
                if (behind <= 0f || behind > Tuning.FoulRange * 2.2f) continue;
                p = Mathf.Max(p, 1f - behind / (Tuning.FoulRange * 2.2f));
            }
        }
        ChasePressure = Mathf.MoveTowards(ChasePressure, p, Time.deltaTime * 2f);
        if (ChasePressure > 0.15f && Time.time >= _heartNext)
        {
            Sfx.Play(_sfx, Sfx.Heart, 1f, 0.35f + ChasePressure * 0.6f);
            _heartNext = Time.time + Mathf.Lerp(0.85f, 0.38f, ChasePressure);
        }
    }

    void MetronomeTick()
    {
        if (!Metronome || !Player.Active) return;
        // 以玩家上一次划水为锚点，在"理想时刻"响一下，帮助建立节奏感
        if (Player.LastStrokeTime < -50f) return;
        float ideal = Player.LastStrokeTime + Tuning.IdealInterval;
        if (_nextTick != ideal && Time.time >= ideal && Time.time - ideal < 0.1f)
        {
            _nextTick = ideal;
            Sfx.Play(_sfx, Sfx.Tick, Player.LastHand == 0 ? 1.15f : 0.9f, 0.6f);
        }
    }

    public int PlayerPlace()
    {
        if (Player.Finished) return Player.FinishOrder + 1;
        if (Player.Drowned) return Swimmers.Count;
        int ahead = Swimmers.Count(s => s != Player && (s.Finished || (!s.Drowned && s.Z > Player.Z)));
        return ahead + 1;
    }
}
