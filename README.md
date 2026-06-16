# Goalkeeper VR

**Goalkeeper VR** é um simulador imersivo de **goleiro** em Realidade Virtual para **Meta Quest 3S**, feito na **Unity 6 (URP)** com **hand tracking** via OpenXR — sem controles. Você calibra o gol abrindo os braços e defende, com as próprias mãos, os chutes de um time inimigo controlado por uma simulação de futebol completa: 21 agentes em formação 4-3-3, com posse de bola, drible, passe e finalização. Agarre, espalme e segure a bola para defender — inclusive em **cobranças de pênalti**.

---

## Sumário

- [Clonar o projeto](#clonar-o-projeto)
- [Requisitos](#requisitos)
- [Como jogar](#como-jogar)
- [Funcionalidades](#funcionalidades)
- [Arquitetura e scripts](#arquitetura-e-scripts)
- [Montagem da cena](#montagem-da-cena)
- [Pipeline de assets](#pipeline-de-assets)
- [Parâmetros de ajuste](#parâmetros-de-ajuste)

---

## Clonar o projeto

> **IMPORTANTE — instale o Git LFS ANTES de clonar.** O projeto usa Git LFS para arquivos grandes (ex.: `Assets/Models/Stadium.glb`, ~350 MB). Sem o LFS, esses arquivos vêm como pequenos "ponteiros" de texto e o projeto **não abre corretamente** na Unity.

```bash
# 1. Instale o Git LFS uma única vez (https://git-lfs.com)
git lfs install

# 2. Clone normalmente — os arquivos LFS baixam junto
git clone https://github.com/ErickMascarenhas/CG-26.1-AB2.git
```

Se já clonou antes de instalar o LFS, rode dentro da pasta do projeto:

```bash
git lfs install
git lfs pull
```

Depois, abra a pasta no **Unity 6** (`6000.0.62f1`). A `Library/` é regenerada no primeiro import.

---

## Requisitos

- **Unity 6** (`6000.0.62f1`), template **3D URP**.
- **Plataforma:** Android (Meta Quest 3S, standalone, 72 Hz).
- **Pacotes (Package Manager):** XR Plugin Management · OpenXR Plugin · XR Interaction Toolkit (com **XR Device Simulator**) · XR Hands · AI Navigation · TextMeshPro · glTFast (`com.unity.cloud.gltfast`).
- **OpenXR (Android):** feature groups **Meta Quest Support** + **Hand Tracking Subsystem**.

> Atalho de Editor: **Tools → Goalkeeper VR → Setup XR Project** aplica as configurações de XR/Player/Quality; **Validate Setup** confere.

---

## Como jogar

### Sem headset (XR Device Simulator)

Dá para jogar e testar tudo no PC, sem Quest:

1. Abra `Assets/Scenes/Goalkeeper.unity` e garanta que o **XR Device Simulator** está na cena.
2. **Play.**
3. **H** — muda para o modo de **input das mãos**.
4. **TAB** — alterna o controle entre **câmera** e **mãos**.
5. **P** com **cada mão** (gesto de punho) — esquerda e direita.
6. Ao detectar os dois punhos, o gol é calibrado pela sua envergadura e **a partida começa**.

### No Quest 3S

1. **Tools → Goalkeeper VR → Setup XR Project**.
2. **Bake do NavMesh** cobrindo o campo (`NavMesh Surface` no chão → Bake).
3. **Build And Run** (Android).
4. No headset, **largue os controles**, abra os braços e feche os punhos para calibrar.

> A calibração inicia o jogo **uma única vez**. Depois disso, fechar os punhos novamente apenas **recalibra o tamanho do gol** — a partida nunca reinicia.

---

## Funcionalidades

- **Calibração dinâmica do gol** pela envergadura real do jogador (largura e altura com limites próprios), adaptando o jogo ao espaço físico do quarto. Recalibração a qualquer momento, sem reiniciar a partida.
- **Defesa por hand tracking** com as duas mãos:
  - **Agarrar** — fechar a mão sobre a bola a prende na palma (com janela de tolerância a falhas de tracking e _squash_ de feedback).
  - **Espalmar** — com a mão aberta, um colisor sólido **rebate a bola fisicamente**, transferindo a velocidade do movimento da mão (defesa "de soco").
  - **Arremessar** — soltar a bola com um gesto rápido a lança de volta ao jogo.
- **Simulação de partida** com 21 agentes (10 + 10 de campo + goleiro inimigo) em **4-3-3**: posse de bola, **perseguição com equilíbrio defensivo**, **drible com desvio** dos marcadores, **passe** para companheiros livres e **finalização**.
- **Time aliado com _nerf_** (atraso, erro e roubo reduzido) para que o time inimigo mantenha a posse e ataque o gol do jogador em ritmo acelerado.
- **Indicador de mira** — um círculo semitransparente aparece **no ponto exato** onde a bola vai chegar, **antes** do chute, e permanece até a defesa/gol.
- **Windup de chute** — o atacante para e telegrafa a finalização, dando leitura ao goleiro.
- **Modo pênalti** — a cada gol/defesa há 10% de chance de uma cobrança: um inimigo no ponto de pênalti e os demais assistindo de longe.
- **Torcida reativa** — torcedores em PNG nas arquibancadas; parte comemora nos gols, parte nas defesas.
- **Luvas de goleiro** nas mãos do jogador (material aplicado ao Hand Visualizer).
- **Áudio** — chute, passe, defesa, gol, apito e ambiente.
- **Console de debug em VR** com FPS, para diagnóstico dentro do headset.
- **Modelo de jogador universal** com 2 cores configuráveis (camisa e pele) por instância.
- **Modo de performance** — um toggle opcional que ativa a IA de apenas 3 zagueiros aliados e 1 atacante inimigo e desliga a torcida, para rodar leve em hardware mais limitado.

---

## Arquitetura e scripts

Toda a lógica está em `Assets/Scripts/`.

| Script | Papel |
|---|---|
| `XRProjectSetup.cs` | (Editor) Configura XR/Player/Quality para Quest. |
| `GoalCalibrator.cs` | Calibra o gol pela envergadura; recalibração redimensiona sem reiniciar. |
| `HandTrackingCatch.cs` | Agarre, espalmar (rebatida física) e arremesso por hand tracking. |
| `VRDebugConsole.cs` | Console em World Space com logs + FPS. |
| `SoccerTypes.cs` | Enums e struct de decisão. |
| `AISoccerBrain.cs` | Decisão pura: formação em bloco, perseguir, driblar, passar, chutar. |
| `SoccerPlayer.cs` | Agente: NavMesh, drible, chute/passe, 2 cores, animação. |
| `MatchManager.cs` | Simulação central: posse, passes, chutes, pênalti, time-slicing. |
| `TeamSpawner.cs` | Instancia os dois times em 4-3-3 + goleiro inimigo. |
| `TeamNerfManager.cs` | Balanceia o time aliado para o inimigo atacar. |
| `ShotIndicator.cs` | Círculo de mira semitransparente no alvo do chute. |
| `CrowdMember.cs` / `CrowdManager.cs` | Torcida em PNG reativa a gol/defesa. |
| `GameManager.cs` | Placar, detecção de gol/defesa, áudio, feedback, ciclo da partida. |

### Fluxo de uma jogada

```
Calibrou o gol → a partida inicia (uma vez)
   → inimigo com a posse → drible/passe rumo ao gol do jogador
       → atacante para (windup) + círculo de mira aparece → CHUTE
           → você agarra ou rebate → DEFESA / GOL → torcida comemora
               → 10% de chance: PÊNALTI · senão segue a partida
```

---

## Montagem da cena

- **XR Origin (VR)** + **Hand Visualizer** + **XR Device Simulator**.
- **Goal Root** com traves filhas (**Post Left/Right**) e **GoalLine**.
- **Left/Right Hand Catcher** — vazios com `HandTrackingCatch` (`Is Left Hand` correto). Eventos: `OnCatch → GameManager.OnBallCaught`, `OnParry → GameManager.OnBallParried`.
- **Ball** — Rigidbody (massa 0.45, **Continuous Dynamic**) + Sphere Collider + tag **`Ball`** + Physics Material.
- **Managers:**
  - `GameManager` → Match Manager, Ball, Goal Line, Post Left/Right, Left/Right Hand, Crowd Manager, Audio Source + clips, UI.
  - `MatchManager` → Spawner, Ball, Nerf, GameManager, Player/Enemy Goal Center, **Shot Indicator**, Sfx + Kick/Pass, parâmetros de pênalti.
  - `TeamSpawner` → Player Prefab, Field Center/Length/Width, cores, tons de pele, goleiro inimigo.
  - `TeamNerfManager`, `ShotIndicator` (vazio + script), `CrowdManager`.
- **NavMesh Surface** no chão, **assada** cobrindo todo o campo.
- Eventos de calibração: `GoalCalibrator.OnGoalCalibrated → GameManager.StartMatch`.

---

## Pipeline de assets

### Jogadores (FBX, Mixamo)
Prefabs `Attacker` (aliado) e `Attacker Enemy`. O `SoccerPlayer` aciona o Animator pelos nomes `IsRunning` (bool, idle ⇄ andar) e `Shoot` (trigger, chute) — as três animações essenciais. Para tingir por instância, o modelo usa 2 slots de material (camisa = 0, pele = 1); para usar materiais próprios de cada time, defina `skinMaterialIndex = -1`.

### Luvas de goleiro (Hand Visualizer)
Aplique um material de **luva** ao **Hand Visualizer** das mãos: selecione o objeto do Hand Visualizer e troque o campo de material da mão pelo material das luvas (sem trocar o modelo). Um material URP/Lit com cor/textura de luva já resolve.

### Torcida (PNG)
`Materials/Crowd/Crowd Idle.png` e `Crowd Cheer.png`. Espalhe quads/sprites na arquibancada; cada um recebe `CrowdMember` (SpriteSwap/MaterialSwap/ObjectToggle) com as duas imagens. Um `CrowdManager` divide a torcida entre os grupos **gol** e **defesa** e é ligado no `GameManager`.

### Sons
`Kick`, `Pass` → `MatchManager`. `Goal`, `Save`, `Whistle` → `GameManager`. `Background` → ambiente em loop.

### Geradores Blender (opcionais)
`BlenderScripts/build_player.py` e `build_crowd.py` criam versões low-poly (jogador 2 cores; torcedor com animação de braço) — _placeholders_ via Blender (Scripting → Run → exportar `.glb`).

---

## Parâmetros de ajuste

No Inspector:

- **Agarre/rebatida** (`HandTrackingCatch`): `palmTriggerRadius`, `catchStartThreshold`, `releaseThreshold`, `parryPunch`, `parryMinSpeed`, `parryBounciness`.
- **Calibração** (`GoalCalibrator`): larguras/alturas mín-máx, `widthMargin`, `closeThreshold`.
- **Partida** (`MatchManager`): `shootingRange`, `tackleRadius`, `kickSpeed`, `passSpeed`, `shootWindup`, `penaltyChance`, `activeBrainCount`, `brainSliceInterval`, separação/bloco.
- **Dificuldade** (`TeamNerfManager`): `decisionDelay`, `dribbleErrorChance`, `speedMultiplier`, `allyTackleRadiusMultiplier`.

---

_Goalkeeper VR — Unity 6 (URP) · Meta Quest 3S · hand tracking via OpenXR._
