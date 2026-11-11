// #name Outfit Folder Memory
// #author Lu Jiejie
// #desc 让 BrowserFolders 的 Outfit folder 面板记住当前选中的子文件夹，
//       切换选中角色 / 取消选中 / 改选其它物品后不再折叠回根目录。
// #proc_filter CharaStudio.exe
// v1.0.0

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

    // 原理：服装列表每次重建时 BrowserFolders 都会 ClearCaches() 丢掉面板的
    // FolderTreeView；下次绘制时新建的树以默认根目录为 CurrentFolder 并立刻重过滤，
    // 于是文件夹树折叠、卡片列表回到根目录。这里记住每个根目录下最后选中的子文件夹，
    // 一旦发现树被换成了新的，就把目录设回去。
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

        private bool _ready;
        private bool _failed;
        private bool _errorLogged;

        // 根目录 -> 用户最后选中的子文件夹
        private readonly Dictionary<string, string> _folders = new Dictionary<string, string>();
        private object _tree;

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

                bool ok = _fListEntries != null && _fTree != null && _pEntryTree != null
                          && _pActive != null && _pTreeFolder != null && _pTreeDefault != null;
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

            object entry = null;
            foreach (object e in dict.Values) {
                if (IsActive(e)) { entry = e; break; }
            }
            if (entry == null) return; // 面板没打开

            object tree = _fTree.GetValue(entry);

            if (tree == null) {
                // 树被插件丢掉了：重建并直接定位到记住的目录
                tree = _pEntryTree.GetValue(entry, null);
                if (tree == null) return;
                string r = GetString(_pTreeDefault, tree);
                string saved;
                _folders.TryGetValue(r, out saved);
                _tree = tree;
                if (!string.IsNullOrEmpty(r) && !string.IsNullOrEmpty(saved)
                    && saved != r && saved.StartsWith(r, StringComparison.OrdinalIgnoreCase)) {
                    SetFolder(tree, saved);
                    Debug.Log("[OutfitFolderMemory] 恢复文件夹: " + saved);
                } else if (!string.IsNullOrEmpty(r)) {
                    if (saved == null) _folders[r] = GetString(_pTreeFolder, tree);
                }
                return;
            }

            string root = GetString(_pTreeDefault, tree);
            string current = GetString(_pTreeFolder, tree);
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(current)) return;

            string remembered;
            _folders.TryGetValue(root, out remembered);

            if (!ReferenceEquals(tree, _tree)) {
                // 换了一棵新树（= 插件重建），不是用户操作
                _tree = tree;
                if (!string.IsNullOrEmpty(remembered) && remembered != root
                    && remembered.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && remembered != current) {
                    SetFolder(tree, remembered);
                    Debug.Log("[OutfitFolderMemory] 恢复文件夹: " + remembered);
                    return;
                }
                if (string.IsNullOrEmpty(remembered)) _folders[root] = current;
                return;
            }

            // 同一棵树：目录变了就是用户自己点的
            if (current != remembered) _folders[root] = current;
        }

        // ========== 工具 ==========

        private bool IsActive(object entry) {
            try { return (bool)_pActive.GetValue(entry, null); } catch { return false; }
        }

        private static string GetString(PropertyInfo prop, object target) {
            try { return (string)prop.GetValue(target, null); } catch { return null; }
        }

        private void SetFolder(object tree, string folder) {
            try { _pTreeFolder.SetValue(tree, folder, null); } catch { }
        }
    }
}
