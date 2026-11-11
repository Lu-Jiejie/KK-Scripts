// #name MaterialEditor Filter Keeper
// #author Lu Jiejie
// #desc 角色切换到另一个角色时，保留 KK_MaterialEditor 的物品类型下拉（如 Clothes Bra）
//       与搜索框 filter：新角色有同名项才保留，没有就按插件默认行为重置。
// #proc_filter CharaStudio.exe
// v2.0.0

using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

public static class MaterialEditorFilterKeeper {
    private const string GAME_OBJECT_NAME = "MaterialEditorFilterKeeper";

    // ★ 可配置区域 ★
    // 是否保留物品类型下拉（Body / Clothes Bra / Hair ...）
    private const bool RESTORE_DROPDOWN = true;
    // 是否保留搜索框 filter
    private const bool RESTORE_TEXT_FILTER = true;
    // 切角色后等多少帧再恢复下拉（等插件重建完选项）
    private const int DROPDOWN_DELAY_FRAMES = 2;
    // 恢复下拉后再等多少帧恢复搜索框（下拉会触发一次列表重建）
    private const int FILTER_DELAY_FRAMES = 4;
    // 恢复后再盯多少帧，插件若又清空 filter 就补回去
    private const int WATCH_FRAMES = 20;
    // 盯守期间最多补几次
    private const int MAX_REAPPLY = 2;
    // 空 filter 持续多少帧才认定用户主动清空
    private const int CLEAR_STICKY_FRAMES = 30;
    // 是否在控制台打印恢复日志
    private const bool VERBOSE_LOG = true;
    // ★ 可配置区域结束 ★

    private static GameObject _gameObject;

    public static void Main() {
        DestroyOldInstances();
        _gameObject = new GameObject(GAME_OBJECT_NAME);
        _gameObject.AddComponent<KeeperController>();
        Debug.Log("[MatFilterKeeper] 加载成功");
    }

    public static void Unload() {
        _gameObject = null;
        DestroyOldInstances();
    }

    private static void DestroyOldInstances() {
        GameObject obj;
        while ((obj = GameObject.Find(GAME_OBJECT_NAME)) != null)
            UnityEngine.Object.DestroyImmediate(obj);
    }

    // 插件 UpdateUI 选角色时会 PopulateList(..., null)，并让 PopulateItemTypeDropdown
    // 末尾固定 RefreshItemTypeDropdownCaption(0)，下拉于是回到第一项 "Body"。
    // 这里把下拉项和 filter 文本都补回去。
    private class KeeperController : MonoBehaviour {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private object _ui;
        private Type _uiType;
        private Type _meType;
        private PropertyInfo _pCurrentGo;
        private PropertyInfo _pCurrentFilter;
        private MethodInfo _mRefreshUI;
        private Type _chaType;

        private object _dropdown;
        private PropertyInfo _pDropValue;
        private PropertyInfo _pDropOptions;
        private PropertyInfo _pOptionText;

        private bool _ready;
        private bool _warned;

        private Component _lastCha;
        private string _savedText;      // 下拉项文本，如 "Clothes Bra"
        private string _savedFilter;    // 搜索框文本
        private int _lastValue = -1;
        private int _phase;
        private int _countdown;
        private int _retry;
        private int _emptyFrames;

        private void Awake() {
            DontDestroyOnLoad(this);
        }

        private void Update() {
            if (!_ready && !Init()) return;
            if (_dropdown == null) TryGetDropdown();
            Tick();
        }

        private bool Init() {
            _uiType = FindType("MaterialEditorAPI.MaterialEditorUI");
            _meType = FindType("KK_Plugins.MaterialEditor.MEStudio");
            if (_uiType == null || _meType == null) return false;

            FieldInfo fInstance = _meType.GetField("Instance", StaticFlags);
            if (fInstance == null) return false;
            _ui = fInstance.GetValue(null);
            if (_ui == null) return false;

            _pCurrentGo = _uiType.GetProperty("CurrentGameObject", InstFlags);
            _pCurrentFilter = _uiType.GetProperty("CurrentFilter", StaticFlags);
            _mRefreshUI = _uiType.GetMethod("RefreshUI", InstFlags, null, new Type[] { typeof(string) }, null);
            _chaType = FindType("ChaControl");

            if (_pCurrentGo == null || _pCurrentFilter == null || _mRefreshUI == null) {
                WarnOnce("MaterialEditorUI 成员不完整，脚本停用");
                return false;
            }

            TryGetDropdown();
            _ready = true;
            return true;
        }

        private void TryGetDropdown() {
            if (_meType == null) return;
            FieldInfo fDrop = _meType.GetField("ItemTypeDropDown", StaticFlags);
            if (fDrop == null) return;
            _dropdown = fDrop.GetValue(null);
            if (_dropdown == null) return;

            Type dt = _dropdown.GetType();
            _pDropValue = dt.GetProperty("value", InstFlags);
            _pDropOptions = dt.GetProperty("options", InstFlags);
            _lastValue = CurrentDropdownValue();
            if (string.IsNullOrEmpty(_savedText)) _savedText = TextAt(_lastValue);
        }

        private void Tick() {
            Component cha = GetCha(GetCurrentGo());

            if (cha != _lastCha) {
                bool charToChar = _lastCha != null && cha != null;
                _lastCha = cha;
                if (charToChar) {
                    _phase = 1;
                    _countdown = DROPDOWN_DELAY_FRAMES;
                    _retry = 0;
                    if (VERBOSE_LOG) Debug.Log("[MatFilterKeeper] 角色切换，准备恢复");
                } else {
                    _phase = 0;
                    _lastValue = CurrentDropdownValue();
                }
                return;
            }

            if (_phase == 1) {
                if (--_countdown <= 0) {
                    if (RESTORE_DROPDOWN) RestoreDropdown();
                    _phase = 2;
                    _countdown = FILTER_DELAY_FRAMES;
                }
                return;
            }

            if (_phase == 2) {
                if (--_countdown <= 0) {
                    if (RESTORE_TEXT_FILTER) RestoreFilter(cha);
                    _phase = 3;
                    _countdown = WATCH_FRAMES;
                }
                return;
            }

            if (_phase == 3) {
                if (--_countdown <= 0) {
                    _phase = 0;
                    _lastValue = CurrentDropdownValue();
                    return;
                }
                if (RESTORE_TEXT_FILTER && _retry < MAX_REAPPLY
                    && !string.IsNullOrEmpty(_savedFilter)
                    && string.IsNullOrEmpty(GetCurrentFilter())) {
                    _retry++;
                    RestoreFilter(cha);
                }
                return;
            }

            // 稳定期：跟踪用户自己的操作
            int v = CurrentDropdownValue();
            if (v >= 0 && v != _lastValue) {
                _lastValue = v;
                string t = TextAt(v);
                if (!string.IsNullOrEmpty(t)) _savedText = t;
            }

            string f = GetCurrentFilter();
            if (!string.IsNullOrEmpty(f)) {
                _savedFilter = f;
                _emptyFrames = 0;
            } else {
                // 空 filter 持续一段时间才认定是用户主动清掉，避免插件延迟刷新把记忆清空
                _emptyFrames++;
                if (_emptyFrames > CLEAR_STICKY_FRAMES) _savedFilter = null;
            }
        }

        private void RestoreDropdown() {
            if (_dropdown == null || string.IsNullOrEmpty(_savedText)) return;

            int idx = IndexOfText(_savedText);
            if (idx < 0) {
                if (VERBOSE_LOG) Debug.Log("[MatFilterKeeper] 新角色没有「" + _savedText + "」，下拉保持默认");
                return;
            }
            if (CurrentDropdownValue() == idx) return;

            try {
                _pDropValue.SetValue(_dropdown, idx, null);
                if (VERBOSE_LOG) Debug.Log("[MatFilterKeeper] 已恢复下拉：" + _savedText);
            } catch (Exception e) {
                Debug.LogWarning("[MatFilterKeeper] 恢复下拉失败：" + e.Message);
            }
        }

        private void RestoreFilter(Component cha) {
            if (string.IsNullOrEmpty(_savedFilter)) return;
            if (GetCurrentFilter() == _savedFilter) return;

            GameObject root = cha == null ? GetCurrentGo() : cha.gameObject;
            if (!MatchesTarget(root, _savedFilter)) {
                if (VERBOSE_LOG) Debug.Log("[MatFilterKeeper] 新角色没有匹配项，filter 不保留");
                return;
            }

            try {
                _mRefreshUI.Invoke(_ui, new object[] { _savedFilter });
                if (VERBOSE_LOG) Debug.Log("[MatFilterKeeper] 已恢复 filter：" + _savedFilter);
            } catch (Exception e) {
                Debug.LogWarning("[MatFilterKeeper] 恢复 filter 失败：" + e.Message);
            }
        }

        // filter 里非 "_" 开头的项按通配符匹配 renderer / material 名，全部落空说明新角色没这项
        private bool MatchesTarget(GameObject go, string filter) {
            if (go == null) return false;
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            bool hasSection = false;

            string[] items = filter.Split(',');
            for (int i = 0; i < items.Length; i++) {
                string s = items[i].Trim();
                if (s.Length == 0 || s.StartsWith("_")) continue;
                hasSection = true;
                Regex re = Wildcard(s);
                for (int j = 0; j < renderers.Length; j++) {
                    Renderer r = renderers[j];
                    if (r == null) continue;
                    if (re.IsMatch(NameFormatted(r.name))) return true;
                    Material[] mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) {
                        if (mats[k] != null && re.IsMatch(NameFormatted(mats[k].name))) return true;
                    }
                }
            }
            return !hasSection;
        }

        private static Regex Wildcard(string s) {
            string body = Regex.Escape(s).Replace("\\?", ".").Replace("\\*", ".*");
            return new Regex("^.*" + body + ".*$", RegexOptions.IgnoreCase);
        }

        private static string NameFormatted(string n) {
            if (string.IsNullOrEmpty(n)) return "";
            return n.Replace("(Instance)", "").Replace(" Instance", "").Trim();
        }

        private int CurrentDropdownValue() {
            if (_dropdown == null || _pDropValue == null) return -1;
            return (int)_pDropValue.GetValue(_dropdown, null);
        }

        private IList Options() {
            if (_dropdown == null || _pDropOptions == null) return null;
            return _pDropOptions.GetValue(_dropdown, null) as IList;
        }

        private string TextAt(int idx) {
            if (idx < 0) return null;
            IList list = Options();
            if (list == null || idx >= list.Count) return null;
            object item = list[idx];
            if (item == null) return null;
            if (_pOptionText == null || _pOptionText.DeclaringType != item.GetType())
                _pOptionText = item.GetType().GetProperty("text", InstFlags);
            if (_pOptionText == null) return null;
            return (string)_pOptionText.GetValue(item, null);
        }

        private int IndexOfText(string text) {
            IList list = Options();
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++) {
                string t = TextAt(i);
                if (t != null && string.Equals(t, text, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private Component GetCha(GameObject go) {
            if (go == null || _chaType == null) return null;
            return go.GetComponentInParent(_chaType);
        }

        private GameObject GetCurrentGo() {
            if (_ui == null || _pCurrentGo == null) return null;
            return (GameObject)_pCurrentGo.GetValue(_ui, null);
        }

        private string GetCurrentFilter() {
            if (_pCurrentFilter == null) return "";
            string s = (string)_pCurrentFilter.GetValue(null, null);
            return s == null ? "" : s;
        }

        private void WarnOnce(string message) {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning("[MatFilterKeeper] " + message);
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
