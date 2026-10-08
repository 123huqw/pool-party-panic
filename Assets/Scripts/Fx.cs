using UnityEngine;

/// <summary>粒子特效：全部代码生成。</summary>
public static class Fx
{
    static Material _particleMat;

    static Material ParticleMat
    {
        get
        {
            if (_particleMat != null) return _particleMat;
            var t = Resources.Load<Material>("Mat_Particle");
            if (t != null) { _particleMat = new Material(t) { mainTexture = Art.SoftDot() }; return _particleMat; }
            var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            _particleMat = new Material(sh) { mainTexture = Art.SoftDot() };
            if (_particleMat.HasProperty("_TintColor")) _particleMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return _particleMat;
        }
    }

    static ParticleSystem Base(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.maxParticles = 300;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = ParticleMat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        return ps;
    }

    public static ParticleSystem MakeSplash(Transform parent)
    {
        var ps = Base(parent, "Splash");
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        m.startColor = new Color(0.92f, 0.97f, 1f, 0.9f);
        m.gravityModifier = 1.1f;
        var em = ps.emission; em.rateOverTime = 0;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = 0.08f;
        sh.rotation = new Vector3(-90, 0, 0);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0, 1) });
        col.color = g;
        ps.Play();
        return ps;
    }

    public static ParticleSystem MakeFoam(Transform parent)
    {
        var ps = Base(parent, "Foam");
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.26f);
        m.startColor = new Color(1, 1, 1, 0.55f);
        m.gravityModifier = 0.35f;
        var em = ps.emission; em.rateOverTime = 0;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.18f;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.4f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0.6f, 0), new GradientAlphaKey(0, 1) });
        col.color = g;
        ps.Play();
        return ps;
    }

    public static ParticleSystem MakeBubbles(Transform parent)
    {
        var ps = Base(parent, "Bubbles");
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        m.startColor = new Color(0.85f, 0.95f, 1f, 0.85f);
        m.gravityModifier = -0.4f;
        var em = ps.emission; em.rateOverTime = 25;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    public static void Burst(ParticleSystem ps, Vector3 pos, int count)
    {
        if (ps == null) return;
        var ep = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
        ps.Emit(ep, count);
    }
}

/// <summary>程序化合成音效：划水、换气、呛水、哨声、节拍器、PERFECT 提示音。</summary>
public static class Sfx
{
    public static AudioClip Splash, Breath, Choke, Whistle, Beep, BeepHigh, Tick, Perfect, Crowd, Thump, Heart;
    public static AudioClip Scream, Whoosh, Splat;
    const int Rate = 44100;

    public static void Init()
    {
        if (Splash != null) return;
        var rnd = new System.Random(3);
        Splash = Make("splash", 0.22f, (t, i) =>
        {
            float env = Mathf.Exp(-t * 18f) * (1 - Mathf.Exp(-t * 400f));
            return ((float)rnd.NextDouble() * 2 - 1) * env * 0.55f;
        }, lowpass: 0.35f);
        Breath = Make("breath", 0.32f, (t, i) =>
        {
            float env = Mathf.Sin(Mathf.Clamp01(t / 0.32f) * Mathf.PI);
            return ((float)rnd.NextDouble() * 2 - 1) * env * 0.35f;
        }, lowpass: 0.12f);
        Choke = Make("choke", 0.5f, (t, i) =>
        {
            float g = Mathf.Sin(t * 2 * Mathf.PI * (180 + 60 * Mathf.Sin(t * 40))) * 0.4f;
            float n = ((float)rnd.NextDouble() * 2 - 1) * 0.4f;
            return (g + n) * Mathf.Exp(-t * 4) * (Mathf.Sin(t * 50) > 0 ? 1 : 0.3f);
        }, lowpass: 0.3f);
        Whistle = Make("whistle", 0.9f, (t, i) =>
        {
            float f = 2900 + 60 * Mathf.Sin(t * 2 * Mathf.PI * 28);
            float env = Mathf.Clamp01(t * 30) * Mathf.Clamp01((0.9f - t) * 10);
            return Mathf.Sin(t * 2 * Mathf.PI * f) * 0.35f * env;
        });
        Beep = Make("beep", 0.18f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 660) * 0.4f * Mathf.Clamp01((0.18f - t) * 30));
        BeepHigh = Make("beep2", 0.4f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 1320) * 0.4f * Mathf.Clamp01((0.4f - t) * 10));
        Tick = Make("tick", 0.04f, (t, i) => Mathf.Sin(t * 2 * Mathf.PI * 1800) * 0.3f * Mathf.Exp(-t * 120));
        Perfect = Make("perfect", 0.12f, (t, i) =>
            (Mathf.Sin(t * 2 * Mathf.PI * 1568) + 0.5f * Mathf.Sin(t * 2 * Mathf.PI * 2349)) * 0.18f * Mathf.Exp(-t * 30));
        Crowd = Make("crowd", 2.5f, (t, i) =>
        {
            float env = Mathf.Clamp01(t * 3) * Mathf.Clamp01((2.5f - t) * 1.5f);
            return ((float)rnd.NextDouble() * 2 - 1) * 0.3f * env * (0.7f + 0.3f * Mathf.Sin(t * 9));
        }, lowpass: 0.08f);
        // 拽/蹬命中：低频「咚」+ 一大团水声
        Thump = Make("thump", 0.45f, (t, i) =>
        {
            float body = Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(140f, 55f, Mathf.Clamp01(t * 6f))) * Mathf.Exp(-t * 9f) * 0.9f;
            float water = ((float)rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 7f) * (1 - Mathf.Exp(-t * 300f)) * 0.6f;
            return body + water;
        }, lowpass: 0.5f);
        // 心跳：咚-咚
        Heart = Make("heart", 0.42f, (t, i) =>
        {
            float a = Mathf.Sin(t * 2 * Mathf.PI * 52f) * Mathf.Exp(-t * 22f);
            float t2 = t - 0.17f;
            float b = t2 > 0 ? Mathf.Sin(t2 * 2 * Mathf.PI * 46f) * Mathf.Exp(-t2 * 26f) * 0.7f : 0f;
            return (a + b) * 0.9f;
        });
        // 被甩飞时的惨叫：音高下坠 + 颤音 + 破音
        Scream = Make("scream", 0.75f, (t, i) =>
        {
            float u = Mathf.Clamp01(t / 0.75f);
            float f = 640f * Mathf.Lerp(1.45f, 0.5f, u * u);
            float vib = 1f + 0.06f * Mathf.Sin(t * 2 * Mathf.PI * 17f);
            float env = Mathf.Clamp01(t * 45f) * Mathf.Clamp01((0.75f - t) * 3.2f);
            float tone = Mathf.Sin(t * 2 * Mathf.PI * f * vib) + 0.4f * Mathf.Sin(t * 2 * Mathf.PI * f * 2.02f * vib);
            float crack = ((float)rnd.NextDouble() * 2 - 1) * 0.18f * Mathf.Clamp01(u * 1.6f - 0.4f);
            return (tone * 0.26f + crack) * env;
        }, lowpass: 0.6f);
        // 破空声：被抓飞的瞬间
        Whoosh = Make("whoosh", 0.4f, (t, i) =>
        {
            float env = Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI);
            return ((float)rnd.NextDouble() * 2 - 1) * env * 0.5f;
        }, lowpass: 0.22f);
        // 大字型拍在水面上
        Splat = Make("splat", 0.6f, (t, i) =>
        {
            float body = Mathf.Sin(t * 2 * Mathf.PI * Mathf.Lerp(190f, 70f, Mathf.Clamp01(t * 5f))) * Mathf.Exp(-t * 11f) * 0.7f;
            float spray = ((float)rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 5.5f) * (1 - Mathf.Exp(-t * 500f)) * 0.8f;
            return body + spray;
        }, lowpass: 0.55f);
    }

    static AudioClip Make(string name, float dur, System.Func<float, int, float> f, float lowpass = 1f)
    {
        int n = Mathf.CeilToInt(dur * Rate);
        var data = new float[n];
        float y = 0;
        for (int i = 0; i < n; i++)
        {
            float x = f((float)i / Rate, i);
            y += (x - y) * lowpass;
            data[i] = y * (lowpass < 1f ? Mathf.Min(3f, 0.5f / lowpass) : 1f) ;
        }
        var c = AudioClip.Create(name, n, 1, Rate, false);
        c.SetData(data, 0);
        return c;
    }

    public static void Play(AudioSource src, AudioClip clip, float pitch = 1f, float vol = 1f)
    {
        if (src == null || clip == null) return;
        src.pitch = pitch;
        src.PlayOneShot(clip, vol);
    }
}
