using UnityEngine;

/// <summary>
/// Fase 7 — Um torcedor (PNG) na arquibancada (Goalkeeper VR).
///
/// Alterna entre a imagem "parado" e "comemorando". Suporta três formas de
/// montagem — use a que combinar com como você espalhou as PNGs:
///   A) SpriteRenderer  → idleSprite / cheerSprite
///   B) Renderer (quad) → idleMaterial / cheerMaterial
///   C) Dois GameObjects → idleObject / cheerObject (liga/desliga)
///
/// O CrowdManager agrupa os torcedores e dispara SetCelebrating() em parte deles
/// após gol (um grupo) ou defesa (outro grupo).
/// </summary>
public class CrowdMember : MonoBehaviour
{
    public enum Mode { SpriteSwap, MaterialSwap, ObjectToggle }

    [Header("Modo de troca")]
    public Mode mode = Mode.SpriteSwap;

    [Header("A) Sprite")]
    public SpriteRenderer spriteRenderer;
    public Sprite idleSprite;
    public Sprite cheerSprite;

    [Header("B) Material")]
    public Renderer targetRenderer;
    public Material idleMaterial;
    public Material cheerMaterial;

    [Header("C) Objetos")]
    public GameObject idleObject;
    public GameObject cheerObject;

    /// <summary>Grupo: comemora em GOL (false) ou em DEFESA (true). Definido pelo CrowdManager.</summary>
    [HideInInspector] public bool celebratesOnSave;

    private bool _celebrating;

    private void Reset()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        targetRenderer = GetComponent<Renderer>();
    }

    public void SetCelebrating(bool on)
    {
        if (_celebrating == on) return;
        _celebrating = on;

        switch (mode)
        {
            case Mode.SpriteSwap:
                if (spriteRenderer != null)
                    spriteRenderer.sprite = on ? cheerSprite : idleSprite;
                break;

            case Mode.MaterialSwap:
                if (targetRenderer != null && idleMaterial != null && cheerMaterial != null)
                    targetRenderer.sharedMaterial = on ? cheerMaterial : idleMaterial;
                break;

            case Mode.ObjectToggle:
                if (idleObject != null)  idleObject.SetActive(!on);
                if (cheerObject != null) cheerObject.SetActive(on);
                break;
        }
    }
}
