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

/// <summary>메시지 만들기·보내기·받기 (방장 ↔ 참가자)</summary>
public partial class FusionMatchBackend : MonoBehaviour, IMatchBackend, INetworkRunnerCallbacks
{
    // ---------------- 메시지 송수신 ----------------

    static byte[] Msg(byte op, Action<BinaryWriter> body)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(op);
            body?.Invoke(w);
            w.Flush();
            return ms.ToArray();
        }
    }

    void SendToHost(byte[] data)
    {
        if (runner != null && runner.IsRunning && !runner.IsServer)
            runner.SendReliableDataToServer(default(ReliableKey), data);
    }

    void SendToClients(byte[] data)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer)
            return;
        foreach (PlayerRef p in runner.ActivePlayers)
        {
            if (p != runner.LocalPlayer)
                runner.SendReliableDataToPlayer(p, default(ReliableKey), data);
        }
    }

    public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
        if (r != runner || data.Length == 0)
            return;
        byte[] buf = data.ToArray();
        try
        {
            using (MemoryStream ms = new MemoryStream(buf))
            using (BinaryReader rd = new BinaryReader(ms))
            {
                byte op = rd.ReadByte();
                if (runner.IsServer)
                    HandleOnHost(player, op, rd);
                else
                    HandleOnClient(op, rd);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Fusion] 메시지 처리 실패: " + e.Message);
        }
    }

    void HandleOnHost(PlayerRef from, byte op, BinaryReader rd)
    {
        switch (op)
        {
            case C2H_Hello:
                HostAddPlayer(from, rd.ReadString());
                break;
            case C2H_Team:
                HostSetTeam(from.PlayerId, rd.ReadByte());
                break;
            case C2H_Ready:
                HostSetReady(from.PlayerId, rd.ReadBoolean());
                break;
            case C2H_Chat:
            {
                string text = rd.ReadString();
                RoomPlayer p = players.Find(x => x.id == from.PlayerId);
                if (p != null)
                    HostBroadcastChat(p.nickname, text);
                break;
            }
            case C2H_Event:
            {
                byte kind = rd.ReadByte();
                int origin = rd.ReadInt32();
                int round = rd.ReadInt32();
                long start = rd.BaseStream.Position;
                byte[] payload = rd.ReadBytes((int)(rd.BaseStream.Length - start));
                if (round != MatchContext.Round)
                    break; // 이전 라운드에 일어난 일이 늦게 도착 → 버림
                // 방장 화면에 적용 + 다른 사람들에게 그대로 전달
                using (BinaryReader pr = new BinaryReader(new MemoryStream(payload)))
                    ApplyGameEvent(kind, origin, pr);
                SendToClients(Msg(H2C_Event, w => { w.Write(kind); w.Write(origin); w.Write(round); w.Write(payload); }));
                break;
            }
            case C2H_Duplicate:
            {
                int ch = rd.ReadByte();
                float yaw = rd.ReadSingle();
                int n = rd.ReadByte();
                Vector3[] positions = new Vector3[n];
                for (int i = 0; i < n; i++)
                    positions[i] = new Vector3(rd.ReadSingle(), rd.ReadSingle(), rd.ReadSingle());
                if (MatchContext.Active)
                    HostSpawnDuplicates(ch, yaw, positions);
                break;
            }
            case C2H_Hit:
            {
                int target = rd.ReadInt32();
                int damage = rd.ReadInt32();
                if (MatchContext.Active)
                    RouteHit(target, damage);
                break;
            }
        }
    }

    void HandleOnClient(byte op, BinaryReader rd)
    {
        switch (op)
        {
            case H2C_Room:
                ReadRoom(rd);
                RoomChanged?.Invoke();
                break;

            case H2C_Start:
            {
                ReadRoom(rd);
                string scene = rd.ReadString();
                MatchContext.BeginMatch(current, players, LocalPlayerId);
                LoadMatchScene(scene);
                break;
            }

            case H2C_LoadMap:
            {
                int round = rd.ReadInt32();
                MatchContext.TickToyWins = rd.ReadInt32();
                MatchContext.WinderWins = rd.ReadInt32();
                string scene = rd.ReadString();
                if (round <= MatchContext.Round)
                    break; // 이미 그 라운드로 넘어감 (네트워크 값으로 먼저 따라간 경우) → 두 번 불러오지 않음
                MatchContext.Round = round;
                LoadMatchScene(scene);
                break;
            }

            case H2C_Return:
                DoReturnToRoom();
                break;

            case H2C_Phase:
            {
                int phase = rd.ReadByte();
                float left = rd.ReadSingle();
                bool winderWon = rd.ReadBoolean();
                int ttWins = rd.ReadInt32();
                int wWins = rd.ReadInt32();
                netAliveTickToys = rd.ReadByte();
                netAliveWinders = rd.ReadByte();
                int hostRound = rd.ReadInt32();
                if (hostRound > MatchContext.Round)
                {
                    ClientCatchUpRound(hostRound, ttWins, wWins);
                    break;
                }
                // 늦게 도착한 '이전 라운드' 상태(예: 이전 판의 결과 화면)는 버림.
                // 이걸 새 라운드에 적용하면 결과 화면이 다시 떠서 그대로 멈춰 있었음
                if (hostRound < MatchContext.Round)
                    break;
                if (!sceneLoading)
                    RoundManager.Instance?.ApplyNetPhase(phase, left, winderWon, ttWins, wWins);
                break;
            }

            case H2C_Hit:
                RoundManager.Instance?.ApplyNetHit(rd.ReadInt32());
                break;

            case H2C_Chat:
            {
                string nick = rd.ReadString();
                ChatLog.Add(nick, rd.ReadString());
                break;
            }

            case H2C_Event:
            {
                byte kind = rd.ReadByte();
                int origin = rd.ReadInt32();
                int round = rd.ReadInt32();
                if (MatchContext.Active && round == MatchContext.Round && !sceneLoading)
                    ApplyGameEvent(kind, origin, rd); // 이전 라운드 일(덫 설치 등)이 새 맵에 생기지 않게
                break;
            }

            case H2C_Abort:
                RoundManager.Instance?.ShowMatchAbort(rd.ReadString());
                break;
        }
    }
}
