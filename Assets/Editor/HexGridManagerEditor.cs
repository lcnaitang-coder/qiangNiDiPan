using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HexGridManager))]
public class HexGridManagerEditor : Editor
{
    public override void OnInspectorGUI() {
        // 绘制默认的 Inspector (属性等)
        DrawDefaultInspector();

        HexGridManager manager = (HexGridManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("地图编辑工具", EditorStyles.boldLabel);

        if (GUILayout.Button("生成/重置网格 (Clear & Generate)")) {
            // 记录撤销操作，防止误点
            Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Generate Grid");
            manager.GenerateGrid();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("数据存取", EditorStyles.boldLabel);

        // 如果没有分配 MapData，提示用户创建
        if (manager.currentMapData == null) {
            EditorGUILayout.HelpBox("请先在上方 'Current Map Data' 槽位中分配一个 HexMapData 资源，或者点击下方按钮创建新的。", MessageType.Info);
            
            if (GUILayout.Button("创建新地图数据 (Create New MapData)")) {
                CreateNewMapData(manager);
            }
        } else {
            if (GUILayout.Button("保存当前地图到数据 (Save to Data)")) {
                manager.SaveToMapData();
            }

            if (GUILayout.Button("从数据加载地图 (Load from Data)")) {
                // 同样记录撤销
                Undo.RegisterFullObjectHierarchyUndo(manager.gameObject, "Load Map");
                manager.LoadMap(manager.currentMapData);
            }
        }
    }

    private void CreateNewMapData(HexGridManager manager) {
        // 弹窗让用户选择保存路径
        string path = EditorUtility.SaveFilePanelInProject(
            "创建新地图数据", 
            "NewMapData", 
            "asset", 
            "请选择保存位置"
        );

        if (string.IsNullOrEmpty(path)) return;

        HexMapData newData = ScriptableObject.CreateInstance<HexMapData>();
        newData.Initialize(manager.width, manager.height);
        
        AssetDatabase.CreateAsset(newData, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 自动赋值给 Manager
        manager.currentMapData = newData;
        EditorUtility.SetDirty(manager);
    }
}
