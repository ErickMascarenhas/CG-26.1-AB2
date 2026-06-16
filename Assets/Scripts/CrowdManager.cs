using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fase 7 — Gerência da torcida (Goalkeeper VR).
///
/// Divide os CrowdMember em dois grupos:
///   - Grupo GOL   → comemora quando o time INIMIGO marca (gol sofrido).
///   - Grupo DEFESA → comemora quando o jogador DEFENDE.
/// (A divisão é aleatória, controlada por 'saveGroupFraction'.)
///
/// O GameManager chama CelebrateGoal() / CelebrateSave() nos respectivos eventos.
/// </summary>
public class CrowdManager : MonoBehaviour
{
    [Header("Torcedores")]
    [Tooltip("Lista de torcedores. Se vazia, busca todos os CrowdMember nos filhos.")]
    public List<CrowdMember> members = new List<CrowdMember>();

    [Range(0f, 1f)]
    [Tooltip("Fração da torcida que comemora em DEFESA (o resto comemora em GOL).")]
    public float saveGroupFraction = 0.5f;

    [Tooltip("Duração (s) da comemoração antes de voltar ao normal.")]
    public float celebrateDuration = 3f;

    [Range(0f, 1f)]
    [Tooltip("Fração do grupo que realmente comemora (nem todos ao mesmo tempo).")]
    public float participation = 0.7f;

    private bool _allDisabled;

    private void Start()
    {
        EnsureMembers();

        // Divide aleatoriamente em grupo GOL / DEFESA
        foreach (var m in members)
        {
            if (m == null) continue;
            m.celebratesOnSave = Random.value < saveGroupFraction;
            m.SetCelebrating(false);
        }

        Debug.Log($"[CrowdManager] {members.Count} torcedores prontos.");
    }

    private void EnsureMembers()
    {
        if (members == null || members.Count == 0)
            members = new List<CrowdMember>(GetComponentsInChildren<CrowdMember>(true));
    }

    /// <summary>Liga/desliga TODA a torcida (usado pelo modo de performance).</summary>
    public void SetActiveAll(bool on)
    {
        EnsureMembers();
        foreach (var m in members)
            if (m != null) m.gameObject.SetActive(on);
        _allDisabled = !on;
        Debug.Log($"[CrowdManager] Torcida {(on ? "ligada" : "desligada")}.");
    }

    /// <summary>Comemoração de GOL (gol sofrido) — parte da torcida do grupo GOL.</summary>
    public void CelebrateGoal() { if (!_allDisabled) StartCoroutine(Celebrate(false)); }

    /// <summary>Comemoração de DEFESA — parte da torcida do grupo DEFESA.</summary>
    public void CelebrateSave() { if (!_allDisabled) StartCoroutine(Celebrate(true)); }

    private IEnumerator Celebrate(bool saveGroup)
    {
        var participants = new List<CrowdMember>();
        foreach (var m in members)
        {
            if (m == null || m.celebratesOnSave != saveGroup) continue;
            if (Random.value <= participation)
            {
                m.SetCelebrating(true);
                participants.Add(m);
            }
        }

        yield return new WaitForSeconds(celebrateDuration);

        foreach (var m in participants)
            if (m != null) m.SetCelebrating(false);
    }
}
