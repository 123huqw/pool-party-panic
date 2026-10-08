using System;
using System.Collections.Generic;
using UnityEngine;

public enum FoulKind { Grab, Kick, Combo }

/// <summary>
/// 互坑规则：拽脚（对手在前）、蹬人（对手在后）、一拽一蹬组合技。
/// 玩家和 NPC 走同一套函数。只在「允许互坑」房间里生效。
/// </summary>
public static class Foul
{
    public static List<Swimmer> All;
    /// <summary>(出手的人, 被坑的人, 类型)</summary>
    public static Action<Swimmer, Swimmer, FoulKind> OnFoul;

    /// <summary>s 现在有没有力气/冷却出手。</summary>
    public static bool CanAct(Swimmer s)
    {
        return Tuning.FoulRoom && s.Active && !s.Tumbling && !s.Stunned && !s.Flung && !s.Ragdolled
               && s.Now >= s.FoulReadyAt && s.Stamina >= Tuning.FoulCost;
    }

    /// <summary>s 这次出手能抓多远：复仇状态或对手是「众矢之的」时更远。</summary>
    public static float RangeOf(Swimmer s, Swimmer t)
    {
        float r = Tuning.FoulRange;
        if (s.Revenge) r *= Tuning.RevengeRangeMul;
        if (t != null && t.IsMarked) r *= Tuning.LeaderGrabRangeMul;
        return r;
    }

    /// <summary>找最近的可互动对手。ahead = 对手在前（拽脚）；否则在后（蹬人）。</summary>
    public static Swimmer FindTarget(Swimmer s, out bool ahead)
    {
        ahead = false;
        if (!Tuning.FoulRoom || All == null || !s.Active || s.Flung || s.Ragdolled) return null;
        Swimmer best = null;
        float bestD = float.MaxValue;
        float now = s.Now;
        foreach (var o in All)
        {
            if (o == s || !o.Active || now < o.ImmuneUntil || o.Flung || o.Ragdolled) continue;
            if (Mathf.Abs(o.Lane - s.Lane) > Tuning.FoulLaneReach) continue;
            float dz = o.Z - s.Z;
            float ad = Mathf.Abs(dz);
            if (ad < Tuning.FoulMinGap || ad > RangeOf(s, o)) continue;
            // 同道优先，其次距离近
            float score = ad + Mathf.Abs(o.Lane - s.Lane) * 0.5f;
            if (score < bestD) { bestD = score; best = o; ahead = dz > 0f; }
        }
        return best;
    }

    /// <summary>普通的单次拽 / 蹬：对方后退 + 僵直，自己前冲一小段。拽脚 = 直接甩飞。</summary>
    public static void Single(Swimmer s, Swimmer t, bool ahead)
    {
        float now = s.Now;
        Pay(s, now);
        s.NFoul++;
        if (ahead && Tuning.FoulRoom)
        {
            // 拽住脚踝往后一扯 —— 整个人飞出去，布娃娃接管
            float side = PickFlingSide(t);
            t.StartFling(side, now);
            s.Shove += Tuning.GrabberBoost;
            s.GrabAnimTime = now;
            OnFoul?.Invoke(s, t, FoulKind.Grab);
            return;
        }
        t.Shove -= Tuning.FoulTargetShove;
        s.Shove += Tuning.FoulSelfShove;
        t.StunUntil = now + Tuning.FoulStunTime;
        t.ImmuneUntil = now + Tuning.FoulImmune;
        t.Flow *= 0.5f;
        t.HitTime = now; t.HitKind = ahead ? 0 : 1;
        t.V *= 0.4f;
        t.NFouled++;
        if (ahead) s.GrabAnimTime = now; else s.KickAnimTime = now;
        OnFoul?.Invoke(s, t, ahead ? FoulKind.Grab : FoulKind.Kick);
    }

    /// <summary>往哪边甩：随机一侧，但别甩出泳池。</summary>
    public static float PickFlingSide(Swimmer t)
    {
        float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        float x = PoolBuilder.LaneX(t.LaneTarget);
        if (x + side * 2.5f > 9f) side = -1f;
        else if (x + side * 2.5f < -9f) side = 1f;
        return side;
    }

    /// <summary>
    /// 组合技「一拽一蹬」：不管对手原来在哪，强制甩到自己身后 ComboThrowBack 米并蹬飞翻滚，自己获得极强前冲。
    /// pay=false 用于玩家把刚打出的单次拽/蹬升级成组合技（体力已经付过了）。
    /// </summary>
    public static void Combo(Swimmer s, Swimmer t, bool pay)
    {
        float now = s.Now;
        if (pay) Pay(s, now);
        // 需要的总位移 = 把对手从当前位置挪到自己身后；冲量 = 位移 × 衰减速率
        float disp = (t.Z - s.Z) + Tuning.ComboThrowBack;
        if (disp < Tuning.ComboThrowBack * 0.5f) disp = Tuning.ComboThrowBack * 0.5f;
        t.Shove = -disp * Tuning.ShoveDecay;
        t.V = 0f;
        s.Shove = Mathf.Max(s.Shove, 0f) + Tuning.ComboSelfShove;
        t.TumbleUntil = now + Tuning.ComboTumbleTime;
        t.StunUntil = now + Tuning.ComboTumbleTime + 0.3f;
        t.ImmuneUntil = now + Tuning.ComboTumbleTime + Tuning.FoulImmune;
        t.Flow = 0f;
        t.Combo = 0;
        t.NFouled++;
        s.NCombo++;
        s.GrabAnimTime = now;
        s.KickAnimTime = now + 0.15f;
        s.FoulReadyAt = now + Tuning.FoulCooldown;
        OnFoul?.Invoke(s, t, FoulKind.Combo);
    }

    /// <summary>抓空：扣一半体力，自己翻滚一小下、短暂僵直。</summary>
    public static void Whiff(Swimmer s)
    {
        float now = s.Now;
        s.Stamina = Mathf.Max(0f, s.Stamina - Tuning.FoulCost * 0.5f);
        s.LastStaminaUse = now;
        s.FoulReadyAt = now + Tuning.FoulCooldown * 0.6f;
        s.StunUntil = now + Tuning.WhiffStun;
        s.WhiffTime = now;
        s.GrabAnimTime = now;
        s.V *= 0.6f;
        s.Flow *= 0.7f;
    }

    static void Pay(Swimmer s, float now)
    {
        s.Stamina = Mathf.Max(0f, s.Stamina - Tuning.FoulCost);
        s.LastStaminaUse = now;
        float cd = Tuning.FoulCooldown * (s.Revenge ? Tuning.RevengeCooldownMul : 1f);
        s.FoulReadyAt = now + cd;
    }
}
