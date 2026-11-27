using UnityEngine;
using UnityEngine.UI;

/// <summary>シーンに置いたロボットとノートのキーホルダーを動かす。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TitleScreen))]
public sealed class StarSweepersTitlePresentation : MonoBehaviour
{
    [SerializeField] private RectTransform robotGroup;
    [SerializeField] private RectTransform[] floatingRobots;
    [SerializeField] private Button[] keychainButtons;

    private Vector2[] robotHomes;
    private Quaternion[] keychainRestRotations;
    private float swingTime;
    private float swingStrength;

    private void Awake()
    {
        robotHomes = new Vector2[floatingRobots.Length];
        for (int i = 0; i < floatingRobots.Length; i++)
        {
            robotHomes[i] = floatingRobots[i].anchoredPosition;
        }

        keychainRestRotations = new Quaternion[keychainButtons.Length];
        for (int i = 0; i < keychainButtons.Length; i++)
        {
            Button button = keychainButtons[i];
            keychainRestRotations[i] = button.transform.localRotation;
            if (button.targetGraphic is Image image)
            {
                // 透明な余白を通して、奥にある部屋のボタンを押せるようにする。
                image.alphaHitTestMinimumThreshold = 0.15f;
            }

            button.onClick.AddListener(PlayClickSwing);
        }
    }

    internal void PlayEntranceSwing()
    {
        Swing(-29f);
    }

    internal void PlayRetractionSwing()
    {
        Swing(21f);
    }

    private void PlayClickSwing()
    {
        Swing(27f);
    }

    private void Swing(float strength)
    {
        swingTime = 0f;
        swingStrength = strength;
    }

    private void Update()
    {
        if (robotGroup.gameObject.activeInHierarchy)
        {
            float time = Time.unscaledTime;
            for (int i = 0; i < floatingRobots.Length; i++)
            {
                float bob = Mathf.Sin(time * (1.45f + i * 0.21f) + i * 2.1f) * (10f + i * 3f);
                floatingRobots[i].anchoredPosition = robotHomes[i] + Vector2.up * bob;
            }
        }

        swingTime += Time.unscaledDeltaTime;
        float angle = Mathf.Sin(swingTime * 10.2f) * Mathf.Exp(-swingTime * 2.9f) * swingStrength;
        Quaternion swing = Quaternion.AngleAxis(angle, Vector3.forward);
        for (int i = 0; i < keychainButtons.Length; i++)
        {
            keychainButtons[i].transform.localRotation = keychainRestRotations[i] * swing;
        }
    }
}
