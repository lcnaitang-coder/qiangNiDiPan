using UnityEngine;
using Steamworks;

/// <summary>
/// Steam 管理器 (Facepunch.Steamworks 版)
/// 负责 SteamClient 的初始化、每帧回调和关闭。
/// </summary>
public class SteamManager : MonoBehaviour {
    
    public static SteamManager Singleton { get; private set; }

    private uint appId = 480; 

    private bool _isInitialized = false;

    private void Awake() {
        if (Singleton != null && Singleton != this) {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);

        try {
            // Facepunch.Steamworks 初始化方式
            SteamClient.Init(appId);
            _isInitialized = true;
            Debug.Log($"[SteamManager] SteamClient Initialized. Name: {SteamClient.Name}");
        } catch (System.Exception e) {
            Debug.LogError($"[SteamManager] Failed to init Steam: {e.Message}");
            // Application.Quit(); // 可选：如果必须依赖 Steam 则退出
        }
    }

    private void Update() {
        if (_isInitialized) {
            // Facepunch.Steamworks 必须每帧调用
            SteamClient.RunCallbacks();
        }
    }

    private void OnDestroy() {
        if (_isInitialized) {
            SteamClient.Shutdown();
            _isInitialized = false;
        }
    }
}
