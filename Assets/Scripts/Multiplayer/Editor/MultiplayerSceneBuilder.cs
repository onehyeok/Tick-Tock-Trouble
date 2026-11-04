using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 메뉴 Tools/TTT/Build Multiplayer Scenes
/// Intro(닉네임) · Lobby(방 목록/방 만들기/방) 씬, MapCatalog·MatchPrefabs 에셋, 빌드 설정을 만듭니다.
/// 다시 실행하면 두 씬을 새로 만들어 덮어씁니다.
/// </summary>
public static class MultiplayerSceneBuilder
{
    const string SceneDir = "Assets/Scenes/";
    const string FontPath = "Assets/Resources/Fonts/Jua-Regular.ttf";
    const string CityImagePath = "Assets/Images/CITY.png";

    [MenuItem("Tools/TTT/Build Multiplayer Scenes")]
    public static void BuildAll()
    {
        PrepareStyle();
        Sprite city = PrepareCitySprite();
        BuildCatalogs(city);
        BuildIntro(city);
        BuildLobby(city);
        SetupBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[TTT] 멀티플레이 씬 생성 완료: Intro, Lobby");
    }

    static void PrepareStyle()
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font != null)
            UIKit.Font = font;
        UIKit.RoundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    static Sprite PrepareCitySprite()
    {
        var importer = AssetImporter.GetAtPath(CityImagePath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(CityImagePath);
    }

    static void BuildCatalogs(Sprite city)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>("Assets/Resources/MapCatalog.asset");
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<MapCatalog>();
            AssetDatabase.CreateAsset(catalog, "Assets/Resources/MapCatalog.asset");
        }
        catalog.maps = new List<MapEntry>
        {
            new MapEntry { displayName = "CITY", sceneName = "DemoScene", preview = city, available = true },
            new MapEntry { displayName = "??", sceneName = "", preview = null, available = false },
            new MapEntry { displayName = "??", sceneName = "", preview = null, available = false }
        };
        EditorUtility.SetDirty(catalog);

        var prefabs = AssetDatabase.LoadAssetAtPath<MatchPrefabs>("Assets/Resources/MatchPrefabs.asset");
        if (prefabs == null)
        {
            prefabs = ScriptableObject.CreateInstance<MatchPrefabs>();
            AssetDatabase.CreateAsset(prefabs, "Assets/Resources/MatchPrefabs.asset");
        }
        prefabs.winderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Brick.prefab");
        prefabs.tickToyPrefabs = new[] { "Bolt", "Momo", "Jephy", "Lulu" }
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + n + ".prefab"))
            .Where(p => p != null)
            .ToArray();
        prefabs.winderStartRoom = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/WinderStartingPosition.prefab");
        EditorUtility.SetDirty(prefabs);
    }

    static void SetupBuildSettings()
    {
        var wanted = new List<string> { SceneDir + "Intro.unity", SceneDir + "Lobby.unity" };
        string demo = AssetDatabase.FindAssets("DemoScene t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => p.EndsWith("/DemoScene.unity"));

        var list = new List<EditorBuildSettingsScene>();
        foreach (string p in wanted)
            list.Add(new EditorBuildSettingsScene(p, true));
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!wanted.Contains(s.path) && s.path != demo)
                list.Add(s);
        }
        if (demo != null)
            list.Add(new EditorBuildSettingsScene(demo, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ------------------------------------------------------------------

    // 지금 열려 있는 씬(작업 중인 씬)은 건드리지 않도록 추가(Additive)로 만들고 저장 후 닫음
    static UnityEngine.SceneManagement.Scene building;
    static UnityEngine.SceneManagement.Scene previousActive;

    static Transform NewScene(string name, out Canvas canvas)
    {
        previousActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        building = scene;
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UIKit.Cream;

        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
        canvas = UIKit.Canvas("Canvas");
        return canvas.transform;
    }

    static void Save(string name)
    {
        EditorSceneManager.SaveScene(building, SceneDir + name + ".unity");
        if (previousActive.IsValid())
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActive);
        EditorSceneManager.CloseScene(building, true);
    }

    static Image Background(Transform root, Sprite city, float dim)
    {
        Image bg = UIKit.Panel(root, "Background", Color.white, false);
        bg.rectTransform.Fill();
        if (city != null)
        {
            bg.sprite = city;
            bg.preserveAspect = false;
        }
        else
            bg.color = UIKit.Cream;
        Image shade = UIKit.Panel(root, "BackgroundShade", new Color(1f, 0.93f, 0.8f, dim), false);
        shade.rectTransform.Fill();
        return bg;
    }

    // ------------------------------------------------------------------

    static void BuildIntro(Sprite city)
    {
        Transform root = NewScene("Intro", out _);
        Background(root, city, 0.55f);

        Text title = UIKit.Label(root, "Title", "Tick Tock Trouble", 150, UIKit.Orange).Outlined(UIKit.Brown, 7f);
        title.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1700f, 200f));
        Text sub = UIKit.Label(root, "Subtitle", "태엽 장난감 숨바꼭질", 54, UIKit.Brown);
        sub.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -355f), new Vector2(1200f, 80f));

        Image card = UIKit.Panel(root, "NicknameCard", UIKit.Cream);
        card.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(900f, 440f));
        card.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -10f);

        Text label = UIKit.Label(card.transform, "Label", "닉네임을 정해 주세요", 50, UIKit.Brown);
        label.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(820f, 70f));
        InputField input = UIKit.Input(card.transform, "NicknameInput", "2~10글자", 46);
        input.characterLimit = 10;
        input.rectTransform().At(new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(720f, 100f));
        Button start = UIKit.Button(card.transform, "StartButton", "시작하기", UIKit.Orange, Color.white, 52);
        start.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 3f);
        ((RectTransform)start.transform).At(new Vector2(0.5f, 0f), new Vector2(0f, 75f), new Vector2(460f, 110f));
        Text msg = UIKit.Label(card.transform, "Message", "", 34, UIKit.Pink);
        msg.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(860f, 56f));

        Text busy = UIKit.Label(root, "Busy", "...", 60, UIKit.Brown);
        busy.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(400f, 80f));

        var ui = new GameObject("IntroUI").AddComponent<IntroUI>();
        ui.nicknameInput = input;
        ui.startButton = start;
        ui.messageText = msg;
        ui.busyRoot = busy.gameObject;
        Save("Intro");
    }

    // ------------------------------------------------------------------

    static void BuildLobby(Sprite city)
    {
        Transform root = NewScene("Lobby", out _);
        Background(root, city, 0.82f);
        var ui = new GameObject("LobbyUI").AddComponent<LobbyUI>();

        // 위쪽 바
        Image bar = UIKit.Panel(root, "TopBar", UIKit.Cream, false);
        bar.rectTransform.anchorMin = new Vector2(0f, 1f);
        bar.rectTransform.anchorMax = new Vector2(1f, 1f);
        bar.rectTransform.pivot = new Vector2(0.5f, 1f);
        bar.rectTransform.sizeDelta = new Vector2(0f, 170f);
        bar.rectTransform.anchoredPosition = Vector2.zero;
        bar.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);

        ui.backButton = UIKit.Button(bar.transform, "BackButton", "<", UIKit.CreamDark, UIKit.Brown, 60);
        ((RectTransform)ui.backButton.transform).At(new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(110f, 110f));
        Text lobbyTitle = UIKit.Label(bar.transform, "Title", "로비", 76, UIKit.Orange, TextAnchor.MiddleLeft).Outlined(UIKit.Brown, 4f);
        lobbyTitle.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(180f, 14f), new Vector2(400f, 100f));
        ui.nicknameText = UIKit.Label(bar.transform, "Nickname", "닉네임", 38, UIKit.Brown, TextAnchor.MiddleLeft);
        ui.nicknameText.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(185f, -48f), new Vector2(600f, 50f));

        ui.joinRandomButton = UIKit.Button(bar.transform, "JoinRoomButton", "Join Room", UIKit.Mint, Color.white, 46);
        ui.joinRandomButton.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 3f);
        ((RectTransform)ui.joinRandomButton.transform).At(new Vector2(1f, 0.5f), new Vector2(-420f, 0f), new Vector2(340f, 110f));
        ui.createRoomButton = UIKit.Button(bar.transform, "CreateRoomButton", "Create Room", UIKit.Orange, Color.white, 46);
        ui.createRoomButton.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 3f);
        ((RectTransform)ui.createRoomButton.transform).At(new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(360f, 110f));

        // 방 목록
        ScrollRect scroll = UIKit.Scroll(root, "RoomList", out RectTransform content);
        ((RectTransform)scroll.transform).Fill(80f, 70f, 80f, 220f);
        ui.roomListContent = content;
        ui.roomItemTemplate = BuildRoomItem(content);
        ui.emptyText = UIKit.Label(scroll.transform, "Empty", "아직 방이 없어요.\n오른쪽 위 Create Room 으로 방을 만들어 보세요!", 46, UIKit.Brown);
        ui.emptyText.rectTransform.Fill();

        BuildCreatePanel(root, ui);
        BuildPasswordPanel(root, ui);
        BuildRoomPanel(root, ui);

        // 로딩
        Image loading = UIKit.Panel(root, "LoadingPanel", new Color(0.12f, 0.07f, 0.04f, 0.85f), false);
        loading.rectTransform.Fill();
        Text loadingText = UIKit.Label(loading.transform, "Text", "게임 로딩중...", 100, Color.white).Outlined(UIKit.Brown, 5f);
        loadingText.rectTransform.Fill();
        ui.loadingPanel = loading.gameObject;

        // 알림
        ui.toastText = UIKit.Label(root, "Toast", "", 44, Color.white).Outlined(UIKit.Brown, 4f);
        ui.toastText.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(1500f, 80f));

        loading.gameObject.SetActive(false);
        Save("Lobby");
    }

    static RoomListItem BuildRoomItem(Transform content)
    {
        Image bg = UIKit.Panel(content, "RoomItemTemplate", Color.white);
        bg.rectTransform.sizeDelta = new Vector2(0f, 130f);
        var item = bg.gameObject.AddComponent<RoomListItem>();

        item.nameText = UIKit.Label(bg.transform, "Name", "방 이름", 46, UIKit.Brown, TextAnchor.MiddleLeft);
        item.nameText.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(40f, 22f), new Vector2(900f, 60f));
        item.infoText = UIKit.Label(bg.transform, "Info", "CITY · 3라운드", 32, UIKit.Gray, TextAnchor.MiddleLeft);
        item.infoText.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(40f, -30f), new Vector2(900f, 44f));
        item.lockText = UIKit.Label(bg.transform, "Lock", "암호", 34, UIKit.Pink);
        item.lockText.rectTransform.At(new Vector2(1f, 0.5f), new Vector2(-560f, 0f), new Vector2(120f, 60f));
        item.countText = UIKit.Label(bg.transform, "Count", "1/4", 46, UIKit.Brown);
        item.countText.rectTransform.At(new Vector2(1f, 0.5f), new Vector2(-330f, 0f), new Vector2(180f, 60f));
        item.joinButton = UIKit.Button(bg.transform, "JoinButton", "입장", UIKit.Mint, Color.white, 42);
        ((RectTransform)item.joinButton.transform).At(new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(220f, 90f));
        return item;
    }

    static Image Modal(Transform root, string name, Vector2 size, out Image card)
    {
        Image shade = UIKit.Panel(root, name, UIKit.Shade, false);
        shade.rectTransform.Fill();
        card = UIKit.Panel(shade.transform, "Card", UIKit.Cream);
        card.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, size);
        card.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -10f);
        return shade;
    }

    static Text RowLabel(Transform parent, string text, float y)
    {
        Text t = UIKit.Label(parent, text + "Label", text, 42, UIKit.Brown, TextAnchor.MiddleLeft);
        t.rectTransform.At(new Vector2(0f, 1f), new Vector2(70f, y), new Vector2(240f, 70f));
        return t;
    }

    static void BuildCreatePanel(Transform root, LobbyUI ui)
    {
        Image shade = Modal(root, "CreatePanel", new Vector2(1200f, 960f), out Image card);
        ui.createPanel = shade.gameObject;
        Transform c = card.transform;

        Text title = UIKit.Label(c, "Title", "방 만들기", 64, UIKit.Orange).Outlined(UIKit.Brown, 4f);
        title.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(800f, 90f));

        // 맵 선택: < [CITY] >
        Image frame = UIKit.Panel(c, "MapFrame", UIKit.Brown);
        frame.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(560f, 315f));
        ui.mapImage = UIKit.Panel(frame.transform, "MapImage", Color.white, false);
        ui.mapImage.rectTransform.Fill(8f, 8f, 8f, 8f);
        ui.mapUnknownText = UIKit.Label(frame.transform, "Unknown", "??", 150, UIKit.Cream).Outlined(UIKit.Brown, 5f);
        ui.mapUnknownText.rectTransform.Fill();
        ui.mapNameText = UIKit.Label(c, "MapName", "CITY", 52, UIKit.Brown).Outlined(UIKit.Cream, 2f);
        ui.mapNameText.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -455f), new Vector2(560f, 70f));
        ui.mapPrevButton = UIKit.Button(c, "MapPrev", "<", UIKit.CreamDark, UIKit.Brown, 80);
        ((RectTransform)ui.mapPrevButton.transform).At(new Vector2(0.5f, 1f), new Vector2(-380f, -235f), new Vector2(110f, 110f));
        ui.mapNextButton = UIKit.Button(c, "MapNext", ">", UIKit.CreamDark, UIKit.Brown, 80);
        ((RectTransform)ui.mapNextButton.transform).At(new Vector2(0.5f, 1f), new Vector2(380f, -235f), new Vector2(110f, 110f));

        // 방 이름
        RowLabel(c, "방 이름", -535f);
        ui.roomNameInput = UIKit.Input(c, "RoomNameInput", "방 이름", 40);
        ui.roomNameInput.characterLimit = 20;
        ui.roomNameInput.rectTransform().At(new Vector2(0f, 1f), new Vector2(330f, -535f), new Vector2(800f, 80f));

        // 인원
        RowLabel(c, "인원", -630f);
        ui.playersText = UIKit.Label(c, "Players", "8 vs 8", 46, UIKit.Brown, TextAnchor.MiddleLeft);
        ui.playersText.rectTransform.At(new Vector2(0f, 1f), new Vector2(330f, -630f), new Vector2(380f, 80f));

        // 라운드
        Text roundLabel = UIKit.Label(c, "RoundLabel", "라운드", 42, UIKit.Brown, TextAnchor.MiddleLeft);
        roundLabel.rectTransform.At(new Vector2(0f, 1f), new Vector2(760f, -630f), new Vector2(160f, 80f));
        ui.rounds3Button = UIKit.Button(c, "Rounds3", "3", UIKit.Orange, Color.white, 46);
        ((RectTransform)ui.rounds3Button.transform).At(new Vector2(0f, 1f), new Vector2(920f, -630f), new Vector2(90f, 80f));
        ui.rounds5Button = UIKit.Button(c, "Rounds5", "5", UIKit.CreamDark, Color.white, 46);
        ((RectTransform)ui.rounds5Button.transform).At(new Vector2(0f, 1f), new Vector2(1030f, -630f), new Vector2(90f, 80f));

        // 암호
        RowLabel(c, "암호", -725f);
        ui.passwordInput = UIKit.Input(c, "PasswordInput", "비워 두면 공개방", 40, true);
        ui.passwordInput.characterLimit = 12;
        ui.passwordInput.rectTransform().At(new Vector2(0f, 1f), new Vector2(330f, -725f), new Vector2(800f, 80f));

        ui.createErrorText = UIKit.Label(c, "Error", "", 34, UIKit.Pink);
        ui.createErrorText.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(1000f, 50f));
        ui.createCancelButton = UIKit.Button(c, "Cancel", "취소", UIKit.CreamDark, UIKit.Brown, 46);
        ((RectTransform)ui.createCancelButton.transform).At(new Vector2(0.5f, 0f), new Vector2(-180f, 35f), new Vector2(300f, 100f));
        ui.createConfirmButton = UIKit.Button(c, "Create", "만들기", UIKit.Orange, Color.white, 46);
        ui.createConfirmButton.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 3f);
        ((RectTransform)ui.createConfirmButton.transform).At(new Vector2(0.5f, 0f), new Vector2(180f, 35f), new Vector2(300f, 100f));
        shade.gameObject.SetActive(false);
    }

    static void BuildPasswordPanel(Transform root, LobbyUI ui)
    {
        Image shade = Modal(root, "PasswordPanel", new Vector2(900f, 450f), out Image card);
        ui.passwordPanel = shade.gameObject;
        ui.passwordRoomText = UIKit.Label(card.transform, "Title", "방 암호", 50, UIKit.Brown);
        ui.passwordRoomText.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(820f, 70f));
        ui.joinPasswordInput = UIKit.Input(card.transform, "PasswordInput", "암호", 44, true);
        ui.joinPasswordInput.rectTransform().At(new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(700f, 95f));
        ui.passwordCancelButton = UIKit.Button(card.transform, "Cancel", "취소", UIKit.CreamDark, UIKit.Brown, 44);
        ((RectTransform)ui.passwordCancelButton.transform).At(new Vector2(0.5f, 0f), new Vector2(-170f, 40f), new Vector2(280f, 95f));
        ui.passwordOkButton = UIKit.Button(card.transform, "Ok", "입장", UIKit.Mint, Color.white, 44);
        ((RectTransform)ui.passwordOkButton.transform).At(new Vector2(0.5f, 0f), new Vector2(170f, 40f), new Vector2(280f, 95f));
        shade.gameObject.SetActive(false);
    }

    /// <summary>팀 칸: 색 머리띠(팀 이름 + 인원) + 플레이어 목록. 칸 전체가 버튼</summary>
    static Button TeamColumn(Transform parent, string name, string title, Color color, Vector2 pos, bool left,
        out RectTransform content, out Text countText)
    {
        Image bg = UIKit.Panel(parent, name, new Color(1f, 1f, 1f, 0.75f));
        Vector2 anchor = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        bg.rectTransform.At(anchor, pos, new Vector2(760f, 640f));
        Button button = bg.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.97f, 0.9f, 1f);
        button.colors = colors;

        Image head = UIKit.Panel(bg.transform, "Header", color);
        head.raycastTarget = false;
        head.rectTransform.anchorMin = new Vector2(0f, 1f);
        head.rectTransform.anchorMax = new Vector2(1f, 1f);
        head.rectTransform.pivot = new Vector2(0.5f, 1f);
        head.rectTransform.sizeDelta = new Vector2(-16f, 96f);
        head.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        Text titleText = UIKit.Label(head.transform, "Title", title, 54, Color.white, TextAnchor.MiddleLeft).Outlined(UIKit.Brown, 3f);
        titleText.rectTransform.Fill(30f, 0f, 160f, 0f);
        countText = UIKit.Label(head.transform, "Count", "0/8", 46, Color.white, TextAnchor.MiddleRight).Outlined(UIKit.Brown, 3f);
        countText.rectTransform.Fill(400f, 0f, 30f, 0f);

        content = UIKit.Rect(bg.transform, "List").Fill(30f, 60f, 30f, 120f);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        Text hint = UIKit.Label(bg.transform, "Hint", "누르면 이 팀으로 이동", 28, UIKit.Gray);
        hint.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(700f, 40f));
        return button;
    }

    static void BuildRoomPanel(Transform root, LobbyUI ui)
    {
        Image panel = UIKit.Panel(root, "RoomPanel", UIKit.Cream, false);
        panel.rectTransform.Fill();
        ui.roomPanel = panel.gameObject;
        Transform c = panel.transform;

        ui.roomTitleText = UIKit.Label(c, "Title", "방 이름", 76, UIKit.Orange).Outlined(UIKit.Brown, 4f);
        ui.roomTitleText.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1600f, 110f));

        // 왼쪽 위 작은 맵 사진 + 가운데 정보
        Image frame = UIKit.Panel(c, "MapFrame", UIKit.Brown);
        frame.rectTransform.At(new Vector2(0f, 1f), new Vector2(80f, -30f), new Vector2(300f, 170f));
        ui.roomMapImage = UIKit.Panel(frame.transform, "MapImage", Color.white, false);
        ui.roomMapImage.rectTransform.Fill(6f, 6f, 6f, 6f);
        ui.roomInfoText = UIKit.Label(c, "Info", "CITY · 3라운드", 40, UIKit.Brown);
        ui.roomInfoText.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -145f), new Vector2(1000f, 60f));

        // 왼쪽 틱토이 / 오른쪽 와인더 (칸을 누르면 그 팀으로 이동)
        RectTransform tickContent;
        RectTransform winderContent;
        ui.tickToyTeamButton = TeamColumn(c, "TickToyTeam", "틱토이", UIKit.Mint, new Vector2(80f, -220f), true, out tickContent, out ui.tickToyCountText);
        ui.winderTeamButton = TeamColumn(c, "WinderTeam", "와인더", UIKit.Pink, new Vector2(-80f, -220f), false, out winderContent, out ui.winderCountText);
        ui.tickToyListContent = tickContent;
        ui.winderListContent = winderContent;
        ui.playerListContent = tickContent;

        ui.switchTeamButton = UIKit.Button(c, "SwitchTeamButton", "<  팀 바꾸기  >", UIKit.Orange, Color.white, 34);
        ui.switchTeamButton.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 2f);
        ((RectTransform)ui.switchTeamButton.transform).At(new Vector2(0.5f, 1f), new Vector2(0f, -480f), new Vector2(210f, 120f));

        Text item = UIKit.Label(tickContent, "PlayerItemTemplate", "플레이어", 40, UIKit.Brown, TextAnchor.MiddleLeft);
        item.rectTransform.sizeDelta = new Vector2(0f, 58f);
        ui.playerItemTemplate = item;

        ui.leaveButton = UIKit.Button(c, "LeaveButton", "나가기", UIKit.CreamDark, UIKit.Brown, 46);
        ((RectTransform)ui.leaveButton.transform).At(new Vector2(0f, 0f), new Vector2(110f, 70f), new Vector2(300f, 110f));
        ui.addBotButton = UIKit.Button(c, "AddBotButton", "테스트 봇 추가", UIKit.Sky, Color.white, 38);
        ((RectTransform)ui.addBotButton.transform).At(new Vector2(0f, 0f), new Vector2(440f, 70f), new Vector2(330f, 110f));
        ui.readyButton = UIKit.Button(c, "ReadyButton", "준비", UIKit.Mint, Color.white, 52);
        ui.readyButton.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 3f);
        ((RectTransform)ui.readyButton.transform).At(new Vector2(1f, 0f), new Vector2(-110f, 70f), new Vector2(480f, 120f));
        ui.startButton = UIKit.Button(c, "StartButton", "게임 시작!", UIKit.Orange, Color.white, 52);
        ui.startButton.GetComponentInChildren<Text>().Outlined(UIKit.Brown, 3f);
        ((RectTransform)ui.startButton.transform).At(new Vector2(1f, 0f), new Vector2(-110f, 70f), new Vector2(480f, 120f));

        panel.gameObject.SetActive(false);
    }
}

static class RectExt
{
    public static RectTransform rectTransform(this Component c) => (RectTransform)c.transform;
}
