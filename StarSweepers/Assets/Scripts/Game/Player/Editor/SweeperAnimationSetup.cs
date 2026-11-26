using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// **プレイヤーの Animator（AC_SweeperPlayer）を、アニメーションの FBX から並べ直す道具。**（2026/11/26。直したリグの体に合わせて作り直し）
/// デザイナーさんが FBX を書き出し直したら、`Assets/Art/Animation/` に上書きして、メニューを1回押す。
/// FBX と取り込みの設定は変えない（くり返しは Animator の中で、終わったら頭へ戻す移り変わりで作る）。
/// 詳しい説明は Documents/機能ドキュメント/プレイヤーの動きのアニメーション.md。
/// </summary>
public static class SweeperAnimationSetup
{
    private const string Folder = "Assets/Art/Animation/";
    private const string ControllerPath = Folder + "AC_SweeperPlayer.controller";

    /// <summary>状態の名前・元の FBX・くり返すか・Animator の中の置き場所。</summary>
    private static readonly (string state, string fbx, bool loop, Vector2 position)[] States =
    {
        ("Idle", "AN_Idle.fbx", true, new Vector2(300, 100)),
        ("Walk", "AN_Move.fbx", true, new Vector2(300, 250)),
        ("AimWalk", "AN_AimWalk.fbx", true, new Vector2(600, 50)),
        ("Shoot", "AN_Shoot.fbx", false, new Vector2(600, 150)),
        ("Pull", "AN_Pull.fbx", true, new Vector2(600, 250)),
        ("Throw", "AN_Throw.fbx", false, new Vector2(600, 350)),
        ("Drop", "AN_Drop.fbx", false, new Vector2(300, 450)),
        ("DropLoot", "AN_DropLoot.fbx", true, new Vector2(300, 550)),
        ("Fall", "AN_Fall.fbx", false, new Vector2(600, 450)),
        ("Rise", "AN_Rise.fbx", false, new Vector2(600, 550)),
    };

    [MenuItem("Tools/StarSweepers/プレイヤーのアニメーションを Animator に並べる")]
    public static void Run()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[Animation] {ControllerPath} がありません。");
            return;
        }

        EnsureFloat(controller, "Speed");
        EnsureFloat(controller, "AimSpeed");

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states.ToArray())
        {
            machine.RemoveState(child.state);
        }

        var made = new Dictionary<string, AnimatorState>();
        foreach (var entry in States)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Folder + entry.fbx).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null)
            {
                Debug.LogWarning($"[Animation] {Folder + entry.fbx} にアニメーションが無いので、状態 {entry.state} は空のまま作りました。");
            }

            AnimatorState state = machine.AddState(entry.state, entry.position);
            state.motion = clip;
            state.writeDefaultValues = true;
            made[entry.state] = state;

            // くり返し：FBX の Loop Time は OFF のままなので、終わったら頭へ戻す（最初と最後のコマが同じ形なので、つなぎ目は出ない）
            if (entry.loop)
            {
                AnimatorStateTransition again = state.AddTransition(state);
                again.hasExitTime = true;
                again.exitTime = 1f;
                again.duration = 0f;
                again.hasFixedDuration = true;
            }
        }

        machine.defaultState = made["Idle"];

        // Idle ⇔ Walk は歩く速さで切り替える
        AddSpeedTransition(made["Idle"], made["Walk"], AnimatorConditionMode.Greater);
        AddSpeedTransition(made["Walk"], made["Idle"], AnimatorConditionMode.Less);

        // 狙い歩き：歩く速さに合わせて再生の速さを変える（止まっていると 0＝止まったポーズ）
        made["AimWalk"].speedParameterActive = true;
        made["AimWalk"].speedParameter = "AimSpeed";

        // 落ちる瞬間（Drop）が終わったら、落ちている間のくり返し（DropLoot）へ
        AnimatorStateTransition next = made["Drop"].AddTransition(made["DropLoot"]);
        next.hasExitTime = true;
        next.exitTime = 1f;
        next.duration = 0.1f;
        next.hasFixedDuration = true;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Animation] {ControllerPath} に {made.Count} 個の状態を並べ直しました。");
    }

    private static void EnsureFloat(AnimatorController controller, string name)
    {
        if (controller.parameters.All(p => p.name != name))
        {
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }
    }

    private static void AddSpeedTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.15f;
        transition.hasFixedDuration = true;
        transition.AddCondition(mode, 0.1f, "Speed");
    }
}
