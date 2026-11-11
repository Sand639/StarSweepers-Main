using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>トランジションの検証シーン用。Space（コントローラーは A）で演出を出し、このシーンを読み直す。</summary>
public class SceneTransitionTester : MonoBehaviour
{
    [Tooltip("背景の色を変えるカメラ")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("読み直すたびに順に変える背景の色（シーンが切り替わったのが分かるように）")]
    [SerializeField] private Color[] backgroundColors =
    {
        new Color(0.35f, 0.5f, 0.7f),
        new Color(0.75f, 0.45f, 0.3f),
        new Color(0.35f, 0.6f, 0.4f),
    };

    private static int loadCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loadCount = 0;
    }

    private void Start()
    {
        if (targetCamera != null && backgroundColors.Length > 0)
        {
            targetCamera.backgroundColor = backgroundColors[loadCount % backgroundColors.Length];
        }
    }

    private void Update()
    {
        bool pressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                       (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (pressed)
        {
            SceneTransition.Play(ReloadScene);
        }
    }

    /// <summary>このシーンを読み直す。</summary>
    private void ReloadScene()
    {
        loadCount++;

#if UNITY_EDITOR
        // 検証シーンはビルドの一覧に入れないので、エディタ専用の読み込み方を使う
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            gameObject.scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(gameObject.scene.name);
#endif
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(20f, 20f, 1000f, 60f),
            $"Space（コントローラーは A）：トランジションを出して、このシーンを読み直す（読み直した回数：{loadCount}）\n" +
            "種類は Assets/Resources/SceneTransition のプルダウンで変える（再生中は DontDestroyOnLoad の中の SceneTransition を選ぶ）");
    }
}
