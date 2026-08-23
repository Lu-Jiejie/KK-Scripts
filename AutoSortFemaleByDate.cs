// #name Auto Sort Female List by Date
// #author Lu Jiejie
// #desc 每次启动工作室时自动将女角色列表排序改为日期倒序（最新在前），无需手动切换
// #proc_filter CharaStudio.exe
// v1.0

using System.Collections;
using System.Reflection;
using UnityEngine;

public static class AutoSortFemaleByDate {
    private static GameObject _gameObject;

    public static void Main() {
        _gameObject = new GameObject("AutoSortFemaleByDate");
        _gameObject.AddComponent<SortController>();
        Debug.Log("[AutoSortFemaleByDate] 加载成功");
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
        private Coroutine _retryCoroutine;

        private System.Type _charaListType;
        private System.Type _charaFileSortType;
        private MethodInfo _onSortMethod;
        private FieldInfo _isInitField;
        private FieldInfo _charaFileSortField;
        private FieldInfo _cfiListField;

        void Awake() { DontDestroyOnLoad(this); }

        void Start() {
            _charaListType = typeof(Studio.CharaList);
            _charaFileSortType = typeof(Studio.CharaFileSort);

            _onSortMethod = _charaListType.GetMethod("OnSort",
                BindingFlags.Public | BindingFlags.Instance);
            _isInitField = _charaListType.GetField("<isInit>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _charaFileSortField = _charaListType.GetField("charaFileSort",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _cfiListField = _charaFileSortType.GetField("cfiList",
                BindingFlags.Public | BindingFlags.Instance);

            if (_onSortMethod == null) {
                Debug.LogError("[AutoSortFemaleByDate] 反射失败：找不到 OnSort 方法");
                return;
            }

            InvokeRepeating("TrySetSort", 0.1f, 0.1f);
        }

        void OnDestroy() {
            CancelInvoke("TrySetSort");
            if (_retryCoroutine != null) StopCoroutine(_retryCoroutine);
        }

        public void Stop() {
            CancelInvoke("TrySetSort");
            if (_retryCoroutine != null) StopCoroutine(_retryCoroutine);
            _done = true;
        }

        void TrySetSort() {
            if (_done) return;
            if (_onSortMethod == null) return;

            GameObject studioScene = GameObject.Find("StudioScene");
            if (studioScene == null) {
                StudioScene ss = Object.FindObjectOfType<StudioScene>();
                if (ss != null) studioScene = ss.gameObject;
            }
            if (studioScene == null) return;

            Transform panelT = FindChildRecursive(studioScene.transform, "00_Female");
            if (panelT == null) return;

            object charaList = panelT.GetComponent(_charaListType);
            if (charaList == null) {
                charaList = panelT.GetComponentInChildren(_charaListType, true);
            }
            if (charaList == null) return;

            if (!IsInitialized(charaList)) return;

            CancelInvoke("TrySetSort");
            _retryCoroutine = StartCoroutine(DoSort(charaList, panelT.gameObject));
        }

        bool IsInitialized(object charaList) {
            try {
                if (_isInitField != null) {
                    object isInit = _isInitField.GetValue(charaList);
                    if (isInit is bool && !(bool)isInit) return false;
                }

                if (_charaFileSortField != null && _cfiListField != null) {
                    object fileSort = _charaFileSortField.GetValue(charaList);
                    if (fileSort != null) {
                        object list = _cfiListField.GetValue(fileSort);
                        if (list != null) {
                            PropertyInfo countProp = list.GetType().GetProperty("Count");
                            if (countProp != null) {
                                int count = (int)countProp.GetValue(list, null);
                                if (count > 0) return true;
                            }
                        }
                    }
                    return false;
                }

                return true;
            } catch {
                return false;
            }
        }

        IEnumerator DoSort(object charaList, GameObject panelFemale) {
            yield return null;

            bool panelWasActive = panelFemale.activeSelf;
            if (!panelWasActive) {
                panelFemale.SetActive(true);
                yield return null;
            }

            object[] sortParam = new object[] { 1 };
            for (int i = 0; i < 2; i++) {
                try {
                    _onSortMethod.Invoke(charaList, sortParam);
                } catch (System.Exception ex) {
                    Debug.LogError("[AutoSortFemaleByDate] 调用 OnSort 失败: " + ex.Message);
                    break;
                }
                yield return new WaitForSeconds(0.2f);
            }

            if (!panelWasActive) {
                panelFemale.SetActive(false);
            }

            Debug.Log("[AutoSortFemaleByDate] ✅ 已切换为日期倒序（最新在前）");
            _done = true;
            _retryCoroutine = null;
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
