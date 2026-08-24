// #name Auto Hotkey
// #author Lu Jiejie
// #desc 工作室启动后或场景卡加载完毕后自动执行一组快捷键
// #proc_filter CharaStudio.exe
// v1.0.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AutoHotkey {
    // ═══════════════════════════════════════════════════════════════
    //  ★ 可配置区域 ★  —  修改下方内容即可调整快捷键
    // ═══════════════════════════════════════════════════════════════

    // HotkeyEntry 结构说明：
    //   key          = 按键（KeyCode.F1~F15, KeyCode.Alpha0~9, KeyCode.A~Z 等）
    //   lctrl/rctrl  = 左/右 Ctrl
    //   lshift/rshift= 左/右 Shift
    //   lalt/ralt    = 左/右 Alt
    //   delaySeconds = 触发前的等待秒数（第一个按键从工作室就绪算起，后续按键从上一次触发算起）
    //
    // 示例：
    //   new HotkeyEntry { key = KeyCode.F3, delaySeconds = 1.5f }
    //   new HotkeyEntry { key = KeyCode.B, lalt = true, delaySeconds = 0.5f }
    //   new HotkeyEntry { key = KeyCode.Alpha1, lctrl = true, lshift = true, delaySeconds = 0.3f }

    // ---- ① 工作室启动后触发的快捷键 ----
    // 每个按键的 delaySeconds 是相对于前一个按键触发后的等待时间
    // 第一个按键的 delaySeconds 是相对于工作室就绪后的等待时间
    private static List<HotkeyEntry> _startupHotkeys = new List<HotkeyEntry> {
        new HotkeyEntry {
            key = KeyCode.F3,
            delaySeconds = 1.5f
        },
    };

    // ---- ② 场景卡加载完毕后触发的快捷键 ----
    // 加载场景卡（打开 .png / 合并场景）后自动执行，适合刷新插件状态等
    private static List<HotkeyEntry> _onSceneCardLoadHotkeys = new List<HotkeyEntry> {
        // new HotkeyEntry {
        //     key = KeyCode.F3,
        //     delaySeconds = 1.0f
        // },
    };

    // ═══════════════════════════════════════════════════════════════
    //  ★ 可配置区域结束 ★
    // ═══════════════════════════════════════════════════════════════

    class HotkeyEntry {
        public KeyCode key;
        public bool lctrl;
        public bool rctrl;
        public bool lshift;
        public bool rshift;
        public bool lalt;
        public bool ralt;
        public float delaySeconds;
    }

    private const string GAME_OBJECT_NAME = "AutoHotkey";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    private const byte VK_LCONTROL = 0xA2;
    private const byte VK_RCONTROL = 0xA3;
    private const byte VK_LSHIFT = 0xA0;
    private const byte VK_RSHIFT = 0xA1;
    private const byte VK_LMENU = 0xA4;
    private const byte VK_RMENU = 0xA5;

    private static GameObject _gameObject;
    private static IntPtr _gameWindowHandle = IntPtr.Zero;

    public static void Main() {
        DestroyOldInstances();
        _gameObject = new GameObject(GAME_OBJECT_NAME);
        _gameObject.AddComponent<AutoHotkeyController>();
        Debug.Log("[AutoHotkey] 加载成功");
    }

    private static void DestroyOldInstances() {
        GameObject obj;
        while ((obj = GameObject.Find(GAME_OBJECT_NAME)) != null)
            UnityEngine.Object.DestroyImmediate(obj);
    }

    public static void Unload() {
        DestroyOldInstances();
        _gameObject = null;
    }

    private static byte KeyCodeToVk(KeyCode key) {
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
            case KeyCode.Minus:        return 0xBD;
            case KeyCode.Plus:         return 0xBB;
            case KeyCode.LeftBracket:  return 0xDB;
            case KeyCode.RightBracket: return 0xDD;
            case KeyCode.Semicolon:    return 0xBA;
            case KeyCode.Quote:        return 0xDE;
            case KeyCode.Comma:        return 0xBC;
            case KeyCode.Period:       return 0xBE;
            case KeyCode.Slash:        return 0xBF;
            case KeyCode.BackQuote:    return 0xC0;
        }
        return 0;
    }

    private static string DescribeHotkey(HotkeyEntry entry) {
        string s = "";
        if (entry.lctrl)  s += "Ctrl+";
        if (entry.rctrl) s += "RCtrl+";
        if (entry.lshift) s += "Shift+";
        if (entry.rshift)s += "RShift+";
        if (entry.lalt)   s += "Alt+";
        if (entry.ralt)  s += "RAlt+";
        s += entry.key.ToString();
        return s;
    }

    private class AutoHotkeyController : MonoBehaviour {
        private bool _startupDone;
        private bool _updateStartupTriggered;

        // 场景卡加载检测：跟踪对象数量变化
        private int _lastObjectCount = -1;
        private float _sceneChangeTimer;

        void Awake() {
            DontDestroyOnLoad(this);
            try { SceneManager.sceneLoaded += OnSceneLoaded; }
            catch (Exception ex) { Debug.LogWarning("[AutoHotkey] SceneManager 注册失败: " + ex.Message); }
        }

        void OnDestroy() {
            try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
        }

        // ========== 启动阶段：场景加载事件 ==========

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            StartCoroutine(TryStartupAfterDelay(1.0f, "1"));
            StartCoroutine(TryStartupAfterDelay(3.0f, "2"));
            StartCoroutine(TryStartupAfterDelay(6.0f, "3"));
        }

        private IEnumerator TryStartupAfterDelay(float seconds, string tag) {
            yield return new WaitForSeconds(seconds);
            if (!_startupDone && IsStudioReady()) {
                _startupDone = true;
                Debug.Log("[AutoHotkey] 工作室就绪 (#" + tag + ")，执行启动快捷键");
                StartCoroutine(ExecuteSequence(_startupHotkeys, "启动"));
            }
        }

        // ========== Update 双检测 ==========

        void Update() {
            // ---- ① 启动检测（备用） ----
            if (!_startupDone) {
                try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
                if (IsStudioReady()) {
                    _startupDone = true;
                    Debug.Log("[AutoHotkey] 工作室就绪（轮询），执行启动快捷键");
                    StartCoroutine(ExecuteSequence(_startupHotkeys, "启动"));
                }
                return; // 启动完成前不做场景卡检测
            }

            // ---- ② 场景卡加载检测 ----
            if (_onSceneCardLoadHotkeys == null || _onSceneCardLoadHotkeys.Count == 0)
                return;

            try {
                var dic = Studio.Studio.Instance?.dicObjectCtrl;
                if (dic == null) return;

                int c = dic.Count;
                if (c != _lastObjectCount) {
                    _lastObjectCount = c;
                    _sceneChangeTimer = 1.5f; // 对象数量变化后等 1.5 秒确认稳定
                }
            } catch { return; }

            if (_sceneChangeTimer > 0f) {
                _sceneChangeTimer -= Time.deltaTime;
                if (_sceneChangeTimer <= 0f) {
                    Debug.Log("[AutoHotkey] 场景卡加载完毕，执行场景快捷键");
                    StartCoroutine(ExecuteSequence(_onSceneCardLoadHotkeys, "场景卡"));
                }
            }
        }

        // ========== 工具方法 ==========

        private bool IsStudioReady() {
            try {
                if (UnityEngine.Object.FindObjectOfType<StudioScene>() == null) return false;
                if (Studio.Studio.Instance == null) return false;
                if (Studio.Studio.Instance.dicObjectCtrl == null) return false;
                return true;
            } catch { return false; }
        }

        private IntPtr GetGameWindowHandle() {
            try {
                System.Diagnostics.Process proc = System.Diagnostics.Process.GetCurrentProcess();
                if (proc != null && proc.MainWindowHandle != IntPtr.Zero)
                    return proc.MainWindowHandle;
            } catch { }

            try {
                string[] titles = new string[] { "CharaStudio", Application.productName };
                foreach (string title in titles) {
                    IntPtr hWnd = FindWindow(null, title);
                    if (hWnd != IntPtr.Zero) return hWnd;
                }
            } catch { }

            return IntPtr.Zero;
        }

        private void SendKeyDown(byte vk) {
            if (_gameWindowHandle == IntPtr.Zero) {
                _gameWindowHandle = GetGameWindowHandle();
                if (_gameWindowHandle == IntPtr.Zero) {
                    Debug.LogWarning("[AutoHotkey] 无法获取窗口句柄");
                    return;
                }
                Debug.Log("[AutoHotkey] 窗口句柄: " + _gameWindowHandle);
            }
            PostMessage(_gameWindowHandle, WM_KEYDOWN, new UIntPtr(vk), IntPtr.Zero);
        }

        private void SendKeyUp(byte vk) {
            if (_gameWindowHandle != IntPtr.Zero)
                PostMessage(_gameWindowHandle, WM_KEYUP, new UIntPtr(vk), IntPtr.Zero);
        }

        private void PressModifiers(HotkeyEntry entry) {
            if (entry.lctrl)  SendKeyDown(VK_LCONTROL);
            if (entry.rctrl)  SendKeyDown(VK_RCONTROL);
            if (entry.lshift) SendKeyDown(VK_LSHIFT);
            if (entry.rshift) SendKeyDown(VK_RSHIFT);
            if (entry.lalt)   SendKeyDown(VK_LMENU);
            if (entry.ralt)   SendKeyDown(VK_RMENU);
        }

        private void ReleaseModifiers(HotkeyEntry entry) {
            if (entry.ralt)   SendKeyUp(VK_RMENU);
            if (entry.lalt)   SendKeyUp(VK_LMENU);
            if (entry.rshift) SendKeyUp(VK_RSHIFT);
            if (entry.lshift) SendKeyUp(VK_LSHIFT);
            if (entry.rctrl)  SendKeyUp(VK_RCONTROL);
            if (entry.lctrl)  SendKeyUp(VK_LCONTROL);
        }

        // ========== 执行快捷键序列 ==========

        private IEnumerator ExecuteSequence(List<HotkeyEntry> sequence, string reason) {
            if (sequence == null || sequence.Count == 0) yield break;

            if (_gameWindowHandle == IntPtr.Zero) {
                _gameWindowHandle = GetGameWindowHandle();
                if (_gameWindowHandle == IntPtr.Zero) {
                    Debug.LogWarning("[AutoHotkey] 无法获取窗口句柄，跳过");
                    yield break;
                }
                Debug.Log("[AutoHotkey] 窗口句柄: " + _gameWindowHandle);
            }

            for (int i = 0; i < sequence.Count; i++) {
                HotkeyEntry entry = sequence[i];
                string desc = DescribeHotkey(entry);

                if (entry.delaySeconds > 0f)
                    yield return new WaitForSeconds(entry.delaySeconds);

                byte vk = KeyCodeToVk(entry.key);
                if (vk == 0) {
                    Debug.LogWarning("[AutoHotkey] 无法映射按键: " + entry.key + "，跳过");
                    continue;
                }

                Debug.Log("[AutoHotkey] [" + reason + "] " + desc);

                PressModifiers(entry);
                SendKeyDown(vk);
                yield return null;
                SendKeyUp(vk);
                ReleaseModifiers(entry);
            }

            Debug.Log("[AutoHotkey] ✅ [" + reason + "] 执行完毕");
        }
    }
}
