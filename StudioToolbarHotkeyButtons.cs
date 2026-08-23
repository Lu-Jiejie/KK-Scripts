// #name Studio Toolbar Hotkey Buttons
// #author Lu Jiejie
// #desc 通过 KKAPI 向 Studio 添加工具栏按钮，点击这些按钮即可触发插件热键
// #proc_filter CharaStudio.exe
// v1.0.0

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public static class StudioToolbarHotkeyButtons {
    private static GameObject gameObject;

    // Win32 API — 模拟按键
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);
    private const uint KEYEVENTF_KEYDOWN = 0;
    private const uint KEYEVENTF_KEYUP = 2;

    // Windows 虚拟键码（修饰键）
    private const byte VK_LCONTROL = 0xA2;
    private const byte VK_RCONTROL = 0xA3;
    private const byte VK_LSHIFT = 0xA0;
    private const byte VK_RSHIFT = 0xA1;
    private const byte VK_LMENU = 0xA4;   // LeftAlt
    private const byte VK_RMENU = 0xA5;   // RightAlt

    public static void Main() {
        gameObject = new GameObject();
        gameObject.AddComponent<ToolbarController>();
        Debug.Log("[ToolbarHotkey] 脚本加载成功");
    }

    public static void Unload() {
        if (gameObject != null) {
            ToolbarController ctrl = gameObject.GetComponent<ToolbarController>();
            if (ctrl != null) ctrl.RemoveAllCreatedButtons();
        }
        Object.Destroy(gameObject);
        gameObject = null;
    }

    // ===== 按钮配置类 =====
    class ButtonConfig {
        public string tooltip;      // 鼠标悬停提示文本
        public string iconPath;     // 图标图片路径（如 "scripts/icons/BlendShapeEditor.png"），留空 = 纯色
        public KeyCode key;         // 主键（KeyCode.F8 / KeyCode.Alpha1 / KeyCode.B 等）
        public bool lctrl;          // Left Ctrl
        public bool rctrl;          // Right Ctrl
        public bool lshift;         // Left Shift
        public bool rshift;         // Right Shift
        public bool lalt;           // Left Alt
        public bool ralt;           // Right Alt
    }

    // KeyCode -> Windows 虚拟键码
    static byte KeyCodeToVk(KeyCode key) {
        if (key >= KeyCode.F1 && key <= KeyCode.F15) return (byte)(0x70 + (key - KeyCode.F1));
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return (byte)(0x30 + (key - KeyCode.Alpha0));
        if (key >= KeyCode.A && key <= KeyCode.Z) return (byte)(0x41 + (key - KeyCode.A));
        if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return (byte)(0x60 + (key - KeyCode.Keypad0));
        switch (key) {
            case KeyCode.Space:        return 0x20;
            case KeyCode.Return:       return 0x0D;
            case KeyCode.Escape:       return 0x1B;
            case KeyCode.Tab:          return 0x09;
            case KeyCode.Backspace:    return 0x08;
            case KeyCode.UpArrow:      return 0x26;
            case KeyCode.DownArrow:    return 0x28;
            case KeyCode.LeftArrow:    return 0x25;
            case KeyCode.RightArrow:   return 0x27;
            case KeyCode.Insert:       return 0x2D;
            case KeyCode.Delete:       return 0x2E;
            case KeyCode.Home:         return 0x24;
            case KeyCode.End:          return 0x23;
            case KeyCode.PageUp:       return 0x21;
            case KeyCode.PageDown:     return 0x22;
        }
        return 0;
    }

    // ===== 加载图标图片 =====
    // 统一图标显示尺寸（按钮工具栏图标的常见大小）
    private const int IconSize = 32;

    static Texture2D LoadIconTexture(string iconPath) {
        if (string.IsNullOrEmpty(iconPath)) return null;
        Texture2D raw = null;
        try {
            string fullPath = iconPath;
            // 若相对路径不存在，尝试基于游戏根目录（dataPath 的上级目录）
            if (!System.IO.File.Exists(fullPath)) {
                string gameRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrEmpty(gameRoot)) {
                    fullPath = System.IO.Path.Combine(gameRoot, iconPath);
                }
            }
            if (!System.IO.File.Exists(fullPath)) {
                Debug.LogWarning("[ToolbarHotkey] 找不到图标文件: " + iconPath);
                return null;
            }

            byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
            raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!raw.LoadImage(bytes)) {
                Debug.LogWarning("[ToolbarHotkey] 图片解码失败或不是支持的格式: " + iconPath);
                Object.Destroy(raw);
                return null;
            }

            // 缩放到统一尺寸（保持长宽比、居中留透明边），避免图标超出按钮
            Texture2D fitted = FitIconTexture(raw, IconSize);
            Object.Destroy(raw);
            return fitted;
        } catch (System.Exception ex) {
            Debug.LogWarning("[ToolbarHotkey] 加载图标失败: " + ex.Message);
            if (raw != null) Object.Destroy(raw);
            return null;
        }
    }

    // 将图片缩放到 iconSize×iconSize，保持长宽比，多出的区域透明
    // 使用 nearest-neighbor 采样以保持清晰
    static Texture2D FitIconTexture(Texture2D source, int iconSize) {
        Texture2D result = new Texture2D(iconSize, iconSize, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[iconSize * iconSize]; // 默认透明 (0,0,0,0)

        float srcW = source.width, srcH = source.height;
        float scale = Mathf.Min(iconSize / srcW, iconSize / srcH);
        int newW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
        int newH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));
        int offX = (iconSize - newW) / 2;
        int offY = (iconSize - newH) / 2;

        // nearest-neighbor 采样，保持清晰
        for (int y = 0; y < newH; y++) {
            for (int x = 0; x < newW; x++) {
                int srcX = Mathf.Clamp(Mathf.RoundToInt((float)x / newW * (srcW - 1)), 0, (int)srcW - 1);
                int srcY = Mathf.Clamp(Mathf.RoundToInt((float)y / newH * (srcH - 1)), 0, (int)srcH - 1);
                Color c = source.GetPixel(srcX, srcY);
                // 提亮 (1.15 倍)，不需要可注释
                c.r = Mathf.Min(1f, c.r * 1.15f);
                c.g = Mathf.Min(1f, c.g * 1.15f);
                c.b = Mathf.Min(1f, c.b * 1.15f);
                pixels[(offY + y) * iconSize + (offX + x)] = c;
            }
        }

        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    class ToolbarController : MonoBehaviour {
        private List<object> _createdButtons = new List<object>();
        private System.Type _toolbarManagerType;
        private System.Reflection.MethodInfo _removeMethod;
        private System.Reflection.MethodInfo _getAllButtonsMethod;
        private const string BUTTON_PREFIX = "ToolbarHotkey_";

        void Awake() {
            DontDestroyOnLoad(this);
        }

        void OnDestroy() {
            RemoveAllCreatedButtons();
        }

        void RemovePreviousButtons() {
            try {
                object[] allBtns = (object[])_getAllButtonsMethod.Invoke(null, new object[] { true });
                if (allBtns != null) {
                    foreach (object btn in allBtns) {
                        if (btn == null) continue;
                        System.Type t = btn.GetType();
                        System.Reflection.PropertyInfo idProp = t.GetProperty("ButtonID");
                        if (idProp != null) {
                            string id = idProp.GetValue(btn, null) as string;
                            if (id != null && id.StartsWith(BUTTON_PREFIX)) {
                                _removeMethod.Invoke(null, new object[] { btn });
                                Debug.Log("[ToolbarHotkey] 已移除旧按钮: " + id);
                            }
                        }
                    }
                }
            } catch (System.Exception ex) {
                Debug.LogWarning("[ToolbarHotkey] 清理旧按钮时出错: " + ex.Message);
            }
        }

        public void RemoveAllCreatedButtons() {
            foreach (object btn in _createdButtons) {
                try {
                    if (btn != null) _removeMethod?.Invoke(null, new object[] { btn });
                } catch { }
            }
            _createdButtons.Clear();
        }

        IEnumerator Start() {
            yield return null;

            // ===== !!!在此添加你的按钮配置!!! =====
            List<ButtonConfig> buttons = new List<ButtonConfig>();

            buttons.Add(new ButtonConfig() {
                tooltip = "BlendShapeEditor",
                iconPath = "scripts/icons/BlendShapeEditor.png",
                key = KeyCode.B, lctrl = false, rctrl = false, lshift = false, rshift = false, lalt = true, ralt = false  // LeftAlt+B
            });
            // =================================

            CreateKKAPIButtons(buttons);
        }

        void CreateKKAPIButtons(List<ButtonConfig> buttons) {
            System.Type simpleToolbarBtnType = null;
            System.Type koikatuApiType = null;

            foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.GetName().Name == "KKAPI") {
                    simpleToolbarBtnType = asm.GetType("KKAPI.Studio.UI.Toolbars.SimpleToolbarButton");
                    _toolbarManagerType = asm.GetType("KKAPI.Studio.UI.Toolbars.ToolbarManager");
                    koikatuApiType = asm.GetType("KKAPI.KoikatuAPI");
                    break;
                }
            }

            if (simpleToolbarBtnType == null || _toolbarManagerType == null) {
                Debug.LogError("[ToolbarHotkey] 未找到 KKAPI！请确保已安装 KKAPI 插件");
                return;
            }

            _removeMethod = _toolbarManagerType.GetMethod("RemoveControl",
                new System.Type[] { simpleToolbarBtnType.BaseType });
            _getAllButtonsMethod = _toolbarManagerType.GetMethod("GetAllButtons");

            RemovePreviousButtons();

            object owner = UnityEngine.Object.FindObjectOfType(koikatuApiType);
            if (owner == null) {
                Debug.LogError("[ToolbarHotkey] 未找到 KKAPI 插件实例");
                return;
            }
            System.Type baseUnityPluginType = koikatuApiType.BaseType;

            System.Reflection.ConstructorInfo ctor = simpleToolbarBtnType.GetConstructor(new System.Type[] {
                typeof(string),
                typeof(string),
                typeof(System.Func<Texture2D>),
                baseUnityPluginType,
                typeof(System.Action<UnityEngine.EventSystems.PointerEventData.InputButton>)
            });

            if (ctor == null) {
                Debug.LogError("[ToolbarHotkey] 无法获取 SimpleToolbarButton 构造器");
                return;
            }

            System.Reflection.MethodInfo addMethod = _toolbarManagerType.GetMethod("AddLeftToolbarControl",
                new System.Type[] { simpleToolbarBtnType.BaseType });

            if (addMethod == null) {
                Debug.LogError("[ToolbarHotkey] 无法获取 ToolbarManager.AddLeftToolbarControl");
                return;
            }

            for (int i = 0; i < buttons.Count; i++) {
                ButtonConfig cfg = buttons[i];

                // 图标：优先图片，其次纯色
                Texture2D iconTx = LoadIconTexture(cfg.iconPath);
                if (iconTx == null) {
                    iconTx = CreateSolidIconTexture(GetButtonColor(i));
                }
                Texture2D iconTex = iconTx; // 闭包捕获

                System.Func<Texture2D> iconGetter = () => iconTex;

                System.Action<UnityEngine.EventSystems.PointerEventData.InputButton> clickHandler =
                    (btn) => SimulateKeyPress(cfg);

                object btnObj = ctor.Invoke(new object[] {
                    BUTTON_PREFIX + i,
                    cfg.tooltip,
                    iconGetter,
                    owner,
                    clickHandler
                });

                addMethod.Invoke(null, new object[] { btnObj });

                _createdButtons.Add(btnObj);
                Debug.Log("[ToolbarHotkey] 已创建按钮: " + cfg.tooltip
                    + " (" + DescribeHotkey(cfg) + ") icon="
                    + (string.IsNullOrEmpty(cfg.iconPath) ? "纯色" : cfg.iconPath));
            }

            Debug.Log("[ToolbarHotkey] 完成，共创建 " + buttons.Count + " 个按钮");
        }

        // ===== 纯色图标纹理（备用）=====
        Texture2D CreateSolidIconTexture(Color bgColor) {
            int size = IconSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bgColor;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        Color GetButtonColor(int index) {
            Color[] palette = new Color[] {
                new Color(0.2f, 0.6f, 1.0f),
                new Color(0.2f, 1.0f, 0.6f),
                new Color(1.0f, 0.6f, 0.2f),
                new Color(1.0f, 0.2f, 0.6f),
                new Color(0.6f, 0.2f, 1.0f),
                new Color(0.2f, 0.8f, 0.8f),
                new Color(0.8f, 0.8f, 0.2f),
                new Color(0.8f, 0.2f, 0.8f),
            };
            return palette[index % palette.Length];
        }

        // ===== 快捷键模拟 =====
        void SimulateKeyPress(ButtonConfig cfg) {
            StartCoroutine(SimulateKeyPressCoroutine(cfg));
        }

        IEnumerator SimulateKeyPressCoroutine(ButtonConfig cfg) {
            byte vk = KeyCodeToVk(cfg.key);
            if (vk == 0) {
                Debug.LogWarning("[ToolbarHotkey] 无法映射按键: " + cfg.key);
                yield break;
            }

            // 按下修饰键（可区分左右）
            if (cfg.lctrl)  keybd_event(VK_LCONTROL, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);
            if (cfg.rctrl) keybd_event(VK_RCONTROL, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);
            if (cfg.lshift) keybd_event(VK_LSHIFT, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);
            if (cfg.rshift)keybd_event(VK_RSHIFT, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);
            if (cfg.lalt)   keybd_event(VK_LMENU, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);
            if (cfg.ralt)  keybd_event(VK_RMENU, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);

            keybd_event(vk, 0, KEYEVENTF_KEYDOWN, System.UIntPtr.Zero);

            yield return null;

            keybd_event(vk, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);

            if (cfg.ralt)  keybd_event(VK_RMENU, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);
            if (cfg.lalt)   keybd_event(VK_LMENU, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);
            if (cfg.rshift)keybd_event(VK_RSHIFT, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);
            if (cfg.lshift) keybd_event(VK_LSHIFT, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);
            if (cfg.rctrl) keybd_event(VK_RCONTROL, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);
            if (cfg.lctrl)  keybd_event(VK_LCONTROL, 0, KEYEVENTF_KEYUP, System.UIntPtr.Zero);

            Debug.Log("[ToolbarHotkey] 已触发: " + DescribeHotkey(cfg));
        }

        string DescribeHotkey(ButtonConfig cfg) {
            string s = "";
            if (cfg.lctrl)  s += "LeftCtrl+";
            if (cfg.rctrl) s += "RightCtrl+";
            if (cfg.lshift) s += "LeftShift+";
            if (cfg.rshift)s += "RightShift+";
            if (cfg.lalt)   s += "LeftAlt+";
            if (cfg.ralt)  s += "RightAlt+";
            s += cfg.key.ToString();
            return s;
        }
    }
}
