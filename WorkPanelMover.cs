// #name Move Work Panel
// #author Lu Jiejie
// #desc 启动游戏后，将工作室右侧的工作面板移动到指定位置
// #proc_filter CharaStudio.exe
// v1.0.0

using UnityEngine;

public static class WorkPanelMover {
    private static GameObject _gameObject;
    private static RectTransform _movedTarget;
    private static Vector3 _originalPosition;

    // ======== 配置区域 ========

    // 偏移量（像素）— 正数向右移，负数向左移
    private const float OffsetX = 137f;
    private const float OffsetY = -10f;

    // 启动后轮询等待 StudioScene 出现的间隔（秒）
    private const float PollInterval = 0.1f;
    private const bool DebugLog = true;
    // ==========================

    public static void Main() {
        _gameObject = new GameObject("WorkPanelMover");
        _gameObject.AddComponent<MoveController>();
        Debug.Log("[WorkPanelMover] 加载成功 | 偏移: (" + OffsetX + ", " + OffsetY + ")");
    }

    public static void Unload() {
        if (_gameObject != null) {
            Object.Destroy(_gameObject);
            _gameObject = null;
        }
        if (_movedTarget != null) {
            _movedTarget.anchoredPosition3D = _originalPosition;
            Debug.Log("[WorkPanelMover] 已恢复原始位置: " + _originalPosition);
        }
        _movedTarget = null;
    }

    class MoveController : MonoBehaviour {
        private bool _done;

        void Awake() { DontDestroyOnLoad(this); }

        void Start() {
            // 轮询等待，直到 StudioScene 出现（工作室界面加载完成）再执行移动，只执行一次
            // 使用 InvokeRepeating 而非协程，避免 Mono.CSharp 的 yield 编译器 bug
            InvokeRepeating("TryMove", PollInterval, PollInterval);
        }

        void TryMove() {
            if (_done) return;
            if (Object.FindObjectOfType<StudioScene>() == null) return; // 工作室还没加载完，继续等
            CancelInvoke("TryMove");
            _done = true;
            DoMove();
        }

        // 目标层级：StudioScene / Canvas Object List / Image Bar
        void DoMove() {
            Log("正在查找 Object List 面板...");
            try {
                // 1) 查找 StudioScene 实例
                StudioScene scene = Object.FindObjectOfType<StudioScene>();
                if (scene == null) { LogError("未找到 StudioScene 实例"); return; }
                Log("StudioScene 实例: " + scene.name);

                // 2) 按名称查找 "Canvas Object List"
                Transform panelT = FindChildRecursive(scene.transform, "Canvas Object List");
                if (panelT == null) {
                    LogError("未找到 \"Canvas Object List\"");
                    LogChildren(scene.gameObject);
                    return;
                }
                GameObject panel = panelT.gameObject;
                Log("Object List 面板: \"" + panel.name + "\" 层级: " + GetPath(panel.transform));
                LogChildren(panel);

                // 3) 优先取 Canvas Object List 下的 Image Bar 作为移动目标
                GameObject target = panel;
                Transform imageBar = FindChildRecursive(panel.transform, "Image Bar");
                if (imageBar != null) {
                    target = imageBar.gameObject;
                    Log("使用 Image Bar: \"" + target.name + "\" 层级: " + GetPath(target.transform));
                } else {
                    Log("未找到子对象 \"Image Bar\"，将直接移动: " + panel.name);
                }

                MoveTransform(target, OffsetX, OffsetY);
                Log("✅ 已移动: " + target.name);
            }
            catch (System.Exception ex) {
                LogError("执行失败: " + ex.ToString());
            }
        }

        Transform FindChildRecursive(Transform parent, string name) {
            foreach (Transform child in parent) {
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        void MoveTransform(GameObject target, float ox, float oy) {
            RectTransform rt = target.GetComponent<RectTransform>();
            if (rt == null) { LogError("目标无 RectTransform"); return; }
            if (_movedTarget == null) _originalPosition = rt.anchoredPosition3D;
            rt.anchoredPosition3D = new Vector3(_originalPosition.x + ox, _originalPosition.y + oy, _originalPosition.z);
            _movedTarget = rt;
            Log("位置: " + _originalPosition + " -> " + rt.anchoredPosition3D);
        }

        void LogChildren(GameObject parent) {
            if (!DebugLog) return;
            string children = "";
            foreach (Transform child in parent.transform) {
                if (children != "") children += ", ";
                children += "\"" + child.name + "\"";
            }
            Log("子对象: [" + children + "]");
        }

        string GetPath(Transform t) {
            string path = t.name;
            while (t.parent != null) {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        void Log(string msg) { if (DebugLog) Debug.Log("[WorkPanelMover] " + msg); }
        void LogError(string msg) { Debug.LogError("[WorkPanelMover] " + msg); }
    }
}
