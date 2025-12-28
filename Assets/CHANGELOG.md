2025 12 27
- 地图系统升级：实现了基于 BFS + 随机扰动的路径生成算法，支持点击首尾自动生成蜿蜒的随机道路。
- 地形稳定性优化：修复了点击地图导致地形重排和消失的问题，增加了 Default Path Prefab 保底机制，防止路径断裂。
- 智能调试工具：在 HexTileProfile 中增加了“标准掩码（Canonical Mask）”计算与提示功能，当缺少特定路块形状时，控制台会直接给出配置建议。
- 交互优化：改进了地图编辑交互，采用“起点-终点”两步确认模式，避免误触破坏现有地图结构。

2025 12 26
- 架构优化：拆分 Troop 逻辑与表现层，创建 TroopVisualController 负责模型生成与对象池管理。
- 架构优化：引入 GameCommandManager 统一管理 Gameplay RPC（如派兵、升级），实现输入逻辑与网络传输解耦。
- 重构：精简 Troop.cs 和 Building.cs，移除直接的 RPC 调用，提高代码可维护性。
- 新增 GameLoadingUI：负责游戏加载界面的显示与隐藏，当所有玩家都加载成功时方可进入游戏，类似金铲铲的风格

2025 12 24
- 网络重构：将 Unity Netcode 项目重构为基于 Steamworks.NET 的 P2P 联机架构。
- 新增 SteamManager：管理 Steam API 初始化与生命周期。
- 新增 SteamLobbyManager：实现 Steam 大厅创建、好友邀请及通过 ID 加入房间的功能。
- UI 优化：废弃 IP 直连输入，新增“邀请好友”与“Lobby ID 加入”入口。
- 修复：解决了 Host/Client 自连接冲突及大厅权限判断逻辑。
- 增加了本地测试与steam测试切换功能

2025 12 23
增加建筑物的种类，增加士兵的种类，找到一些低多边形的建筑和士兵模型以及动画，增加ctrl多选建筑的功能

2025 12 22
将建筑物分为逻辑层与变现层，逻辑层负责处理建筑的基本逻辑，变现层负责处理建筑的可视化效果。
实现点击升级按钮建筑物的升级效果


# 开发日志 (Devlog) | 2025-12-20
## 一、核心任务目标

### 1. 架构升级
- 将单机逻辑重构为基于 **Netcode for GameObjects (NGO)** 的服务器权威架构。

### 2. 视觉优化
- 实现“**长蛇阵**”出兵效果（Sequence Spawning）。
- 采用“**视觉集群（Visual Clustering）**”方案，解决 1:1 派兵带来的网络性能问题。

### 3. 数值驱动
- 建立基于等级（Level）的建筑成长体系。

---

## 二、技术实现深度解析

### A. 建筑等级与动态上限系统

**数据结构**
- 在 `BuildingData` 中定义 `LevelStats` 列表。
- 支持为不同等级配置：
  - `maxCapacity`
  - `productionRate`

**计算公式**
```math
MaxCapacity = Level × 10


