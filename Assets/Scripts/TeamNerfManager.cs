using UnityEngine;

/// <summary>
/// Fase 6 — Nerf do time aliado (Goalkeeper VR).
///
/// O time do jogador (Ally) é deliberadamente enfraquecido para PERDER a posse,
/// garantindo que o time inimigo ataque o gol do jogador em ritmo acelerado.
///
/// Mecanismos:
///   - decisionDelay: atraso (s) que o MatchManager aplica antes de o dono aliado agir.
///   - dribbleErrorChance: chance por decisão de o aliado "errar" (perder a bola/passe ruim).
///   - speedMultiplier: aliados correm um pouco mais devagar.
///
/// O MatchManager consulta estes valores. Este componente é só o "painel de balança".
/// </summary>
public class TeamNerfManager : MonoBehaviour
{
    [Header("Nerf do time aliado (Ally)")]
    [Range(0f, 2f)]
    [Tooltip("Atraso de decisão (s) do dono da bola aliado.")]
    public float decisionDelay = 0.6f;

    [Range(0f, 1f)]
    [Tooltip("Chance (0–1) de o aliado errar (perder posse / passe ruim) a cada decisão.")]
    public float dribbleErrorChance = 0.35f;

    [Range(0.3f, 1f)]
    [Tooltip("Multiplicador de velocidade dos aliados (1 = sem nerf).")]
    public float speedMultiplier = 0.85f;

    [Header("Orquestração do ataque inimigo")]
    [Range(1f, 1.5f)]
    [Tooltip("Bônus de velocidade do dono inimigo (acelera o ataque ao gol do jogador).")]
    public float enemyOwnerSpeedBoost = 1.15f;

    [Range(0f, 1f)]
    [Tooltip("Multiplicador do raio de roubo dos ALIADOS (baixo = aliados raramente roubam, " +
             "deixando o inimigo driblar/passar por eles de forma scriptada).")]
    public float allyTackleRadiusMultiplier = 0.45f;

    /// <summary>Raio efetivo de roubo conforme o time que TENTA roubar.</summary>
    public float GetTackleRadius(Team tacklingTeam, float baseRadius)
        => tacklingTeam == Team.Ally ? baseRadius * allyTackleRadiusMultiplier : baseRadius;

    /// <summary>Retorna o atraso de decisão para um time.</summary>
    public float GetDecisionDelay(Team team) => team == Team.Ally ? decisionDelay : 0f;

    /// <summary>Sorteia se o jogador erra nesta decisão (só aliados erram por nerf).</summary>
    public bool RollError(Team team) =>
        team == Team.Ally && Random.value < dribbleErrorChance;

    /// <summary>Multiplicador de velocidade a aplicar conforme time e posse.</summary>
    public float GetSpeedMultiplier(Team team, bool hasBall)
    {
        if (team == Team.Ally) return speedMultiplier;
        if (hasBall) return enemyOwnerSpeedBoost;
        return 1f;
    }
}
