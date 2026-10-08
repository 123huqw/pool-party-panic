#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 一键搭工程：材质模板、场景、Build Settings、打包 Windows exe。
/// 命令行：Unity.exe -batchmode -projectPath ... -executeMethod SwimSetup.SetupAndBuild -quit
/// 编辑器：菜单「游泳原型」。
/// </summary>
public static class SwimSetup
{
    const string ScenePath = "Assets/Scenes/SwimRace.unity";
    const string ResDir = "Assets/Resources";

    [MenuItem("游泳原型/1. 初始化工程（材质 + 场景）")]
    public static void Setup()
    {
        Directory.CreateDirectory(ResDir);
        Directory.CreateDirectory("Assets/Scenes");

        var std = Shader.Find("Standard");
        // 不透明模板
        var opaque = new Material(std) { enableInstancing = true };
        CreateOrReplace(opaque, ResDir + "/Mat_Opaque.mat");

        // 透明 + 法线贴图模板（保证打包时有这个变体）
        var water = new Material(std);
        water.SetFloat("_Mode", 3);
        water.SetInt("_SrcBlend", (int)BlendMode.One);
        water.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        water.SetInt("_ZWrite", 0);
        water.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        water.EnableKeyword("_NORMALMAP");
        water.SetTexture("_BumpMap", Texture2D.normalTexture);
        water.renderQueue = 3000;
        CreateOrReplace(water, ResDir + "/Mat_Water.mat");

        var psh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
        var particle = new Material(psh);
        CreateOrReplace(particle, ResDir + "/Mat_Particle.mat");

        // 注意：这里绝对不要再把着色器塞进「Always Included Shaders」。
        // "GUI/Text Shader" 属于 Unity 内置的 Library/unity default resources，
        // 带 HideFlags.DontSave，被强制包含时打包会在写 Resources/unity_builtin_extra 时崩：
        //   Assertion failed on expression: 'm_LockCount == 0'
        //   Building - Failed to write file: .../Resources/unity_builtin_extra
        // TextMesh 用的动态字体材质由 Resources 里的材质保证，
        // 粒子变体由 Resources/Mat_Particle.mat 保证，都不需要常驻包含。

        // 场景：空场景即可，GameManager 运行时自动生成一切
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.productName = "游泳竞速 原型";
        PlayerSettings.companyName = "SwimParty";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SwimSetup] 初始化完成");
    }

    static void CreateOrReplace(Material m, string path)
    {
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }

    static void AddAlwaysIncluded(string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) return;
        var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
        arr.InsertArrayElementAtIndex(arr.arraySize);
        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
        so.ApplyModifiedProperties();
    }

    [MenuItem("游泳原型/2. 打包 Windows 版")]
    public static void Build()
    {
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/SwimRace.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("[SwimSetup] Build result: " + report.summary.result + "  errors=" + report.summary.totalErrors);
        if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    public static void SetupAndBuild()
    {
        Setup();
        Build();
    }

    [MenuItem("游泳原型/打开比赛场景")]
    public static void OpenScene() => EditorSceneManager.OpenScene(ScenePath);
}
#endif
