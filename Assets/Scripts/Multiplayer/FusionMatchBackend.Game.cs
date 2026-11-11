using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>게임 시작·라운드·방 복귀, 생존 수, 채팅, 피격 전달, 네트워크 캐릭터(NetCharacter) 스폰</summary>
public partial class FusionMatchBackend : MonoBehaviour, IMatchBackend, INetworkRunnerCallbacks
{
    // ---------------- 게임 시작 / 라운드 / 방으로 ----------------

    public void StartGame()
    {
        if (!IsHost)
            return;
        // 모두 준비할 필요 없이, 양쪽 팀에 준비된 사람(방장 포함)이 한 명씩만 있으면 시작 (1 vs 1 부터)
        if (ReadyCount(RoomPlayer.TeamTickToy) < 1 || ReadyCount(RoomPlayer.TeamWinder) < 1)
        {
            Error?.Invoke("틱토이와 와인더에 준비된 사람이 한 명씩은 있어야 해요.");
            return;
        }

        MapEntry map = MapCatalog.Load()?.Get(current.mapIndex);
        if (map == null)
            return;

        MatchContext.BeginMatch(current, players, LocalPlayerId);
        UpdateSessionProps();
        SendToClients(Msg(H2C_Start, w =>
        {
            WriteRoom(w);
            w.Write(map.sceneName);
        }));
        LoadMatchScene(map.sceneName);
    }

    public void LoadMap(string sceneName)
    {
        if (!IsHost)
            return;
        RefreshMatchRoster();
        SendToClients(Msg(H2C_LoadMap, w =>
        {
            w.Write(MatchContext.Round);
            w.Write(MatchContext.TickToyWins);
            w.Write(MatchContext.WinderWins);
            w.Write(sceneName);
        }));
        LoadMatchScene(sceneName);
    }

    public void ReturnToRoom()
    {
        if (!IsHost)
            return;
        SendToClients(Msg(H2C_Return, w => { }));
        DoReturnToRoom();
        foreach (RoomPlayer p in players)
            p.ready = p.isHost;
        BroadcastRoom();
    }

    void DoReturnToRoom()
    {
        matchSawNet = false;
        lostNetSeconds = 0f;
        MatchContext.End();
        ClearGameState();
        HostDespawnAll(true);
        SceneManager.LoadSceneAsync(MatchService.LobbyScene);
    }

    /// <summary>씬을 불러오는 중 (다음 라운드 맵 로딩 중복 방지)</summary>
    bool sceneLoading;
    float sceneLoadingSince;

    void LoadMatchScene(string sceneName)
    {
        sceneLoading = true;
        sceneLoadingSince = Time.realtimeSinceStartup;
        ClearGameState();
        HostDespawnAll(false); // 맵 AI 는 새 맵에서 다시 만듦 (플레이어 캐릭터는 유지)
        try { GameLoading?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        SceneManager.LoadSceneAsync(sceneName);
    }

    void ClearGameState()
    {
        aiIds.Clear();
        aiById.Clear();
        nextAiId = AiIdBase;
        netAliveTickToys = -1;
        netAliveWinders = -1;
    }

    void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;
        sceneLoading = false;
        aiIds.Clear();
        aiById.Clear();
    }

    // ---------------- 게임 중 동기화 ----------------

    /// <summary>(참가자) 매 프레임: 방장이 네트워크 캐릭터에 기록한 라운드·단계를 그대로 따라감.
    /// 메시지는 늦거나 빠질 수 있지만 네트워크 값은 계속 갱신되므로, 인원이 많아도 모두 같은 라운드·단계에 있게 됨</summary>
    void ClientFollowNetwork()
    {
        // 맵 로딩이 끝나지 않고 멈춰 있으면(20초) 풀어서 다시 따라가게
        if (sceneLoading && Time.realtimeSinceStartup - sceneLoadingSince > 20f)
        {
            Debug.LogWarning("[Fusion] 맵 로딩이 너무 오래 걸려서 다시 시도해요.");
            sceneLoading = false;
        }

        NetCharacter src = null;
        foreach (NetCharacter nc in NetCharacter.Players.Values)
        {
            if (nc == null || nc.Object == null || !nc.Object.IsValid || nc.MatchRound <= 0)
                continue;
            if (src == null || nc.MatchRound > src.MatchRound)
                src = nc;
        }

        // 방장이 방으로 돌아가며 캐릭터를 모두 지웠는데 '방으로' 메시지를 놓친 경우 → 3초 뒤 따라서 방으로
        if (src == null)
        {
            if (matchSawNet && !sceneLoading)
            {
                lostNetSeconds += Time.unscaledDeltaTime;
                if (lostNetSeconds > 3f)
                {
                    Debug.LogWarning("[Fusion] 방으로 돌아가기 메시지를 놓쳐서 직접 돌아가요.");
                    DoReturnToRoom();
                }
            }
            return;
        }
        matchSawNet = true;
        lostNetSeconds = 0f;

        if (src.MatchRound > MatchContext.Round)
        {
            ClientCatchUpRound(src.MatchRound, src.MatchTickToyWins, src.MatchWinderWins);
            return;
        }
        if (src.MatchRound < MatchContext.Round || sceneLoading)
            return; // 방장이 아직 다음 맵을 불러오는 중

        netAliveTickToys = src.MatchAliveTickToys;
        netAliveWinders = src.MatchAliveWinders;
        RoundManager.Instance?.ApplyNetPhase(src.MatchPhase, src.MatchPhaseLeft, src.MatchWinderWon, src.MatchTickToyWins, src.MatchWinderWins);
    }

    bool matchSawNet;
    float lostNetSeconds;

    /// <summary>(참가자) 방장은 다음 라운드인데 나는 아직 이전 라운드 → 다음 라운드 맵을 직접 불러옴.
    /// 맵 로딩 메시지를 놓쳐 결과 화면에 멈춰 있는 일을 막음</summary>
    void ClientCatchUpRound(int hostRound, int tickToyWins, int winderWins)
    {
        if (IsHost || sceneLoading || !MatchContext.Active || hostRound <= MatchContext.Round)
            return;
        MapEntry map = MapCatalog.Load()?.Get(MatchContext.Settings.mapIndex);
        if (map == null)
            return;
        Debug.LogWarning("[Fusion] 다음 라운드 로딩 메시지를 놓쳐서 라운드 " + hostRound + " 맵을 직접 불러와요.");
        MatchContext.Round = hostRound;
        MatchContext.TickToyWins = tickToyWins;
        MatchContext.WinderWins = winderWins;
        RefreshMatchRoster();
        LoadMatchScene(map.sceneName);
    }

    /// <summary>새 라운드 시작 때 경기 명단 = 지금 방에 있는 사람 (게임 중에 들어온 사람은 이때부터 참가, 나간 사람은 빠짐)</summary>
    void RefreshMatchRoster()
    {
        MatchContext.Players.Clear();
        MatchContext.Players.AddRange(players);
        MatchContext.AssignRolesFromTeams();
    }

    int netAliveTickToys = -1;
    int netAliveWinders = -1;
    readonly HashSet<int> connectedIds = new HashSet<int>();

    /// <summary>살아있는 수. 방장은 직접 계산, 나머지는 방장이 보낸 값</summary>
    public int AliveCount(bool winders, int localAliveFallback)
    {
        if (!IsHost)
        {
            int v = winders ? netAliveWinders : netAliveTickToys;
            return v >= 0 ? v : localAliveFallback;
        }

        HashSet<int> connected = connectedIds;
        connected.Clear();
        foreach (PlayerRef p in runner.ActivePlayers)
            connected.Add(p.PlayerId);

        RoundManager rm = RoundManager.Instance;
        int n = 0;
        foreach (RoomPlayer p in MatchContext.Players)
        {
            bool isWinder = MatchContext.WinderIds.Contains(p.id);
            if (isWinder != winders)
                continue;
            if (p.id == LocalPlayerId)
            {
                if (rm != null && rm.LocalAlive)
                    n++;
                continue;
            }
            if (IsBotId(p.id))
            {
                n++; // 테스트 봇은 항상 살아있음 (오프라인 봇과 같음)
                continue;
            }
            if (!connected.Contains(p.id))
                continue;
            // 이번 라운드에 보낸 상태로만 판단 (이전 라운드에 쓰러진 값이 남아 있어도 살아있는 것으로)
            if (NetCharacter.Players.TryGetValue(p.id, out NetCharacter nc) && nc != null && nc.HasPose
                && nc.PoseRound == MatchContext.Round && !nc.Alive)
                continue;
            n++;
        }
        return n;
    }

    // ---------------- 채팅 ----------------

    public void SendChat(string text)
    {
        if (!InRoom || string.IsNullOrEmpty(text))
            return;
        if (IsHost)
            HostBroadcastChat(nickname, text);
        else
            SendToHost(Msg(C2H_Chat, w => w.Write(text)));
    }

    void HostBroadcastChat(string nick, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;
        if (text.Length > 50)
            text = text.Substring(0, 50);
        SendToClients(Msg(H2C_Chat, w =>
        {
            w.Write(nick ?? "");
            w.Write(text);
        }));
        ChatLog.Add(nick, text);
    }

    /// <summary>내 와인더가 다른 사람(또는 방장 쪽 AI)의 퍼펫을 때림</summary>
    public void SendHit(NetPuppet target, int damage)
    {
        if (target == null || !InRoom)
            return;
        if (IsHost)
            RouteHit(target.NetId, damage, LocalPlayerId);
        else
            SendToHost(Msg(C2H_Hit, w =>
            {
                w.Write(target.NetId);
                w.Write(damage);
            }));
    }

    /// <summary>맞은 사람에게 피해 전달 (때린 와인더 id 도 같이 → 쓰러지면 그 와인더의 '잡은 틱토이' 기록)</summary>
    void RouteHit(int targetId, int damage, int attackerId)
    {
        if (targetId >= AiIdBase)
        {
            if (aiById.TryGetValue(targetId, out TickToyAI ai) && ai != null && !ai.IsDead)
                ai.TakeHit(damage);
            return;
        }
        if (targetId == LocalPlayerId)
        {
            RoundManager.Instance?.ApplyNetHit(damage, attackerId);
            return;
        }
        foreach (PlayerRef p in runner.ActivePlayers)
        {
            if (p.PlayerId == targetId)
            {
                runner.SendReliableDataToPlayer(p, default(ReliableKey), Msg(H2C_Hit, w => { w.Write(damage); w.Write(attackerId); }));
                return;
            }
        }
    }

    void Update()
    {
        if (!InRoom || runner == null || !MatchContext.Active)
            return;
        if (!IsHost)
        {
            ClientFollowNetwork();
            return;
        }
        RoundManager rm = RoundManager.Instance;
        if (rm == null || rm.LocalPlayerObject == null)
            return;

        aiRefreshTimer += Time.unscaledDeltaTime;

        // 방장: 플레이어 캐릭터(매치 동안 유지)와 맵 AI 를 네트워크 오브젝트로.
        // 라운드·단계·생존 수는 각 플레이어 캐릭터(NetCharacter)에 매 틱 기록됨
        if (!playersSpawned)
            HostSpawnPlayers();
        if (aiRefreshTimer >= 1f)
        {
            aiRefreshTimer = 0f;
            RefreshAIs();
        }
    }

    // ---------------- 네트워크 캐릭터 (Fusion NetworkObject) ----------------

    GameObject NetCharacterPrefab => MatchPrefabs.Load() != null ? MatchPrefabs.Load().netCharacterPrefab : null;

    /// <summary>매치 시작 시 사람 플레이어마다 NetCharacter 하나 (입력 권한 = 그 플레이어)</summary>
    void HostSpawnPlayers()
    {
        GameObject prefab = NetCharacterPrefab;
        if (prefab == null)
        {
            Debug.LogError("[Fusion] MatchPrefabs.netCharacterPrefab 이 비어 있어요.");
            playersSpawned = true;
            return;
        }
        playersSpawned = true;
        foreach (PlayerRef p in runner.ActivePlayers)
        {
            if (NetCharacter.Players.TryGetValue(p.PlayerId, out NetCharacter existing) && existing != null)
                continue;
            int id = p.PlayerId;
            runner.Spawn(prefab, Vector3.zero, Quaternion.identity, p, (r, obj) =>
            {
                NetCharacter nc = obj.GetComponent<NetCharacter>();
                nc.NetId = id;
            });
        }
    }

    void RefreshAIs()
    {
        GameObject prefab = NetCharacterPrefab;
        foreach (TickToyAI ai in FindObjectsByType<TickToyAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
        {
            if (aiIds.ContainsKey(ai))
                continue;
            int id = nextAiId++;
            aiIds[ai] = id;
            aiById[id] = ai;
            if (prefab == null)
                continue;
            TickToyAI source = ai;
            runner.Spawn(prefab, ai.transform.position, Quaternion.identity, null, (r, obj) =>
            {
                NetCharacter nc = obj.GetComponent<NetCharacter>();
                nc.NetId = id;
                nc.Kind = NetPose.KindAI;
                nc.Ch = (byte)CharIndexOf(source.gameObject);
                nc.SourceAI = source;
            });
        }
    }

    /// <summary>방장: 네트워크 캐릭터 정리 (맵 바뀔 때 AI 만 / 방으로 돌아갈 때 전부)</summary>
    void HostDespawnAll(bool includePlayers)
    {
        if (!IsHost || runner == null)
            return;
        foreach (NetCharacter nc in FindObjectsByType<NetCharacter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (nc == null || nc.Object == null || !nc.Object.IsValid)
                continue;
            if (!includePlayers && nc.NetId < AiIdBase)
                continue;
            runner.Despawn(nc.Object);
        }
        if (includePlayers)
            playersSpawned = false;
    }

    /// <summary>매 틱: 내 캐릭터 상태를 입력으로 보냄 (방장이 NetCharacter 에 기록 → 모두에게 전달)</summary>
    public void OnInput(NetworkRunner r, NetworkInput input)
    {
        if (!MatchContext.Active)
            return;
        RoundManager rm = RoundManager.Instance;
        if (rm == null || rm.LocalPlayerObject == null)
            return;
        if (rm.LateSpectator)
        {
            // 게임 중에 들어와 관전만 하는 중: 몸은 보내지 않고(다른 화면에 안 보임) 관전 대상만
            input.Set(new NetPoseInput { Spectate = rm.SpectateTargetId, Round = MatchContext.Round });
            return;
        }
        NetPose pose = NetPose.Capture(rm.LocalPlayerObject, LocalPlayerId,
            rm.LocalIsWinderRole ? NetPose.KindWinder : NetPose.KindTickToy, (byte)rm.LocalCharIndex, rm.LocalAlive, rm.LocalFlags);
        NetPoseInput inp = NetPoseInput.From(pose);
        inp.Spectate = rm.SpectateTargetId;
        inp.Round = MatchContext.Round;
        rm.LocalHudState(out inp.Hud, out inp.HudWind, out inp.HudTimer);
        inp.Spring = rm.LocalSpringDegrees;
        inp.StatSurvive = (int)MatchStats.SurviveSeconds;
        inp.StatStops = MatchStats.Stops;
        inp.StatAbilities = MatchStats.Abilities;
        inp.StatKills = MatchStats.Kills;
        input.Set(inp);
    }

    /// <summary>틱토이 프리팹 중 몇 번째 캐릭터인지 (이름으로)</summary>
    public static int CharIndexOf(GameObject go)
    {
        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs == null || prefabs.tickToyPrefabs == null || go == null)
            return 0;
        string n = go.name.ToLowerInvariant();
        for (int i = 0; i < prefabs.tickToyPrefabs.Length; i++)
        {
            GameObject p = prefabs.tickToyPrefabs[i];
            if (p != null && n.Contains(p.name.ToLowerInvariant()))
                return i;
        }
        // 방장 본인이 만든 분신 (이름이 "TickToy_닉네임_Duplicate_1") → 내 캐릭터와 같은 모양
        if (n.Contains("_duplicate") && RoundManager.Instance != null)
            return RoundManager.Instance.LocalCharIndex;
        return 0;
    }

    // ---------------- 게임 이벤트 (모두의 화면에 똑같이) ----------------

    public const byte EvTrapPlaced = 1;
    public const byte EvTrapSprung = 2;
    public const byte EvWinderStop = 3;
    public const byte EvDuplicateCloud = 4;
    public const byte EvSound = 5;
    public const byte EvMark = 6;
    public const byte EvCommand = 7;
    public const byte EvHeal = 8;
    public const byte EvKill = 9;

    /// <summary>내가 한 일을 모두에게 알림 (나는 이미 내 화면에서 했으므로 다시 하지 않음)</summary>
    void SendGameEvent(byte kind, Action<BinaryWriter> body)
    {
        if (!InRoom || runner == null || !MatchContext.Active)
            return;
        int origin = LocalPlayerId;
        int round = MatchContext.Round; // 받는 쪽이 이전 라운드 일을 걸러냄
        if (IsHost)
            SendToClients(Msg(H2C_Event, w => { w.Write(kind); w.Write(origin); w.Write(round); body(w); }));
        else
            SendToHost(Msg(C2H_Event, w => { w.Write(kind); w.Write(origin); w.Write(round); body(w); }));
    }

    public void SendTrapPlaced(int trapId, Vector3 pos, float yaw) =>
        SendGameEvent(EvTrapPlaced, w => { w.Write(trapId); w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(yaw); });

    public void SendTrapSprung(int trapId) =>
        SendGameEvent(EvTrapSprung, w => w.Write(trapId));

    public void SendWinderStop(int winderId) =>
        SendGameEvent(EvWinderStop, w => w.Write(winderId));

    public void SendDuplicateCloud(Vector3 pos, float seconds) =>
        SendGameEvent(EvDuplicateCloud, w => { w.Write(pos.x); w.Write(pos.y); w.Write(pos.z); w.Write(seconds); });

    /// <summary>와인더 Z: 의심되는 틱토이(사람·AI) 표시 → 모두의 화면에 빨간 테두리</summary>
    public void SendMark(int targetId) =>
        SendGameEvent(EvMark, w => w.Write(targetId));

    /// <summary>내 틱토이가 쓰러짐 → 때린 와인더의 '잡은 틱토이' 기록 +1 (그 와인더 화면에서 셈)</summary>
    public void SendKill(int attackerId) => SendGameEvent(EvKill, w => w.Write(attackerId));

    /// <summary>와인더 회복하기: 다른 화면의 내 브릭 주변에도 초록 이펙트</summary>
    public void SendHeal() => SendGameEvent(EvHeal, w => { });

    /// <summary>와인더 명령하기: 원 중심·이모트·레벨·걸린 대상 id (AI·틱토이 플레이어)</summary>
    public void SendCommand(Vector3 center, int emote, int level, List<int> ids) =>
        SendGameEvent(EvCommand, w =>
        {
            w.Write(center.x); w.Write(center.y); w.Write(center.z);
            w.Write((byte)emote);
            w.Write((byte)level);
            w.Write((short)ids.Count);
            foreach (int id in ids)
                w.Write(id);
        });

    /// <summary>id 의 캐릭터가 내 화면에서 어떤 오브젝트인지 (나 / 방장의 진짜 AI / 다른 사람 복사본)</summary>
    public GameObject CharacterObject(int netId)
    {
        if (netId == LocalPlayerId)
            return RoundManager.Instance != null ? RoundManager.Instance.LocalPlayerObject : null;
        if (!NetCharacter.ById.TryGetValue(netId, out NetCharacter nc) || nc == null)
            return null;
        if (nc.SourceAI != null)
            return nc.SourceAI.gameObject;
        return nc.Puppet != null ? nc.Puppet.gameObject : null;
    }

    /// <summary>(방장) 맵 AI 의 네트워크 id (아직 등록 전이면 0)</summary>
    public int AiNetId(TickToyAI ai) => ai != null && aiIds.TryGetValue(ai, out int id) ? id : 0;

    /// <summary>소리를 다른 사람에게도 (누가 들을지는 받는 쪽에서 scope 로 판단)</summary>
    public void SendSound(SoundId id, SoundScope scope) =>
        SendGameEvent(EvSound, w => { w.Write((byte)id); w.Write((byte)scope); });

    /// <summary>받은 게임 이벤트를 내 화면에 적용</summary>
    void ApplyGameEvent(byte kind, int origin, BinaryReader rd)
    {
        if (origin == LocalPlayerId)
            return;
        switch (kind)
        {
            case EvTrapPlaced:
            {
                int id = rd.ReadInt32();
                Vector3 pos = new Vector3(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
                float yaw = rd.ReadSingle();
                WinderMove.SpawnRemoteTrap(id, pos, yaw);
                break;
            }
            case EvTrapSprung:
                WinderTrap.RemoteSprung(rd.ReadInt32());
                break;
            case EvWinderStop:
            {
                int target = rd.ReadInt32();
                RoundManager rm = RoundManager.Instance;
                if (target == LocalPlayerId)
                    rm?.ApplyWinderStop(5f);
                // 안내는 와인더에게만
                if (rm != null && rm.LocalIsWinderRole)
                {
                    TickToyHud hud = FindAnyObjectByType<TickToyHud>();
                    if (hud != null)
                        hud.ShowWinderStoppedNotice();
                }
                break;
            }
            case EvMark:
                SuspectMarks.Add(rd.ReadInt32(), null);
                break;
            case EvKill:
                if (rd.ReadInt32() == LocalPlayerId)
                    MatchStats.Kills++;
                break;
            case EvHeal:
            {
                GameObject winder = CharacterObject(origin);
                if (winder != null)
                    HealEffect.Spawn(winder.transform);
                break;
            }
            case EvCommand:
            {
                Vector3 center = new Vector3(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
                int emote = rd.ReadByte();
                int level = rd.ReadByte();
                int n = rd.ReadInt16();
                List<int> ids = new List<int>(n);
                for (int i = 0; i < n; i++)
                {
                    int id = rd.ReadInt32();
                    ids.Add(id);
                    // AI 는 방장 화면에서만 진짜로 움직임 → 방장이 명령 (다른 화면엔 AI 동작이 그대로 보임)
                    if (IsHost && id >= AiIdBase && aiById.TryGetValue(id, out TickToyAI ai) && ai != null && !ai.IsDead)
                        ai.Command(WinderCommand.Delay, emote);
                }
                WinderCommand.ApplyRemote(center, emote, level, ids);
                break;
            }
            case EvSound:
            {
                SoundId id = (SoundId)rd.ReadByte();
                GameAudio.PlayRemote(id, (SoundScope)rd.ReadByte(), origin);
                break;
            }
            case EvDuplicateCloud:
            {
                Vector3 pos = new Vector3(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
                TickToyMove.SpawnDuplicateCloud(pos, rd.ReadSingle());
                break;
            }
        }
    }

    // ---------------- 분신 (방장이 만들어 모두에게 보이게) ----------------

    /// <summary>방장이 아닐 때: 분신을 방장에게 만들어 달라고 요청 (방장이 만든 AI 가 모두에게 보임)</summary>
    public void RequestDuplicates(int ch, Vector3[] positions, float yaw)
    {
        SendToHost(Msg(C2H_Duplicate, w =>
        {
            w.Write((byte)ch);
            w.Write(yaw);
            w.Write((byte)positions.Length);
            foreach (Vector3 p in positions)
            {
                w.Write(p.x);
                w.Write(p.y);
                w.Write(p.z);
            }
        }));
    }

    void HostSpawnDuplicates(int ch, float yaw, Vector3[] positions)
    {
        MatchPrefabs prefabs = MatchPrefabs.Load();
        if (prefabs == null || prefabs.tickToyPrefabs == null || prefabs.tickToyPrefabs.Length == 0)
            return;
        GameObject prefab = prefabs.tickToyPrefabs[Mathf.Clamp(ch, 0, prefabs.tickToyPrefabs.Length - 1)];
        foreach (Vector3 pos in positions)
        {
            GameObject clone = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
            clone.name = prefab.name + "_Duplicate";
            DemoSceneAIBootstrap.ConvertToAI(clone, pos);
            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Spring"))
                    t.gameObject.SetActive(false); // 분신은 등 태엽 없음 (원래 분신과 똑같이)
            }
            TickToyAI ai = clone.GetComponent<TickToyAI>();
            if (ai != null)
                ai.BeginMovingNow();
        }
        aiRefreshTimer = 1f; // 바로 네트워크 캐릭터로 등록
    }

    // ---------------- 게임 중 상대가 나감 ----------------

    const float AbortSeconds = 3f;

    /// <summary>방장: 나간 사람 때문에 한 팀에 사람이 아무도 없으면 게임 종료 → 방으로</summary>
    void HostCheckTeamEmptyAfterLeave()
    {
        if (!MatchContext.Active)
            return;
        HashSet<int> connected = new HashSet<int>();
        foreach (PlayerRef p in runner.ActivePlayers)
            connected.Add(p.PlayerId);
        bool tickToyLeft = false, winderLeft = false;
        foreach (RoomPlayer p in MatchContext.Players)
        {
            if (IsBotId(p.id) || !connected.Contains(p.id))
                continue;
            if (MatchContext.WinderIds.Contains(p.id))
                winderLeft = true;
            else
                tickToyLeft = true;
        }
        if (tickToyLeft && winderLeft)
            return;

        const string msg = "상대가 없어 게임을 종료합니다.";
        SendToClients(Msg(H2C_Abort, w => w.Write(msg)));
        StartCoroutine(AbortThen(msg, () => ReturnToRoom()));
    }

    IEnumerator AbortThen(string message, Action after)
    {
        RoundManager.Instance?.ShowMatchAbort(message);
        yield return new WaitForSecondsRealtime(AbortSeconds);
        after?.Invoke();
    }
}
