using UnityEngine;
using UnityEngine.UI;

/// <summary>Tab 을 누르고 있는 동안 플레이어 창 (매치 전체 합계).
/// 틱토이: 닉네임 · 따돌린 시간 · 와인더 정지 · 능력 사용 · 사망 / 와인더: 닉네임 · 잡은 틱토이 · 능력 사용 · 사망.
/// 쓰러진 사람은 빨간 대각선. 값은 각자 네트워크 캐릭터로 보낸 것 → 모두 같은 값. UI 는 DemoScene 에 미리 만들어져 있음</summary>
public class Scoreboard : MonoBehaviour
{
    [System.Serializable]
    public class Row
    {
        public GameObject root;
        public Text nameText;
        public Text[] statTexts;
        public GameObject deadLine;
    }

    public GameObject board;
    public Row[] tickToyRows; // 8줄
    public Row[] winderRows;  // 8줄

    void Awake()
    {
        if (board != null)
            board.SetActive(false);
    }

    void Update()
    {
        bool show = MatchContext.Active && !GameSettings.MenuOpen && Input.GetKey(KeyCode.Tab);
        if (board == null)
            return;
        if (board.activeSelf != show)
            board.SetActive(show);
        if (show)
            Refresh();
    }

    void Refresh()
    {
        int t = 0, w = 0;
        foreach (RoomPlayer p in MatchContext.Players)
        {
            bool winder = MatchContext.WinderIds.Contains(p.id);
            Row row = winder ? Get(winderRows, w++) : Get(tickToyRows, t++);
            if (row == null)
                continue;
            Read(p.id, out int survive, out int stops, out int abilities, out int kills, out int deaths, out bool dead);
            row.root.SetActive(true);
            row.nameText.text = p.nickname;
            if (winder)
            {
                row.statTexts[0].text = kills.ToString();
                row.statTexts[1].text = abilities.ToString();
                row.statTexts[2].text = deaths.ToString();
            }
            else
            {
                row.statTexts[0].text = (survive / 60) + ":" + (survive % 60).ToString("00");
                row.statTexts[1].text = stops.ToString();
                row.statTexts[2].text = abilities.ToString();
                row.statTexts[3].text = deaths.ToString();
            }
            row.deadLine.SetActive(dead);
        }
        Hide(tickToyRows, t);
        Hide(winderRows, w);
    }

    /// <summary>나는 내 기록 그대로, 다른 사람은 그 사람이 보낸 네트워크 값</summary>
    static void Read(int id, out int survive, out int stops, out int abilities, out int kills, out int deaths, out bool dead)
    {
        survive = stops = abilities = kills = deaths = 0;
        dead = false;
        if (id == MatchContext.LocalId)
        {
            survive = (int)MatchStats.SurviveSeconds;
            stops = MatchStats.Stops;
            abilities = MatchStats.Abilities;
            kills = MatchStats.Kills;
            deaths = MatchStats.Deaths;
            RoundManager rm = RoundManager.Instance;
            dead = rm != null && !rm.LocalAlive && !rm.LateSpectator;
            return;
        }
        if (!NetCharacter.Players.TryGetValue(id, out NetCharacter nc) || nc == null || nc.Object == null || !nc.Object.IsValid)
            return;
        survive = nc.StatSurvive;
        stops = nc.StatStops;
        abilities = nc.StatAbilities;
        kills = nc.StatKills;
        deaths = nc.StatDeaths;
        dead = nc.HasPose && nc.PoseRound == MatchContext.Round && !nc.Alive; // 이번 라운드에 쓰러짐
    }

    static Row Get(Row[] rows, int i) => rows != null && i < rows.Length ? rows[i] : null;

    static void Hide(Row[] rows, int from)
    {
        if (rows == null)
            return;
        for (int i = from; i < rows.Length; i++)
        {
            if (rows[i] != null && rows[i].root != null)
                rows[i].root.SetActive(false);
        }
    }
}
