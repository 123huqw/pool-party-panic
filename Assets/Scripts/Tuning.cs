using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>标记可以在游戏内调参面板（F1）里实时调整的参数。</summary>
[AttributeUsage(AttributeTargets.Field)]
public class TuneAttribute : Attribute
{
    public readonly string Group, Label;
    public readonly float Min, Max;
    public TuneAttribute(string group, string label, float min, float max)
    {
        Group = group; Label = label; Min = min; Max = max;
    }
}

/// <summary>
/// 全部手感参数集中在这里。
/// 设计原则：核心是游泳。所以绝大部分参数都只服务于「双键节奏划水」。
/// 游戏内按 F1 打开调参面板，调好后点「复制参数」贴回这里固化。
/// </summary>
public static class Tuning
{
    // ───────── 比赛 ─────────
    public static float Distance = 100f;         // 泳池长度（米）
    public const int LaneCount = 8;
    public static int PlayerLane = 3;            // 0 起
    public static float LaneWidth = 2.5f;
    public static float CountdownTime = 3f;
    public static float PushOffSpeed = 1.6f;     // 出发蹬壁初速度

    // ───────── 换泳道（为了绕到别人旁边去拽脚） ─────────
    [Tune("换道", "换道横移速度 (米/秒)", 0.5f, 10f)] public static float LaneShiftSpeed = 3.6f;
    [Tune("换道", "换道后冷却 (秒)", 0f, 2f)] public static float LaneShiftCooldown = 0.3f;
    [Tune("换道", "换道体力消耗", 0f, 30f)] public static float LaneShiftCost = 4f;
    [Tune("换道", "横移时前进速度倍率", 0.4f, 1f)] public static float LaneShiftSlow = 0.92f;

    // ───────── 追赶加成（被超了还有机会追上去拽脚） ─────────
    /// <summary>落后第一名超过 CatchUpGap 米开始加一点速度，到 CatchUpFull 米吃满 CatchUpMax。</summary>
    [Tune("追赶", "落后多少米开始有追赶加成", 0f, 25f)] public static float CatchUpGap = 3f;
    [Tune("追赶", "落后多少米吃满追赶加成", 2f, 40f)] public static float CatchUpFull = 10f;
    [Tune("追赶", "追赶最大速度加成", 0f, 0.6f)] public static float CatchUpMax = 0.4f;

    // ───────── 双键节奏划水（核心） ─────────
    [Tune("划水", "理想划水间隔 (秒)", 0.2f, 0.9f)] public static float IdealInterval = 0.42f;
    [Tune("划水", "乱划阈值：快于此 (秒)", 0.05f, 0.4f)] public static float MinInterval = 0.16f;
    [Tune("划水", "PERFECT 窗口 ±(秒)", 0.02f, 0.2f)] public static float PerfectWindow = 0.06f;
    [Tune("划水", "GOOD 窗口 ±(秒)", 0.03f, 0.35f)] public static float GoodWindow = 0.13f;
    [Tune("划水", "OK 窗口 ±(秒)", 0.05f, 0.6f)] public static float OkWindow = 0.24f;
    public static float QPerfect = 1.00f, QGood = 0.88f, QOk = 0.78f, QStart = 0.80f;
    // 失误时的速度下限：调高 = 节奏错了也不至于停在水里
    [Tune("划水", "太慢 的速度比例", 0f, 1f)] public static float QSlow = 0.68f;
    [Tune("划水", "太急 的速度比例", 0f, 1f)] public static float QRush = 0.60f;
    [Tune("划水", "乱划 的速度比例", 0f, 1f)] public static float QMash = 0.42f;
    [Tune("划水", "同手连划 的速度比例", 0f, 1f)] public static float QFumble = 0.35f;
    [Tune("划水", "完美巡航速度 (米/秒)", 0.6f, 4f)] public static float MaxV = 2.2f;
    [Tune("划水", "单次划水有效时长 (秒)", 0.2f, 1.2f)] public static float HoldTime = 0.55f;
    public static float FumbleHold = 0.35f;

    [Tune("流畅度", "流畅度满时速度加成", 0f, 0.6f)] public static float FlowSpeedBonus = 0.22f;
    [Tune("流畅度", "PERFECT 流畅度增益", 0f, 40f)] public static float FlowGainPerfect = 16f;
    [Tune("流畅度", "GOOD 流畅度增益", 0f, 30f)] public static float FlowGainGood = 8f;
    [Tune("流畅度", "乱节奏流畅度损失", 0f, 80f)] public static float FlowLoss = 16f;
    [Tune("流畅度", "流畅度每秒衰减", 0f, 30f)] public static float FlowDecay = 6f;

    // ───────── 水阻 / 惯性 ─────────
    [Tune("水阻", "加速逼近速率", 0.5f, 10f)] public static float ApproachRate = 3.2f;
    [Tune("水阻", "松手减速速率", 0.2f, 6f)] public static float DecayRate = 1.6f;

    // ───────── 换气（独立计时，不耗体力） ─────────
    [Tune("换气", "换气倒计时：满→空 (秒)", 1.5f, 10f)] public static float BreathDrainTime = 3.6f;
    [Tune("换气", "PERFECT 换气区下限 (%)", 0f, 50f)] public static float BreathPerfectMin = 4f;
    [Tune("换气", "PERFECT 换气区上限 (%)", 5f, 80f)] public static float BreathPerfectMax = 34f;
    [Tune("换气", "抢气阈值：高于此 (%)", 40f, 100f)] public static float BreathEarlyMax = 80f;
    [Tune("换气", "气空→脸红 (秒)", 0.3f, 6f)] public static float BreathTier2After = 1.4f;
    [Tune("换气", "气空→沉底 (秒)", 0.8f, 10f)] public static float BreathDrownAfter = 3.0f;
    [Tune("换气", "脸变直 速度倍率", 0.1f, 1f)] public static float BreathTier1Mul = 0.62f;
    [Tune("换气", "脸变红 速度倍率", 0.05f, 1f)] public static float BreathTier2Mul = 0.30f;
    public static float BreathEarlyMul = 0.80f, BreathEarlyTime = 0.8f;
    [Tune("换气", "PERFECT 换气加速", 0f, 0.5f)] public static float BreathPerfectBonus = 0.12f;
    public static float BreathPerfectTime = 1.3f;

    // ───────── 体力（只管加速/拽脚/蹬腿） ─────────
    public static float StaminaMax = 100f;
    [Tune("体力", "体力恢复 (每秒)", 1f, 60f)] public static float StaminaRegen = 14f;
    [Tune("体力", "停止消耗后恢复延迟 (秒)", 0f, 3f)] public static float StaminaRegenDelay = 0.6f;
    [Tune("体力", "加速消耗 (每秒)", 4f, 80f)] public static float SprintDrain = 28f;
    [Tune("体力", "加速速度倍率", 1f, 2.2f)] public static float SprintMul = 1.35f;
    [Tune("体力", "力竭虚弱时长 (秒)", 0f, 5f)] public static float ExhaustTime = 1.6f;
    [Tune("体力", "力竭速度倍率", 0.2f, 1f)] public static float ExhaustMul = 0.65f;
    public static float ExhaustRecover = 20f;

    // ───────── 房间规则 ─────────
    /// <summary>true = 允许互坑房（换气只加分、不会淹死、可拽脚蹬人）；false = 纯竞速房（严厉窒息惩罚、无互坑）。</summary>
    public static bool FoulRoom = true;
    [Tune("互坑", "互坑房：气空时速度倍率", 0.3f, 1f)] public static float BreathlessMul = 0.85f;

    // ───────── 互坑：拽脚 / 蹬人 / 组合技 ─────────
    [Tune("互坑", "可互动距离 (米)", 0.5f, 8f)] public static float FoulRange = 4.5f;
    public static float FoulMinGap = 0.2f;        // 前后差小于此算并排，不出提示
    public static int FoulLaneReach = 1;          // 够得着几条泳道以外的人
    [Tune("互坑", "拽/蹬 体力消耗", 0f, 60f)] public static float FoulCost = 20f;
    [Tune("互坑", "拽/蹬 冷却 (秒)", 0f, 5f)] public static float FoulCooldown = 1.5f;
    [Tune("互坑", "被坑后的无敌时间 (秒)", 0f, 5f)] public static float FoulImmune = 1.8f;
    [Tune("互坑", "对方后退冲量 (米/秒)", 0f, 15f)] public static float FoulTargetShove = 6.5f;
    [Tune("互坑", "自己前冲冲量 (米/秒)", 0f, 10f)] public static float FoulSelfShove = 3.2f;
    [Tune("互坑", "被坑僵直 (秒)", 0f, 3f)] public static float FoulStunTime = 0.8f;
    [Tune("互坑", "命中顿帧 (秒)", 0f, 0.3f)] public static float HitStop = 0.08f;
    public static float HitReactTime = 0.75f;     // 被拽/被蹬的受击动画时长
    [Tune("互坑", "抓空僵直 (秒)", 0f, 2f)] public static float WhiffStun = 0.6f;
    public static float FoulStunMul = 0.3f;       // 僵直期间的划水速度倍率
    [Tune("互坑", "冲量衰减速率", 0.5f, 6f)] public static float ShoveDecay = 2.2f;  // 位移 ≈ 冲量 / 此值
    // 组合技：两次 F 的间隔落进一个「隐藏 + 每次随机」的窗口才触发
    // 成功率 ≈ 窗口宽度 / (中心最大 - 中心最小)：0.03 / 0.30 ≈ 10%
    [Tune("组合技", "隐藏窗口中心 最小 (秒)", 0.05f, 0.6f)] public static float ComboWinMin = 0.12f;
    [Tune("组合技", "隐藏窗口中心 最大 (秒)", 0.05f, 0.8f)] public static float ComboWinMax = 0.42f;
    [Tune("组合技", "隐藏窗口宽度 (秒)", 0.01f, 0.3f)] public static float ComboWinWidth = 0.03f;
    public static float ComboMaxWait = 0.6f;      // 第一下之后多久内的第二下算「连按」
    [Tune("组合技", "把对手甩到身后几米", 0.5f, 8f)] public static float ComboThrowBack = 3.2f;
    [Tune("组合技", "自己前冲冲量 (米/秒)", 0f, 15f)] public static float ComboSelfShove = 6f;
    [Tune("组合技", "对手翻滚时长 (秒)", 0.3f, 4f)] public static float ComboTumbleTime = 1.7f;
    [Tune("NPC", "NPC 每秒出手概率", 0f, 1f)] public static float NpcFoulRate = 0.12f;
    [Tune("NPC", "NPC 打出组合技概率", 0f, 1f)] public static float NpcComboChance = 0.10f;

    // ───────── 软绵绵四肢 ─────────
    [Tune("软体", "手部追踪刚度 (越小越软)", 10f, 500f)] public static float HandKp = 150f;
    [Tune("软体", "手部追踪阻尼", 0f, 40f)] public static float HandKd = 9f;
    [Tune("软体", "四肢水阻", 0f, 10f)] public static float LimbDrag = 2.2f;
    [Tune("软体", "肩关节回位弹簧", 0f, 100f)] public static float ShoulderSpring = 6f;
    [Tune("软体", "肘/膝关节回位弹簧", 0f, 200f)] public static float JointSpring = 25f;
    [Tune("软体", "身体侧滚角度", 0f, 60f)] public static float RollAmp = 28f;
    [Tune("软体", "打腿刚度", 0f, 300f)] public static float KickKp = 90f;

    // ───────── NPC ─────────
    [Tune("NPC", "NPC 水平下限", 0f, 1f)] public static float NpcSkillMin = 0.22f;
    [Tune("NPC", "NPC 水平上限", 0f, 1f)] public static float NpcSkillMax = 0.68f;
    [Tune("NPC", "NPC 卡壳概率（每秒）", 0f, 0.3f)] public static float NpcStallRate = 0.04f;
    [Tune("NPC", "NPC 卡壳时长 最小 (秒)", 0f, 1.5f)] public static float NpcStallMin = 0.25f;
    [Tune("NPC", "NPC 卡壳时长 最大 (秒)", 0f, 3f)] public static float NpcStallMax = 0.9f;

    // ───────── 拽脚甩飞（布娃娃） ─────────
    [Tune("甩飞", "横向甩出去的速度 (米/秒)", 0f, 20f)] public static float FlingSide = 7.5f;
    [Tune("甩飞", "向上抛起的速度 (米/秒)", 0f, 20f)] public static float FlingUp = 8.5f;
    [Tune("甩飞", "翻滚角速度 (度/秒)", 0f, 2000f)] public static float FlingSpin = 900f;
    [Tune("甩飞", "失去控制的飞行时间 (秒)", 0.2f, 4f)] public static float FlingFlight = 1f;
    [Tune("甩飞", "甩飞力度总倍率", 0f, 3f)] public static float FlingPower = 1f;
    [Tune("甩飞", "入水水花顿帧 (秒)", 0f, 0.5f)] public static float FlingHitStop = 0.16f;
    [Tune("甩飞", "甩飞时自身前冲 (米/秒)", 0f, 10f)] public static float GrabberBoost = 2.4f;
    [Tune("甩飞", "落水可撞倒别人", 0f, 1f)] public static float FlingChainKnock = 1f;

    // ───────── 挣扎恢复（连打无效，要按键上的提示） ─────────
    [Tune("挣扎", "需要按对几次才翻回来", 1f, 8f)] public static float RecoverSteps = 3f;
    [Tune("挣扎", "按错惩罚 (秒)", 0f, 2f)] public static float WrongKeyPenalty = 0.55f;
    [Tune("挣扎", "没按完最多挣扎多久 (秒)", 1f, 12f)] public static float RecoverMaxTime = 5f;
    [Tune("挣扎", "按键提示停留时间 (秒)", 0.3f, 3f)] public static float PromptLife = 1.3f;
    [Tune("挣扎", "NPC 挣扎多久后自己爬回来 (秒)", 0f, 6f)] public static float NpcRecoverTime = 1.6f;

    // ───────── 复仇 ─────────
    [Tune("复仇", "翻回来后的无敌时间 (秒)", 0f, 6f)] public static float PostRecoverImmune = 2.2f;
    [Tune("复仇", "复仇状态时长 (秒)", 0f, 12f)] public static float RevengeTime = 5f;
    [Tune("复仇", "复仇时抓人距离倍率", 1f, 3f)] public static float RevengeRangeMul = 1.5f;
    [Tune("复仇", "复仇时出手冷却倍率", 0.1f, 1f)] public static float RevengeCooldownMul = 0.5f;

    // ───────── 橡皮筋（互坑房专属：落后加速 / 领先减速） ─────────
    [Tune("橡皮筋", "领先者减速比例", 0f, 0.4f)] public static float LeaderSlow = 0.15f;
    [Tune("橡皮筋", "超过第二名多少米开始减速", 0f, 20f)] public static float LeaderSlowGap = 2f;
    [Tune("橡皮筋", "领先者被抓的距离倍率（众矢之的）", 1f, 3f)] public static float LeaderGrabRangeMul = 1.4f;

    // ───────── 调参面板支持 ─────────
    public class Entry
    {
        public FieldInfo Field;
        public TuneAttribute Attr;
        public float Default;
        public float Value { get => (float)Field.GetValue(null); set => Field.SetValue(null, value); }
    }

    static List<Entry> _entries;
    public static List<Entry> Entries
    {
        get
        {
            if (_entries != null) return _entries;
            _entries = new List<Entry>();
            var fields = new List<FieldInfo>(typeof(Tuning).GetFields(BindingFlags.Public | BindingFlags.Static));
            foreach (var f in fields)
            {
                var a = f.GetCustomAttribute<TuneAttribute>();
                if (a == null || f.FieldType != typeof(float)) continue;
                _entries.Add(new Entry { Field = f, Attr = a, Default = (float)f.GetValue(null) });
            }
            return _entries;
        }
    }

    public static void ResetAll()
    {
        foreach (var e in Entries) e.Value = e.Default;
    }

    public static string Export()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// 游泳竞速原型 · 调参导出 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        foreach (var e in Entries)
        {
            string mark = Mathf.Approximately(e.Value, e.Default) ? "" : "   // 改过，默认 " + e.Default.ToString("0.###");
            sb.AppendLine($"{e.Field.Name} = {e.Value:0.###}f;  // {e.Attr.Group} · {e.Attr.Label}{mark}");
        }
        return sb.ToString();
    }
}
