// #name Outfit Folder Memory
// #author Lu Jiejie
// #desc 让 BrowserFolders 的 Outfit folder 面板记住当前选中的子文件夹、展开状态和滚动位置，
//       切换选中角色 / 取消选中 / 改选其它物品后不再折叠回根目录。
// #proc_filter CharaStudio.exe
// v1.1.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class OutfitFolderMemory {
    private const string GAME_OBJECT_NAME = "OutfitFolderMemory";

    private static GameObject _gameObject;

    public static void Main() {
        DestroyOldInstances();
        _gameObject = new GameObject(GAME_OBJECT_NAME);
        _gameObject.AddComponent<MemoryController>();
        Debug.Log("[OutfitFolderMemory] 加载成功");
    }

    public static void Unload() {
        DestroyOldInstances();
        _gameObject = null;
    }

    private static void DestroyOldInstances() {
        GameObject obj;
        while ((obj = GameObject.Find(GAME_OBJECT_NAME)) != null)
            UnityEngine.Object.DestroyImmediate(obj);
    }

    // 原理：服装列表重建时 BrowserFolders 会 ClearCaches() 丢掉面板的 FolderTreeView，
    // 下次绘制新建的树以默认根目录为 CurrentFolder 并立刻重过滤 —— 文件夹树折叠、
    // 卡片列表回根目录、滚动条归零。
    // 对策：能抢回原树就抢回（状态原样）；真建了新树就把目录、展开状态、滚动位置还回去。
    private class MemoryController : MonoBehaviour {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static
                                                     | BindingFlags.Public | BindingFlags.NonPublic
                                                     | BindingFlags.DeclaredOnly;

        private FieldInfo _fListEntries;
        private FieldInfo _fTree;
        private PropertyInfo _pEntryTree;
        private PropertyInfo _pActive;
        private PropertyInfo _pTreeFolder;
        private PropertyInfo _pTreeDefault;
        private FieldInfo _fScroll;
        private FieldInfo _fOpened;

        private bool _ready;
        private bool _failed;
        private bool _errorLogged;

        private class TreeState {
            public Vector2 Scroll;
            public HashSet<string> Opened;
        }

        // 根目录 -> 用户最后选中的子文件夹
        private readonly Dictionary<string, string> _folders = new Dictionary<string, string>();
        // 子文件夹 -> 该文件夹下的展开状态与滚动位置
        private readonly Dictionary<string, TreeState> _states = new Dictionary<string, TreeState>();

        private object _entry;
        private object _tree;
        private string _root;
        private string _folder;
        // 新树第一帧内容高度还是 0，滚动值会被钳回顶部，所以要多推几帧
        private int _restoreFrames;

        void Awake() {
            DontDestroyOnLoad(this);
        }

        void Update() { SafeTick(); }
        void LateUpdate() { SafeTick(); }

        private void SafeTick() {
            if (_failed) return;
            if (!_ready && !InitReflection()) return;
            try {
                Tick();
            } catch (Exception ex) {
                if (!_errorLogged) {
                    _errorLogged = true;
                    Debug.LogWarning("[OutfitFolderMemory] " + ex.Message);
                }
            }
        }

        // ========== 反射绑定 ==========

        private bool InitReflection() {
            Assembly asm = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) {
                try {
                    if (a.GetType("BrowserFolders.Studio.CostumeInfoEntry") != null) { asm = a; break; }
                } catch { }
            }
            if (asm == null) return false; // BrowserFolders 还没加载，下一帧再试

            try {
                Type entryType = asm.GetType("BrowserFolders.Studio.CostumeInfoEntry");
                Type helperType = asm.GetType("BrowserFolders.Studio.StudioOutfitFoldersHelper");
                Type treeType = asm.GetType("BrowserFolders.FolderTreeView");
                if (entryType == null || helperType == null || treeType == null) return false;

                _fListEntries = FindField(helperType, "ListEntries");
                _fTree = FindField(entryType, "_folderTreeView");
                _pEntryTree = FindProperty(entryType, "FolderTreeView");
                _pActive = FindProperty(entryType, "isActiveAndEnabled");
                _pTreeFolder = treeType.GetProperty("CurrentFolder");
                _pTreeDefault = treeType.GetProperty("DefaultPath");
                _fScroll = FindField(treeType, "_treeScrollPosition");
                _fOpened = FindField(treeType, "_openedObjects");

                bool ok = _fListEntries != null && _fTree != null && _pEntryTree != null
                          && _pActive != null && _pTreeFolder != null && _pTreeDefault != null;
                if (_fScroll == null || _fOpened == null)
                    Debug.LogWarning("[OutfitFolderMemory] 未找到滚动/展开字段，只记文件夹不记滚动位置");
                if (!ok) {
                    Debug.LogWarning("[OutfitFolderMemory] BrowserFolders 成员结构不符，脚本停用");
                    _failed = true;
                    return false;
                }
                _ready = true;
                return true;
            } catch (Exception ex) {
                Debug.LogWarning("[OutfitFolderMemory] 反射初始化失败: " + ex.Message);
                _failed = true;
                return false;
            }
        }

        private static FieldInfo FindField(Type type, string name) {
            while (type != null) {
                FieldInfo f = type.GetField(name, Flags);
                if (f != null) return f;
                type = type.BaseType;
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name) {
            while (type != null) {
                PropertyInfo p = type.GetProperty(name, Flags);
                if (p != null) return p;
                type = type.BaseType;
            }
            return null;
        }

        // ========== 主逻辑 ==========

        private void Tick() {
            IDictionary dict = _fListEntries.GetValue(null) as IDictionary;
            if (dict == null || dict.Count == 0) return;

            object active = null;
            object first = null;
            foreach (object e in dict.Values) {
                if (first == null) first = e;
                if (IsActive(e)) { active = e; break; }
            }
            object entry = active != null ? active : first;
            if (entry == null) return;

            if (!ReferenceEquals(entry, _entry)) {
                _entry = entry;
                _tree = null;
                _root = null;
                _folder = null;
                _restoreFrames = 0;
            }

            object tree = _fTree.GetValue(entry);

            if (tree == null) {
                if (_tree != null) {
                    // 抢回原树：展开状态与滚动位置全都还在
                    _fTree.SetValue(entry, _tree);
                    tree = _tree;
                } else {
                    if (!IsActive(entry)) return; // 面板没打开就不急着建树
                    tree = _pEntryTree.GetValue(entry, null);
                    if (tree == null) return;
                    Adopt(tree);
                    return;
                }
            }

            if (!ReferenceEquals(tree, _tree)) {
                Adopt(tree);
                return;
            }

            string current = GetString(_pTreeFolder, tree);
            if (string.IsNullOrEmpty(current)) return;

            if (current != _folder) {
                // 同一棵树里目录变了 = 用户自己点的
                _folder = current;
                if (!string.IsNullOrEmpty(_root)) _folders[_root] = current;
            }

            RollState(tree, current);
        }

        private void Adopt(object tree) {
            _tree = tree;
            _root = GetString(_pTreeDefault, tree);

            string saved = null;
            bool hasSaved = !string.IsNullOrEmpty(_root) && _folders.TryGetValue(_root, out saved);
            if (hasSaved && !string.IsNullOrEmpty(saved) && saved != _root
                && saved.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) {
                if (SetFolder(tree, saved)) Debug.Log("[OutfitFolderMemory] 恢复文件夹: " + saved);
            } else if (!hasSaved && !string.IsNullOrEmpty(_root)) {
                _folders[_root] = GetString(_pTreeFolder, tree);
            }

            _folder = GetString(_pTreeFolder, tree);

            TreeState st;
            if (!string.IsNullOrEmpty(_folder) && _states.TryGetValue(_folder, out st)) {
                ApplyOpened(tree, st);
                ApplyScroll(tree, st);
                _restoreFrames = 40;
            } else {
                _restoreFrames = 0;
            }
        }

        // ========== 滚动位置 / 展开状态 ==========

        private void RollState(object tree, string folder) {
            TreeState st;
            if (_restoreFrames > 0 && _states.TryGetValue(folder, out st)) {
                ApplyScroll(tree, st);
                _restoreFrames--;
                return;
            }
            CaptureState(tree, folder);
        }

        private void CaptureState(object tree, string folder) {
            if (string.IsNullOrEmpty(folder) || _fScroll == null) return;
            TreeState st;
            if (!_states.TryGetValue(folder, out st)) {
                st = new TreeState();
                _states[folder] = st;
            }
            try { st.Scroll = (Vector2)_fScroll.GetValue(tree); } catch { return; }

            if (_fOpened == null) return;
            try {
                HashSet<string> src = _fOpened.GetValue(tree) as HashSet<string>;
                if (src == null) return;
                if (st.Opened == null) st.Opened = new HashSet<string>();
                else st.Opened.Clear();
                foreach (string s in src) st.Opened.Add(s);
            } catch { }
        }

        private void ApplyScroll(object tree, TreeState st) {
            if (tree == null || st == null) return;
            try {
                Vector2 now = (Vector2)_fScroll.GetValue(tree);
                if (now == st.Scroll) return;
                if (now != Vector2.zero) {
                    // 用户在这几帧里自己滚了，以用户为准
                    st.Scroll = now;
                    _restoreFrames = 0;
                    return;
                }
                _fScroll.SetValue(tree, st.Scroll);
            } catch { }
        }

        private void ApplyOpened(object tree, TreeState st) {
            if (tree == null || st == null || st.Opened == null) return;
            try {
                HashSet<string> dst = _fOpened.GetValue(tree) as HashSet<string>;
                if (dst == null) return;
                foreach (string s in st.Opened) dst.Add(s);
            } catch { }
        }

        // ========== 工具 ==========

        private bool IsActive(object entry) {
            try { return (bool)_pActive.GetValue(entry, null); } catch { return false; }
        }

        private static string GetString(PropertyInfo prop, object target) {
            try { return (string)prop.GetValue(target, null); } catch { return null; }
        }

        private bool SetFolder(object tree, string folder) {
            try {
                if (GetString(_pTreeFolder, tree) == folder) return false;
                _pTreeFolder.SetValue(tree, folder, null);
                return true;
            } catch { return false; }
        }
    }
}
