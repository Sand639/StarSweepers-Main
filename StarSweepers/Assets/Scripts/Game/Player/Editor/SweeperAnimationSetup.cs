using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// **プレイヤーの動きのアニメーション（狙い歩き・撃つ・引っ張る・投げる・落ちる）を、体の骨に合わせて取り込み、Animator に並べる道具。**（2026/11/25）
/// 詳しい説明は Documents/スクリプトの説明/ と 機能ドキュメント/プレイヤーの動きのアニメーション.md。
/// </summary>
public static class SweeperAnimationSetup
{
    private const string Folder = "Assets/Art/Animation/";
    private const string ControllerPath = Folder + "AC_SweeperPlayer.controller";

    /// <summary>元の FBX・作るクリップ・Animator の状態の名前・くり返すか。</summary>
    private static readonly (string fbx, string clip, string state, bool loop)[] Entries =
    {
        ("AN_Character_Aimwalk.fbx", "AN_AimWalk_SK.anim", "AimWalk", true),
        ("AN_Shoot.fbx", "AN_Shoot_SK.anim", "Shoot", false),
        ("AN_Pull.fbx", "AN_Pull_SK.anim", "Pull", true),
        ("AN_Throw.fbx", "AN_Throw_SK.anim", "Throw", false),
        ("AN_Drop.fbx", "AN_Drop_SK.anim", "Drop", false),
        ("AN_DropLoot.fbx", "AN_DropLoot_SK.anim", "DropLoot", true),
    };

    /// <summary>
    /// 骨の名前の置き換え（前から順に、最初に当てはまったものだけ）。行き先が null の骨は捨てる。
    /// 2026/11/19 に来た動きの FBX は、体（SK_Character）と ①いちばん上の骨の名前 ②右腕の骨の名前（_L2・_L）が違う。
    /// </summary>
    private static readonly (string from, string to)[] PathRules =
    {
        ("GRP_ExportScale_DO_NOT_TOUCH/CTRL_Global/JNT_Tool/JNT_Hip/JNT_Body/JNT_Chest/JNT_Shoulder_L2/JNT_Arm_L/JNT_Hand_L",
         "SCL_Root/JNT_Root/JNT_Hip/JNT_Body/JNT_Chest/JNT_Shoulder_R/JNT_Arm_R/JNT_Hand_R"),
        ("GRP_ExportScale_DO_NOT_TOUCH/CTRL_Global/JNT_Tool/JNT_Hip/JNT_Body/JNT_Chest/JNT_Shoulder_L2/JNT_Arm_L",
         "SCL_Root/JNT_Root/JNT_Hip/JNT_Body/JNT_Chest/JNT_Shoulder_R/JNT_Arm_R"),
        ("GRP_ExportScale_DO_NOT_TOUCH/CTRL_Global/JNT_Tool/JNT_Hip/JNT_Body/JNT_Chest/JNT_Shoulder_L2",
         "SCL_Root/JNT_Root/JNT_Hip/JNT_Body/JNT_Chest/JNT_Shoulder_R"),
        ("GRP_ExportScale_DO_NOT_TOUCH/CTRL_Global/JNT_Tool/", "SCL_Root/JNT_Root/"),
        // いちばん上の3つは動いていない入れ物。体の側の値をそのまま使う
        ("GRP_ExportScale_DO_NOT_TOUCH/CTRL_Global/JNT_Tool", null),
        ("GRP_ExportScale_DO_NOT_TOUCH/CTRL_Global", null),
        ("GRP_ExportScale_DO_NOT_TOUCH", null),
    };

    [MenuItem("Tools/StarSweepers/プレイヤーの動きのアニメーションを取り込む")]
    public static void Run()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Models/SK_Character.fbx");
        var clips = new Dictionary<string, (AnimationClip clip, bool loop)>();
        foreach (var entry in Entries)
        {
            AnimationClip clip = BuildClip(Folder + entry.fbx, Folder + entry.clip, entry.loop, model);
            if (clip != null)
            {
                clips[entry.state] = (clip, entry.loop);
            }
        }

        BuildController(clips);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Animation] プレイヤーの動きのアニメーションを {clips.Count} 個取り込み、{ControllerPath} に並べました。");
    }

    /// <summary>FBX の動きを、骨の名前を体に合わせて別のクリップ（.anim）に写す。元の FBX は変えない。</summary>
    private static AnimationClip BuildClip(string fbxPath, string clipPath, bool loop, GameObject model)
    {
        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (source == null)
        {
            Debug.LogError($"[Animation] {fbxPath} にアニメーションがありません。");
            return null;
        }

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, clipPath);
        }
        clip.ClearCurves();
        clip.frameRate = source.frameRate;

        int missing = 0;
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            string path = Remap(binding.path, out bool drop);
            if (drop)
            {
                continue;
            }
            if (model != null && path.Length > 0 && model.transform.Find(path) == null)
            {
                missing++;
                continue;
            }

            EditorCurveBinding target = binding;
            target.path = path;
            AnimationUtility.SetEditorCurve(clip, target, AnimationUtility.GetEditorCurve(source, binding));
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);

        if (missing > 0)
        {
            Debug.Log($"[Animation] {clipPath}: 体に無い骨 {missing} 本ぶんの動きは捨てました（銃のモデルなど）。");
        }
        return clip;
    }

    private static string Remap(string path, out bool drop)
    {
        drop = false;
        foreach (var rule in PathRules)
        {
            bool whole = path == rule.from;
            bool prefix = rule.from.EndsWith("/") && path.StartsWith(rule.from);
            bool child = !rule.from.EndsWith("/") && path.StartsWith(rule.from + "/");
            if (!whole && !prefix && !child)
            {
                continue;
            }
            if (rule.to == null)
            {
                drop = whole;
                if (whole) return path;
                continue;
            }
            return rule.to + path.Substring(rule.from.Length);
        }
        return path;
    }

    /// <summary>AC_SweeperPlayer に動きの状態を足す（同じ名前の状態があれば中身を入れ替える）。Idle・Walk は触らない。</summary>
    private static void BuildController(Dictionary<string, (AnimationClip clip, bool loop)> clips)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[Animation] {ControllerPath} がありません。");
            return;
        }

        if (controller.parameters.All(p => p.name != "AimSpeed"))
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "AimSpeed",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f,
            });
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var positions = new Dictionary<string, Vector3>
        {
            { "AimWalk", new Vector3(600, 100, 0) },
            { "Shoot", new Vector3(600, 200, 0) },
            { "Pull", new Vector3(600, 300, 0) },
            { "Throw", new Vector3(600, 400, 0) },
            { "Drop", new Vector3(300, 450, 0) },
            { "DropLoot", new Vector3(300, 550, 0) },
        };

        var states = new Dictionary<string, AnimatorState>();
        foreach (var pair in clips)
        {
            AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == pair.Key)
                                  ?? machine.AddState(pair.Key, positions[pair.Key]);
            state.motion = pair.Value.clip;
            state.writeDefaultValues = true;
            foreach (AnimatorStateTransition old in state.transitions.ToArray())
            {
                state.RemoveTransition(old);
            }
            states[pair.Key] = state;
        }

        // 狙い歩き：歩く速さに合わせて再生の速さを変える（止まっていると 0＝止まったポーズ。2026/11/25・小野田さんの指定）
        if (states.TryGetValue("AimWalk", out AnimatorState aimWalk))
        {
            aimWalk.speedParameterActive = true;
            aimWalk.speedParameter = "AimSpeed";
        }

        // 落ちる瞬間（Drop）が終わったら、落ちている間のくり返し（DropLoot）へ
        if (states.TryGetValue("Drop", out AnimatorState drop) && states.TryGetValue("DropLoot", out AnimatorState dropLoop))
        {
            AnimatorStateTransition next = drop.AddTransition(dropLoop);
            next.hasExitTime = true;
            next.exitTime = 1f;
            next.duration = 0.1f;
            next.hasFixedDuration = true;
        }

        EditorUtility.SetDirty(controller);
    }
}
