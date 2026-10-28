# Tick Tock Trouble — 프로젝트 구조 분석

> 작성일: 2026-10-28 · 기준 커밋: `ef56d0a` (Character prefab size revise)

## 1. 개요

| 항목 | 내용 |
|---|---|
| 엔진 | Unity **6000.3.10f1** (Unity 6.3) |
| 렌더 파이프라인 | URP 17.3.0 (PC / Mobile 에셋 분리) |
| 입력 | 구 Input Manager(`Input.GetKey…`) + 새 Input System(`Keyboard.current`) 혼용 |
| 빌드 씬 | `Assets/Scenes/SampleScene.unity` 1개 |
| 장르(코드 기준) | 비대칭 3D 액션 — 태엽 장난감 **TickToy**(3인칭) vs 술래 **Winder**(1인칭) |

### 게임 컨셉 요약 (코드에서 추론)

- **TickToy (Jephy / Lulu / Momo / Volt)**: 3인칭(TPS) 시점. 제자리에서 마우스 좌클릭을 누르고 있으면 태엽을 감아 게이지를 채우고, 다 채우면 랜덤 능력(달리기 / 기절 / 분신)을 얻거나 레벨업(최대 3). Winder 근처에서 `E`를 누르면 Winder를 5초 동안 정지시킴.
- **Winder (Brick)**: 1인칭(FPS) 시점. 몽둥이로 공격(데미지 30)해 TickToy를 처치. 등에 달린 태엽(Spring)이 10초마다 한 바퀴 돌며 능력(달리기 / 기절)을 얻음.
- HUD: 인원 수, 라운드, 남은 시간, HP, 태엽 게이지, 능력 아이콘/레벨 표시.

## 2. 최상위 폴더

```
Tick Tock Trouble/
├── Assets/                 ← 실제 게임 리소스와 코드 (아래 3절)
├── Packages/               ← manifest.json (패키지 의존성)
├── ProjectSettings/        ← 프로젝트 설정, 빌드 씬 목록
├── Library/ Temp/ Logs/ UserSettings/ .vs/   ← 자동 생성 (.gitignore 대상)
├── Assembly-CSharp*.csproj, *.sln, *.slnx    ← IDE용 자동 생성 파일
├── .gitignore / .gitattributes / .vsconfig
└── PROJECT_STRUCTURE.md    ← 이 문서
```

## 3. Assets 폴더 구조

```
Assets/
├── Scripts/            게임 로직 C# 스크립트 (10개, 약 4,000줄)
├── Scenes/
│   ├── SampleScene.unity         메인(유일) 게임 씬
│   └── SampleScene/NavMesh-Plane.asset
├── Prefabs/            캐릭터·소품 프리팹
├── Models/             캐릭터/무기 FBX + 텍스처 (Meshy AI 생성 텍스처 포함)
│   ├── Brick, Jephy, Lulu, Momo, Spring, Volt .fbx
│   ├── BrickWeapon/, BrickGoldWeapon/   무기 모델
│   └── texture/<모델명>/                albedo / metallic / normal / roughness
├── Materials/          캐릭터·무기·소품 머티리얼
├── Animations/
│   ├── *.fbx           ATTACK, DIE_T, DIE_W, GetHit, IDLE, IDLE2, JUMP, PRONE, RUN, TickToyRUN, WIND, WindPause
│   ├── Animator/       TickToyAnimator.controller, WinderAnimator.controller
│   └── Mask/           상체/점프/태엽/피격용 Avatar Mask
├── Resources/
│   └── BGM/            배경음악 mp3 (런타임에 Resources.LoadAll로 자동 재생)
├── Settings/           URP 에셋(PC/Mobile), 렌더러, 볼륨 프로필
├── ExternalAsset/      외부(스토어) 에셋
│   ├── Kevin Iglesias/      Human Animations (애니메이션 팩)
│   ├── POLYGON city pack/   맵용 도시 환경 에셋 (~1,000개 파일)
│   └── WinderStartingPos/   Winder 시작 위치 관련 에셋
├── Kevin Iglesias/     Dwarf Animations (ExternalAsset 밖에 따로 있음)
├── TutorialInfo/       URP 템플릿 기본 Readme (게임과 무관)
├── Readme.asset        URP 템플릿 기본 Readme
├── InputSystem_Actions.inputactions   템플릿 기본 Input Actions
└── _Recovery/          Unity 크래시 복구용 씬 백업 (게임과 무관)
```

## 4. 스크립트 상세 (`Assets/Scripts`)

| 파일 | 줄 수 | 역할 |
|---|---:|---|
| `TicktoyMove.cs` (`TickToyMove`) | 1,469 | TickToy 이동·점프·엎드리기·TPS 카메라·계단/턱 오르기·능력(달리기/기절/분신)·피격/사망 |
| `WinderMove.cs` | 1,176 | Winder 이동·점프·FPS 카메라·팔 조준·태엽 회전으로 능력 획득·정지(Freeze)·기절 |
| `TickToyHud.cs` | 440 | UI: 상태(인원/라운드/시간/HP), 태엽 게이지, 능력 아이콘·레벨, "E" 상호작용 프롬프트 |
| `TickToyAnimation.cs` | 387 | TickToy Animator 제어: 태엽 감기, WindPause, 기절, 피격, 사망, 발 고정(IK 보정) |
| `WinderAnimation.cs` | 250 | Winder Animator 제어: 공격 판정(OverlapBox + Trigger), 점프, 기절 |
| `ArmpitSeparator.cs` | 165 | 정적 유틸. 휴머노이드 메시의 겨드랑이 본 웨이트를 런타임에 분리해 메시 늘어짐 방지 |
| `BackgroundMusic.cs` | 53 | 씬 로드 후 자동 생성되어 `Resources/BGM` 음악을 순서대로 반복 재생 |
| `JephyModelController.cs` / `LuluModelController.cs` / `MomoModelController.cs` | 13 each | `Awake`에서 `ArmpitSeparator.Apply` 호출 (내용 동일) |

### 상태 머신

- `TickToyMove.TickToyState`: `Play`, `Run`, `Faint`, `Dead`
- `WinderMove.WinderState`: `Play`, `Faint`, `Dead`
- `TickToyHud.AbilityKind`: `None`, `Run`, `Faint`, `Duplicate`

### 능력 수치

| 능력 | Lv1 | Lv2 | Lv3 | 사용 가능 |
|---|---|---|---|---|
| 달리기 (Run) | 5초 | 8초 | 12초 | TickToy, Winder |
| 기절 (Faint) | 10초 | 20초 | 무제한 | TickToy, Winder |
| 분신 (Duplicate) | 3초 / 1개 | 5초 / 2개 | 7초 / 4개 | TickToy |

### 스크립트 간 의존 관계

```
TickToyMove ◄──► TickToyAnimation          WinderMove ◄──► WinderAnimation
     │  ▲               │ E키: Freeze(5s)         │                  │
     │  └──────────────────────────── TakeHit(30) ┼──────────────────┘
     ▼                  ▼                         ▼
                  TickToyHud  ◄── PresentAbility / ClearPresentedAbility
                  (FindAnyObjectByType / FindObjectsByType 로 서로 검색)

*ModelController ──► ArmpitSeparator (static)
BackgroundMusic   (독립, RuntimeInitializeOnLoadMethod)
```

참조는 대부분 `GetComponent` / `FindAnyObjectByType` 로 런타임 검색하며, 별도의 GameManager나 이벤트 버스는 없습니다.

## 5. 프리팹 ↔ 스크립트 매핑

| 프리팹 | 역할 | 붙은 스크립트 |
|---|---|---|
| `Brick.prefab` | Winder (술래) | `WinderMove`, `WinderAnimation` |
| `Jephy.prefab` | TickToy | `TickToyMove`, `TickToyAnimation` |
| `Lulu.prefab` | TickToy | `TickToyMove`, `TickToyAnimation` |
| `Momo.prefab` | TickToy | `TickToyMove`, `TickToyAnimation`, `MomoModelController` |
| `Volt.prefab` | TickToy | `TickToyMove`, `TickToyAnimation` |
| `Spring.prefab` | 태엽 소품 | (없음) |

## 6. 씬 구성 (`SampleScene.unity`)

- 캐릭터 프리팹 인스턴스: Brick, Jephy, Lulu, Momo, Volt
- 환경: `Plane`(NavMesh 베이크됨), `Cube`, `Capsule`, `Track`, `WinderStartingWall`, `ReadyCollider`
- UI: `Canvas` → `Count`, `Round`, `Time`, `Hp`, `Level`, `WindGauge`(`Fill`, `WindText`), `Ability`(`AbilityIcon`, `AbilityTitle`) + `TickToyHud`
- 기타: `Main Camera`, `Directional Light`, `Global Volume`, `EventSystem`

## 7. 애니메이터

| 컨트롤러 | 레이어 | 파라미터 |
|---|---|---|
| `TickToyAnimator` | Base, Hit, Jump, Wind | `State`, `MoveX`, `MoveY`, `IsProne`, `IsWinding`, `Jump`, `GetHit`, `WindPause`, `Die` |
| `WinderAnimator` | Base, Attack, Jump | `State`, `IsProne`, `Attack`, `Jump`, `Die` |

## 8. 조작키

| 키 | TickToy | Winder |
|---|---|---|
| WASD / 방향키 | 이동 | 이동 |
| 마우스 | 시점 회전 | 시점 회전 |
| 마우스 좌클릭 (제자리 유지) | 태엽 감기 | 공격 |
| Space | 점프 | 점프 |
| Left Ctrl | 엎드리기 | 엎드리기 |
| R | 능력 사용 | 능력 사용 |
| F | 뒤돌기 | 뒤돌기 |
| E | Winder 근처에서 정지시키기 / 능력 획득·레벨업 프롬프트 | — |
| **디버그** `1` `2` `3` | 테스트용 능력 부여 (Run/Faint/Duplicate) | — |
| **디버그** `H` / `K` | 피격 / 즉사 애니메이션 | `K`: 즉사 |

## 9. 패키지 (`Packages/manifest.json`)

주요 패키지: `render-pipelines.universal` 17.3.0, `inputsystem` 1.18.0, `ai.navigation` 2.0.10, `timeline` 1.8.10, `ugui` 2.0.0, `visualscripting`, `multiplayer.center`, `test-framework`, IDE 연동(Rider / Visual Studio).

## 10. 참고 및 정리 제안

- **템플릿 잔여물**: `TutorialInfo/`, `Readme.asset`, `InputSystem_Actions.inputactions`는 URP 템플릿 기본 파일로, 현재 코드에서 사용하지 않습니다.
- **`_Recovery/`**: Unity 크래시 복구 씬 백업입니다. 필요 없으면 삭제하고 `.gitignore`에 추가하는 것을 권장합니다.
- **ModelController 중복**: `Jephy/Lulu/MomoModelController`는 내용이 같고, 프리팹에는 `MomoModelController`만 붙어 있습니다. 하나의 공용 컴포넌트로 합칠 수 있습니다.
- **에셋 위치 불일치**: `Kevin Iglesias/Dwarf Animations`가 `ExternalAsset/` 밖에 있습니다.
- **파일명/클래스명 불일치**: `TicktoyMove.cs` 안의 클래스 이름은 `TickToyMove`입니다 (대소문자 차이).
- **입력 처리 혼용**: 구 `Input` API와 새 Input System을 함께 사용합니다. Player Settings의 Active Input Handling이 `Both`로 설정되어 있어야 동작합니다.
- **디버그 키**: `H`, `K`, `1~3` 키는 테스트용이므로 출시 전에 제거하거나 `#if UNITY_EDITOR`로 감싸는 것이 좋습니다.
