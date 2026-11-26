using System;
using UnityEngine;

/// <summary>インタラクトできる物の色と、頭上に出すボタン画像をまとめて扱う。</summary>
[Serializable]
public sealed class InteractPromptVisual
{
    public enum Mode
    {
        ColorOnly,
        PromptOnly,
        Both,
        None
    }

    [Header("近づいたときの演出")]
    [SerializeField] private Mode mode = Mode.Both;
    [SerializeField] private Sprite promptSprite;
    [SerializeField] private Vector3 promptOffset = new Vector3(0f, 2f, 0f);
    [SerializeField, Min(0.01f)] private float promptSize = 0.75f;

    [NonSerialized] private Transform owner;
    [NonSerialized] private Renderer highlightRenderer;
    [NonSerialized] private Color originalColor;
    [NonSerialized] private SpriteRenderer promptRenderer;

    public void Initialize(Transform target, Renderer renderer)
    {
        owner = target;
        highlightRenderer = renderer;
        if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
        {
            originalColor = highlightRenderer.sharedMaterial.color;
        }

        GameObject prompt = new GameObject("InteractPrompt");
        prompt.transform.SetParent(owner, false);
        promptRenderer = prompt.AddComponent<SpriteRenderer>();
        promptRenderer.sortingOrder = 100;
        promptRenderer.enabled = false;
    }

    public void SetNearby(bool nearby, Color nearColor)
    {
        bool showColor = nearby && (mode == Mode.ColorOnly || mode == Mode.Both);
        if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
        {
            highlightRenderer.material.color = showColor ? nearColor : originalColor;
        }

        bool showPrompt = nearby && (mode == Mode.PromptOnly || mode == Mode.Both) && promptSprite != null;
        if (promptRenderer != null)
        {
            promptRenderer.sprite = promptSprite;
            promptRenderer.enabled = showPrompt;
            if (showPrompt)
            {
                promptRenderer.transform.position = owner.TransformPoint(promptOffset);
                promptRenderer.transform.localScale = Vector3.one * promptSize;
            }
        }
    }

    public void LateUpdate()
    {
        if (promptRenderer == null || !promptRenderer.enabled)
        {
            return;
        }

        Camera view = Camera.main;
        if (view != null)
        {
            promptRenderer.transform.rotation = view.transform.rotation;
        }
    }

    public void Cleanup()
    {
        SetNearby(false, Color.white);
        if (promptRenderer != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(promptRenderer.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(promptRenderer.gameObject);
            }
        }
        promptRenderer = null;
    }
}
