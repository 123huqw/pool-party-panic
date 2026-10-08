using System;
using UnityEngine;

public enum Judge { Perfect, Good, Ok, Slow, Rush, Mash, Fumble, Start }

public struct StrokeEvent
{
    public int Hand;        // 0 左 1 右
    public Judge Judge;
    public float Quality;
    public float Interval;  // 与上一划的间隔（秒），首划为 -1
    public float Time;
}

public enum BreathResult { Perfect, Good, Close, Early, Recover, Choke }

/// <summary>
/// 一个泳者的全部「游泳规则」。玩家和 NPC 用的是同一套逻辑，区别只在谁来调用 Stroke/Breathe/SprintHeld。
/// 速度模型：每次有效划水把「目标速度」设为 MaxV × 节奏质量 × 各种倍率，并维持 HoldTime；
/// 实际速度以 ApproachRate 逼近目标，停手就以 DecayRate 滑行衰减。节奏 = 速度。
/// </summary>
public class Swimmer : MonoBehaviour
{
    // ───── 身份 ─────
    public int Lane;            // 当前所在泳道（离 X 最近的那条）
    public int HomeLane;        // 出发泳道
    public int LaneTarget;      // 正在横移去的泳道
    public float X;             // 横向世界坐标（米）
    public float LaneShiftReadyAt = -99f;
    public bool Shifting => Mathf.Abs(X - PoolBuilder.LaneX(LaneTarget)) > 0.05f;
    /// <summary>全场领先者已游距离（GameManager 每帧写入），用于追赶加成。</summary>
    public static float LeaderZ;
    /// <summary>第二名已游距离（GameManager 每帧写入），用于领先者减速。</summary>
    public static float SecondZ;
    public string DisplayName;
    public bool IsPlayer;
    public Color CapColor;

    // ───── 运动 ─────
    public float Z;           // 已游距离（米）
    public float V;           // 当前速度（米/秒）
    public float RaceStart;   // 发令时刻（Time.time）

    // ───── 划水 ─────
    public float LastStrokeTime = -99f;
    public int LastHand = -1;
    public float StrokeQ;
    public float HoldUntil = -99f;
    public float Flow;          // 0~100
    public int Combo, MaxCombo;
    public Judge LastJudge;
    public float LastJudgeTime = -99f;

    // ───── 换气 ─────
    public float Breath = 100f;
    public float Airless;       // 气空后经过的秒数
    public int BreathTier;      // 0 正常 1 脸变直 2 脸变红
    public float BreathAnimTime = -99f;
    public int BreathSide = 1;
    public float SlowUntil = -99f, SlowMul = 1f;
    public float PerfectBreathUntil = -99f;
    public BreathResult LastBreath;

    // ───── 体力 ─────
    public float Stamina = 100f;
    public float LastStaminaUse = -99f;
    public float ExhaustedUntil = -99f;
    public bool ExhaustLock;
    public bool SprintHeld;
    public bool Sprinting;

    // ───── 互坑 ─────
    public float Shove;                 // 被拽/蹬/前冲带来的额外速度（米/秒），会快速衰减
    public float StunUntil = -99f;      // 被坑后的僵直
    public float TumbleUntil = -99f;    // 被组合技蹬飞：翻滚中，完全游不动
    public float ImmuneUntil = -99f;    // 刚被坑过，短时间内不会再被选中
    public float FoulReadyAt = -99f;    // 自己下次能出手的时刻
    public float GrabAnimTime = -99f, KickAnimTime = -99f;
    public float HitTime = -99f;        // 被拽/被蹬的时刻（受击动画）
    public int HitKind;                 // 0 被拽脚（往后拖）1 被蹬（往后翻）
    public float WhiffTime = -99f;      // 抓空：自己用力过猛翻一圈
    public int NFoul, NCombo, NFouled;
    public bool Tumbling => Now < TumbleUntil;
    public bool Stunned => Now < StunUntil;

    // ───── 被拽脚甩飞：布娃娃 + 挣扎恢复 ─────
    public float FlingTime = -99f;        // 被甩出去的时刻
    public float FlungUntil = -99f;       // 失控飞行结束、开始挣扎的时刻
    public float BodyFreeUntil = -99f;    // 布娃娃接管到这一刻（之后交还规则驱动）
    public bool Flung => Now < FlungUntil;
    public bool Ragdolled => Now < BodyFreeUntil;
    public float LandTime = -99f;         // 入水时刻（做水花/顿帧）
    public bool Landed;
    public float LandX, LandZ;            // 落水时的位置，身体脚本用它把逻辑坐标拉过去
    public float FlySide;                 // 这一次甩飞的横向速度（给身体脚本用）
    public int RecoverStep;               // 已经按对几次
    public int RecoverNeed = 3;           // 这一次需要按对几次
    public float RecoverPromptUntil = -99f;   // 当前这个提示还能按到什么时候
    public bool RecoverPromptActive;
    public KeyCode[] RecoverKeys = new KeyCode[4];  // 提示上的键（第 0 个是当前要按的）
    public int RecoverWrongs;
    public float RecoverPenaltyUntil = -99f;  // 按错后的呛水硬直
    public float RecoverStart = -99f;
    public float RevengeUntil = -99f;     // 复仇状态
    public bool Revenge => Now < RevengeUntil;
    public bool CanRecover => Now >= RecoverPenaltyUntil && RecoverPromptActive && Now < RecoverPromptUntil;

    // ───── 比赛状态 ─────
    public bool Started, Finished, Drowned;
    public float FinishTime = -1f;
    public float DrownTime = -99f;
    public int FinishOrder = -1;

    // ───── 统计 ─────
    public int NStrokes, NPerfect, NGood, NOk, NBad, NBreaths, NBreathPerfect, NTier1, NTier2;
    public float MaxFlow, MaxSpeed, SprintSeconds;

    public event Action<StrokeEvent> OnStroke;
    public event Action<BreathResult> OnBreath;
    public event Action<string> OnStatus;     // "tier1" "tier2" "drown" "exhaust" "finish"

    public float Now => Time.time;
    public float RaceTime => Started ? Now - RaceStart : 0f;
    public bool Active => Started && !Finished && !Drowned;
    public bool Stroking => Now < HoldUntil;

    public void ResetState()
    {
        Z = 0; V = 0; LastStrokeTime = -99f; LastHand = -1; StrokeQ = 0; HoldUntil = -99f;
        Flow = 0; Combo = MaxCombo = 0; LastJudgeTime = -99f;
        Breath = 100; Airless = 0; BreathTier = 0; BreathAnimTime = -99f; SlowUntil = -99f; PerfectBreathUntil = -99f;
        Stamina = Tuning.StaminaMax; LastStaminaUse = -99f; ExhaustedUntil = -99f; ExhaustLock = false; SprintHeld = false; Sprinting = false;
        Started = Finished = Drowned = false; FinishTime = -1f; DrownTime = -99f; FinishOrder = -1;
        NStrokes = NPerfect = NGood = NOk = NBad = NBreaths = NBreathPerfect = NTier1 = NTier2 = 0;
        MaxFlow = MaxSpeed = SprintSeconds = 0;
        Shove = 0; StunUntil = TumbleUntil = ImmuneUntil = FoulReadyAt = GrabAnimTime = KickAnimTime = HitTime = WhiffTime = -99f;
        NFoul = NCombo = NFouled = 0;
        Lane = LaneTarget = HomeLane; X = PoolBuilder.LaneX(HomeLane); LaneShiftReadyAt = -99f;
        FlingTime = FlungUntil = BodyFreeUntil = LandTime = -99f; Landed = false; LandX = X; LandZ = 0;
        FlySide = 0; RecoverStep = 0; RecoverNeed = Mathf.Max(1, Mathf.RoundToInt(Tuning.RecoverSteps));
        RecoverPromptUntil = -99f; RecoverPromptActive = false; RecoverWrongs = 0;
        RecoverPenaltyUntil = -99f; RecoverStart = -99f; RevengeUntil = -99f;
    }

    // ═════════════════ 被拽脚甩飞 ═════════════════
    /// <summary>被从身后拽住脚，整个人飞出去。身体脚本会切成布娃娃接管。</summary>
    public void StartFling(float side, float now)
    {
        FlingTime = now;
        FlungUntil = now + Tuning.FlingFlight;
        BodyFreeUntil = now + Tuning.FlingFlight + Tuning.RecoverMaxTime;
        LandTime = -99f; Landed = false;
        FlySide = side;
        StunUntil = Mathf.Max(StunUntil, FlungUntil);
        TumbleUntil = Mathf.Max(TumbleUntil, FlungUntil);
        ImmuneUntil = Mathf.Max(ImmuneUntil, now + Tuning.FoulImmune);
        Flow *= 0.35f; Combo = 0; HoldUntil = -99f; SprintHeld = false;
        V *= 0.3f;
        BeginRecover(FlungUntil);
    }

    /// <summary>开始挣扎阶段：给出一串按键提示，按对 RecoverNeed 次才能爬回来。</summary>
    public void BeginRecover(float now)
    {
        RecoverStart = now;
        RecoverStep = 0;
        RecoverWrongs = 0;
        RecoverNeed = Mathf.Max(1, Mathf.RoundToInt(Tuning.RecoverSteps));
        RollPrompts(now);
    }

    /// <summary>挣扎时可能被要求的按键池。玩家用；NPC 不用。</summary>
    public static readonly KeyCode[] PromptPool =
    {
        KeyCode.A, KeyCode.D, KeyCode.W, KeyCode.S,
        KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.I,
        KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
    };

    void RollPrompts(float now)
    {
        for (int i = 0; i < RecoverKeys.Length; i++)
        {
            KeyCode k;
            int guard = 0;
            do { k = PromptPool[UnityEngine.Random.Range(0, PromptPool.Length)]; } while (i > 0 && k == RecoverKeys[i - 1] && ++guard < 12);
            RecoverKeys[i] = k;
        }
        RecoverPromptActive = true;
        RecoverPromptUntil = now + Tuning.PromptLife;
    }

    public static string KeyName(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            default: return k.ToString();
        }
    }

    /// <summary>挣扎时按了一个键。返回：2 = 按对了并翻回来，1 = 按对了，-1 = 按错了，0 = 无关的键。</summary>
    public int RecoverPress(KeyCode k, float now)
    {
        if (!RecoverPromptActive) return 0;
        if (!IsPromptKey(k)) return 0;
        if (now < RecoverPenaltyUntil) return 0;   // 正在呛水：按啥都没用（界面上显示「…」）

        if (k != RecoverKeys[0] || now >= RecoverPromptUntil)
        {
            RecoverWrongs++;
            RecoverPenaltyUntil = now + Tuning.WrongKeyPenalty;
            RecoverPromptUntil = now + Tuning.WrongKeyPenalty + Tuning.PromptLife;
            // 进度不清零：只把当前这个键换成另一个随机键（不会和原键、刚按错的键相同）
            KeyCode old = RecoverKeys[0], nk;
            int guard = 0;
            do { nk = PromptPool[UnityEngine.Random.Range(0, PromptPool.Length)]; } while ((nk == old || nk == k) && ++guard < 20);
            RecoverKeys[0] = nk;
            OnStatus?.Invoke("choke");
            return -1;
        }

        RecoverStep++;
        if (RecoverStep >= RecoverNeed)
        {
            FinishRecover(now);
            return 2;
        }
        RollPrompts(now);
        return 1;
    }

    bool IsPromptKey(KeyCode k)
    {
        for (int i = 0; i < PromptPool.Length; i++) if (PromptPool[i] == k) return true;
        return false;
    }

    /// <summary>翻回来：交还身体控制权，给一段无敌 + 复仇状态。</summary>
    public void FinishRecover(float now)
    {
        RecoverPromptActive = false;
        RecoverPromptUntil = -99f;
        BodyFreeUntil = now;
        FlungUntil = now;
        StunUntil = Mathf.Min(StunUntil, now);
        TumbleUntil = Mathf.Min(TumbleUntil, now);
        ImmuneUntil = Mathf.Max(ImmuneUntil, now + Tuning.PostRecoverImmune);
        RevengeUntil = now + (IsPlayer ? Tuning.RevengeTime : Tuning.RevengeTime * 0.6f);
        Stamina = Mathf.Max(Stamina, Tuning.StaminaMax * 0.45f);
        Shove = 0f;
        V = 0f;
        HoldUntil = -99f;
        LastStrokeTime = -99f;
        // 布娃娃可能横着飞出去两三条道，爬回来后就近归位（不能留在隔壁泳道里）
        if (Tuning.FoulRoom)
        {
            float half = (Tuning.LaneCount - 1) * 0.5f;
            int lane = Mathf.RoundToInt(Mathf.Clamp(X / Tuning.LaneWidth + half, 0f, half * 2f));
            Lane = lane; LaneTarget = lane;
            X = PoolBuilder.LaneX(lane);
        }
        OnStatus?.Invoke("recover");
    }

    // ═════════════════ 换泳道 ═════════════════
    /// <summary>向左(-1)/右(+1)换一条泳道。互坑房专属：为了绕到别人身后去拽脚。</summary>
    public bool ShiftLane(int dir)
    {
        if (!Tuning.FoulRoom || !Active || Tumbling || Stunned) return false;
        if (Now < LaneShiftReadyAt) return false;
        int nt = Mathf.Clamp(LaneTarget + dir, 0, Tuning.LaneCount - 1);
        if (nt == LaneTarget) return false;
        if (Stamina < Tuning.LaneShiftCost) return false;
        Stamina -= Tuning.LaneShiftCost;
        LastStaminaUse = Now;
        LaneTarget = nt;
        LaneShiftReadyAt = Now + Tuning.LaneShiftCooldown;
        return true;
    }

    public void Begin(float now)
    {
        Started = true;
        RaceStart = now;
        V = Tuning.PushOffSpeed;
        Breath = 100;
    }

    // ═════════════════ 划水 ═════════════════
    public void Stroke(int hand)
    {
        if (!Active) return;
        float now = Now;
        Judge j;
        float q;
        float interval = LastStrokeTime < -50f ? -1f : now - LastStrokeTime;
        float hold = Tuning.HoldTime;

        if (hand == LastHand)
        {
            // 同一只手连划：身体失衡，几乎没推力
            j = Judge.Fumble; q = Tuning.QFumble; hold = Tuning.FumbleHold;
            Flow -= Tuning.FlowLoss;
        }
        else if (interval < 0f || interval > 2.2f * Tuning.IdealInterval + Tuning.OkWindow)
        {
            // 第一划，或停了很久重新起划
            j = Judge.Start; q = Tuning.QStart;
            Flow -= Tuning.FlowLoss * 0.5f;
        }
        else if (interval < Tuning.MinInterval)
        {
            // 疯狂乱按
            j = Judge.Mash; q = Tuning.QMash;
            Flow -= Tuning.FlowLoss;
        }
        else
        {
            float dev = interval - Tuning.IdealInterval;
            float ad = Mathf.Abs(dev);
            if (ad <= Tuning.PerfectWindow)
            {
                j = Judge.Perfect; q = Tuning.QPerfect;
                Flow += Tuning.FlowGainPerfect;
            }
            else if (ad <= Tuning.GoodWindow)
            {
                j = Judge.Good;
                q = Mathf.Lerp(Tuning.QPerfect, Tuning.QGood, Mathf.InverseLerp(Tuning.PerfectWindow, Tuning.GoodWindow, ad));
                Flow += Tuning.FlowGainGood;
            }
            else if (ad <= Tuning.OkWindow)
            {
                j = Judge.Ok;
                q = Mathf.Lerp(Tuning.QGood, Tuning.QOk, Mathf.InverseLerp(Tuning.GoodWindow, Tuning.OkWindow, ad));
                Flow -= Tuning.FlowLoss * 0.25f;
            }
            else if (dev > 0)
            {
                j = Judge.Slow; q = Tuning.QSlow;
                Flow -= Tuning.FlowLoss * 0.5f;
            }
            else
            {
                j = Judge.Rush; q = Tuning.QRush;
                Flow -= Tuning.FlowLoss;
            }
        }

        Flow = Mathf.Clamp(Flow, 0f, 100f);
        MaxFlow = Mathf.Max(MaxFlow, Flow);

        if (j == Judge.Perfect || j == Judge.Good) { Combo++; MaxCombo = Mathf.Max(MaxCombo, Combo); }
        else if (j != Judge.Start) Combo = 0;

        NStrokes++;
        switch (j)
        {
            case Judge.Perfect: NPerfect++; break;
            case Judge.Good: NGood++; break;
            case Judge.Ok: case Judge.Start: NOk++; break;
            default: NBad++; break;
        }

        StrokeQ = q;
        HoldUntil = now + hold;
        LastStrokeTime = now;
        LastHand = hand;
        LastJudge = j;
        LastJudgeTime = now;

        OnStroke?.Invoke(new StrokeEvent { Hand = hand, Judge = j, Quality = q, Interval = interval, Time = now });
    }

    // ═════════════════ 换气 ═════════════════
    public void Breathe()
    {
        if (!Active) return;
        float now = Now;
        if (now - BreathAnimTime < 0.3f) return; // 换气动作本身有个最短时长，防止狂按
        BreathAnimTime = now;
        BreathSide = LastHand == 0 ? -1 : 1; // 朝刚划完那只手的一侧转头
        NBreaths++;

        BreathResult r;
        if (Tuning.FoulRoom)
        {
            // 互坑房：换气只是加分项。按准了加速，按早/按晚都没有惩罚。
            if (Breath > 0f && Breath <= Tuning.BreathPerfectMax)
            {
                r = BreathResult.Perfect;
                PerfectBreathUntil = now + Tuning.BreathPerfectTime;
                NBreathPerfect++;
            }
            else r = BreathResult.Good;
        }
        else if (BreathTier == 2)
        {
            r = BreathResult.Choke;           // 憋太久，呛一口水
            SlowUntil = now + 1.1f; SlowMul = 0.55f;
            Flow *= 0.4f;
        }
        else if (BreathTier == 1)
        {
            r = BreathResult.Recover;
            SlowUntil = now + 0.6f; SlowMul = 0.75f;
        }
        else if (Breath > Tuning.BreathEarlyMax)
        {
            r = BreathResult.Early;           // 气还满着就换 = 抢气，打乱泳姿
            SlowUntil = now + Tuning.BreathEarlyTime; SlowMul = Tuning.BreathEarlyMul;
        }
        else if (Breath >= Tuning.BreathPerfectMin && Breath <= Tuning.BreathPerfectMax)
        {
            r = BreathResult.Perfect;
            PerfectBreathUntil = now + Tuning.BreathPerfectTime;
            NBreathPerfect++;
        }
        else if (Breath < Tuning.BreathPerfectMin)
        {
            r = BreathResult.Close;           // 惊险，但没扣
        }
        else r = BreathResult.Good;

        Breath = 100f;
        Airless = 0f;
        BreathTier = 0;
        LastBreath = r;
        OnBreath?.Invoke(r);
    }

    // ═════════════════ 每帧 ═════════════════
    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float now = Now;

        if (!Started) return;

        // ── 被甩飞 / 挣扎：布娃娃期间规则层完全不参与，位置由身体脚本回写
        if (Flung || Ragdolled || RecoverPromptActive)
        {
            UpdateRecover(now);
            V = Mathf.MoveTowards(V, 0f, 6f * dt);
            Shove = Mathf.MoveTowards(Shove, 0f, Tuning.ShoveDecay * dt);
            Flow = Mathf.Max(0f, Flow - Tuning.FlowDecay * dt * 5f);
            if (Flung || Ragdolled)
            {
                if (Z >= Tuning.Distance) { Z = Tuning.Distance; FinishNow(now); }
                return;
            }
        }

        if (Drowned)
        {
            V = Mathf.MoveTowards(V, 0f, 3f * dt);
            return;
        }

        if (Finished)
        {
            V = 0f;
            Sprinting = false;
            Stamina = Mathf.Min(Tuning.StaminaMax, Stamina + Tuning.StaminaRegen * dt);
            return;
        }

        // ── 流畅度自然衰减（停手更快）
        Flow = Mathf.Max(0f, Flow - Tuning.FlowDecay * dt * (Stroking ? 1f : 3f));

        // ── 换气
        if (Breath > 0f)
        {
            Breath -= 100f / Tuning.BreathDrainTime * dt;
            if (Breath <= 0f) { Breath = 0f; }
        }
        else
        {
            Airless += dt;
        }
        // 互坑房：没有窒息分级，也不会淹死，气空了只是游得慢
        if (Tuning.FoulRoom) Airless = 0f;
        int tier = (Breath > 0f || Tuning.FoulRoom) ? 0 : (Airless < Tuning.BreathTier2After ? 1 : 2);
        if (tier != BreathTier)
        {
            if (tier == 1) { NTier1++; OnStatus?.Invoke("tier1"); }
            if (tier == 2) { NTier2++; OnStatus?.Invoke("tier2"); }
            BreathTier = tier;
        }
        if (Breath <= 0f && Airless >= Tuning.BreathDrownAfter)
        {
            Drowned = true;
            DrownTime = now;
            Sprinting = false;
            OnStatus?.Invoke("drown");
            return;
        }

        // ── 速度目标
        float baseTarget = Stroking && !Tumbling && !Flung ? Tuning.MaxV * StrokeQ : 0f;
        float mul = StateMul(now);

        // ── 加速 & 体力（普通划水不耗体力）
        Sprinting = SprintHeld && baseTarget > 0f && !ExhaustLock && Stamina > 0f;
        if (Sprinting)
        {
            mul *= Tuning.SprintMul;
            Stamina -= Tuning.SprintDrain * dt;
            LastStaminaUse = now;
            SprintSeconds += dt;
            if (Stamina <= 0f)
            {
                Stamina = 0f;
                ExhaustLock = true;
                ExhaustedUntil = now + Tuning.ExhaustTime;
                OnStatus?.Invoke("exhaust");
            }
        }
        else if (now - LastStaminaUse > Tuning.StaminaRegenDelay)
        {
            Stamina = Mathf.Min(Tuning.StaminaMax, Stamina + Tuning.StaminaRegen * dt);
        }
        if (ExhaustLock && Stamina >= Tuning.ExhaustRecover) ExhaustLock = false;

        float target = baseTarget * mul;
        float rate = target > V ? Tuning.ApproachRate : Tuning.DecayRate;
        V = Mathf.Lerp(V, target, 1f - Mathf.Exp(-rate * dt));
        MaxSpeed = Mathf.Max(MaxSpeed, V);

        // 横移换道
        float tx = PoolBuilder.LaneX(LaneTarget);
        if (Mathf.Abs(X - tx) > 0.001f)
        {
            X = Mathf.MoveTowards(X, tx, Tuning.LaneShiftSpeed * dt);
            Lane = Mathf.Clamp(Mathf.RoundToInt(X / Tuning.LaneWidth + (Tuning.LaneCount - 1) * 0.5f), 0, Tuning.LaneCount - 1);
        }

        // 互坑带来的额外位移：冲量指数衰减，总位移 ≈ 冲量 / ShoveDecay
        Z += (V + Shove) * dt;
        Shove *= Mathf.Exp(-Tuning.ShoveDecay * dt);
        if (Mathf.Abs(Shove) < 0.02f) Shove = 0f;
        if (Z < 0f) { Z = 0f; if (Shove < 0f) Shove = 0f; }
        if (Z >= Tuning.Distance)
        {
            Z = Tuning.Distance;
            FinishNow(now);
        }
    }

    /// <summary>冲线。被甩飞时也可能整个人飞过终点线。</summary>
    public void FinishNow(float now)
    {
        if (Finished) return;
        Finished = true;
        FinishTime = now - RaceStart;
        V = 0f;
        Shove = 0f;
        Sprinting = false;
        SprintHeld = false;
        FlungUntil = Mathf.Min(FlungUntil, now);
        BodyFreeUntil = Mathf.Min(BodyFreeUntil, now);
        RecoverPromptActive = false;
        OnStatus?.Invoke("finish");
    }

    /// <summary>当前状态对应的最终速度倍率（HUD 显示用）</summary>
    public float DebugMultiplier()
    {
        float mul = StateMul(Now);
        if (Sprinting) mul *= Tuning.SprintMul;
        return mul;
    }

    /// <summary>除加速以外的全部速度倍率。</summary>
    float StateMul(float now)
    {
        float mul = 1f + Flow / 100f * Tuning.FlowSpeedBonus;
        if (BreathTier == 1) mul *= Tuning.BreathTier1Mul;
        else if (BreathTier == 2) mul *= Tuning.BreathTier2Mul;
        else if (Tuning.FoulRoom && Breath <= 0f) mul *= Tuning.BreathlessMul;
        if (now < SlowUntil) mul *= SlowMul;
        if (now < PerfectBreathUntil) mul *= 1f + Tuning.BreathPerfectBonus;
        if (now < ExhaustedUntil) mul *= Tuning.ExhaustMul;
        if (now < StunUntil) mul *= Tuning.FoulStunMul;
        if (Tuning.FoulRoom)
        {
            if (Shifting) mul *= Tuning.LaneShiftSlow;
            mul *= 1f + CatchUp;
            mul *= LeaderSlowMul;
        }
        return mul;
    }

    /// <summary>领先者减速：把甩开第二名的差距压回来，避免被甩飞后就再也追不上。</summary>
    public float LeaderSlowMul
    {
        get
        {
            if (!Tuning.FoulRoom) return 1f;
            float lead = Z - SecondZ;
            if (lead <= Tuning.LeaderSlowGap) return 1f;
            float k = Mathf.Clamp01((lead - Tuning.LeaderSlowGap) / Mathf.Max(1f, Tuning.CatchUpFull * 0.5f));
            return 1f - Tuning.LeaderSlow * k;
        }
    }

    /// <summary>被别人当成「众矢之的」：领先且甩开第二名时，别人抓你的距离变长、冷却变短。</summary>
    public bool IsMarked
    {
        get
        {
            if (!Tuning.FoulRoom || !Active) return false;
            return Z - SecondZ > Tuning.LeaderSlowGap && Z >= LeaderZ - 0.01f;
        }
    }

    /// <summary>落后领先者越多，速度加成越大（只在互坑房）。0 ~ CatchUpMax。</summary>
    public float CatchUp
    {
        get
        {
            if (!Tuning.FoulRoom) return 0f;
            float behind = LeaderZ - Z;
            float k = Mathf.InverseLerp(Tuning.CatchUpGap, Mathf.Max(Tuning.CatchUpGap + 0.1f, Tuning.CatchUpFull), behind);
            return k * Tuning.CatchUpMax;
        }
    }

    // ═════════════════ 挣扎恢复 ═════════════════
    /// <summary>甩飞期间每帧跑：提示超时重随、NPC 自动爬回来、挣扎太久强制爬回来。</summary>
    void UpdateRecover(float now)
    {
        // 兜底：没在挣扎却被布娃娃接管着（比如状态被外部清掉），别把人永远挂在天上
        if (!RecoverPromptActive)
        {
            if (Ragdolled || Flung) BeginRecover(now);
            return;
        }

        if (IsPlayer)
        {
            // 挣扎太久：强制爬回来（姿势一样难看，还要吃满惩罚）
            if (now >= BodyFreeUntil) { FinishRecover(now); return; }
            // 提示超时：换一个键，进度不掉，但白等一会儿
            if (now >= RecoverPromptUntil && now >= RecoverPenaltyUntil) RollPrompts(now);
            return;
        }

        // NPC：随机手忙脚乱一会儿再自己爬回来
        float struggle = Tuning.NpcRecoverTime * Mathf.Lerp(1.4f, 0.55f, Stamina / Mathf.Max(1f, Tuning.StaminaMax));
        if (now - RecoverStart >= struggle) FinishRecover(now);
    }
}
