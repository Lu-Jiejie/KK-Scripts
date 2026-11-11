# KK Scripts

基于 [BepInEx.ScriptLoader](https://github.com/ghorsington/BepInEx.ScriptLoader) ,主要适用于 Koikatu，其余游戏未测试。

## 安装

1. 安装 [BepInEx.ScriptLoader](https://github.com/ghorsington/BepInEx.ScriptLoader)
2. 下载本仓库的脚本，放在 `~\Koikatu\scripts\` 目录下

## 作用

### Quick Setup

按下快捷键快速配置工作室女角色的状态（服装、皮肤等）。选中了女角色则只改变被选中角色的状态，否则改变所有女角色。

在脚本内根据注释来配置。

兼容 KK/KKS 。

### Auto Hotkey

配置“工作室启动完毕后”、“场景卡加载完毕后”执行指定的快捷键或快捷键序列。

### Disable New Dynamic Bones On Colliders

读取场景后，自动找出所有的 DynamicBoneCollider ，将它们的 KKPE 内的 “Enable New Dynamic Bones” 选项变为 false。

作用：防止加入新角色后动态骨骼异常吸附。

### Auto Sort Female By Date

启动游戏后，默认将加载 Female 后的排序选项卡变为“日期从新到旧”

### Auto Srot Clothes By Date

将服装列表的默认排序改为“日期从新到旧”

### Studio Toolbar Hotkey Buttons

通过 KKAPI 向 Studio 添加工具栏按钮，点击这些按钮即可触发插件热键，以执行指令或唤出插件面板等

通过修改 206 行的配置列表：

```cs
buttons.Add(new ButtonConfig() {
    tooltip = "BlendShapeEditor",
    iconPath = "scripts/icons/BlendShapeEditor.png",
    key = KeyCode.B, lctrl = false, rctrl = false, lshift = false, rshift = false, lalt = true, alt = false  // LeftAlt+B
});
```

仿照以上格式写配置，注意 iconPath 为按钮的图标，像素最好32x32，不提供 iconPath 则按钮会是纯色按钮

### Work Panel Mover

启动游戏后，将工作室右侧的工作面板移动到指定位置。

作用：开窗口模式后如果最大化游戏，工作面板不会自动到最右侧，这个脚本是为了解决这个痛点。

修改 17 行代码以更改偏移：
```cs
private const float OffsetX = 137f;
private const float OffsetY = -10f;
```

### Outfit Folder Memory

持久化 服装卡文件夹列表 选项，在切换选中角色或取消选中的时候仍保持对应选项。
