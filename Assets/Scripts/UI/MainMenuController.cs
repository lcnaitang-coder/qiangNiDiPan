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
    private TextField _ipInput;

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

        Button btnVersus = CreateButton("Versus Mode", OnVersusClicked);
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

        _ipInput = new TextField("IP Address");
        _ipInput.value = "127.0.0.1";
        _ipInput.style.color = Color.white;
        _ipInput.style.marginBottom = 10;
        _versusContainer.Add(_ipInput);

        Button btnCreate = CreateButton("Create Room (Host)", OnCreateRoomClicked);
        _versusContainer.Add(btnCreate);

        Button btnJoin = CreateButton("Join Room (Client)", OnJoinRoomClicked);
        _versusContainer.Add(btnJoin);

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
        Debug.Log("[MainMenu] Creating Room (Host)...");
        bool success = NetworkManager.Singleton.StartHost();
        if (success) {
            NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        } else {
            Debug.LogError("Failed to start Host!");
        }
    }

    private void OnJoinRoomClicked() {
        string ip = _ipInput.value;
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        Debug.Log($"[MainMenu] Joining Room at {ip}...");
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null) {
            transport.ConnectionData.Address = ip;
        }

        bool success = NetworkManager.Singleton.StartClient();
        if (!success) {
            Debug.LogError("Failed to start Client!");
        }
    }

    private void OnBackClicked() {
        ShowMainMenu();
    }
}
