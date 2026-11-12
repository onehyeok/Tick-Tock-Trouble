using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로비: 방 목록(스크롤) + 오른쪽 위 Join Room / Create Room
/// 방 만들기: 맵 선택(화살표), 인원수, 암호, 라운드(3/5)
/// 방: 준비 / 게임 시작(방장, 2명 이상 + 모두 준비) → 게임 로딩중...
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("Top")]
    public Text nicknameText;
    public Button joinRandomButton;
    public Button createRoomButton;
    public Button backButton;

    [Header("Room List")]
    public RectTransform roomListContent;
    public RoomListItem roomItemTemplate;
    public Text emptyText;

    [Header("Create Room")]
    public GameObject createPanel;
    public Button mapPrevButton;
    public Button mapNextButton;
    public Image mapImage;
    public Text mapUnknownText;
    public Text mapNameText;
    public InputField roomNameInput;
    public Button playersMinusButton;
    public Button playersPlusButton;
    public Text playersText;
    public InputField passwordInput;
    public Button rounds3Button;
    public Button rounds5Button;
    public Button createConfirmButton;
    public Button createCancelButton;
    public Text createErrorText;

    [Header("Password Prompt")]
    public GameObject passwordPanel;
    public Text passwordRoomText;
    public InputField joinPasswordInput;
    public Button passwordOkButton;
    public Button passwordCancelButton;

    [Header("Room")]
    public GameObject roomPanel;
    public Text roomTitleText;
    public Image roomMapImage;
    public Text roomInfoText;
    public RectTransform playerListContent;
    [Tooltip("왼쪽: 틱토이 팀")]
    public RectTransform tickToyListContent;
    [Tooltip("오른쪽: 와인더 팀")]
    public RectTransform winderListContent;
    public Button tickToyTeamButton;
    public Button winderTeamButton;
    public Text tickToyCountText;
    public Text winderCountText;
    public Button switchTeamButton;
    public Text playerItemTemplate;
    public Button readyButton;
    public Button startButton;
    public Button leaveButton;
    public Button addBotButton;

    [Header("Player Menu (방장이 플레이어 우클릭)")]
    public GameObject playerMenu;
    public RectTransform playerMenuCard;
    public Text playerMenuTitle;
    public Button makeHostButton;
    public Button kickButton;
    public Button playerMenuCloseButton;

    [Header("Overlay")]
    public GameObject loadingPanel;
    public Text toastText;

    const int MinPlayers = 2;
    const int MaxPlayers = MatchContext.TeamSize * 2;

    IMatchBackend backend;
    readonly List<GameObject> roomItems = new List<GameObject>();
    readonly List<GameObject> playerItems = new List<GameObject>();
    int mapIndex;
    int maxPlayers = MaxPlayers;
    int rounds = 3;
    string pendingJoinRoom;
    float toastLeft;
    bool localReady;
    int localTeam;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UIKit.EnsureEventSystem();

        backend = MatchService.Backend;
        backend.RoomsChanged += RefreshRoomList;
        backend.RoomChanged += RefreshRoom;
        backend.Error += ShowToast;
        backend.GameLoading += ShowLoading;

        if (nicknameText != null)
            nicknameText.text = PlayerProfile.Nickname;

        Hook(joinRandomButton, () => backend.JoinRandom());
        Hook(createRoomButton, OpenCreate);
        Hook(backButton, () =>
        {
            if (backend.CurrentRoom != null)
                backend.LeaveRoom();
            SceneManager.LoadScene(MatchService.IntroScene);
        });
        Hook(mapPrevButton, () => ChangeMap(-1));
        Hook(mapNextButton, () => ChangeMap(1));
        Hook(playersMinusButton, () => ChangePlayers(-1));
        Hook(playersPlusButton, () => ChangePlayers(1));
        Hook(rounds3Button, () => SetRounds(3));
        Hook(rounds5Button, () => SetRounds(5));
        Hook(createConfirmButton, ConfirmCreate);
        Hook(createCancelButton, () => createPanel.SetActive(false));
        Hook(passwordOkButton, ConfirmPassword);
        Hook(passwordCancelButton, () => passwordPanel.SetActive(false));
        Hook(readyButton, ToggleReady);
        Hook(startButton, () => backend.StartGame());
        Hook(leaveButton, () => backend.LeaveRoom());
        Hook(tickToyTeamButton, () => backend.SetTeam(RoomPlayer.TeamTickToy));
        Hook(winderTeamButton, () => backend.SetTeam(RoomPlayer.TeamWinder));
        Hook(switchTeamButton, () => backend.SetTeam(localTeam == RoomPlayer.TeamTickToy ? RoomPlayer.TeamWinder : RoomPlayer.TeamTickToy));
        Hook(makeHostButton, () => OnPlayerMenu(true));
        Hook(kickButton, () => OnPlayerMenu(false));
        Hook(playerMenuCloseButton, ClosePlayerMenu);
        SetActive(playerMenu, false);
        Hook(addBotButton, () =>
        {
            if (backend is OfflineMatchBackend offline)
                offline.AddTestBot();
            else if (backend is FusionMatchBackend online)
                online.AddTestBot();
        });

        if (roomItemTemplate != null)
            roomItemTemplate.gameObject.SetActive(false);
        if (playerItemTemplate != null)
            playerItemTemplate.gameObject.SetActive(false);
        SetActive(createPanel, false);
        SetActive(passwordPanel, false);
        SetActive(loadingPanel, false);
        SetActive(toastText != null ? toastText.gameObject : null, false);
#if !UNITY_EDITOR
        SetActive(addBotButton != null ? addBotButton.gameObject : null, false);
#endif
        if (addBotButton != null && !(backend is OfflineMatchBackend) && !(backend is FusionMatchBackend))
            addBotButton.gameObject.SetActive(false);

        if (!backend.IsConnected)
            backend.ConnectLobby(PlayerProfile.Nickname, (ok, err) => { if (!ok) ShowToast(err ?? "서버 연결 실패"); });

        MatchContext.ReturnToRoom = false;
        RefreshRoomList();
        RefreshRoom();
    }

    void OnDestroy()
    {
        if (backend == null)
            return;
        backend.RoomsChanged -= RefreshRoomList;
        backend.RoomChanged -= RefreshRoom;
        backend.Error -= ShowToast;
        backend.GameLoading -= ShowLoading;
    }

    void Update()
    {
        if (toastLeft > 0f)
        {
            toastLeft -= Time.deltaTime;
            if (toastLeft <= 0f && toastText != null)
                toastText.gameObject.SetActive(false);
        }
    }

    static void Hook(Button b, UnityEngine.Events.UnityAction a)
    {
        if (b != null)
            b.onClick.AddListener(a);
    }

    static void SetActive(GameObject go, bool v)
    {
        if (go != null)
            go.SetActive(v);
    }

    // ---------------- 방 목록 ----------------

    void RefreshRoomList()
    {
        for (int i = 0; i < roomItems.Count; i++)
            Destroy(roomItems[i]);
        roomItems.Clear();

        IReadOnlyList<RoomInfo> rooms = backend.Rooms;
        MapCatalog catalog = MapCatalog.Load();
        for (int i = 0; i < rooms.Count; i++)
        {
            RoomInfo info = rooms[i];
            RoomListItem item = Instantiate(roomItemTemplate, roomListContent);
            item.gameObject.SetActive(true);
            item.nameText.text = info.name;
            string mapName = catalog != null && catalog.Get(info.mapIndex) != null ? catalog.Get(info.mapIndex).displayName : "?";
            item.infoText.text = $"{mapName}  ·  {info.rounds}라운드" + (info.inGame ? "  ·  게임 중" : "");
            item.countText.text = $"{info.players}/{info.maxPlayers}";
            item.lockText.gameObject.SetActive(info.locked);
            item.joinButton.interactable = info.IsJoinable;
            string roomName = info.name;
            bool locked = info.locked;
            item.joinButton.onClick.AddListener(() => TryJoin(roomName, locked));
            roomItems.Add(item.gameObject);
        }

        if (emptyText != null)
            emptyText.gameObject.SetActive(rooms.Count == 0);
    }

    void TryJoin(string roomName, bool locked)
    {
        if (!locked)
        {
            backend.JoinRoom(roomName, "");
            return;
        }

        pendingJoinRoom = roomName;
        joinPasswordInput.text = "";
        passwordRoomText.text = $"'{roomName}' 방 암호";
        passwordPanel.SetActive(true);
    }

    void ConfirmPassword()
    {
        passwordPanel.SetActive(false);
        backend.JoinRoom(pendingJoinRoom, joinPasswordInput.text);
    }

    // ---------------- 방 만들기 ----------------

    void OpenCreate()
    {
        mapIndex = 0;
        maxPlayers = MaxPlayers;
        rounds = 3;
        roomNameInput.text = PlayerProfile.Nickname + "의 방";
        passwordInput.text = "";
        createErrorText.text = "";
        RefreshCreate();
        createPanel.SetActive(true);
    }

    void ChangeMap(int dir)
    {
        MapCatalog catalog = MapCatalog.Load();
        int count = catalog != null ? catalog.maps.Count : 1;
        mapIndex = (mapIndex + dir + count) % count;
        RefreshCreate();
    }

    void ChangePlayers(int dir)
    {
        maxPlayers = Mathf.Clamp(maxPlayers + dir, MinPlayers, MaxPlayers);
        RefreshCreate();
    }

    void SetRounds(int value)
    {
        rounds = value;
        RefreshCreate();
    }

    void RefreshCreate()
    {
        MapEntry map = MapCatalog.Load()?.Get(mapIndex);
        bool available = map != null && map.available;
        ShowMap(mapImage, mapUnknownText, map);
        mapNameText.text = available ? map.displayName : "??";
        if (playersText != null)
            playersText.text = $"{MatchContext.TeamSize} vs {MatchContext.TeamSize}";
        UIKit.SetColor(rounds3Button, rounds == 3 ? UIKit.Orange : UIKit.CreamDark);
        UIKit.SetColor(rounds5Button, rounds == 5 ? UIKit.Orange : UIKit.CreamDark);
        createConfirmButton.interactable = available;
        createErrorText.text = available ? "" : "아직 준비 중인 맵이에요!";
    }

    static void ShowMap(Image image, Text unknown, MapEntry map)
    {
        bool known = map != null && map.available && map.preview != null;
        if (image != null)
        {
            image.sprite = known ? map.preview : null;
            image.color = known ? Color.white : new Color(0.55f, 0.5f, 0.45f, 1f);
            image.preserveAspect = known;
        }
        if (unknown != null)
            unknown.gameObject.SetActive(!known);
    }

    void ConfirmCreate()
    {
        string name = roomNameInput.text.Trim();
        if (name.Length == 0)
            name = PlayerProfile.Nickname + "의 방";
        if (name.Length > 20)
            name = name.Substring(0, 20);

        createPanel.SetActive(false);
        backend.CreateRoom(new RoomSettings
        {
            roomName = name,
            mapIndex = mapIndex,
            maxPlayers = maxPlayers,
            password = passwordInput.text,
            rounds = rounds
        });
    }

    // ---------------- 방 ----------------

    void RefreshRoom()
    {
        bool inRoom = backend.CurrentRoom != null; // 방장이 바뀌는 중에도 방 화면 유지
        SetActive(roomPanel, inRoom);
        // 메뉴 대상이 나갔거나 내가 방장이 아니게 되면 메뉴 닫기
        if (menuTarget != null && (!inRoom || !backend.IsHost || !HasPlayer(menuTarget.id)))
            ClosePlayerMenu();
        if (!inRoom)
        {
            localReady = false;
            return;
        }

        RoomSettings s = backend.CurrentRoom;
        MapEntry map = MapCatalog.Load()?.Get(s.mapIndex);
        roomTitleText.text = s.roomName + (s.HasPassword ? "  (암호)" : "");
        ShowMap(roomMapImage, null, map);
        IReadOnlyList<RoomPlayer> players = backend.RoomPlayers;
        roomInfoText.text = $"{(map != null ? map.displayName : "?")}  ·  {s.rounds}라운드  ·  {players.Count}/{s.maxPlayers}명";

        for (int i = 0; i < playerItems.Count; i++)
            Destroy(playerItems[i]);
        playerItems.Clear();

        int tickToys = 0;
        int winders = 0;
        int readyTickToys = 0;
        int readyWinders = 0;
        for (int i = 0; i < players.Count; i++)
        {
            RoomPlayer p = players[i];
            bool winder = p.team == RoomPlayer.TeamWinder;
            if (winder)
                winders++;
            else
                tickToys++;
            if (p.isHost || p.ready)
            {
                if (winder)
                    readyWinders++;
                else
                    readyTickToys++;
            }

            RectTransform list = winder ? winderListContent : tickToyListContent;
            if (list == null)
                list = playerListContent;
            Text item = Instantiate(playerItemTemplate, list);
            item.gameObject.SetActive(true);
            string tag = p.connecting ? "  연결 중…" : (p.isHost ? "  [방장]" : (p.ready ? "  준비 완료" : "  대기 중"));
            item.text = (p.isLocal ? "★ " : "") + p.nickname + tag;
            item.color = p.isHost || p.ready ? UIKit.Brown : UIKit.Gray;
            // 방장: 다른 사람 이름을 우클릭 → 방장 넘기기 / 강퇴하기
            item.raycastTarget = true;
            PlayerItemClick click = item.gameObject.AddComponent<PlayerItemClick>();
            RoomPlayer target = p;
            click.onRightClick = e => OpenPlayerMenu(target, e);
            playerItems.Add(item.gameObject);
            if (p.isLocal)
            {
                localReady = p.ready;
                localTeam = p.team;
            }
        }

        if (tickToyCountText != null)
            tickToyCountText.text = $"{tickToys}/{MatchContext.TeamSize}";
        if (winderCountText != null)
            winderCountText.text = $"{winders}/{MatchContext.TeamSize}";

        bool bothTeams = tickToys > 0 && winders > 0;
        // 모두 준비할 필요 없이 양쪽 팀에 준비된 사람(방장 포함)이 한 명씩이면 시작 가능
        bool canStart = readyTickToys > 0 && readyWinders > 0;
        bool host = backend.IsHost;
        startButton.gameObject.SetActive(host);
        readyButton.gameObject.SetActive(!host);
        bool connecting = false;
        foreach (RoomPlayer p in players)
            connecting |= p.connecting;
        startButton.interactable = players.Count >= MinPlayers && canStart && !connecting; // 다시 들어오는 중인 사람이 있으면 잠깐 기다림
        string startLabel = !bothTeams ? "양쪽 팀에 1명씩 필요" : (canStart ? "게임 시작!" : "준비 기다리는 중");
        UIKit.SetText(startButton, startLabel);
        UIKit.SetText(readyButton, localReady ? "준비 취소" : "준비");
        UIKit.SetColor(readyButton, localReady ? UIKit.Gray : UIKit.Mint);
    }

    // ---------------- 방장 메뉴 (우클릭) ----------------

    RoomPlayer menuTarget;

    bool HasPlayer(int id)
    {
        foreach (RoomPlayer p in backend.RoomPlayers)
        {
            if (p.id == id)
                return true;
        }
        return false;
    }

    void OpenPlayerMenu(RoomPlayer target, PointerEventData e)
    {
        FusionMatchBackend online = backend as FusionMatchBackend;
        if (online == null || !online.IsHost || target.isLocal || target.isHost || target.connecting || playerMenu == null)
            return;
        menuTarget = target;
        playerMenuTitle.text = target.nickname;
        makeHostButton.interactable = !FusionMatchBackend.IsBotPlayer(target.id); // 테스트 봇에겐 방장을 넘길 수 없음
        playerMenu.SetActive(true);
        playerMenu.transform.SetAsLastSibling();

        // 누른 자리 오른쪽 아래에 메뉴 (화면 밖으로 나가지 않게)
        RectTransform area = (RectTransform)playerMenu.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out Vector2 local);
        Vector2 size = playerMenuCard.sizeDelta;
        Rect r = area.rect;
        local.x = Mathf.Clamp(local.x, r.xMin, r.xMax - size.x);
        local.y = Mathf.Clamp(local.y, r.yMin + size.y, r.yMax);
        playerMenuCard.anchoredPosition = local;
    }

    void OnPlayerMenu(bool makeHost)
    {
        FusionMatchBackend online = backend as FusionMatchBackend;
        RoomPlayer target = menuTarget;
        ClosePlayerMenu();
        if (online == null || target == null)
            return;
        if (makeHost)
            online.TransferHost(target.id);
        else
            online.KickPlayer(target.id);
    }

    void ClosePlayerMenu()
    {
        menuTarget = null;
        SetActive(playerMenu, false);
    }

    void ToggleReady()
    {
        backend.SetReady(!localReady);
    }

    // ---------------- 알림 ----------------

    void ShowLoading()
    {
        SetActive(createPanel, false);
        SetActive(passwordPanel, false);
        SetActive(loadingPanel, true);
    }

    void ShowToast(string msg)
    {
        if (toastText == null || string.IsNullOrEmpty(msg))
            return;
        toastText.text = msg;
        toastText.gameObject.SetActive(true);
        toastLeft = 2.5f;
    }
}
