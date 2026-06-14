/// <summary>
/// Tipos compartilhados do ecossistema de futebol (Goalkeeper VR).
/// </summary>
public enum Team
{
    Ally,   // time do jogador (humano é o goleiro). Sofre "nerf".
    Enemy   // time adversário (full strength). Ataca o gol do jogador.
}

public enum Role
{
    Goalkeeper,
    Defender,
    Midfielder,
    Forward
}

/// <summary>Tipo de decisão que a IA toma para um jogador.</summary>
public enum DecisionType
{
    MoveToFormation, // ir para a posição tática (blend home/bola/gol)
    ChaseBall,       // perseguir a bola livre
    Dribble,         // conduzir a bola em direção ao gol alvo
    Pass,            // passar para um companheiro
    Shoot            // finalizar ao gol alvo
}

/// <summary>Resultado de uma decisão da IA.</summary>
public struct Decision
{
    public DecisionType type;
    public UnityEngine.Vector3 targetPosition; // para MoveToFormation/ChaseBall/Dribble
    public SoccerPlayer passTarget;            // para Pass
    public UnityEngine.Vector3 shootTarget;    // para Shoot
}
