using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 와인더 능력 "명령하기": R → 와인더 발밑 빨간 원(반지름 5m) 안의 틱토이 AI 는 5초 동안 가만히 있다가 지정된 이모트(Z/X/C)를 함.
/// 원 안의 틱토이 플레이어는 멈추지 않고, 화면에 5초 카운트다운 + 지시(레벨에 따라 3초 전 / 1초 전 / 안 알려줌)가 뜸.
/// 와인더는 바로 "5초 후 Z키 사용" 이 보임. 원은 모든 사람 화면에 보임.
/// </summary>
public static class WinderCommand
{
    public const float Radius = 5f;
    public const float Delay = 5f;
    static readonly string[] Keys = { "Z", "X", "C" };

    public static string KeyName(int emote) => Keys[Mathf.Clamp(emote, 0, Keys.Length - 1)];

    /// <summary>레벨별로 틱토이 플레이어에게 지시를 보여 주는 시점 (남은 초). Lv1 = 3초 전, Lv2 = 1초 전, Lv3 = 안 알려줌</summary>
    public static float RevealLeft(int level) => level <= 1 ? 3f : (level == 2 ? 1f : -1f);

    /// <summary>와인더(나): 원 안의 대상을 모아 명령. 방장/오프라인이면 진짜 AI 에 바로, 다른 화면엔 이벤트로</summary>
    public static void Use(Vector3 center, int level)
    {
        int emote = Random.Range(0, Keys.Length);
        FusionMatchBackend online = FusionMatchBackend.Online;
        List<int> ids = new List<int>();

        // 내 화면의 진짜 AI (방장·오프라인)
        foreach (TickToyAI ai in Object.FindObjectsByType<TickToyAI>(FindObjectsSortMode.None))
        {
            if (ai == null || ai.IsDead || !InRange(center, ai.transform.position))
                continue;
            ai.Command(Delay, emote);
            CommandMark.Attach(ai.gameObject, Delay);
            if (online != null)
            {
                int id = online.AiNetId(ai);
                if (id != 0)
                    ids.Add(id);
            }
        }
        // 다른 화면에서 온 복사본 (방장 AI · 틱토이 플레이어)
        foreach (NetPuppet p in Object.FindObjectsByType<NetPuppet>(FindObjectsSortMode.None))
        {
            if (p == null || !p.Alive || p.Kind == NetPose.KindWinder || !InRange(center, p.transform.position))
                continue;
            ids.Add(p.NetId);
            CommandMark.Attach(p.gameObject, Delay);
        }

        ShowCircle(center);
        CommandHud.Show(emote, Delay, 99f, true); // 와인더는 바로 지시가 보임
        online?.SendCommand(center, emote, level, ids);
    }

    /// <summary>다른 사람이 쓴 명령을 내 화면에 (원 + 걸린 캐릭터 머리 위 "!" + 내가 걸렸으면 카운트다운)</summary>
    public static void ApplyRemote(Vector3 center, int emote, int level, List<int> ids)
    {
        ShowCircle(center);
        FusionMatchBackend online = FusionMatchBackend.Online;
        bool iAmTarget = false;
        foreach (int id in ids)
        {
            if (online != null && id == online.LocalPlayerId)
                iAmTarget = true;
            if (online != null)
                CommandMark.Attach(online.CharacterObject(id), Delay);
        }
        RoundManager rm = RoundManager.Instance;
        if (iAmTarget && rm != null && rm.LocalAlive && !rm.LocalIsWinderRole)
            CommandHud.Show(emote, Delay, RevealLeft(level), false);
    }

    static bool InRange(Vector3 center, Vector3 p)
    {
        Vector3 d = p - center;
        d.y = 0f;
        return d.sqrMagnitude <= Radius * Radius && Mathf.Abs(p.y - center.y) < 4f;
    }

    static void ShowCircle(Vector3 center)
    {
        GameObject go = new GameObject("CommandCircle");
        go.transform.position = center;
        go.AddComponent<CommandCircle>().Begin(Radius, Delay);
    }
}
