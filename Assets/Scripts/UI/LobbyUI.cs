using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// 大厅 UI 逻辑 (UIToolkit 版)
/// 挂载在 Lobby Scene 的物体上，无需 Canvas。
/// 自动生成简单的 UI 供测试逻辑。
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class LobbyUI : MonoBehaviour {

    private UIDocument _uiDocument;
    private VisualElement _root;

    private Label _roomInfoLabel;
    private ScrollView _playerList;
    private Button _readyButton;
    private Button _startGameButton;

    private void OnEnable() {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument == null) return;

        _root = _uiDocument.rootVisualElement;
        _root.Clear();

        // 样式
        _root.style.justifyContent = Justify.FlexStart;
        _root.style.alignItems = Align.Center;
        _root.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.2f, 1f));
        _root.style.paddingTop = 50;

        // 标题
        Label title = new Label("Lobby");
        title.style.fontSize = 32;
        title.style.color = Color.white;
        title.style.marginBottom = 10;
        _root.Add(title);

        // 房间信息
        _roomInfoLabel = new Label("Room: Connecting...");
        _roomInfoLabel.style.fontSize = 18;
        _roomInfoLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
        _roomInfoLabel.style.marginBottom = 20;
        _root.Add(_roomInfoLabel);

        // 玩家列表容器
        VisualElement listContainer = new VisualElement();
        listContainer.style.width = 400;
        listContainer.style.height = 300;
        listContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.5f));
        listContainer.style.marginBottom = 20;
        listContainer.style.borderTopWidth = 1;
        listContainer.style.borderBottomWidth = 1;
        listContainer.style.borderLeftWidth = 1;
        listContainer.style.borderRightWidth = 1;
        listContainer.style.borderTopColor = Color.gray;
        listContainer.style.borderBottomColor = Color.gray;
        listContainer.style.borderLeftColor = Color.gray;
        listContainer.style.borderRightColor = Color.gray;
        _root.Add(listContainer);

        _playerList = new ScrollView();
        listContainer.Add(_playerList);

        // 底部按钮区域
        VisualElement buttonArea = new VisualElement();
        buttonArea.style.flexDirection = FlexDirection.Row;
        _root.Add(buttonArea);

        _readyButton = new Button(OnReadyClicked);
        _readyButton.text = "Ready";
        _readyButton.style.width = 150;
        _readyButton.style.height = 50;
        _readyButton.style.fontSize = 20;
        _readyButton.style.marginRight = 10;
        buttonArea.Add(_readyButton);

        _startGameButton = new Button(OnStartGameClicked);
        _startGameButton.text = "Start Game";
        _startGameButton.style.width = 150;
        _startGameButton.style.height = 50;
        _startGameButton.style.fontSize = 20;
        _startGameButton.style.display = DisplayStyle.None; // 默认隐藏
        buttonArea.Add(_startGameButton);
    }

    private void Start() {
        UpdateRoomInfo();
    }

    private void Update() {
        RefreshPlayerList();
        
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) {
            _startGameButton.style.display = DisplayStyle.Flex;
            UpdateStartButtonState();
        } else {
            _startGameButton.style.display = DisplayStyle.None;
        }
    }

    private void UpdateRoomInfo() {
        if (NetworkManager.Singleton != null && _roomInfoLabel != null) {
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            string ip = transport != null ? transport.ConnectionData.Address : "Unknown";
            _roomInfoLabel.text = $"Room IP: {ip}";
        }
    }

    private void RefreshPlayerList() {
        if (_playerList == null) return;

        LobbyPlayerState[] players = FindObjectsOfType<LobbyPlayerState>();
        System.Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        _playerList.Clear();

        foreach (var p in players) {
            VisualElement item = new VisualElement();
            item.style.flexDirection = FlexDirection.Row;
            item.style.justifyContent = Justify.SpaceBetween;
            item.style.paddingLeft = 10;
            item.style.paddingRight = 10;
            item.style.paddingTop = 5;
            item.style.paddingBottom = 5;
            item.style.height = 40;
            item.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            item.style.marginBottom = 2;

            string name = p.PlayerName.Value.ToString();
            if (string.IsNullOrEmpty(name)) name = $"Player {p.OwnerClientId}";
            if (p.IsOwner) name += " (You)";

            Label nameLabel = new Label(name);
            nameLabel.style.color = Color.white;
            nameLabel.style.alignSelf = Align.Center;
            item.Add(nameLabel);

            Label statusLabel = new Label(p.IsReady.Value ? "READY" : "WAITING");
            statusLabel.style.color = p.IsReady.Value ? Color.green : Color.red;
            statusLabel.style.alignSelf = Align.Center;
            item.Add(statusLabel);

            _playerList.Add(item);

            // 更新自己的按钮状态
            if (p.IsOwner && _readyButton != null) {
                _readyButton.text = p.IsReady.Value ? "Cancel Ready" : "Ready";
                _readyButton.style.backgroundColor = p.IsReady.Value ? new StyleColor(new Color(0.6f, 0.2f, 0.2f)) : new StyleColor(new Color(0.2f, 0.6f, 0.2f));
            }
        }
    }

    private void UpdateStartButtonState() {
        if (_startGameButton == null) return;

        LobbyPlayerState[] players = FindObjectsOfType<LobbyPlayerState>();
        bool allReady = players.Length > 0;
        foreach (var p in players) {
            if (!p.IsReady.Value) {
                allReady = false;
                break;
            }
        }
        _startGameButton.SetEnabled(allReady);
    }

    private void OnReadyClicked() {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient) {
            // 尝试 1: 直接获取
            if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null) {
                var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<LobbyPlayerState>();
                if (localPlayer != null) {
                    localPlayer.ToggleReady();
                    return;
                }
            }

            // 尝试 2: 遍历查找 (备用方案)
            var players = FindObjectsOfType<LobbyPlayerState>();
            foreach (var p in players) {
                if (p.IsOwner) {
                    p.ToggleReady();
                    return;
                }
            }
            
            Debug.LogWarning("[LobbyUI] Could not find local LobbyPlayerState!");
        }
    }

    private void OnStartGameClicked() {
        if (GameFlowManager.Singleton != null) {
            GameFlowManager.Singleton.StartGame();
        } else {
            Debug.LogError("[LobbyUI] GameFlowManager Singleton not found!");
        }
    }
}
