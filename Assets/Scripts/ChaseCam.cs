using UnityEngine;

/// <summary>第三人称追尾相机：速度越快拉得越远、FOV 越大；PERFECT 轻微脉冲；沉底时下潜。</summary>
public class ChaseCam : MonoBehaviour
{
    public Swimmer Target;
    public SwimmerBody Body;
    public float Height = 3.1f, Back = 5.2f, LookAhead = 4.5f;

    Camera _cam;
    float _shake;
    float _pulse;
    Vector3 _vel;
    public bool Intro;      // 开场环绕镜头
    float _introT;

    [Header("互坑：被甩飞镜头")]
    public float FlingUp = 3.5f, FlingBack = 3f, FlingFov = 66f;
    public float FlingBlendIn = 3f, FlingBlendOut = 1.2f;   // 每秒混合速度
    float _fling;           // 0..1 甩飞镜头混合量
    float _torsoY;          // 平滑后的躯干高度

    void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    public void Shake(float s) { _shake = Mathf.Max(_shake, s); }
    public void Pulse(float s) { _pulse = Mathf.Max(_pulse, s); }

    public void StartIntro() { Intro = true; _introT = 0f; }

    void LateUpdate()
    {
        if (Target == null || Body == null) return;
        float dt = Time.deltaTime;
        Vector3 rawTorso = Body.TorsoPos;
        Vector3 tp = rawTorso;
        tp.y = 0f;

        // ── 互坑：被甩飞（布娃娃）时拉远拉高、跟随躯干真实高度；结束后平滑混回
        bool flingNow = Tuning.FoulRoom && Target.Ragdolled && !Intro;
        _fling = Mathf.MoveTowards(_fling, flingNow ? 1f : 0f, dt * (flingNow ? FlingBlendIn : FlingBlendOut));
        float flingK = Mathf.SmoothStep(0f, 1f, _fling);
        if (flingNow)
        {
            // 空中跟得紧一点，落水挣扎时慢一点
            float yRate = Body.Airborne ? 6f : 3f;
            _torsoY = Mathf.Lerp(_torsoY, rawTorso.y, 1f - Mathf.Exp(-yRate * dt));
        }
        else _torsoY = Mathf.Lerp(_torsoY, 0f, 1f - Mathf.Exp(-4f * dt));

        Vector3 desiredPos, look;
        float fov;
        if (Intro)
        {
            _introT += dt;
            float a = Mathf.Lerp(-55f, 0f, Mathf.SmoothStep(0, 1, _introT / 3.2f));
            Quaternion q = Quaternion.Euler(0, a, 0);
            desiredPos = tp + q * new Vector3(0, Height + 0.6f, -Back - 1.5f);
            look = tp + new Vector3(0, 0.2f, 1.5f);
            fov = 55f;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _vel, 0.25f);
        }
        else
        {
            float speed = Target.V;
            float k = Mathf.Clamp01(speed / 3f);
            float back = Back + k * 1.2f;
            float h = Height + k * 0.3f;
            if (Target.Drowned) { h = 0.9f; back = 3.2f; }
            if (Target.Finished) { h = 2.4f; back = 4.2f; }
            desiredPos = tp + new Vector3(0, h, -back);
            look = tp + new Vector3(0, Target.Drowned ? -1f : 0f, LookAhead);
            if (Target.Finished) { desiredPos = tp + new Vector3(2.2f, 1.8f, -3.2f); look = tp + new Vector3(0, 0.5f, 0); }
            fov = 58f + k * 9f + (Target.Sprinting ? 6f : 0f);
            // 被拽/被蹬往后拖时镜头跟得慢一点，能看清自己被拖回去多远
            float zRate = Target.Shove < -0.5f ? 2.2f : 9f;
            float xRate = 5f;
            if (flingK > 0f)
            {
                // 跟随躯干真实 x / 高度，拉远拉高、盯住躯干，看清自己空中翻滚和落水
                Vector3 torso = new Vector3(rawTorso.x, _torsoY, rawTorso.z);
                Vector3 flingPos = torso + new Vector3(0f, Height + FlingUp, -(Back + FlingBack));
                flingPos.y = Mathf.Max(flingPos.y, Height + FlingUp * 0.6f);
                desiredPos = Vector3.Lerp(desiredPos, flingPos, flingK);
                look = Vector3.Lerp(look, torso, flingK);
                fov = Mathf.Lerp(fov, FlingFov, flingK);
                zRate = Mathf.Lerp(zRate, 4f, flingK);
                xRate = Mathf.Lerp(5f, 3.5f, flingK);
            }
            Vector3 p = transform.position;
            p.x = Mathf.Lerp(p.x, desiredPos.x, 1f - Mathf.Exp(-xRate * dt));
            p.y = Mathf.Lerp(p.y, desiredPos.y, 1f - Mathf.Exp(-4f * dt));
            p.z = Mathf.Lerp(p.z, desiredPos.z, 1f - Mathf.Exp(-zRate * dt));
            transform.position = p;
        }

        var rot = Quaternion.LookRotation(look - transform.position);
        if (_shake > 0f)
        {
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.5f);
            rot *= Quaternion.Euler(Random.Range(-1f, 1f) * _shake * 2f, Random.Range(-1f, 1f) * _shake * 2f, Random.Range(-1f, 1f) * _shake * 3f);
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-12f * dt));

        _pulse = Mathf.MoveTowards(_pulse, 0f, dt * 6f);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, fov - _pulse * 2.5f, 1f - Mathf.Exp(-6f * dt));
    }
}
