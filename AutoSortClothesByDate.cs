// #name Auto Sort Clothes List by Date
// #author Lu Jiejie
// #desc 每次打开服装面板时自动将服装列表排序改为日期倒序（最新在前）
// #proc_filter CharaStudio.exe
// v1.0.0

using System.Collections;
using System.Reflection;
using UnityEngine;

public static class AutoSortClothesByDate {
    private static GameObject _gameObject;

    public static void Main() {
        _gameObject = new GameObject("AutoSortClothesByDate");
        _gameObject.AddComponent<SortController>();
        Debug.Log("[AutoSortClothesByDate] 加载成功");
    }

    public static void Unload() {
        if (_gameObject != null) {
            SortController ctrl = _gameObject.GetComponent<SortController>();
            if (ctrl != null) ctrl.Stop();
            Object.Destroy(_gameObject);
            _gameObject = null;
        }
    }

    class SortController : MonoBehaviour {
        private bool _done;
        private bool _wasOpen;
        private bool _pendingSort;
        // 引用链快照
        private object _lastOciChar;
        private object _lastCostumeInfo;
        private object _lastFileSort;
        private object _lastList;
        // 列表内容签名
        private int _lastSig;
        private int _sigStableCount;
        // 单次验证（应用后 0.3s 检查一次，捕获游戏二次重置）
        private float _verifyTime;
        private bool _verified;

        private PropertyInfo _itemProp;
        private PropertyInfo _sortKindProp;

        private System.Type _mpCharCtrlType;
        private FieldInfo _costumeInfoField;
        private FieldInfo _ociCharField;
        private FieldInfo _fileSortField;
        private FieldInfo _cfiListField;
        private MethodInfo _sortMethod;

        void Awake() { DontDestroyOnLoad(this); }

        void Start() {
            _mpCharCtrlType = typeof(Studio.MPCharCtrl);
            _costumeInfoField = _mpCharCtrlType.GetField("costumeInfo",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _ociCharField = _mpCharCtrlType.GetField("m_OCIChar",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (_costumeInfoField == null) {
                Debug.LogError("[AutoSortClothesByDate] 反射失败：找不到 costumeInfo 字段");
                return;
            }

            System.Type costumeInfoType = _costumeInfoField.FieldType;
            _fileSortField = costumeInfoType.GetField("fileSort",
                BindingFlags.Public | BindingFlags.Instance);
            if (_fileSortField == null) {
                Debug.LogError("[AutoSortClothesByDate] 反射失败：找不到 fileSort 字段");
                return;
            }

            System.Type fileSortType = _fileSortField.FieldType;
            _sortKindProp = fileSortType.GetProperty("sortKind");
            _cfiListField = fileSortType.GetField("cfiList",
                BindingFlags.Public | BindingFlags.Instance);
            _sortMethod = fileSortType.GetMethod("Sort",
                BindingFlags.Public | BindingFlags.Instance,
                null, new System.Type[] { typeof(int), typeof(bool) }, null);
            if (_sortMethod == null) {
                Debug.LogError("[AutoSortClothesByDate] 反射失败：找不到 Sort(int, bool) 方法");
                return;
            }

            InvokeRepeating("TrySetSort", 0.05f, 0.05f);
        }

        void OnDestroy() {
            CancelInvoke("TrySetSort");
        }

        public void Stop() {
            CancelInvoke("TrySetSort");
            _done = true;
        }

        void TrySetSort() {
            if (_done) return;
            if (_sortMethod == null) return;

            GameObject studioScene = GameObject.Find("StudioScene");
            if (studioScene == null) {
                StudioScene ss = Object.FindObjectOfType<StudioScene>();
                if (ss != null) studioScene = ss.gameObject;
            }
            if (studioScene == null) return;

            Transform panelT = FindChildRecursive(studioScene.transform, "05_Costume");
            bool isOpen = panelT != null && panelT.gameObject.activeInHierarchy;

            // 面板关闭 → 重置状态
            if (!isOpen) {
                _wasOpen = false;
                _pendingSort = false;
                _verified = false;
                _lastOciChar = null;
                _lastCostumeInfo = null;
                _lastFileSort = null;
                _lastList = null;
                _lastSig = 0;
                _sigStableCount = 0;
                return;
            }

            // 面板刚打开 → 标记
            if (!_wasOpen) {
                _wasOpen = true;
                _pendingSort = true;
                _sigStableCount = 0;
                _verified = false;
                Debug.Log("[AutoSortClothesByDate] 检测到服装面板打开，等待列表加载...");
            }

            // 获取组件链
            object mpCharCtrl = null;
            Component[] parents = panelT.GetComponentsInParent(_mpCharCtrlType, true);
            if (parents != null && parents.Length > 0) mpCharCtrl = parents[0];
            if (mpCharCtrl == null) return;

            object ociChar = _ociCharField?.GetValue(mpCharCtrl);
            object costumeInfo = _costumeInfoField.GetValue(mpCharCtrl);
            if (costumeInfo == null) return;

            object fileSort = _fileSortField.GetValue(costumeInfo);
            if (fileSort == null) return;

            object list = _cfiListField != null ? _cfiListField.GetValue(fileSort) : null;
            if (list == null) return;

            int count = (int)list.GetType().GetProperty("Count").GetValue(list, null);
            int sig = ComputeSig(list, count);

            // 检测切换角色
            bool reloaded = (ociChar != _lastOciChar)
                         || (costumeInfo != _lastCostumeInfo)
                         || (fileSort != _lastFileSort)
                         || (list != _lastList)
                         || (_lastSig != 0 && sig != _lastSig);

            if (reloaded) {
                _pendingSort = true;
                _sigStableCount = 0;
                _verified = false;
                Debug.Log("[AutoSortClothesByDate] 检测到角色切换/列表重载");
            }

            // 更新快照
            _lastOciChar = ociChar;
            _lastCostumeInfo = costumeInfo;
            _lastFileSort = fileSort;
            _lastList = list;

            // 单次验证：应用后 0.3s 检查一次，捕获游戏二次重置
            if (_verified && _verifyTime > 0 && Time.time >= _verifyTime) {
                _verifyTime = 0;
                if (_sortKindProp != null) {
                    int sortKind = (int)_sortKindProp.GetValue(fileSort, null);
                    if (sortKind == 0) {
                        Debug.Log("[AutoSortClothesByDate] 验证发现排序被重置为姓名，重新应用");
                        ApplySort(fileSort, list, count);
                        return;
                    }
                }
            }

            // 等待排序条件
            if (!_pendingSort) return;
            if (count == 0) { _sigStableCount = 0; return; }

            // 列表签名稳定后才应用
            if (sig != _lastSig) { _lastSig = sig; _sigStableCount = 0; return; }
            _sigStableCount++;
            if (_sigStableCount < 5) { _lastSig = sig; return; }

            // 应用日期倒序
            ApplySort(fileSort, list, count);
            _lastSig = ComputeSig(list, count);
            _pendingSort = false;
            _verified = true;
            _verifyTime = Time.time + 0.3f;
        }

        void ApplySort(object fileSort, object list, int count) {
            try {
                _sortMethod.Invoke(fileSort, new object[] { 1, false });
                Debug.Log("[AutoSortClothesByDate] ✅ 已切换为日期倒序（最新在前）");
            } catch (System.Exception ex) {
                Debug.LogError("[AutoSortClothesByDate] 调用 Sort 失败: " + ex.Message);
            }
        }

        int ComputeSig(object list, int count) {
            int sig = count * 397;
            if (count <= 0) return sig;
            if (_itemProp == null) _itemProp = list.GetType().GetProperty("Item");
            if (_itemProp == null) return sig;
            for (int i = 0; i < count; i++) {
                object item = _itemProp.GetValue(list, new object[] { i });
                if (item != null) sig += item.GetHashCode();
            }
            return sig;
        }

        Transform FindChildRecursive(Transform parent, string name) {
            foreach (Transform child in parent) {
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
