# Goalkeeper VR

Simulador imersivo de **goleiro** em Realidade Virtual para **Meta Quest 3S**, feito na **Unity 6 (URP)** com **hand tracking** via OpenXR (sem controles). O jogador calibra o gol abrindo os braços, e defende — agarrando ou espalmando com as próprias mãos — os chutes de um time inimigo controlado por IA (21 agentes, simulação de partida 4-3-3 com posse, passes e finalização).

> **Estado:** Protótipo jogável (Partes 1 e 2 do plano implementadas em código). Em fase de integração de assets (modelos FBX, animações, torcida) e ajuste de _feel_. Veja [Estado por fase](#estado-por-fase).

---

## Sumário

- [Clonar o projeto](#clonar-o-projeto)
- [Requisitos](#requisitos)
- [Como testar SEM headset (XR Device Simulator)](#como-testar-sem-headset)
- [Como rodar no Quest 3S](#como-rodar-no-quest-3s)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Arquitetura e scripts](#arquitetura-e-scripts)
- [Montagem da cena (wiring)](#montagem-da-cena)
- [Estado por fase](#estado-por-fase)
- [Pipeline de assets](#pipeline-de-assets)
- [Parâmetros de ajuste (feel)](#parâmetros-de-ajuste)
- [Problemas conhecidos](#problemas-conhecidos)
- [Publicação no GitHub](#publicação-no-github)
- [Como contribuir](#como-contribuir)

---

## Clonar o projeto

> **IMPORTANTE — instale o Git LFS ANTES de clonar.** O projeto usa Git LFS para arquivos grandes (ex.: `Assets/Models/Stadium.glb`, ~350 MB). Sem o LFS, esses arquivos vêm como pequenos "ponteiros" de texto e o projeto **não abre corretamente** na Unity.

```bash
# 1. Instale o Git LFS uma única vez (https://git-lfs.com)
git lfs install

# 2. Clone normalmente — os arquivos LFS baixam junto
git clone https://github.com/ErickMascarenhas/CG-26.1-AB2.git
```

Se você **já clonou antes de instalar o LFS**, rode dentro da pasta do projeto para baixar os arquivos reais:

```bash
git lfs install
git lfs pull
```

Depois, abra a pasta no **Unity 6** (`6000.0.62f1`). A `Library/` é regenerada automaticamente no primeiro import.

---

## Requisitos

- **Unity 6** (testado em `6000.0.62f1`), template **3D URP**.
- **Build target:** Android (Meta Quest 3S, standalone, 72 Hz).
- **Pacotes (Package Manager):**
  - XR Plugin Management
  - OpenXR Plugin
  - XR Interaction Toolkit (inclui o **XR Device Simulator**)
  - XR Hands
  - AI Navigation
  - TextMeshPro (Essentials importados)
  - glTFast (`com.unity.cloud.gltfast`) — usado para importar `.glb` (opcional)
- **OpenXR (Android):** feature groups **Meta Quest Support** + **Hand Tracking Subsystem** ativos.

> Há um script de Editor que aplica as configurações de XR/Player/Quality automaticamente: **Tools → Goalkeeper VR → Setup XR Project** (e **Validate Setup** para conferir).

---

## Como testar SEM headset

Você **não precisa de um Quest** para testar a lógica do jogo. Use o **XR Device Simulator** (vem com o XR Interaction Toolkit):

1. Abra a cena `Assets/Scenes/Goalkeeper.unity`.
2. Garanta que o **XR Device Simulator** está ativo na cena (GameObject do prefab `XR Device Simulator`).
3. Entre em **Play**.
4. Pressione **H** para mudar para o modo de **input das mãos** (hand tracking simulado).
5. Pressione **TAB** para alternar o controle entre **câmera** e **mãos**.
6. Com **cada uma das mãos**, pressione **P** (gesto de "punho fechado" simulado) — faça para a mão esquerda e a direita.
7. Aguarde: ao detectar os dois punhos, o gol é calibrado e **a partida começa** sozinha.

> Dica: o **VR Debug Console** (painel em World Space na cena) mostra logs e FPS em tempo real — útil tanto no simulador quanto no headset, já que no build standalone não há console do Unity.

---

## Como rodar no Quest 3S

1. **Tools → Goalkeeper VR → Setup XR Project** (configura Android, Vulkan, Single Pass, etc.).
2. Confirme OpenXR + Meta Quest + Hand Tracking em **Project Settings → XR Plug-in Management → Android**.
3. **Bake do NavMesh** cobrindo todo o campo (componente `NavMesh Surface` no chão → Bake).
4. Conecte o Quest, **File → Build Settings → Build And Run** (ou gere o `.apk`).
5. No headset, **largue os controles** (o hand tracking só ativa sem controles), abra os braços e feche os punhos para calibrar.

---

## Estrutura do projeto

```
Assets/
├─ Scenes/Goalkeeper.unity      # cena principal
├─ Scripts/                     # toda a lógica (ver abaixo)
├─ Editor/XRProjectSetup.cs     # setup automático de XR (Editor only)
├─ Prefabs/                     # Attacker (aliado) e Attacker Enemy
├─ Models/Attacker, Attacker Enemy   # FBX dos jogadores + animações (Mixamo)
├─ Materials/                   # Attacker, Attacker Enemy, Ball, Crowd, Goal, Skybox
├─ Materials/Crowd/             # Crowd Idle.png, Crowd Cheer.png (torcida)
├─ Sounds/                      # Kick, Pass, Goal, Save, Whistle, Background
├─ BlenderScripts/              # build_player.py, build_crowd.py (geradores opcionais)
└─ XR / XRI / Settings          # configurações de XR e URP
```

---

## Arquitetura e scripts

Toda a lógica está em `Assets/Scripts/`. Visão por responsabilidade e **estado**:

### Núcleo do goleiro (Parte 1)

| Script | Papel | Estado |
|---|---|---|
| `XRProjectSetup.cs` | (Editor) Configura XR/Player/Quality para Quest. | ✅ Pronto |
| `GoalCalibrator.cs` | Calibra o gol pela envergadura (punhos fechados). Largura e altura com mín/máx; margem para a mão alcançar as traves. | ✅ Pronto |
| `HandTrackingCatch.cs` | Agarre/espalmar por hand tracking. Segue a palma, _catch-assist_, janela de tolerância de 300 ms, squash da bola. | ✅ Pronto (ajuste de _feel_) |
| `VRDebugConsole.cs` | Console em World Space que captura todos os logs + FPS. | ✅ Pronto |

### Simulação de partida (Parte 2)

| Script | Papel | Estado |
|---|---|---|
| `SoccerTypes.cs` | Enums (`Team`, `Role`, `DecisionType`) e struct `Decision`. **Não é anexado a nada.** | ✅ Pronto |
| `AISoccerBrain.cs` | Lógica de decisão pura (4-3-3, perseguir/barreira, chutar/passar/driblar). **Estático — não é anexado a nada.** | ✅ Pronto |
| `SoccerPlayer.cs` | Agente por jogador: NavMesh, drible, chute/passe, 2 cores (camisa/pele), hooks de Animator. **Vai NO PREFAB do jogador.** | ✅ Pronto (animação a integrar) |
| `MatchManager.cs` | Cérebro da partida: posse, passes, chutes (com windup + indicador), troca de posse, time-slicing dos 21 agentes, kickoff/reset. | ✅ Pronto (ajuste) |
| `TeamSpawner.cs` | Instancia os dois times em 4-3-3 (10+10) + goleiro inimigo estático. O goleiro aliado é o humano. | ✅ Pronto |
| `TeamNerfManager.cs` | "Nerfa" o time aliado (atraso, erro, raio de roubo reduzido) para o inimigo atacar. | ✅ Pronto |
| `ShotIndicator.cs` | Indicador de mira no céu antes do chute. | ✅ Pronto |
| `CrowdMember.cs` / `CrowdManager.cs` | Torcida em PNG: parte comemora em gol, parte em defesa. | ✅ Pronto (montar na cena) |
| `GameManager.cs` | Placar, detecção de gol/defesa no gol do jogador, áudio, feedback, ciclo de partida. | ✅ Pronto |
| `EnemyShooter.cs` | **Legado.** Atacante único da Fase 3 (anterior à simulação completa). Mantido como fallback; ignorado quando há `MatchManager`. | ⚠️ Legado |

### Fluxo de uma jogada

```
Calibrou o gol (GoalCalibrator.OnGoalCalibrated)
   → GameManager.StartMatch()  →  MatchManager.KickOff()  (inimigo com a posse)
       → IA: posse / passes / drible (evasão) rumo ao gol do jogador
           → atacante para (windup) + indicador no céu → CHUTE
               → você defende (agarra/espalma)  → GameManager: DEFESA / GOL / FORA
                   → torcida comemora (grupo certo) → reset → próxima jogada
```

---

## Montagem da cena

Objetos e ligações esperados na `Goalkeeper.unity`:

- **XR Origin (VR)** + **Hand Visualizer** (ou XRHandSkeletonDriver E/D) + **XR Device Simulator**.
- **Goal Root** (modelo do gol) com **traves** filhas (Post Left / Post Right) e **GoalLine**.
- **Left/Right Hand Catcher** — objetos vazios com `HandTrackingCatch` (`Is Left Hand` correto). Eventos: `OnCatch → GameManager.OnBallCaught`, `OnParry → GameManager.OnBallParried`.
- **Ball** — Rigidbody (massa 0.45, Continuous Dynamic) + Sphere Collider + tag **`Ball`** + Physics Material.
- **Managers:**
  - `GameManager` → referências: Match Manager, Ball, Goal Line, Post Left/Right, Left/Right Hand, Crowd Manager, Audio Source, clips, UI de placar/mensagem.
  - `MatchManager` → Spawner, Ball, Nerf, GameManager, Player/Enemy Goal Center, larguras, **Shot Indicator**, **Sfx** (AudioSource) + Kick/Pass clips, Debug Text.
  - `TeamSpawner` → **Player Prefab** (Attacker), Field Center/Length/Width, cores, tons de pele, goleiro inimigo.
  - `TeamNerfManager`, `ShotIndicator` (GameObject vazio + script), `CrowdManager`.
- **NavMesh Surface** no chão, **assado**.

> **Confirmações de uso:**
> - **ShotIndicator**: sim — basta um GameObject vazio (em qualquer posição) com o script, ligado ao `MatchManager`. Ele se reposiciona sozinho ao mostrar a mira.
> - **AISoccerBrain / SoccerTypes**: corretos sem anexar a nada (lógica/estática).
> - **SoccerPlayer**: deve ficar **no prefab** do jogador, com `teamRenderer` = o SkinnedMeshRenderer do modelo e os índices de material de camisa/pele (ou `-1` para não tingir, se cada time já tem material próprio).

---

## Estado por fase

Baseado no plano técnico do projeto.

**Parte 1 — Fatia vertical do goleiro**
- [x] Fase 0 — Setup do projeto (XR/OpenXR/Hand Tracking/Android)
- [x] Fase 1 — Calibração dinâmica do gol
- [x] Fase 2 — Bola + agarre/espalmar por hand tracking
- [x] Fase 3 — Atacante + chute + placar (loop completo)
- [x] Fase 4 — Feedback (flash, squash, sons, mensagens)

**Parte 2 — Ecossistema completo**
- [x] Fase 5 — Port da IA (4-3-3, perseguir/barreira, posse) → `AISoccerBrain`
- [x] Fase 6 — 21 agentes + time-slicing + nerf do time aliado
- [x] Fase 7 (código) — Sons, indicador de mira, ataque scriptado, torcida
- [ ] **Fase 7 (assets)** — Integrar **animações** (idle / andar / chute) nos modelos via Animator
- [ ] Espalhar os PNGs da torcida na arquibancada e ligar ao `CrowdManager`
- [ ] Passe de **otimização** no headset (medir 72 fps com 21 agentes)
- [ ] Ajuste final de **dificuldade** e _feel_

---

## Pipeline de assets

### Modelos de jogador (FBX, Mixamo)
- Prefabs `Attacker` (aliado) e `Attacker Enemy`. O `TeamSpawner` usa o prefab atribuído.
- **Animações necessárias (mínimo):** `idle`, `andar/jog`, `chute`. As demais são opcionais.
- **Animator Controller a montar:** um bool **`IsRunning`** (idle ⇄ andar) e um trigger **`Shoot`** (chute). O `SoccerPlayer` já dispara esses nomes (configuráveis no Inspector: `runBool`, `shootTrigger`, `passTrigger`).
- **2 cores (camisa/pele):** se quiser tingir por instância, o modelo precisa de 2 slots de material (camisa = índice 0, pele = índice 1). Se cada time já tem material próprio, deixe `skinMaterialIndex = -1` para não sobrescrever.

### Torcida (PNG)
- `Materials/Crowd/Crowd Idle.png` e `Crowd Cheer.png`.
- Espalhe quads/sprites pela arquibancada; em cada um, adicione **`CrowdMember`** (modo SpriteSwap, MaterialSwap ou ObjectToggle) com as imagens idle/cheer.
- Adicione um **`CrowdManager`** (pai dos torcedores ou com a lista preenchida). Ele divide a torcida em grupo **GOL** e grupo **DEFESA** (`saveGroupFraction`) e comemora com parte deles nos eventos certos. Ligue-o no `GameManager` (campo **Crowd Manager**).

### Sons
- `Kick`, `Pass` → `MatchManager` (Sfx). `Goal`, `Save`, `Whistle` → `GameManager`. `Background` → AudioSource ambiente em loop.

### Modelos gerados por script (opcional)
- `BlenderScripts/build_player.py` e `build_crowd.py` criam versões low-poly (humano 2-cores; torcedor com animação de braço). Rode no Blender (Scripting → Run), exporte `.glb`. Úteis como _placeholder_ ou fallback.

---

## Parâmetros de ajuste

Tudo exposto no Inspector. Os que mais mudam o _feel_:

- **Agarre** (`HandTrackingCatch`): `palmTriggerRadius`, `catchStartThreshold`, `releaseThreshold`, `throwSpeedThreshold`, `toleranceWindow`.
- **Calibração** (`GoalCalibrator`): `minGoalWidth/maxGoalWidth`, `minGoalHeight/maxGoalHeight`, `widthMargin`, `closeThreshold`.
- **Partida** (`MatchManager`): `shootingRange`, `controlRadius`, `tackleRadius`, `kickSpeed`, `passSpeed`, `shootWindup`, `dribbleEvadeStrength`, `activeBrainCount`, `brainSliceInterval`.
- **Dificuldade/nerf** (`TeamNerfManager`): `decisionDelay`, `dribbleErrorChance`, `speedMultiplier`, `allyTackleRadiusMultiplier`.

---

## Problemas conhecidos

- **Espalmar não desvia fisicamente:** o colisor da palma é _trigger_, então o evento `OnParry` dispara mas a bola não é fisicamente defletida. Para defesa física real, adicionar um colisor sólido na palma (a definir).
- **Performance não medida no headset:** os 21 agentes + física + hand tracking precisam de medição de 72 fps no Quest. Use o FPS do `VRDebugConsole`; ajuste `brainSliceInterval`/`activeBrainCount` e LOD.
- **Animações ainda não ligadas:** os FBX existem, falta montar o Animator (idle/andar/chute) e referenciar no `SoccerPlayer`.
- **Blocos grandes não 100% testados em build:** a simulação de partida e o ataque scriptado nasceram de iterações sem teste em headset a cada passo — esperar ajuste de _feel_.

---

## Publicação no GitHub

> **Atenção — Git LFS é obrigatório.** O projeto contém arquivos grandes (ex.: `Assets/Models/Stadium.glb` ~350 MB) acima do limite de **100 MB/arquivo** do GitHub. Eles só sobem via **Git LFS**.

**1. Coloque na RAIZ do repositório** (um nível acima de `Assets/`): este `README.md`, mais `.gitignore` e `.gitattributes`. Versione `Assets/`, `Packages/`, `ProjectSettings/` (NUNCA `Library/`).

**2. `.gitignore`** (padrão Unity):

```gitignore
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
[Mm]emoryCaptures/
*.csproj
*.sln
*.apk
*.aab
.vs/
.idea/
.DS_Store
```

**3. `.gitattributes`** (Git LFS para binários grandes):

```gitattributes
* text=auto
*.glb     filter=lfs diff=lfs merge=lfs -text
*.fbx     filter=lfs diff=lfs merge=lfs -text
*.hdr     filter=lfs diff=lfs merge=lfs -text
*.wav     filter=lfs diff=lfs merge=lfs -text
*.png     filter=lfs diff=lfs merge=lfs -text
*.tga     filter=lfs diff=lfs merge=lfs -text
*.psd     filter=lfs diff=lfs merge=lfs -text
*.texture2D filter=lfs diff=lfs merge=lfs -text
```

**4. Comandos** (no terminal, na raiz do projeto), após instalar **git** e **Git LFS**:

```bash
git lfs install
git init
git add .gitattributes
git add .
git commit -m "Goalkeeper VR — protótipo jogável (Partes 1 e 2)"
git branch -M main
git remote add origin https://github.com/<seu-usuario>/<repo>.git
git push -u origin main
```

> **Limites do LFS gratuito:** o GitHub Free dá ~1 GB de armazenamento e ~1 GB/mês de banda em LFS. O estádio (350 MB) cabe, mas muitos clones podem estourar a banda. Alternativa: hospedar o `Stadium.glb` fora (Releases/Drive) e mantê-lo no `.gitignore`, documentando o download à parte.

Colaboradores: clonam (`git lfs install` antes), abrem no **Unity 6** (a `Library/` é regenerada) e seguem este README.

---

## Como contribuir

- Use o **VR Device Simulator** para iterar rápido sem headset (fluxo H → TAB → P/P).
- Cada script tem cabeçalho explicando seu papel; a lógica de IA é **pura** em `AISoccerBrain` (fácil de testar/ajustar sem render).
- Mantenha logs via `Debug.Log` — eles aparecem no `VRDebugConsole` dentro do jogo.
- Antes de PR: rode a cena no simulador e confira que o ciclo (calibrar → atacar → defender → placar → reset) roda sem travar.

---

_Projeto desenvolvido na Unity 6 (URP) para Meta Quest 3S, com hand tracking via OpenXR._
