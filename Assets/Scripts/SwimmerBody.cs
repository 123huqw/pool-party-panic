using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 软绵绵的物理身体（类《人类一败涂地》）：
///   · 躯干是运动学刚体，严格跟随 Swimmer.Z（比赛结果只由规则层决定，物理只负责"表演"）。
///   · 手臂、腿是真实刚体链 + ConfigurableJoint，关节只有很弱的回位弹簧。
///   · 每条手臂只在"手"这一个点上施加 PD 力，追踪自由泳轨迹 —— 于是肘、肩会被水阻拖着走，
///     节奏乱了手臂就甩得乱七八糟，节奏好就是一条顺滑的弧线。
/// 这样做也为以后联机铺路：只需要同步 Z/速度/状态，四肢在每个客户端本地模拟，不会因物理误差失步。
/// </summary>
public class SwimmerBody : MonoBehaviour
{
    public const int BodyLayer = 8;

    public Swimmer S;
    public float LaneX;

    Transform _torso, _head, _headPivot;
    Rigidbody _torsoRb;
    Material _faceMat;
    Color _skin;
    Transform _ragRoot;

    class Limb
    {
        public Rigidbody Upper, Lower;
        public float UpperLen, LowerLen;
        public Vector3 RootLocal;   // 肩/髋在躯干局部坐标
        public int Side;            // -1 左 +1 右
        public float Phase = 1f;    // 手臂循环：0~0.55 划水，0.55~1 空中移臂，1 = 前伸待命
        public bool Splashed = true;
        public Vector3 Noise;       // 挣扎时的随机目标偏移
        public float NoiseT;
    }

    readonly Limb[] _arms = new Limb[2];
    readonly Limb[] _legs = new Limb[2];
    readonly List<Rigidbody> _all = new List<Rigidbody>();

    float _roll, _bob, _sinkY, _pitch;
    Vector3 _lastTorsoPos;
    Vector3 _torsoVel;
    float _kickT;
    float _shakeT;
    float _tumbleAng;                 // 被蹬飞时躯干向后翻滚的累计角度
    float _seenGrab = -99f, _seenKick = -99f, _seenTumble = -99f, _seenHit = -99f, _seenWhiff = -99f;
    float _dragTrailT;
    bool _hitReact;
    const float GrabDur = 0.38f, KickDur = 0.4f;

    ParticleSystem _splash, _bubbles, _foam;
    AudioSource _audio;
    TextMesh _nameTag;

    // ───── 被拽脚甩飞：布娃娃 ─────
    bool _ragdoll;                    // 躯干是否已交给物理
    bool _flingAirborne;              // 已经飞离水面（用来判断入水）
    float _flingSeen = -99f;
    Quaternion _blendFrom;            // 恢复后从布娃娃姿势混回泳姿
    Vector3 _blendFromPos;
    float _blendT = 1f;
    const float BlendDur = 0.35f;
    Transform _starRoot;
    readonly Transform[] _stars = new Transform[5];
    float _starsT, _starHideAt = -99f;
    bool _air;                        // 布娃娃此刻在水面以上
    int _stepSeen;                    // 上一帧看到的挣扎进度（按对一次往上蹿一下）
    float _flailT;
    float _limbGain = 1f;             // 四肢跟随力度：飞在空中时放软，甩得开
    /// <summary>布娃娃砸进水里。第二个参数是被砸到的倒霉蛋（没有就是 null）。</summary>
    public static System.Action<Swimmer, Swimmer> OnSplashDown;
    public bool IsRagdoll => _ragdoll;
    public bool Airborne => _ragdoll && _air;

    public void Build(Swimmer s, float laneX, Color capColor, Color suitColor, Color skin)
    {
        S = s; LaneX = laneX; _skin = skin;

        // ── 躯干（运动学）
        _torso = new GameObject("Torso").transform;
        _torso.SetParent(transform, false);
        _torso.gameObject.layer = BodyLayer;
        _torsoRb = _torso.gameObject.AddComponent<Rigidbody>();
        _torsoRb.isKinematic = true;
        _torsoRb.useGravity = false;
        _torsoRb.mass = 34f;                   // 比四肢加起来还重，被甩飞时是躯干带着四肢走
        _torsoRb.maxAngularVelocity = 40f;
        _torsoRb.interpolation = RigidbodyInterpolation.Interpolate;
        var tc = _torso.gameObject.AddComponent<CapsuleCollider>();
        tc.direction = 2; tc.radius = 0.2f; tc.height = 0.9f; tc.center = new Vector3(0, 0, 0.05f);

        var skinMat = Art.Mat(skin, 0.45f);
        var suitMat = Art.Mat(suitColor, 0.55f);
        // 胸背
        Art.Prim(PrimitiveType.Capsule, _torso, new Vector3(0, 0, 0.12f), new Vector3(0.48f, 0.42f, 0.28f), skinMat, Quaternion.Euler(90, 0, 0));
        // 泳裤
        Art.Prim(PrimitiveType.Capsule, _torso, new Vector3(0, -0.01f, -0.28f), new Vector3(0.4f, 0.2f, 0.27f), suitMat, Quaternion.Euler(90, 0, 0));

        // ── 头（视觉子物体，由代码转动：换气、脸变直、挣扎）
        _headPivot = new GameObject("Neck").transform;
        _headPivot.SetParent(_torso, false);
        _headPivot.localPosition = new Vector3(0, 0.02f, 0.5f);
        _head = new GameObject("Head").transform;
        _head.SetParent(_headPivot, false);
        _head.localPosition = new Vector3(0, 0, 0.13f);
        _faceMat = Art.NewMat(skin, 0.45f);
        Art.Prim(PrimitiveType.Sphere, _head, Vector3.zero, new Vector3(0.26f, 0.25f, 0.28f), _faceMat);
        // 泳帽
        var capMat = Art.Mat(capColor, 0.75f);
        Art.Prim(PrimitiveType.Sphere, _head, new Vector3(0, 0.035f, -0.02f), new Vector3(0.275f, 0.25f, 0.27f), capMat);
        // 泳镜（脸朝下前方 = -Y +Z）
        var goggle = Art.Mat(new Color(0.05f, 0.05f, 0.08f), 0.95f, 0.5f);
        var lens = Art.Mat(capColor * 0.7f + new Color(0.1f, 0.1f, 0.15f), 0.98f, 0.7f);
        Art.Prim(PrimitiveType.Cube, _head, new Vector3(0, -0.035f, 0.095f), new Vector3(0.27f, 0.025f, 0.04f), goggle, Quaternion.Euler(30, 0, 0));
        Art.Prim(PrimitiveType.Sphere, _head, new Vector3(-0.055f, -0.06f, 0.11f), new Vector3(0.075f, 0.055f, 0.05f), lens, Quaternion.Euler(30, 0, 0));
        Art.Prim(PrimitiveType.Sphere, _head, new Vector3(0.055f, -0.06f, 0.11f), new Vector3(0.075f, 0.055f, 0.05f), lens, Quaternion.Euler(30, 0, 0));
        // 嘴
        Art.Prim(PrimitiveType.Sphere, _head, new Vector3(0, -0.105f, 0.06f), new Vector3(0.06f, 0.025f, 0.04f), Art.Mat(new Color(0.45f, 0.12f, 0.12f)));

        // ── 四肢
        _ragRoot = new GameObject("Ragdoll_" + s.DisplayName).transform; // 不挂在运动物体下，避免层级拖拽刚体

        var armDir = new Vector3(0, -0.35f, 0.94f).normalized;
        var legDir = new Vector3(0, -0.06f, -1f).normalized;
        for (int i = 0; i < 2; i++)
        {
            int side = i == 0 ? -1 : 1;
            _arms[i] = MakeLimb(new Vector3(0.23f * side, 0.02f, 0.4f), side, armDir, 0.31f, 0.36f, 0.062f, 0.052f, 2.2f, 1.6f, skinMat, skinMat, true);
            _legs[i] = MakeLimb(new Vector3(0.11f * side, -0.03f, -0.36f), side, legDir, 0.42f, 0.46f, 0.085f, 0.065f, 4f, 3f, suitMat, skinMat, false);
        }

        // ── 粒子 & 声音
        _splash = Fx.MakeSplash(transform);
        _foam = Fx.MakeFoam(transform);
        _bubbles = Fx.MakeBubbles(transform);
        _audio = _torso.gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = s.IsPlayer ? 0.2f : 0.9f;
        _audio.minDistance = 3f; _audio.maxDistance = 30f;
        _audio.volume = s.IsPlayer ? 0.9f : 0.35f;

        // ── 名字
        if (!s.IsPlayer)
        {
            _nameTag = Art.Label(transform, s.DisplayName, Vector3.zero, 0.22f, new Color(1, 1, 1, 0.9f));
        }

        s.OnStroke += OnStroke;
        s.OnBreath += r => { if (S.IsPlayer || r == BreathResult.Choke) Sfx.Play(_audio, r == BreathResult.Choke ? Sfx.Choke : Sfx.Breath, r == BreathResult.Perfect ? 1.15f : 1f); };
        s.OnStatus += st =>
        {
            if (st == "drown") { _bubbles.Play(); Sfx.Play(_audio, Sfx.Choke, 0.7f); }
            else if (st == "choke") OnChoke();
            else if (st == "recover") OnRecovered();
        };

        // ── 头顶转圈的小星星（被甩晕了）
        _starRoot = new GameObject("Stars").transform;
        _starRoot.SetParent(transform, false);
        var starMat = Art.Mat(Art.Hex("#ffd60a"), 0.9f);
        for (int i = 0; i < _stars.Length; i++)
        {
            float a = i * Mathf.PI * 2f / _stars.Length;
            _stars[i] = Art.Prim(PrimitiveType.Sphere, _starRoot, new Vector3(Mathf.Cos(a) * 0.28f, 0, Mathf.Sin(a) * 0.28f), Vector3.one * 0.085f, starMat).transform;
        }
        _starRoot.gameObject.SetActive(false);

        PlaceAll(true);
    }

    Limb MakeLimb(Vector3 rootLocal, int side, Vector3 dir, float l1, float l2, float r1, float r2, float m1, float m2,
        Material mat1, Material mat2, bool isArm)
    {
        var limb = new Limb { RootLocal = rootLocal, Side = side, UpperLen = l1, LowerLen = l2 };
        Vector3 rootW = _torso.TransformPoint(rootLocal);
        Vector3 dW = _torso.TransformDirection(dir);
        if (isArm) dW = Quaternion.AngleAxis(8f * side, _torso.up) * dW;
        limb.Upper = MakeSeg((isArm ? "UpperArm" : "Thigh") + side, rootW, rootW + dW * l1, r1, m1, mat1, _torsoRb, isArm ? Tuning.ShoulderSpring : Tuning.JointSpring, isArm ? 180f : 60f);
        limb.Lower = MakeSeg((isArm ? "Forearm" : "Shin") + side, rootW + dW * l1, rootW + dW * (l1 + l2), r2, m2, mat2, limb.Upper, Tuning.JointSpring, 130f);
        // 手掌 / 脚掌
        var tip = Art.Prim(PrimitiveType.Sphere, limb.Lower.transform, new Vector3(0, l2 * 0.5f, 0),
            isArm ? new Vector3(0.1f, 0.12f, 0.05f) : new Vector3(0.1f, 0.2f, 0.06f), isArm ? mat2 : mat2);
        tip.name = isArm ? "Hand" : "Foot";
        return limb;
    }

    Rigidbody MakeSeg(string name, Vector3 a, Vector3 b, float radius, float mass, Material mat, Rigidbody parent, float spring, float limitX)
    {
        float len = Vector3.Distance(a, b);
        var go = new GameObject(name) { layer = BodyLayer };
        go.transform.SetParent(_ragRoot, false);
        go.transform.position = (a + b) * 0.5f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        Art.Prim(PrimitiveType.Capsule, go.transform, Vector3.zero, new Vector3(radius * 2f, (len + radius * 2f) * 0.5f, radius * 2f), mat);
        var col = go.AddComponent<CapsuleCollider>();
        col.direction = 1; col.radius = radius; col.height = len + radius;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.useGravity = false;                 // 浮力 ≈ 重力
        rb.linearDamping = Tuning.LimbDrag;    // 水阻
        rb.angularDamping = Tuning.LimbDrag * 1.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.solverIterations = 16;
        rb.solverVelocityIterations = 4;
        rb.maxAngularVelocity = 30f;

        var j = go.AddComponent<ConfigurableJoint>();
        j.connectedBody = parent;
        j.anchor = new Vector3(0, -len * 0.5f, 0);
        j.autoConfigureConnectedAnchor = true;
        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
        j.axis = Vector3.right;
        j.secondaryAxis = Vector3.up;
        j.angularXMotion = ConfigurableJointMotion.Limited;
        j.angularYMotion = ConfigurableJointMotion.Limited;
        j.angularZMotion = ConfigurableJointMotion.Limited;
        j.lowAngularXLimit = new SoftJointLimit { limit = -limitX };
        j.highAngularXLimit = new SoftJointLimit { limit = limitX };
        j.angularYLimit = new SoftJointLimit { limit = limitX > 150f ? 80f : 25f };
        j.angularZLimit = new SoftJointLimit { limit = limitX > 150f ? 80f : 25f };
        j.rotationDriveMode = RotationDriveMode.Slerp;
        j.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = spring * 0.08f + 0.5f, maximumForce = 1e6f };
        j.enablePreprocessing = false;
        _all.Add(rb);
        return rb;
    }

    void OnStroke(StrokeEvent e)
    {
        var arm = _arms[e.Hand];
        arm.Phase = 0f;
        arm.Splashed = false;
        float pitch = e.Judge == Judge.Perfect ? 1.1f : e.Judge == Judge.Good ? 1f : e.Judge == Judge.Fumble || e.Judge == Judge.Mash ? 0.7f : 0.9f;
        Sfx.Play(_audio, Sfx.Splash, pitch * Random.Range(0.95f, 1.05f));
    }

    /// <summary>重置后把整个布娃娃瞬移回躯干旁边。</summary>
    public void PlaceAll(bool snap)
    {
        _sinkY = 0; _roll = 0; _pitch = 0; _tumbleAng = 0;
        if (!_torsoRb.isKinematic)
        {
            _torsoRb.linearVelocity = Vector3.zero;
            _torsoRb.angularVelocity = Vector3.zero;
            _torsoRb.isKinematic = true;
        }
        _torsoRb.useGravity = false;
        _ragdoll = false; _air = false; _flingAirborne = false; _blendT = 1f; _limbGain = 1f;
        _flingSeen = S.FlingTime; _starHideAt = -99f;
        if (_starRoot != null) _starRoot.gameObject.SetActive(false);
        Vector3 p = new Vector3(S.X, -0.06f, S.Z);
        Vector3 delta = p - _torso.position;
        _torso.position = p;
        _torso.rotation = Quaternion.identity;
        _torsoRb.position = p;
        _torsoRb.rotation = Quaternion.identity;
        _lastTorsoPos = p;
        foreach (var rb in _all)
        {
            rb.position += delta;
            rb.transform.position = rb.position;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        for (int i = 0; i < 2; i++) { _arms[i].Phase = 1f; _arms[i].Splashed = true; }
        if (_bubbles) _bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public Vector3 TorsoPos => _torso.position;

    // ═════════════════ 物理步 ═════════════════
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float t = Time.time;

        // ── 被拽脚甩飞：躯干交给物理，这一步不走下面的规则驱动
        if (RagdollStep(dt, t)) return;

        // ── 躯干位姿
        float rollTarget = 0f;
        for (int i = 0; i < 2; i++)
            if (_arms[i].Phase < 0.55f) rollTarget += _arms[i].Side * Tuning.RollAmp * Mathf.Sin(_arms[i].Phase / 0.55f * Mathf.PI);
        if (S.BreathTier == 2 && S.Active) rollTarget += Mathf.Sin(t * 13f) * 25f;
        // 换道横移：身体朝移动方向侧倾
        float dxLane = PoolBuilder.LaneX(S.LaneTarget) - S.X;
        if (Mathf.Abs(dxLane) > 0.05f) rollTarget += -Mathf.Sign(dxLane) * 22f;
        _roll = Mathf.Lerp(_roll, rollTarget, 1f - Mathf.Exp(-8f * dt));

        float pitchTarget = S.BreathTier == 1 ? -10f : S.BreathTier == 2 ? -24f : 0f;
        if (S.Drowned) pitchTarget = -40f;
        _pitch = Mathf.Lerp(_pitch, pitchTarget, 1f - Mathf.Exp(-4f * dt));

        _bob = Mathf.Sin(t * 2.1f + LaneX) * 0.025f;
        if (S.Drowned) _sinkY = Mathf.MoveTowards(_sinkY, -1.6f, 0.7f * dt);
        else _sinkY = Mathf.MoveTowards(_sinkY, S.BreathTier == 2 ? -0.12f : 0f, 0.6f * dt);

        Vector3 pos = new Vector3(S.X, -0.06f + _bob + _sinkY, S.Z);

        // ── 被一拽一蹬：向后翻跟头（绕 X 轴后滚），翻完回正
        bool tumbling = S.Tumbling;
        if (tumbling)
        {
            if (_seenTumble != S.TumbleUntil)
            {
                _seenTumble = S.TumbleUntil;
                Fx.Burst(_splash, new Vector3(pos.x, 0.05f, pos.z), 40);
            }
            _tumbleAng -= 520f * dt;
            pos.y += 0.12f * Mathf.Abs(Mathf.Sin(_tumbleAng * Mathf.Deg2Rad));
            if (_bubbles != null && !_bubbles.isEmitting) _bubbles.Play();
            _roll += Mathf.Sin(t * 9f) * 40f * dt;
        }
        else if (_tumbleAng != 0f)
        {
            if (_bubbles != null && !S.Drowned && _bubbles.isEmitting) _bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            // 回到最近的整圈
            float targetAng = Mathf.Round(_tumbleAng / 360f) * 360f;
            _tumbleAng = Mathf.MoveTowards(_tumbleAng, targetAng, 400f * dt);
            if (Mathf.Approximately(_tumbleAng, targetAng)) _tumbleAng = 0f;
        }
        Quaternion rot = Quaternion.Euler(_pitch + _tumbleAng, 0, _roll);

        // ── 普通被拽 / 被蹬 / 抓空：短而夸张的受击姿势
        float hs = t - S.HitTime;
        _hitReact = !tumbling && hs >= 0f && hs < Tuning.HitReactTime;
        if (_hitReact)
        {
            float k = Mathf.Sin(Mathf.Clamp01(hs / Tuning.HitReactTime) * Mathf.PI);   // 0→1→0
            float shake = Mathf.Sin(hs * 38f) * (1f - hs / Tuning.HitReactTime);
            if (S.HitKind == 0)
            {
                // 被拽脚：脚被往后拖，头被拉得翘起、身体后仰下沉，左右乱扭
                rot = Quaternion.Euler(_pitch - 38f * k, shake * 14f, _roll + shake * 35f);
                pos.y -= 0.22f * k;
            }
            else
            {
                // 被蹬：屁股被踹，整个人向前栽头扎水再弹起，侧翻
                rot = Quaternion.Euler(_pitch + 55f * k, shake * 10f, _roll + 70f * k * Mathf.Sign(LaneX + 0.01f));
                pos.y += 0.12f * k;
            }
            if (_seenHit != S.HitTime)
            {
                _seenHit = S.HitTime;
                Fx.Burst(_splash, new Vector3(pos.x, 0.05f, pos.z + (S.HitKind == 0 ? -0.9f : -0.4f)), S.IsPlayer ? 60 : 45);
                Fx.Burst(_splash, new Vector3(pos.x, 0.05f, pos.z + 0.3f), 25);
                if (_bubbles != null) _bubbles.Emit(25);
                Sfx.Play(_audio, Sfx.Thump, Random.Range(0.9f, 1.15f), S.IsPlayer ? 0.4f : 0.9f);
            }
        }

        // 抓空：自己往前扑空，原地横滚一圈
        float ws = t - S.WhiffTime;
        if (!tumbling && ws >= 0f && ws < 0.7f)
        {
            float u = ws / 0.7f;
            rot = Quaternion.Euler(_pitch + 20f * Mathf.Sin(u * Mathf.PI), 0, _roll + 360f * Mathf.SmoothStep(0, 1, u));
            if (_seenWhiff != S.WhiffTime) { _seenWhiff = S.WhiffTime; Fx.Burst(_splash, new Vector3(pos.x, 0.05f, pos.z + 0.8f), 30); }
        }

        // 被往后拖：拖出一条水痕（越快越密），让「被拽回去多远」看得见
        if (S.Shove < -0.8f)
        {
            _dragTrailT -= dt;
            if (_dragTrailT <= 0f)
            {
                _dragTrailT = 0.035f;
                int n = Mathf.Clamp(Mathf.RoundToInt(-S.Shove * 2.5f), 3, 18);
                Fx.Burst(_splash, new Vector3(pos.x + Random.Range(-0.3f, 0.3f), 0.04f, pos.z + 0.9f), n);
                if (_bubbles != null) _bubbles.Emit(3);
            }
            pos.y -= Mathf.Clamp01(-S.Shove / 6f) * 0.15f;   // 被拖时半沉
        }

        // 拽/蹬瞬间溅一大片水花
        if (S.GrabAnimTime != _seenGrab && t >= S.GrabAnimTime && t - S.GrabAnimTime < 0.1f)
        {
            _seenGrab = S.GrabAnimTime;
            Fx.Burst(_splash, new Vector3(pos.x, 0.03f, pos.z + 1.0f), 45);
        }
        if (S.KickAnimTime != _seenKick && t >= S.KickAnimTime + 0.12f && t - S.KickAnimTime < 0.3f)
        {
            _seenKick = S.KickAnimTime;
            Fx.Burst(_splash, new Vector3(pos.x, 0.03f, pos.z - 1.3f), 55);
        }
        // 刚从布娃娃爬回来：从乱七八糟的姿势混回泳姿
        if (_blendT < 1f)
        {
            _blendT = Mathf.Min(1f, _blendT + dt / BlendDur);
            float bk = Mathf.SmoothStep(0f, 1f, _blendT);
            pos = Vector3.Lerp(_blendFromPos, pos, bk);
            rot = Quaternion.Slerp(_blendFrom, rot, bk);
        }
        _torsoRb.MovePosition(pos);
        _torsoRb.MoveRotation(rot);
        _torsoVel = (pos - _lastTorsoPos) / dt;
        _lastTorsoPos = pos;

        LimbsStep(dt, t, pos, rot, tumbling);
    }

    // ═════════════════ 布娃娃：被拽脚甩飞 ═════════════════
    /// <summary>返回 true = 这一步躯干归物理管。</summary>
    bool RagdollStep(float dt, float t)
    {
        bool want = Tuning.FoulRoom && S.Ragdolled && !S.Finished && !S.Drowned;
        if (S.FlingTime != _flingSeen)
        {
            _flingSeen = S.FlingTime;
            if (want) BeginFling(t);
        }
        if (_ragdoll && !want) { EndRagdoll(); return false; }
        if (!_ragdoll) return false;

        var rb = _torsoRb;
        Vector3 p = rb.position;
        Vector3 v = rb.linearVelocity;
        _air = p.y > 0.2f;
        if (p.y > 0.45f) _flingAirborne = true;

        // 空中：加重力，让滞空时间 ≈ FlingFlight（不然飞得又高又慢，没有「被甩出去」的劲）
        float gWant = 2f * Tuning.FlingUp * Tuning.FlingPower / Mathf.Max(0.3f, Tuning.FlingFlight * 0.9f);
        float extraG = Mathf.Max(0f, gWant - 9.81f);
        if (p.y > 0f) rb.AddForce(Vector3.down * extraG, ForceMode.Acceleration);

        // 入水
        if (_flingAirborne && p.y < 0.05f && v.y < 0f) SplashDown(t, p, v);
        if (!S.Landed && t >= S.FlungUntil + 0.6f) { S.Landed = true; S.LandTime = t; }

        // 水里：浮力（把人托回水面附近，呛水时往下沉）+ 水阻
        float sub = Mathf.Clamp01((0.15f - p.y) / 0.3f);
        if (sub > 0f)
        {
            bool choking = t < S.RecoverPenaltyUntil;
            float floatY = choking ? -0.95f : -0.1f + Mathf.Sin(t * 5f + LaneX) * 0.06f;
            float up = 9.81f + (floatY - p.y) * 20f - v.y * 5.5f;
            rb.AddForce(Vector3.up * up * sub, ForceMode.Acceleration);
            float k = Mathf.Exp(-3.2f * sub * dt);
            v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * k, v.y, v.z * k);
            rb.angularVelocity *= Mathf.Exp(-2.4f * sub * dt);

            // 挣扎：乱扭 + 慢慢翻回肚皮朝下，按对的键越多翻得越正
            _flailT -= dt;
            if (_flailT <= 0f)
            {
                _flailT = Random.Range(0.12f, 0.28f);
                rb.AddTorque(Random.onUnitSphere * (choking ? 9f : 5f), ForceMode.VelocityChange);
                Fx.Burst(_splash, new Vector3(p.x + Random.Range(-0.5f, 0.5f), 0.03f, p.z + Random.Range(-0.6f, 0.6f)), S.IsPlayer ? 14 : 8);
            }
            float prog = S.RecoverNeed > 0 ? Mathf.Clamp01((float)S.RecoverStep / S.RecoverNeed) : 0f;
            Quaternion.Inverse(rb.rotation).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                rb.AddTorque(axis * ang * Mathf.Deg2Rad * Mathf.Lerp(2f, 16f, prog) * sub, ForceMode.Acceleration);

            if (_bubbles != null && p.y < -0.25f) _bubbles.Emit(2);
        }

        // 按对一个键：整个人往上蹿一下
        if (S.RecoverStep != _stepSeen)
        {
            if (S.RecoverStep > _stepSeen)
            {
                rb.AddForce(Vector3.up * 2.6f, ForceMode.VelocityChange);
                Fx.Burst(_splash, new Vector3(p.x, 0.04f, p.z), 30);
            }
            _stepSeen = S.RecoverStep;
        }

        // 泳池没有碰撞体：手动挡住池底和池壁（撞墙弹回来）
        v = rb.linearVelocity;
        bool fix = false;
        if (p.y < -2.0f) { p.y = -2.0f; if (v.y < 0f) v.y = -v.y * 0.3f; fix = true; }
        if (p.x > 9.5f) { p.x = 9.5f; if (v.x > 0f) v.x = -v.x * 0.55f; fix = true; }
        if (p.x < -9.5f) { p.x = -9.5f; if (v.x < 0f) v.x = -v.x * 0.55f; fix = true; }
        if (p.z < 0.4f) { p.z = 0.4f; if (v.z < 0f) v.z = -v.z * 0.4f; fix = true; }
        if (p.z > Tuning.Distance) { p.z = Tuning.Distance; if (v.z > 0f) v.z = 0f; fix = true; }
        if (fix) { rb.position = p; rb.linearVelocity = v; }

        // 四肢：空中没有浮力，跟着重力甩；水里和平时一样悬浮
        foreach (var l in _all)
        {
            bool above = l.position.y > 0.05f;
            if (l.useGravity != above) l.useGravity = above;
            if (above && extraG > 0f) l.AddForce(Vector3.down * extraG, ForceMode.Acceleration);
        }
        _limbGain = _air ? 0.3f : 0.8f;

        // 逻辑坐标跟着身体走（排名、被谁砸到、爬回来后在哪条道，都以身体为准）
        S.X = p.x;
        S.Z = Mathf.Clamp(p.z, 0f, Tuning.Distance);
        S.LandX = p.x; S.LandZ = p.z;
        S.Lane = Mathf.Clamp(Mathf.RoundToInt(p.x / Tuning.LaneWidth + (Tuning.LaneCount - 1) * 0.5f), 0, Tuning.LaneCount - 1);

        _torsoVel = rb.linearVelocity;
        _lastTorsoPos = p;
        LimbsStep(dt, t, p, rb.rotation, false);
        return true;
    }

    void BeginFling(float t)
    {
        var rb = _torsoRb;
        _ragdoll = true; _flingAirborne = false; _air = false; _blendT = 1f;
        _stepSeen = S.RecoverStep; _flailT = 0.4f;
        _tumbleAng = 0f; _sinkY = 0f; _hitReact = false;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearDamping = 0.02f;
        rb.angularDamping = 0.05f;

        float pw = Tuning.FlingPower;
        Vector3 v = new Vector3(S.FlySide * Tuning.FlingSide * pw * Random.Range(0.75f, 1.1f),
                                Tuning.FlingUp * pw,
                                Random.Range(-2.2f, -0.4f) * pw);
        rb.linearVelocity = v;
        // 以后空翻为主（脚被往后拽 → 头朝前栽），再加一点横滚，每次都不一样
        float spin = Tuning.FlingSpin * Mathf.Deg2Rad;
        rb.angularVelocity = new Vector3(-spin * Random.Range(0.6f, 1f), spin * Random.Range(-0.35f, 0.35f), -S.FlySide * spin * Random.Range(0.25f, 0.7f));
        foreach (var l in _all)
        {
            l.linearVelocity = v + Random.insideUnitSphere * 3f;
            l.angularVelocity = Random.insideUnitSphere * 12f;
        }

        Vector3 p = rb.position;
        Fx.Burst(_splash, new Vector3(p.x, 0.05f, p.z), S.IsPlayer ? 90 : 70);
        Fx.Burst(_splash, new Vector3(p.x, 0.05f, p.z - 0.9f), 50);
        if (_bubbles != null) _bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Sfx.Play(_audio, Sfx.Whoosh, Random.Range(0.9f, 1.15f), 1f);
        Sfx.Play(_audio, Sfx.Scream, Random.Range(0.8f, 1.35f), S.IsPlayer ? 0.8f : 1f);
        if (_starRoot != null) _starRoot.gameObject.SetActive(true);
        _starHideAt = float.MaxValue;
    }

    void SplashDown(float t, Vector3 p, Vector3 v)
    {
        _flingAirborne = false;
        bool first = !S.Landed;
        S.Landed = true; S.LandTime = t;
        float hard = Mathf.Clamp01(-v.y / 8f);
        int n = Mathf.RoundToInt(Mathf.Lerp(40f, 130f, hard));
        Fx.Burst(_splash, new Vector3(p.x, 0.06f, p.z), n);
        Fx.Burst(_splash, new Vector3(p.x + 0.5f, 0.06f, p.z), n / 3);
        Fx.Burst(_splash, new Vector3(p.x - 0.5f, 0.06f, p.z), n / 3);
        if (_bubbles != null) _bubbles.Emit(30);
        Sfx.Play(_audio, Sfx.Splat, Random.Range(0.85f, 1.1f), Mathf.Lerp(0.5f, 1f, hard));
        if (!first) return;

        // 砸到人：被砸的呛一口水、往后顿一下
        Swimmer hit = null;
        if (Tuning.FlingChainKnock >= 0.5f && Foul.All != null)
        {
            foreach (var o in Foul.All)
            {
                if (o == null || o == S || o.Finished || o.Drowned || !o.Started) continue;
                if (o.Flung || o.Ragdolled || t < o.ImmuneUntil) continue;
                if (Mathf.Abs(o.X - p.x) > 1.3f || Mathf.Abs(o.Z - p.z) > 1.7f) continue;
                o.HitTime = t; o.HitKind = 1;
                o.StunUntil = Mathf.Max(o.StunUntil, t + 0.7f);
                o.ImmuneUntil = t + Tuning.FoulImmune;
                o.Shove -= 1.6f;
                o.Flow *= 0.6f;
                o.V *= 0.5f;
                hit = o;
            }
        }
        OnSplashDown?.Invoke(S, hit);
    }

    void OnChoke()
    {
        if (!_ragdoll) return;
        // 按错键：呛一大口，整个人往下栽
        _torsoRb.AddForce(Vector3.down * 2.8f, ForceMode.VelocityChange);
        _torsoRb.AddTorque(Random.onUnitSphere * 10f, ForceMode.VelocityChange);
        if (_bubbles != null) _bubbles.Emit(45);
        Vector3 p = _torsoRb.position;
        Fx.Burst(_splash, new Vector3(p.x, 0.04f, p.z), 35);
        Sfx.Play(_audio, Sfx.Choke, Random.Range(1.1f, 1.5f), S.IsPlayer ? 0.9f : 0.6f);
    }

    void OnRecovered()
    {
        Vector3 p = _torso.position;
        Fx.Burst(_splash, new Vector3(p.x, 0.05f, p.z), 60);
        Sfx.Play(_audio, Sfx.Splash, 0.8f, 1f);
        _starHideAt = Time.time + 0.6f;
    }

    void EndRagdoll()
    {
        var rb = _torsoRb;
        _blendFrom = rb.rotation;
        _blendFromPos = rb.position;
        _blendT = 0f;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        _ragdoll = false; _air = false; _flingAirborne = false; _limbGain = 1f;
        _lastTorsoPos = _blendFromPos;
        _torsoVel = Vector3.zero;
        _roll = 0f; _pitch = 0f; _tumbleAng = 0f; _sinkY = 0f;
        _seenTumble = S.TumbleUntil;
        foreach (var l in _all) l.useGravity = false;
        if (_starHideAt == float.MaxValue) _starHideAt = Time.time + 0.6f;
    }

    void LimbsStep(float dt, float t, Vector3 pos, Quaternion rot, bool tumbling)
    {
        // 关节参数实时读取调参面板（布娃娃飞在空中时几乎没有阻力，四肢甩得更开）
        float drag = Tuning.LimbDrag;
        if (_ragdoll && _torso.position.y > 0.15f) drag = 0.15f;
        foreach (var rb in _all)
        {
            rb.linearDamping = drag;
            rb.angularDamping = drag * 1.5f;
        }

        // ── 手臂
        float pullDur = Mathf.Clamp(Tuning.IdealInterval * 0.95f, 0.18f, 0.8f);
        float recDur = Mathf.Clamp(Tuning.IdealInterval * 0.95f, 0.18f, 0.8f);
        for (int i = 0; i < 2; i++)
        {
            var a = _arms[i];
            if (a.Phase < 0.55f) a.Phase += dt * 0.55f / pullDur;          // 水下划水
            else if (a.Phase < 1f) a.Phase += dt * 0.45f / recDur;         // 空中移臂
            if (a.Phase > 1f) a.Phase = 1f;                                // 前伸待命

            Vector3 local = ArmTarget(a, t);
            Vector3 targetW = rot * local + pos;
            DriveTip(a.Lower, a.LowerLen, a.Upper.mass + a.Lower.mass, targetW, Tuning.HandKp * _limbGain, Tuning.HandKd);

            // 入水溅水花
            if (!a.Splashed && a.Phase > 0.04f)
            {
                a.Splashed = true;
                Vector3 hp = TipPos(a.Lower, a.LowerLen);
                Fx.Burst(_splash, new Vector3(hp.x, 0.02f, hp.z), S.IsPlayer ? 18 : 8);
            }
        }

        // ── 腿：打腿
        bool swimming = S.Active && S.Stroking;
        float kickHz = swimming ? 0.5f / Mathf.Max(0.15f, Tuning.IdealInterval) * 1.5f : 0.8f;
        float kickAmp = swimming ? 0.17f : (S.Active ? 0.06f : 0.02f);
        if (S.Sprinting) { kickHz *= 1.35f; kickAmp *= 1.25f; }
        if (S.Drowned || S.BreathTier == 2 || tumbling || _hitReact || _ragdoll) { kickHz = 2.6f; kickAmp = 0.35f; }
        _kickT += dt * kickHz * Mathf.PI * 2f;
        float ks = t - S.KickAnimTime;
        for (int i = 0; i < 2; i++)
        {
            var l = _legs[i];
            float off = Mathf.Sin(_kickT + (i == 0 ? 0 : Mathf.PI)) * kickAmp;
            float len = (l.UpperLen + l.LowerLen);
            Vector3 local = l.RootLocal + new Vector3(0.02f * l.Side, -0.14f + off, -len * 0.97f);
            if (ks >= 0f && ks < KickDur && !tumbling)
            {
                // 蹬人：先收腿（膝盖顶到胸下），再双脚并拢猛地向后蹬直
                if (ks < 0.12f) local = l.RootLocal + new Vector3(0.05f * l.Side, -0.45f, -len * 0.25f);
                else local = l.RootLocal + new Vector3(0.0f, -0.05f, -len * 1.15f);
            }
            // 飞在空中：四仰八叉，腿劈开
            if (_ragdoll && _air) local = l.RootLocal + new Vector3(0.38f * l.Side, off * 1.5f, -len * 0.85f);
            float kp = ((ks >= 0f && ks < KickDur) ? Tuning.KickKp * 2.5f : Tuning.KickKp) * _limbGain;
            Vector3 targetW = rot * local + pos;
            DriveTip(l.Lower, l.LowerLen, l.Upper.mass + l.Lower.mass, targetW, kp, 7f);
        }

        // 脚下白沫
        if (_foam != null)
        {
            var em = _foam.emission;
            em.rateOverTime = S.Active ? Mathf.Clamp(S.V * (S.Sprinting ? 18f : 9f), 0, 50) : 0;
            Vector3 fp = rot * new Vector3(0, 0, -1.15f) + pos;
            _foam.transform.position = new Vector3(fp.x, 0.03f, fp.z);
        }
        if (_bubbles != null) _bubbles.transform.position = _head.position;
    }

    Vector3 ArmTarget(Limb a, float t)
    {
        int sg = a.Side;
        Vector3 s = a.RootLocal;
        // 自由泳关键点（躯干局部）
        Vector3 E = s + new Vector3(sg * 0.03f, -0.12f, 0.6f);    // 入水/抓水：前伸
        Vector3 M = s + new Vector3(-sg * 0.1f, -0.5f, 0.08f);    // 胸下划水
        Vector3 X = s + new Vector3(sg * 0.06f, -0.2f, -0.58f);   // 出水：大腿旁
        Vector3 R = s + new Vector3(sg * 0.3f, 0.42f, -0.02f);    // 空中移臂：高肘

        if (S.Drowned || (S.BreathTier == 2 && S.Active) || S.Tumbling || _hitReact || _ragdoll)
        {
            // 挣扎 / 翻滚：双手乱拍
            a.NoiseT -= Time.fixedDeltaTime;
            // 飞在空中：大字型张开
            if (_ragdoll && _air) return s + new Vector3(sg * 0.7f, 0.12f, 0.2f) + a.Noise * 0.35f;
            if (a.NoiseT <= 0f)
            {
                a.NoiseT = Random.Range(0.12f, 0.3f);
                a.Noise = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.2f, 0.7f), Random.Range(-0.2f, 0.5f));
            }
            return s + new Vector3(sg * 0.25f, 0, 0) + a.Noise;
        }
        if (S.Finished)
        {
            // 到终点：一只手高举庆祝（赢了双手）
            bool both = S.FinishOrder == 0;
            if (a.Side == 1 || both) return s + new Vector3(sg * 0.15f, 0.75f + Mathf.Sin(t * 8f + sg) * 0.08f, 0.15f);
            return s + new Vector3(sg * 0.1f, -0.3f, 0.35f);
        }

        // 拽脚：右手（组合技时双手）猛地往前下方一抓，再往回拉到腰边
        float gs = t - S.GrabAnimTime;
        if (gs >= 0f && gs < GrabDur && (a.Side == 1 || S.KickAnimTime > S.GrabAnimTime))
        {
            Vector3 reach = s + new Vector3(sg * 0.05f, -0.35f, 1.05f);
            Vector3 pull = s + new Vector3(sg * 0.15f, -0.25f, -0.45f);
            return gs < 0.14f ? reach : Vector3.Lerp(reach, pull, (gs - 0.14f) / (GrabDur - 0.14f));
        }

        float p = a.Phase;
        if (p < 0.55f)
        {
            float u = p / 0.55f;
            return Bez(E, M, X, u);
        }
        if (p < 1f)
        {
            float u = (p - 0.55f) / 0.45f;
            return Bez(X, R, E, u);
        }
        return E;
    }

    static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float u)
    {
        float v = 1f - u;
        return v * v * a + 2f * v * u * b + u * u * c;
    }

    static Vector3 TipPos(Rigidbody seg, float len) => seg.position + seg.rotation * new Vector3(0, len * 0.5f, 0);

    void DriveTip(Rigidbody seg, float len, float mass, Vector3 target, float kp, float kd)
    {
        Vector3 tip = TipPos(seg, len);
        Vector3 rel = seg.GetPointVelocity(tip) - _torsoVel;
        Vector3 acc = (target - tip) * kp - rel * kd;
        acc = Vector3.ClampMagnitude(acc, 400f);
        seg.AddForceAtPosition(acc * mass, tip, ForceMode.Force);
    }

    // ═════════════════ 视觉（头部 / 脸色 / 名牌） ═════════════════
    void LateUpdate()
    {
        float t = Time.time;
        // 头部朝向
        float yaw = 0, rollH = 0, pitchH = 18f; // 默认脸朝下前方
        float since = t - S.BreathAnimTime;
        if (since < 0.45f && !S.Drowned)
        {
            float k = Mathf.Sin(Mathf.Clamp01(since / 0.45f) * Mathf.PI);
            rollH = S.BreathSide * 75f * k;   // 侧头吸气
            pitchH = Mathf.Lerp(18f, 0f, k);
        }
        if (S.BreathTier == 1) { pitchH = -35f; }           // 脸变直：抬头看前方
        if (S.BreathTier == 2 || S.Drowned)
        {
            _shakeT += Time.deltaTime * 25f;
            pitchH = -55f + Mathf.Sin(_shakeT * 0.7f) * 10f;
            yaw = Mathf.Sin(_shakeT) * 25f;
        }
        if (_ragdoll)
        {
            // 被甩晕：脑袋乱晃
            _shakeT += Time.deltaTime * 30f;
            pitchH = -30f + Mathf.Sin(_shakeT * 0.8f) * 25f;
            yaw = Mathf.Sin(_shakeT) * 40f;
            rollH = Mathf.Sin(_shakeT * 0.6f) * 20f;
        }
        _headPivot.localRotation = Quaternion.Slerp(_headPivot.localRotation, Quaternion.Euler(pitchH, yaw, -rollH), 1f - Mathf.Exp(-14f * Time.deltaTime));

        // 脸色：正常 → 脸红 → 发紫
        Color face = _skin;
        if (S.Started && !S.Finished)
        {
            if (S.Drowned) face = new Color(0.45f, 0.35f, 0.75f);
            else if (S.BreathTier == 2) face = Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(1f, 0.05f, 0.05f), Mathf.PingPong(t * 4f, 1f));
            else if (S.BreathTier == 1) face = Color.Lerp(_skin, new Color(0.95f, 0.45f, 0.35f), 0.5f);
            else if (S.Breath < 30f) face = Color.Lerp(new Color(0.95f, 0.55f, 0.45f), _skin, S.Breath / 30f);
        }
        if (_ragdoll)
        {
            // 呛水：红得发紫一闪一闪；晕着：脸发绿
            if (t < S.RecoverPenaltyUntil) face = Color.Lerp(new Color(1f, 0.1f, 0.1f), new Color(0.6f, 0.15f, 0.7f), Mathf.PingPong(t * 9f, 1f));
            else if (_air) face = new Color(1f, 0.95f, 0.85f);
            else face = Color.Lerp(_skin, new Color(0.55f, 0.8f, 0.45f), 0.65f);
        }
        _faceMat.color = Color.Lerp(_faceMat.color, face, 1f - Mathf.Exp(-(_ragdoll ? 16f : 8f) * Time.deltaTime));

        // 头顶转星星
        if (_starRoot != null && _starRoot.gameObject.activeSelf)
        {
            if (t >= _starHideAt) _starRoot.gameObject.SetActive(false);
            else
            {
                _starsT += Time.deltaTime;
                _starRoot.position = _head.position + Vector3.up * 0.34f;
                _starRoot.rotation = Quaternion.Euler(12f * Mathf.Sin(_starsT * 3f), _starsT * 420f, 0);
            }
        }

        if (_nameTag != null)
        {
            var cam = Camera.main;
            _nameTag.transform.position = _torso.position + new Vector3(0, 0.85f, 0.3f);
            if (cam != null) _nameTag.transform.rotation = cam.transform.rotation;
            _nameTag.text = _ragdoll ? (_air ? S.DisplayName + " 啊啊啊——" : S.DisplayName + " 咕噜咕噜") : S.Drowned ? S.DisplayName + " 沉底了" : S.Finished ? $"{S.DisplayName} 第{S.FinishOrder + 1}" : S.DisplayName;
        }
    }

    void OnDestroy()
    {
        if (_ragRoot != null) Destroy(_ragRoot.gameObject);
        if (_faceMat != null) Destroy(_faceMat);
    }
}
