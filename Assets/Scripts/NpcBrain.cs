using UnityEngine;

/// <summary>
/// NPC：按「虚拟按键」驱动同一套 Swimmer 规则，没有任何作弊加速。
/// skill 0~1 决定节奏准度、换气判断、体力管理。
/// </summary>
public class NpcBrain : MonoBehaviour
{
    public Swimmer S;
    public float Skill;

    float _interval;        // 个人习惯节奏
    float _noise;           // 节奏抖动
    float _nextStrokeAt = -1f;
    int _nextHand;
    float _breathAt;        // 剩余气量低于此值就换气
    float _forgetUntil = -1f;
    float _sprintUntil = -1f;
    float _sprintCooldown;
    float _sprintStopAt;    // 体力低于此值停止加速
    bool _wasStarted;
    float _stallUntil = -1f;
    bool _fumbleNext;
    float _laneThinkAt;

    // 互坑：针对领头者 / 复仇状态的倾向（只在 Tuning.FoulRoom 下生效）
    const float MarkedFoulMul = 1.6f;       // 目标是众矢之的时出手概率倍率
    const float MarkedLanePrefer = 0.6f;    // 换道选目标时领头者的距离折算（越小越优先）
    const float RevengeFoulMul = 2.0f;      // 自己复仇中出手概率倍率
    const float RevengeLaneMul = 2.0f;      // 自己复仇中换道追人的概率倍率
    const float RevengeStaminaGate = 20f;   // 复仇中出手/换道所需最低体力

    public void Init(Swimmer s, float skill)
    {
        S = s; Skill = skill;
        _stallUntil = -1f; _fumbleNext = false; _laneThinkAt = 0f;
        _interval = Tuning.IdealInterval * Mathf.Lerp(1.28f, 1.04f, skill) * Random.Range(0.98f, 1.04f);
        _noise = Mathf.Lerp(0.12f, 0.035f, skill);
        _sprintStopAt = Mathf.Lerp(5f, 30f, skill);
        _nextHand = Random.value < 0.5f ? 0 : 1;
        PickBreathPoint();
        _wasStarted = false;
        _nextStrokeAt = -1f;
    }

    void PickBreathPoint()
    {
        float center = (Tuning.BreathPerfectMin + Tuning.BreathPerfectMax) * 0.5f;
        float spread = Mathf.Lerp(28f, 7f, Skill);
        _breathAt = Mathf.Clamp(center + Gauss() * spread, -1f, 95f);
        // 低水平 NPC 偶尔走神忘了换气
        if (Random.value < Mathf.Lerp(0.18f, 0.0f, Skill))
        {
            _breathAt = -1f;
            _forgetUntil = -1f;
        }
    }

    static float Gauss()
    {
        float u1 = 1f - Random.value, u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
    }

    void Update()
    {
        if (S == null || !S.Active) { if (S != null) S.SprintHeld = false; return; }
        float now = Time.time;

        if (!_wasStarted)
        {
            _wasStarted = true;
            _nextStrokeAt = now + Mathf.Lerp(0.55f, 0.18f, Skill) + Random.Range(0f, 0.15f); // 出发反应
        }

        // ── 卡壳：时不时整个人卡住（呛口水、找不着节奏、看别人），水平越低越常见
        if (now < _stallUntil) { S.SprintHeld = false; _nextStrokeAt = Mathf.Max(_nextStrokeAt, _stallUntil); }
        else if (Random.value < Tuning.NpcStallRate * Mathf.Lerp(1.6f, 0.5f, Skill) * Time.deltaTime)
        {
            _stallUntil = now + Random.Range(Tuning.NpcStallMin, Tuning.NpcStallMax);
            // 卡完之后第一划多半是乱的（同手 / 节奏断）
            _fumbleNext = Random.value < 0.5f;
        }

        // ── 划水
        if (now >= _nextStrokeAt)
        {
            int hand = _nextHand;
            if (_fumbleNext || Random.value < Mathf.Lerp(0.06f, 0.012f, Skill)) hand = S.LastHand < 0 ? hand : S.LastHand; // 手滑
            _fumbleNext = false;
            S.Stroke(hand);
            _nextHand = 1 - hand;
            float iv = _interval + Gauss() * _noise;
            if (Random.value < Mathf.Lerp(0.07f, 0.015f, Skill)) iv += Random.Range(0.2f, 0.5f); // 走神停顿
            if (S.BreathTier == 2) iv *= Random.Range(0.4f, 1.6f); // 挣扎时节奏全乱
            _nextStrokeAt = now + Mathf.Max(0.12f, iv);
        }

        // ── 换气
        if (S.BreathTier == 0 && _breathAt >= 0f && S.Breath <= _breathAt)
        {
            S.Breathe();
            PickBreathPoint();
        }
        else if (S.BreathTier > 0 || (Tuning.FoulRoom && S.Breath <= 0f))
        {
            if (_forgetUntil < 0f)
                _forgetUntil = now + Random.Range(0.15f, Mathf.Lerp(Tuning.BreathTier2After * 1.6f, 0.4f, Skill));
            if (now >= _forgetUntil)
            {
                S.Breathe();
                _forgetUntil = -1f;
                PickBreathPoint();
            }
        }

        // ── 加速：最后冲刺 + 体力满时偶尔爆发
        float remain = Tuning.Distance - S.Z;
        bool want = false;
        if (remain < Mathf.Lerp(6f, 14f, Skill) && S.Stamina > 2f) want = true;
        else if (now < _sprintUntil && S.Stamina > _sprintStopAt) want = true;
        else
        {
            _sprintCooldown -= Time.deltaTime;
            if (_sprintCooldown <= 0f && S.Stamina > 90f && remain > 15f)
            {
                _sprintCooldown = Random.Range(2f, 6f);
                if (Random.value < 0.6f) _sprintUntil = now + Random.Range(0.8f, 2.2f);
            }
        }
        S.SprintHeld = want;

        // ── 互坑：看到可互动对手，按概率出手（水平越高越果断，也越容易打出组合技）
        bool selfFree = !S.Ragdolled && !S.Flung;   // 自己被甩飞/挣扎时什么都不想
        bool revenge = Tuning.FoulRoom && S.Revenge;
        if (Tuning.FoulRoom && selfFree && Foul.CanAct(S) && S.Stamina > (revenge ? RevengeStaminaGate : 35f))
        {
            var t = Foul.FindTarget(S, out bool ahead);
            if (t != null)
            {
                float rate = Tuning.NpcFoulRate * Mathf.Lerp(0.6f, 1.4f, Skill) * (t.IsPlayer ? 1.2f : 1f);
                if (t.IsMarked) rate *= MarkedFoulMul;      // 众矢之的：领头的更想坑
                if (revenge) rate *= RevengeFoulMul;        // 复仇：更凶
                if (Random.value < rate * Time.deltaTime)
                {
                    if (Random.value < Tuning.NpcComboChance * Mathf.Lerp(0.5f, 1.5f, Skill)) Foul.Combo(S, t, true);
                    else Foul.Single(S, t, ahead);
                }
            }
        }

        // ── 换道：隔壁两道以内、前方不远有人 → 偶尔横移过去准备拽他；没目标就慢慢回自己的道
        if (Tuning.FoulRoom && selfFree && now >= _laneThinkAt)
        {
            _laneThinkAt = now + (revenge ? Random.Range(0.4f, 0.9f) : Random.Range(0.8f, 1.8f));
            int laneDir = 0;
            Swimmer best = null; float bestD = 99f;
            float reach = Tuning.FoulRange * (revenge ? 2.2f : 1.6f);
            foreach (var o in Foul.All)
            {
                if (o == S || !o.Active || o.Ragdolled) continue;
                int dl = o.Lane - S.LaneTarget;
                if (dl == 0 || Mathf.Abs(dl) > 2) continue;
                float dz = o.Z - S.Z;
                if (dz < 0.5f || dz > reach) continue;
                // 领头的（众矢之的）算作「更近」，优先换道去坑他
                float score = o.IsMarked ? dz * MarkedLanePrefer : dz;
                if (score < bestD) { bestD = score; best = o; }
            }
            float laneChance = Mathf.Lerp(0.15f, 0.4f, Skill);
            if (best != null && best.IsMarked) laneChance *= MarkedFoulMul;
            if (revenge) laneChance *= RevengeLaneMul;
            if (best != null && S.Stamina > (revenge ? RevengeStaminaGate : 45f) && Random.value < Mathf.Min(0.9f, laneChance)) laneDir = (int)Mathf.Sign(best.Lane - S.LaneTarget);
            else if (best == null && S.LaneTarget != S.HomeLane && Random.value < 0.35f) laneDir = (int)Mathf.Sign(S.HomeLane - S.LaneTarget);
            if (laneDir != 0) S.ShiftLane(laneDir);
        }
    }
}
