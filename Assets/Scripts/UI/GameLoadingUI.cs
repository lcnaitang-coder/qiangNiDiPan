using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// 游戏加载界面
/// 模仿金铲铲之战风格：显示每个玩家的卡片和加载进度
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class GameLoadingUI : MonoBehaviour {
    private UIDocument _uiDocument;
    private VisualElement _root;
    private VisualElement _playerContainer;
    
    // UI 元素缓存 (Key: ClientId)
    private Dictionary<ulong, PlayerCardUI> _playerCards = new Dictionary<ulong, PlayerCardUI>();

    private class PlayerCardUI {
        public VisualElement Root;
        public VisualElement ProgressBarFill;
        public Label ProgressLabel;
        public Label NameLabel;
        
        // 模拟进度动画用
        public float SimulatedProgress;
    }

    private void Awake() {
        _uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable() {
        if (_uiDocument == null) return;
        
        // 1. 根节点样式
        _root = _uiDocument.rootVisualElement;
        _root.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.15f, 1f)); // 深蓝/黑色背景
        _root.style.justifyContent = Justify.Center;
        _root.style.alignItems = Align.Center;

        // 2. 玩家卡片容器 (横向排列)
        _playerContainer = new VisualElement();
        _playerContainer.style.flexDirection = FlexDirection.Row;
        _playerContainer.style.flexWrap = Wrap.Wrap; // 允许换行
        _playerContainer.style.justifyContent = Justify.Center;
        _playerContainer.style.alignItems = Align.Center;
        _playerContainer.style.width = Length.Percent(100);
        _root.Add(_playerContainer);

        // 3. 初始刷新
        RefreshCards();
    }

    private void Update() {
        if (GameFlowManager.Singleton == null) return;

        // 检查游戏是否开始
        if (GameFlowManager.Singleton.IsGameStarted.Value) {
            _root.style.display = DisplayStyle.None;
            return;
        } else {
            _root.style.display = DisplayStyle.Flex;
        }

        // 检查是否有新玩家加入或状态变化
        // 注意：NetworkList 的 OnListChanged 事件可能在某些情况下不如每帧检查直接（对于简单的 UI）
        // 这里为了简单，如果数量不一致则重绘，否则更新进度
        var states = GameFlowManager.Singleton.PlayerLoadStates;
        
        if (states.Count != _playerCards.Count) {
            RefreshCards();
        }

        // 更新进度条动画
        UpdateProgress(states);
    }

    private void RefreshCards() {
        _playerContainer.Clear();
        _playerCards.Clear();
        
        if (GameFlowManager.Singleton == null) return;

        foreach (var state in GameFlowManager.Singleton.PlayerLoadStates) {
            CreateCard(state);
        }
    }

    private void CreateCard(PlayerLoadState state) {
        // 卡片背景框
        var card = new VisualElement();
        card.style.width = 200;
        card.style.height = 300;
        card.style.marginRight = 20;
        card.style.marginLeft = 20;
        card.style.marginBottom = 20;
        card.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.25f));
        card.style.borderTopLeftRadius = 10;
        card.style.borderTopRightRadius = 10;
        card.style.borderBottomLeftRadius = 10;
        card.style.borderBottomRightRadius = 10;
        card.style.alignItems = Align.Center;
        card.style.justifyContent = Justify.SpaceBetween;
        card.style.paddingTop = 10;
        card.style.paddingBottom = 10;

        // 头像区域 (占位)
        var avatar = new VisualElement();
        avatar.style.width = 120;
        avatar.style.height = 120;
        avatar.style.backgroundColor = new StyleColor(GameConfig.GetPlayerColor(state.ClientId)); // 使用玩家颜色
        avatar.style.borderTopLeftRadius = 60;
        avatar.style.borderTopRightRadius = 60;
        avatar.style.borderBottomLeftRadius = 60;
        avatar.style.borderBottomRightRadius = 60;
        avatar.style.marginTop = 30;
        card.Add(avatar);

        // 昵称
        var nameLabel = new Label(state.PlayerName.ToString());
        nameLabel.style.fontSize = 18;
        nameLabel.style.color = Color.white;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        card.Add(nameLabel);

        // 进度条容器
        var progressContainer = new VisualElement();
        progressContainer.style.width = Length.Percent(80);
        progressContainer.style.height = 20;
        progressContainer.style.backgroundColor = new StyleColor(Color.black);
        progressContainer.style.borderTopLeftRadius = 5;
        progressContainer.style.borderTopRightRadius = 5;
        progressContainer.style.borderBottomLeftRadius = 5;
        progressContainer.style.borderBottomRightRadius = 5;
        progressContainer.style.overflow = Overflow.Hidden; // 裁剪
        card.Add(progressContainer);

        // 进度条填充
        var fill = new VisualElement();
        fill.style.width = Length.Percent(0);
        fill.style.height = Length.Percent(100);
        fill.style.backgroundColor = new StyleColor(new Color(0f, 0.8f, 0.4f)); // 绿色
        progressContainer.Add(fill);

        // 进度文字
        var progressLabel = new Label("0%");
        progressLabel.style.fontSize = 14;
        progressLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
        progressLabel.style.marginBottom = 10;
        card.Add(progressLabel);

        _playerContainer.Add(card);

        // 缓存
        var uiData = new PlayerCardUI {
            Root = card,
            ProgressBarFill = fill,
            ProgressLabel = progressLabel,
            NameLabel = nameLabel,
            SimulatedProgress = 0f
        };
        _playerCards.Add(state.ClientId, uiData);
    }

    private void UpdateProgress(NetworkList<PlayerLoadState> states) {
        foreach (var state in states) {
            if (_playerCards.TryGetValue(state.ClientId, out var uiData)) {
                // 目标进度：如果是 Finished 则 100%，否则缓慢增加到 99%
                float target = state.IsFinished ? 100f : 99f;
                
                // 模拟进度增长
                if (state.IsFinished) {
                    uiData.SimulatedProgress = 100f; // 瞬间完成
                } else {
                    // 缓慢增长，每秒约 30%，最高到 99%
                    if (uiData.SimulatedProgress < 99f) {
                        uiData.SimulatedProgress += Time.deltaTime * 30f;
                        if (uiData.SimulatedProgress > 99f) uiData.SimulatedProgress = 99f;
                    }
                }

                // 更新 UI
                uiData.ProgressBarFill.style.width = Length.Percent(uiData.SimulatedProgress);
                
                if (uiData.SimulatedProgress >= 100f) {
                    uiData.ProgressLabel.text = "READY";
                    uiData.ProgressLabel.style.color = new StyleColor(Color.green);
                } else {
                    uiData.ProgressLabel.text = $"{(int)uiData.SimulatedProgress}%";
                }
            }
        }
    }
}
