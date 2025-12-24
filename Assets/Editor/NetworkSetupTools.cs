using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using System.IO;

public class NetworkSetupTools : EditorWindow {
    [MenuItem("Tools/Fix Network Setup")]
    public static void FixNetworkSetup() {
        // 1. 确保 Prefab 存放目录存在
        string prefabDir = "Assets/Prefabs";
        if (!Directory.Exists(prefabDir)) {
            Directory.CreateDirectory(prefabDir);
            AssetDatabase.Refresh();
        }

        // 2. 检查是否已经存在 LobbyPlayer Prefab
        string prefabPath = "Assets/Prefabs/LobbyPlayer.prefab";
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (playerPrefab == null) {
            Debug.Log("Creating new LobbyPlayer prefab...");
            // 创建临时 GameObject
            GameObject go = new GameObject("LobbyPlayer");
            go.AddComponent<NetworkObject>();
            go.AddComponent<LobbyPlayerState>();
            
            // 保存为 Prefab
            playerPrefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            GameObject.DestroyImmediate(go);
            Debug.Log($"Created LobbyPlayer prefab at {prefabPath}");
        } else {
            Debug.Log("LobbyPlayer prefab already exists.");
            // 确保组件存在
            bool dirty = false;
            if (playerPrefab.GetComponent<NetworkObject>() == null) {
                playerPrefab.AddComponent<NetworkObject>();
                dirty = true;
            }
            if (playerPrefab.GetComponent<LobbyPlayerState>() == null) {
                playerPrefab.AddComponent<LobbyPlayerState>();
                dirty = true;
            }
            if (dirty) {
                EditorUtility.SetDirty(playerPrefab);
                AssetDatabase.SaveAssets();
            }
        }

        // 3. 找到场景中的 NetworkManager
        NetworkManager networkManager = GameObject.FindObjectOfType<NetworkManager>();
        if (networkManager == null) {
            Debug.LogError("Could not find NetworkManager in the scene!");
            return;
        }

        // 4. 设置 PlayerPrefab
        // 注意：我们需要修改 NetworkManager 组件的 serializedObject
        SerializedObject so = new SerializedObject(networkManager);
        SerializedProperty networkConfigProp = so.FindProperty("NetworkConfig");
        if (networkConfigProp != null) {
            SerializedProperty playerPrefabProp = networkConfigProp.FindPropertyRelative("PlayerPrefab");
            if (playerPrefabProp != null) {
                playerPrefabProp.objectReferenceValue = playerPrefab;
                so.ApplyModifiedProperties();
                Debug.Log("Successfully set NetworkManager PlayerPrefab!");
            } else {
                Debug.LogError("Could not find PlayerPrefab property in NetworkConfig!");
            }
        } else {
            // 尝试直接赋值（运行时属性可能不同，编辑器下最好用 SerializedObject）
            // 但 NetworkManager 的 PlayerPrefab 是在 NetworkConfig 里的，且部分是 private/internal
            // 所以 SerializedObject 是最稳妥的
             Debug.LogError("Could not find NetworkConfig property!");
        }
        
        // 5. 确保 Prefab 被添加到 NetworkPrefabsList (如果有的话)
        // 这里稍微复杂，因为 NetworkManager 可能使用 NetworkPrefabsList 资源，也可能使用内置列表
        // 暂时只设置 PlayerPrefab，通常这对 Player 来说足够了
        
        EditorUtility.SetDirty(networkManager);
        Debug.Log("Network Setup Fix Complete!");
    }
}
