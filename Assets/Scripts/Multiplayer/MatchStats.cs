/// <summary>내 기록 (매치 전체 합계, Tab 플레이어 창). 매 틱 입력으로 방장에게 보내 모두에게 같은 값이 보임.
/// 틱토이: 따돌린 시간·와인더 정지 횟수·능력 사용 수·사망 수 / 와인더: 잡은 틱토이 수·능력 사용 수·사망 수</summary>
public static class MatchStats
{
    public static float SurviveSeconds;
    public static int Stops;
    public static int Abilities;
    public static int Kills;
    public static int Deaths;

    public static void Reset()
    {
        SurviveSeconds = 0f;
        Stops = 0;
        Abilities = 0;
        Kills = 0;
        Deaths = 0;
    }
}
