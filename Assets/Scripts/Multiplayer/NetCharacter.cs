using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>매 틱 내 캐릭터 상태를 방장에게 보내는 입력 (Fusion NetworkInput)</summary>
public struct NetPoseInput : INetworkInput
{
    public Vector3 Pos;
    public float Yaw;
    public int Info; // kind | ch << 8 | alive << 16 | valid << 17
    public int Flags; // NetPose.Flag* (정지·달리기·황금 몽둥이·덫)
    public int H0, H1, H2, H3, H4, H5;
    public float W1, W2, W3, W4, W5;
    public float P0, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11;
    public byte ParamCount;
    public int Spectate; // 죽은 틱토이가 관전 중인 틱토이 플레이어 id (0 = 안 봄)
    public int Round;    // 이 상태를 보낸 라운드 (이전 라운드에 죽은 상태가 새 라운드에 남지 않게)
    public int Hud;      // 관전자에게 보여줄 내 화면 정보 (NetCharacter.PackHud)
    public float HudWind;  // 태엽 바 진행 (0~1, 안 감으면 -1)
    public float HudTimer; // 능력 시계 남은 비율 (0~1, 없으면 -1)
    public float Spring;   // 와인더 등 태엽 각도 (0~360)

    public bool Valid => (Info & (1 << 17)) != 0;

    public static NetPoseInput From(NetPose p)
    {
        NetPoseInput i = new NetPoseInput
        {
            Pos = p.pos,
            Yaw = p.yaw,
            Info = p.kind | (p.ch << 8) | ((p.alive ? 1 : 0) << 16) | (1 << 17),
            Flags = p.flags,
            H0 = p.hashes[0], H1 = p.hashes[1], H2 = p.hashes[2], H3 = p.hashes[3], H4 = p.hashes[4], H5 = p.hashes[5],
            W1 = p.weights[1], W2 = p.weights[2], W3 = p.weights[3], W4 = p.weights[4], W5 = p.weights[5],
            ParamCount = (byte)Mathf.Min(p.prm.Length, NetPose.MaxParams)
        };
        float[] v = new float[NetPose.MaxParams];
        for (int k = 0; k < i.ParamCount; k++)
            v[k] = p.prm[k];
        i.P0 = v[0]; i.P1 = v[1]; i.P2 = v[2]; i.P3 = v[3]; i.P4 = v[4]; i.P5 = v[5];
        i.P6 = v[6]; i.P7 = v[7]; i.P8 = v[8]; i.P9 = v[9]; i.P10 = v[10]; i.P11 = v[11];
        return i;
    }

    public float Param(int k)
    {
        switch (k)
        {
            case 0: return P0; case 1: return P1; case 2: return P2; case 3: return P3;
            case 4: return P4; case 5: return P5; case 6: return P6; case 7: return P7;
            case 8: return P8; case 9: return P9; case 10: return P10; default: return P11;
        }
    }
}

/// <summary>
/// 네트워크로 동기화되는 캐릭터 하나 (사람 플레이어 또는 방장 쪽 맵 AI).
/// - 사람: 그 사람이 매 틱 입력으로 보낸 상태를 방장이 [Networked] 값에 기록 → Fusion 이 모두에게 전달
/// - AI: 방장이 실제 AI 상태를 매 틱 기록
/// - 다른 사람 화면: NetPuppet(조작 없는 복사본)을 만들어 그 상태로 보여줌
/// 매치 동안 씬이 바뀌어도 유지 (DontDestroyOnLoad), 보이는 복사본은 씬마다 다시 만듦.
/// </summary>
public class NetCharacter : NetworkBehaviour
{
    public static readonly Dictionary<int, NetCharacter> Players = new Dictionary<int, NetCharacter>();
    /// <summary>사람 + 맵 AI 전부 (id → 캐릭터). 와인더 Z 표시 대상 찾기에 사용</summary>
    public static readonly Dictionary<int, NetCharacter> ById = new Dictionary<int, NetCharacter>();
    const int AiIdBase = 10000;

    [Networked] public int NetId { get; set; }
    [Networked] public byte Kind { get; set; }
    [Networked] public byte Ch { get; set; }
    [Networked] public NetworkBool Alive { get; set; }
    [Networked] public NetworkBool HasPose { get; set; }
    [Networked] public int Flags { get; set; }
    [Networked] public Vector3 Pos { get; set; }
    [Networked] public float Yaw { get; set; }
    [Networked] public byte ParamCount { get; set; }
    /// <summary>이 플레이어가 관전 중인 틱토이 플레이어 id (0 = 안 봄) → 관전자 수 표시에 사용</summary>
    [Networked] public int Spectating { get; set; }
    /// <summary>방장이 기록하는 지금 라운드·단계·승수·생존 수. 참가자는 메시지 대신 이 값을 계속 따라감
    /// (메시지가 늦거나 빠져도 결과 화면에 멈추지 않음). 라운드와 단계는 항상 함께 바뀜</summary>
    [Networked] public int MatchRound { get; set; }
    [Networked] public byte MatchPhase { get; set; }
    [Networked] public float MatchPhaseLeft { get; set; }
    [Networked] public NetworkBool MatchWinderWon { get; set; }
    [Networked] public int MatchTickToyWins { get; set; }
    [Networked] public int MatchWinderWins { get; set; }
    [Networked] public byte MatchAliveTickToys { get; set; }
    [Networked] public byte MatchAliveWinders { get; set; }
    /// <summary>이 플레이어가 상태를 보낸 라운드 (방장이 생존 수를 셀 때 이전 라운드 값 무시)</summary>
    [Networked] public int PoseRound { get; set; }
    /// <summary>관전자 화면용: 능력·레벨·변장 남은 수·체력 (PackHud) / 태엽 바 / 능력 시계</summary>
    [Networked] public int Hud { get; set; }
    [Networked] public float HudWind { get; set; }
    [Networked] public float HudTimer { get; set; }
    /// <summary>와인더 등 태엽 각도 → 모두의 화면에서 같은 각도로 돌아감</summary>
    [Networked] public float SpringDeg { get; set; }
    [Networked, Capacity(6)] public NetworkArray<int> Hashes => default;
    [Networked, Capacity(6)] public NetworkArray<float> Weights => default;
    [Networked, Capacity(12)] public NetworkArray<float> Params => default;

    /// <summary>방장 쪽: 이 오브젝트가 따라 기록할 실제 맵 AI</summary>
    [System.NonSerialized] public TickToyAI SourceAI;

    NetPuppet puppet;
    /// <summary>다른 사람 화면에 보이는 이 캐릭터의 복사본 (관전 카메라가 따라감)</summary>
    public NetPuppet Puppet => puppet;
    NetPose pose;

    bool IsLocalPlayer => NetId < AiIdBase && FusionMatchBackend.Instance != null && NetId == FusionMatchBackend.Instance.LocalPlayerId;

    public override void Spawned()
    {
        Runner.MakeDontDestroyOnLoad(gameObject);
        if (NetId < AiIdBase)
            Players[NetId] = this;
        ById[NetId] = this;
        pose = new NetPose
        {
            hashes = new int[NetPose.MaxLayers],
            weights = new float[NetPose.MaxLayers],
            prm = new float[NetPose.MaxParams]
        };
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Players.TryGetValue(NetId, out NetCharacter nc) && nc == this)
            Players.Remove(NetId);
        if (ById.TryGetValue(NetId, out NetCharacter any) && any == this)
            ById.Remove(NetId);
        if (puppet != null)
            Destroy(puppet.gameObject);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        // 방장: 맵 AI
        if (NetId >= AiIdBase)
        {
            if (SourceAI == null || !SourceAI.isActiveAndEnabled)
            {
                HasPose = false;
                return;
            }
            Write(NetPose.Capture(SourceAI.gameObject, NetId, NetPose.KindAI, Ch, !SourceAI.IsDead,
                SourceAI.HitFaceActive ? NetPose.FlagHitFace : 0));
            return;
        }

        // 방장: 지금 라운드 상태 (사람 캐릭터에만). 라운드 번호는 지금 맵의 번호로 → 다음 맵을 불러오는 동안
        // '새 라운드 번호 + 이전 라운드 결과 화면' 이 섞여 보내지지 않음
        RoundManager rm = RoundManager.Instance;
        if (MatchContext.Active && rm != null && FusionMatchBackend.Instance != null)
        {
            MatchRound = rm.RoundNumber;
            MatchPhase = (byte)rm.PhaseIndex;
            MatchPhaseLeft = rm.PhaseLeft;
            MatchWinderWon = rm.ResultWinderWon;
            MatchTickToyWins = MatchContext.TickToyWins;
            MatchWinderWins = MatchContext.WinderWins;
            MatchAliveTickToys = (byte)FusionMatchBackend.Instance.AliveCount(false, 0);
            MatchAliveWinders = (byte)FusionMatchBackend.Instance.AliveCount(true, 0);
        }

        // 방장: 사람 플레이어 (본인이 보낸 입력). 관전만 하는 사람(게임 중에 들어옴)은 관전 대상만 옴
        if (GetInput(out NetPoseInput spec) && !spec.Valid)
            Spectating = spec.Spectate;
        if (GetInput(out NetPoseInput inp) && inp.Valid)
        {
            Pos = inp.Pos;
            Yaw = inp.Yaw;
            Kind = (byte)(inp.Info & 0xFF);
            Ch = (byte)((inp.Info >> 8) & 0xFF);
            Alive = ((inp.Info >> 16) & 1) != 0;
            Flags = inp.Flags;
            Hashes.Set(0, inp.H0);
            Hashes.Set(1, inp.H1);
            Hashes.Set(2, inp.H2);
            Hashes.Set(3, inp.H3);
            Hashes.Set(4, inp.H4);
            Hashes.Set(5, inp.H5);
            Weights.Set(1, inp.W1);
            Weights.Set(2, inp.W2);
            Weights.Set(3, inp.W3);
            Weights.Set(4, inp.W4);
            Weights.Set(5, inp.W5);
            ParamCount = inp.ParamCount;
            for (int k = 0; k < NetPose.MaxParams; k++)
                Params.Set(k, inp.Param(k));
            Spectating = inp.Spectate;
            PoseRound = inp.Round;
            Hud = inp.Hud;
            HudWind = inp.HudWind;
            HudTimer = inp.HudTimer;
            SpringDeg = inp.Spring;
            HasPose = true;
        }
    }

    // ---- 관전자 화면 정보 묶기: 능력 종류 3비트 | 레벨 2비트 | 변장 남은 수 4비트 | 체력 8비트 ----
    public static int PackHud(int kind, int level, int disguiseLeft, int hp) =>
        (kind & 7) | ((level & 3) << 3) | ((Mathf.Clamp(disguiseLeft, 0, 15) & 15) << 5) | ((Mathf.Clamp(hp, 0, 255) & 255) << 9);
    public int HudKind => Hud & 7;
    public int HudLevel => (Hud >> 3) & 3;
    public int HudDisguise => (Hud >> 5) & 15;
    public int HudHp => (Hud >> 9) & 255;

    /// <summary>id 플레이어를 관전 중인 사람 수</summary>
    public static int SpectatorCount(int id)
    {
        if (id == 0)
            return 0;
        int n = 0;
        foreach (NetCharacter nc in Players.Values)
        {
            if (nc != null && nc.Object != null && nc.Object.IsValid && nc.NetId != id && nc.Spectating == id)
                n++;
        }
        return n;
    }

    void Write(NetPose p)
    {
        Pos = p.pos;
        Yaw = p.yaw;
        Alive = p.alive;
        Flags = p.flags;
        for (int l = 0; l < NetPose.MaxLayers; l++)
        {
            Hashes.Set(l, p.hashes[l]);
            Weights.Set(l, p.weights[l]);
        }
        ParamCount = (byte)Mathf.Min(p.prm.Length, NetPose.MaxParams);
        for (int k = 0; k < ParamCount; k++)
            Params.Set(k, p.prm[k]);
        HasPose = true;
    }

    public override void Render()
    {
        // 내 캐릭터는 내가 직접 조작 / 방장 화면의 맵 AI 는 진짜 AI 가 보임
        if (IsLocalPlayer || (HasStateAuthority && NetId >= AiIdBase))
            return;
        if (!HasPose || RoundManager.Instance == null)
        {
            if (puppet != null && !HasPose)
                puppet.gameObject.SetActive(false);
            return;
        }

        pose.id = NetId;
        pose.kind = Kind;
        pose.ch = Ch;
        pose.alive = Alive;
        pose.flags = Flags;
        pose.pos = Pos;
        pose.yaw = Yaw;
        pose.spring = SpringDeg;
        for (int l = 0; l < NetPose.MaxLayers; l++)
        {
            pose.hashes[l] = Hashes[l];
            pose.weights[l] = Weights[l];
        }
        int n = Mathf.Min(ParamCount, NetPose.MaxParams);
        if (pose.prm.Length != n)
            pose.prm = new float[n];
        for (int k = 0; k < n; k++)
            pose.prm[k] = Params[k];

        // 라운드가 바뀌어 캐릭터가 달라졌으면 복사본을 새로 만듦
        if (puppet != null && (puppet.Kind != Kind || puppet.Ch != Ch))
        {
            Destroy(puppet.gameObject);
            puppet = null;
        }

        if (puppet == null)
        {
            puppet = NetPuppet.Create(PrefabFor(Kind, Ch), pose);
            if (puppet == null)
                return;
        }
        else if (!puppet.gameObject.activeSelf)
            puppet.gameObject.SetActive(true);
        puppet.SetTarget(pose);
    }

    static GameObject PrefabFor(byte kind, byte ch)
    {
        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs == null)
            return null;
        if (kind == NetPose.KindWinder)
            return prefabs.winderPrefab;
        if (prefabs.tickToyPrefabs == null || prefabs.tickToyPrefabs.Length == 0)
            return null;
        return prefabs.tickToyPrefabs[Mathf.Clamp(ch, 0, prefabs.tickToyPrefabs.Length - 1)];
    }
}
