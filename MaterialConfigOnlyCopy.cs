// #name Material Config-Only Copy
// #author Lu Jiejie
// #desc 在 KK_MaterialEditor 顶部的 filter 左边加一个开关：
//       勾选为「完全复制」，取消勾选为「部分复制」（不携带着色器，只写同名属性）。
// #proc_filter CharaStudio.exe
// v4.1.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class MaterialConfigOnlyCopy {
    private const string GAME_OBJECT_NAME = "MaterialConfigOnlyCopy";

    // ★ 可配置区域 ★
    private const string TOGGLE_NAME = "MaterialEditorConfigOnlyToggle";
    // 勾选 = 完全复制，取消勾选 = 部分复制
    private const string LABEL_CHECKED = "完全复制";
    private const string LABEL_UNCHECKED = "部分复制";
    private const float TOGGLE_WIDTH = 100f;
    private const float TOGGLE_GAP = 4f;
    private const bool START_FULL_COPY = false;
    // 检查顶部控件是否还在的间隔（秒）
    private const float SCAN_INTERVAL = 0.25f;
    // 中文标签要能显示，UILib 默认的 Arial 没有汉字
    private static readonly string[] CJK_FONTS = new string[] {
        "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "Arial Unicode MS", "MS Gothic" };

    private const string TOOLTIP_CHECKED =
        "复制模式：完全复制\n" +
        "着色器与渲染队列会一并复制到目标\n" +
        "取消勾选切换为部分复制";
    private const string TOOLTIP_UNCHECKED =
        "复制模式：部分复制\n" +
        "复制时不携带着色器，粘贴只写入目标着色器也有的同名属性\n" +
        "目标着色器与渲染队列保持不变";
    // ★ 可配置区域结束 ★

    private static GameObject _gameObject;

    public static void Main() {
        DestroyOldInstances();
        _gameObject = new GameObject(GAME_OBJECT_NAME);
        _gameObject.AddComponent<ConfigCopyController>();
        Debug.Log("[MatConfigCopy] 加载成功，MaterialEditor 顶部 filter 左侧会多一个模式开关");
    }

    public static void Unload() {
        GameObject old = _gameObject;
        _gameObject = null;
        if (old != null) {
            ConfigCopyController ctrl = old.GetComponent<ConfigCopyController>();
            if (ctrl != null) ctrl.Cleanup();
        }
        DestroyOldInstances();
    }

    private static void DestroyOldInstances() {
        GameObject obj;
        while ((obj = GameObject.Find(GAME_OBJECT_NAME)) != null)
            UnityEngine.Object.DestroyImmediate(obj);
    }

    // 插件原生复制会把源着色器写进剪贴板的 MaterialShaderList，粘贴时先按它 SetMaterialShader。
    // 开关开着的时候，复制完成后立刻清空这个列表，粘贴就只写两边同名的属性，着色器不动。
    private class ConfigCopyController : MonoBehaviour {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const string MODE_PANEL_NAME = "MaterialEditorModePanel";
        private const string FILTER_NAME = "Filter";

        private Type _extType;

        private FieldInfo _fWindow;
        private FieldInfo _fCopyData;
        private FieldInfo _fShaderList;

        private MethodInfo _mCreateToggle;
        private MethodInfo _mSetRect;
        private MethodInfo _mAddTooltip;
        private MethodInfo _mSetTooltipText;

        private Type _shaderListOwner;

        private Canvas _canvas;
        private Transform _modePanel;
        private InputField _filter;
        private Toggle _toggle;
        private Text _label;
        private object _tooltip;

        private float _filterLeft = 3f;
        private float _filterRight = -93f;
        private bool _haveFilterRect;

        private bool _fullCopy = START_FULL_COPY;
        private bool _ready;
        private bool _warned;
        private float _timer;

        void Awake() {
            DontDestroyOnLoad(this);
        }

        void Update() {
            if (!_ready && !Init()) return;

            // 每帧清一次，避免复制到粘贴之间漏掉
            if (!_fullCopy) StripShader();

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = SCAN_INTERVAL;

            Tick();
        }

        public void Cleanup() {
            RestoreFilterRect();
            RemoveToggle();
        }

        // ===== 反射初始化 =====

        private bool Init() {
            Type uiType = FindType("MaterialEditorAPI.MaterialEditorUI");
            Type pluginBase = FindType("MaterialEditorAPI.MaterialEditorPluginBase");
            if (uiType == null || pluginBase == null) return false;

            Assembly pluginAsm = pluginBase.Assembly;
            Type factory = pluginAsm.GetType("MaterialEditorAPI.MaterialEditorControlFactory");
            Type tooltipMgr = pluginAsm.GetType("MaterialEditorAPI.TooltipManager");
            // UILib 由各插件各自内嵌，必须取 MaterialEditor 自己那一份
            _extType = pluginAsm.GetType("UILib.UIExtensions");
            if (factory == null || tooltipMgr == null || _extType == null) {
                Warn("没有在 MaterialEditor 里找到 UI 工具类型，脚本停用");
                return false;
            }

            _fWindow = uiType.GetField("MaterialEditorWindow", StaticFlags);
            _fCopyData = pluginBase.GetField("CopyData", StaticFlags);

            _mCreateToggle = factory.GetMethod("CreateToggle", StaticFlags, null,
                new Type[] { typeof(string), typeof(Transform), typeof(string) }, null);
            _mAddTooltip = tooltipMgr.GetMethod("AddTooltip", StaticFlags, null,
                new Type[] { typeof(GameObject), typeof(string) }, null);
            _mSetRect = _extType.GetMethod("SetRect", StaticFlags, null,
                new Type[] { typeof(Transform), typeof(float), typeof(float), typeof(float), typeof(float),
                             typeof(float), typeof(float), typeof(float), typeof(float) }, null);

            if (_fWindow == null || _fCopyData == null || _mCreateToggle == null ||
                _mAddTooltip == null || _mSetRect == null) {
                Warn("MaterialEditor 的接口和预期不一致，脚本停用");
                return false;
            }

            _ready = true;
            Debug.Log("[MatConfigCopy] 已挂接 MaterialEditor");
            return true;
        }

        // ===== 顶部开关 =====

        private void Tick() {
            Canvas canvas = _fWindow.GetValue(null) as Canvas;
            if (canvas == null) {
                _canvas = null;
                _modePanel = null;
                _toggle = null;
                _filter = null;
                _haveFilterRect = false;
                return;
            }
            if (canvas != _canvas) {
                _canvas = canvas;
                _modePanel = null;
                _toggle = null;
                _filter = null;
                _haveFilterRect = false;
            }

            if (_modePanel == null) _modePanel = FindDeep(canvas.transform, MODE_PANEL_NAME);
            if (_modePanel == null) return;

            if (_toggle == null && !EnsureToggle()) return;

            ApplyRects();
        }

        private bool EnsureToggle() {
            Transform stale = _modePanel.Find(TOGGLE_NAME);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);

            Transform filterTr = FindDeep(_modePanel, FILTER_NAME);
            if (filterTr == null) return false;
            _filter = filterTr.GetComponent<InputField>();
            if (_filter == null) return false;

            Toggle toggle;
            try {
                object created = _mCreateToggle.Invoke(null, new object[] {
                    TOGGLE_NAME, _modePanel, _fullCopy ? LABEL_CHECKED : LABEL_UNCHECKED });
                toggle = created as Toggle;
            } catch (Exception ex) {
                Warn("创建模式开关失败: " + ex.Message);
                return false;
            }
            if (toggle == null) return false;

            _toggle = toggle;
            _label = toggle.GetComponentInChildren<Text>(true);
            ApplyCjkFont(_label);
            toggle.isOn = _fullCopy;
            toggle.onValueChanged.AddListener(new UnityAction<bool>(OnToggleChanged));

            try {
                _tooltip = _mAddTooltip.Invoke(null, new object[] {
                    toggle.gameObject, _fullCopy ? TOOLTIP_CHECKED : TOOLTIP_UNCHECKED });
                if (_tooltip != null)
                    _mSetTooltipText = _tooltip.GetType().GetMethod("SetStandardTooltipText", InstFlags);
            } catch (Exception ex) {
                Debug.LogWarning("[MatConfigCopy] 开关提示设置失败: " + ex.Message);
            }

            // 只在第一次记录原始 rect，避免重复补按钮时把自己的改动当成原值而逐次右移
            if (!_haveFilterRect) {
                RectTransform filterRt = _filter.GetComponent<RectTransform>();
                _filterLeft = filterRt.offsetMin.x;
                _filterRight = filterRt.offsetMax.x;
                _haveFilterRect = true;
            }

            Debug.Log("[MatConfigCopy] 顶部模式开关已就绪（当前：" + (_fullCopy ? "完全复制" : "部分复制") + "）");
            return true;
        }

        // UILib 的默认字体是内置 Arial，不含汉字，中文标签会显示成空白
        private static Font _cjkFont;

        private static void ApplyCjkFont(Text label) {
            if (label == null) return;
            if (_cjkFont == null) {
                Font fallback = null;
                for (int i = 0; i < CJK_FONTS.Length; i++) {
                    Font font = Font.CreateDynamicFontFromOSFont(CJK_FONTS[i], 14);
                    if (font == null) continue;
                    if (fallback == null) fallback = font;
                    if (font.HasCharacter('完')) {
                        _cjkFont = font;
                        break;
                    }
                }
                if (_cjkFont == null) _cjkFont = fallback;
            }
            if (_cjkFont == null) {
                Debug.LogWarning("[MatConfigCopy] 没找到中文字体，开关标签可能显示空白");
                return;
            }
            label.font = _cjkFont;
            // 光换 font 不够，Text 的材质仍指向原字体，动态字体会渲染不出来
            if (_cjkFont.material != null) label.material = _cjkFont.material;
        }

        private void ApplyRects() {
            if (_toggle == null || _filter == null || !_haveFilterRect) return;

            _mSetRect.Invoke(null, new object[] { _toggle.transform, 0f, 0f, 0f, 1f,
                _filterLeft, 1f, _filterLeft + TOGGLE_WIDTH, -1f });

            float left = _filterLeft + TOGGLE_WIDTH + TOGGLE_GAP;
            RectTransform filterRt = _filter.GetComponent<RectTransform>();
            if (Mathf.Abs(filterRt.offsetMin.x - left) > 0.5f) {
                _mSetRect.Invoke(null, new object[] { filterRt, 0f, 0f, 1f, 1f,
                    left, 1f, _filterRight, -1f });
            }
        }

        private void RestoreFilterRect() {
            if (_filter == null || !_haveFilterRect) return;
            try {
                _mSetRect.Invoke(null, new object[] { _filter.transform, 0f, 0f, 1f, 1f,
                    _filterLeft, 1f, _filterRight, -1f });
            } catch (Exception) { }
        }

        private void RemoveToggle() {
            if (_toggle != null) UnityEngine.Object.DestroyImmediate(_toggle.gameObject);
            _toggle = null;
            _label = null;
            _tooltip = null;
        }

        private void OnToggleChanged(bool on) {
            _fullCopy = on;
            if (_label != null) _label.text = on ? LABEL_CHECKED : LABEL_UNCHECKED;
            if (_tooltip != null && _mSetTooltipText != null) {
                try {
                    _mSetTooltipText.Invoke(_tooltip, new object[] { on ? TOOLTIP_CHECKED : TOOLTIP_UNCHECKED });
                } catch (Exception) { }
            }
            Debug.Log("[MatConfigCopy] 复制模式切换为：" + (on ? "完全复制" : "部分复制"));
        }

        // ===== 复制拦截 =====

        private void StripShader() {
            object container = _fCopyData.GetValue(null);
            if (container == null) return;

            Type owner = container.GetType();
            if (owner != _shaderListOwner) {
                _fShaderList = owner.GetField("MaterialShaderList", InstFlags);
                _shaderListOwner = owner;
            }
            if (_fShaderList == null) return;

            IList shaders = _fShaderList.GetValue(container) as IList;
            if (shaders == null || shaders.Count == 0) return;

            shaders.Clear();
            Debug.Log("[MatConfigCopy] 已复制，丢弃剪贴板里的着色器");
        }

        // ===== 工具 =====

        private void Warn(string message) {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[MatConfigCopy] " + message);
        }

        private static Transform FindDeep(Transform root, string name) {
            if (root == null) return null;
            if (root.name == name) return root;
            int count = root.childCount;
            for (int i = 0; i < count; i++) {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static Type FindType(string fullName) {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++) {
                Type t = asms[i].GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
