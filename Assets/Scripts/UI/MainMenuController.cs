using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

/// <summary>
/// 主菜单控制器 (UIToolkit 版)
/// 挂载在 MainMenu Scene 的物体上，无需 Canvas。
/// 自动生成简单的 UI 供测试逻辑。
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MainMenuController : MonoBehaviour {

    private UIDocument _uiDocument;
    private VisualElement _root;
    
    // Containers
    private VisualElement _mainMenuContainer;
    private VisualElement _versusContainer;
    
    // Inputs
    private TextField _lobbyIdInput;
    
    // UI Elements for dynamic update
    private Button _btnCreate;
    private Button _btnJoin;
    private Button _btnInvite;
    private Label _tipLabel;

    private void OnEnable() {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null) return;

        _root = _uiDocument.rootVisualElement;
        _root.Clear(); // 清空原有内容

        // 设置基本样式
        _root.style.justifyContent = Justify.Center;
        _root.style.alignItems = Align.Center;
        _root.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 1f));

        CreateMainMenu();
        CreateVersusMenu();

        ShowMainMenu();
    }

    private void CreateMainMenu() {
        _mainMenuContainer = new VisualElement();
        _mainMenuContainer.style.width = 300;
        _mainMenuContainer.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.8f));
        _mainMenuContainer.style.paddingTop = 20;
        _mainMenuContainer.style.paddingBottom = 20;
        _mainMenuContainer.style.paddingLeft = 20;
        _mainMenuContainer.style.paddingRight = 20;
        _mainMenuContainer.style.borderTopLeftRadius = 10;
        _mainMenuContainer.style.borderTopRightRadius = 10;
        _mainMenuContainer.style.borderBottomLeftRadius = 10;
        _mainMenuContainer.style.borderBottomRightRadius = 10;

        Label title = new Label("Main Menu");
        title.style.fontSize = 24;
        title.style.color = Color.white;
        title.style.alignSelf = Align.Center;
        title.style.marginBottom = 20;
        _mainMenuContainer.Add(title);

        Button btnAdventure = CreateButton("Adventure Mode", OnAdventureClicked);
        _mainMenuContainer.Add(btnAdventure);

        Button btnVersus = CreateButton("Versus Mode (Steam P2P)", OnVersusClicked);
        _mainMenuContainer.Add(btnVersus);

        Button btnSettings = CreateButton("Settings", OnSettingsClicked);
        _mainMenuContainer.Add(btnSettings);

        Button btnExit = CreateButton("Exit", OnExitClicked);
        _mainMenuContainer.Add(btnExit);

        _root.Add(_mainMenuContainer);
    }

    private void CreateVersusMenu() {
        _versusContainer = new VisualElement();
        _versusContainer.style.width = 300;
        _versusContainer.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.8f));
        _versusContainer.style.paddingTop = 20;
        _versusContainer.style.paddingBottom = 20;
        _versusContainer.style.paddingLeft = 20;
        _versusContainer.style.paddingRight = 20;
        _versusContainer.style.borderTopLeftRadius = 10;
        _versusContainer.style.borderTopRightRadius = 10;
        _versusContainer.style.borderBottomLeftRadius = 10;
        _versusContainer.style.borderBottomRightRadius = 10;
        _versusContainer.style.display = DisplayStyle.None; // 默认隐藏

        Label title = new Label("Versus Mode");
        title.style.fontSize = 24;
        title.style.color = Color.white;
        title.style.alignSelf = Align.Center;
        title.style.marginBottom = 20;
        _versusContainer.Add(title);

        // IP Input removed for Steam P2P
        _tipLabel = new Label("Invite friends via Steam Overlay");
        _tipLabel.style.color = Color.gray;
        _tipLabel.style.fontSize = 12;
        _tipLabel.style.marginBottom = 10;
        _tipLabel.style.alignSelf = Align.Center;
        _versusContainer.Add(_tipLabel);

        // Lobby ID Input
        _lobbyIdInput = new TextField();
        _lobbyIdInput.label = "Lobby ID"; // 设置标签
        _lobbyIdInput.value = ""; // 默认空
        _lobbyIdInput.style.marginBottom = 5;
        _versusContainer.Add(_lobbyIdInput);

        // Init Buttons
        _btnJoin = CreateButton("Join by ID", OnJoinByIdClicked);
        _versusContainer.Add(_btnJoin);

        _btnCreate = CreateButton("Create Steam Lobby", OnCreateRoomClicked);
        _versusContainer.Add(_btnCreate);

        _btnInvite = CreateButton("Open Friends List", OnInviteClicked);
        _versusContainer.Add(_btnInvite);

        Button btnBack = CreateButton("Back", OnBackClicked);
        btnBack.style.marginTop = 20;
        _versusContainer.Add(btnBack);

        _root.Add(_versusContainer);
    }

    private Button CreateButton(string text, System.Action onClick) {
        Button btn = new Button(onClick);
        btn.text = text;
        btn.style.height = 40;
        btn.style.marginBottom = 10;
        btn.style.fontSize = 14;
        return btn;
    }

    // --- Actions ---

    private void ShowMainMenu() {
        _mainMenuContainer.style.display = DisplayStyle.Flex;
        _versusContainer.style.display = DisplayStyle.None;
    }

    private void ShowVersusMenu() {
        _mainMenuContainer.style.display = DisplayStyle.None;
        _versusContainer.style.display = DisplayStyle.Flex;

        UpdateVersusUIState();
    }

    private void UpdateVersusUIState() {
        if (_tipLabel == null || _btnCreate == null || _btnJoin == null || _btnInvite == null || _lobbyIdInput == null) {
            return;
        }

        bool isUnityTransport = false;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null) {
             var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
             if (transport is Unity.Netcode.Transports.UTP.UnityTransport) {
                 isUnityTransport = true;
             }
        }

        if (isUnityTransport) {
            // --- Local Dev Mode (UnityTransport) ---
            _tipLabel.text = "Local Dev Mode (UnityTransport)";
            
            // Host Button
            _btnCreate.text = "Start Local Host";
            
            // Client Button
            _btnJoin.text = "Join Localhost (127.0.0.1)";
            
            // Hide unrelated elements
            _lobbyIdInput.style.display = DisplayStyle.None;
            _btnInvite.style.display = DisplayStyle.None;
        } else {
            // --- Steam P2P Mode ---
            _tipLabel.text = "Invite friends via Steam Overlay";
            
            _btnCreate.text = "Create Steam Lobby";
            _btnJoin.text = "Join by ID";
            
            _lobbyIdInput.style.display = DisplayStyle.Flex;
            _btnInvite.style.display = DisplayStyle.Flex;
        }
    }

    private void OnAdventureClicked() {
        Debug.Log("Adventure Mode Coming Soon...");
    }

    private void OnVersusClicked() {
        ShowVersusMenu();
    }

    private void OnSettingsClicked() {
        Debug.Log("Settings Coming Soon...");
    }

    private void OnExitClicked() {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void OnCreateRoomClicked() {
        if (IsUnityTransport()) {
            Debug.Log("[MainMenu] Starting Local Host...");
            NetworkManager.Singleton.StartHost();
            NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            return;
        }

        Debug.Log("[MainMenu] Creating Steam Lobby...");
        if (SteamLobbyManager.Singleton != null) {
            SteamLobbyManager.Singleton.CreateLobby();
        } else {
            Debug.LogError("SteamLobbyManager is missing!");
        }
    }

    private void OnInviteClicked() {
        Debug.Log("[MainMenu] Opening Friends Overlay...");
        if (SteamLobbyManager.Singleton != null) {
            SteamLobbyManager.Singleton.OpenInviteOverlay();
        }
    }

    private void OnJoinByIdClicked() {
        if (IsUnityTransport()) {
             Debug.Log("[MainMenu] Joining Localhost...");
             NetworkManager.Singleton.StartClient();
             return;
        }

        string id = _lobbyIdInput.value;
        if (string.IsNullOrEmpty(id)) {
            Debug.LogWarning("[MainMenu] Lobby ID is empty.");
            return;
        }
        
        Debug.Log($"[MainMenu] Joining Lobby ID: {id}");
        if (SteamLobbyManager.Singleton != null) {
            SteamLobbyManager.Singleton.JoinLobbyByID(id);
        }
    }

    private bool IsUnityTransport() {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null) {
             var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
             return transport is Unity.Netcode.Transports.UTP.UnityTransport;
        }
        return false;
    }

    private void OnBackClicked() {
        ShowMainMenu();
    }
}
