# KK Scripts

基于 [BepInEx.ScriptLoader](https://github.com/ghorsington/BepInEx.ScriptLoader) ,主要适用于 Koikatu，其余游戏未测试。

## 安装

1. 安装 [BepInEx.ScriptLoader](https://github.com/ghorsington/BepInEx.ScriptLoader)
2. 下载本仓库的脚本，放在 `~\Koikatu\scripts\` 目录下

## 作用

### Disable New Dynamic Bones On Colliders

读取场景后，自动找出所有的 DynamicBoneCollider ，将它们的 KKPE 内的 “Enable New Dynamic Bones” 选项变为 false。

作用：防止加入新角色后动态骨骼异常吸附。

### Work Panel Mover

启动游戏后，将工作室右侧的工作面板移动到指定位置。

作用：开窗口模式后如果最大化游戏，工作面板不会自动到最右侧，这个脚本是为了解决这个痛点。

修改 17 行代码以更改偏移：
```cs
private const float OffsetX = 137f;
private const float OffsetY = -10f;
```

### Auto Sort Female By Date

启动游戏后，默认将加载 Female 后的排序选项卡变为“日期从新到旧”

### Auto Srot Clothes By Date

将服装列表的默认排序改为“日期从新到旧”
