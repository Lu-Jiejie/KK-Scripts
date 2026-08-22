// #name Disable New Dynamic Bones on Colliders
// #author Lu Jiejie
// #desc 读取场景后，自动找出所有的 DynamicBoneCollider ，将它们的 KKPE 内的 “Enable New Dynamic Bones” 选项变为 false 
// #proc_filter CharaStudio.exe
// v1.3.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Studio;

public static class DisableNewDynamicBonesOnColliders {
    const int TARGET_GROUP = 3;
    const int TARGET_CATEGORY = 100;

    public static void Main() {
        GameObject go = new GameObject("DisableColliderNDB");
        go.AddComponent<ColliderDisabler>();
    }

    public static void Unload() {
        GameObject go = GameObject.Find("DisableColliderNDB");
        if (go != null) {
            GameObject.DestroyImmediate(go);
        }
    }

    private class ColliderDisabler : MonoBehaviour {
        // ---- Reflection cache ----
        private Type T_PoseController;
        private Type T_CollidersEditor;
        private Type T_GenericOCITarget;

        // PoseController members (try multiple names/formats for compatibility)
        private FieldInfo F_PoseControllers;        // static _poseControllers
        private FieldInfo F_Pose_CE;                // _collidersEditor
        private MemberInfo M_Pose_Target;           // target (field or property)

        // GenericOCITarget.ociItem (public field)
        private FieldInfo F_Target_ociItem;

        // CollidersEditor members
        private FieldInfo F_CE_AddNewAsDefault;     // _addNewDynamicBonesAsDefault (public bool)
        private FieldInfo F_CE_LoneColliders;       // static _loneColliders dict

        // ObjectInfo / OIItemInfo
        private FieldInfo F_OCIItem_objectInfo;     // OCIItem.objectInfo (from ObjectCtrlInfo)
        private PropertyInfo P_OIItem_group;
        private PropertyInfo P_OIItem_category;

        private bool _refOk;

        // Scene detection
        private int _lastObjectCount = -1;
        private float _processTimer;

        void Awake() {
            DontDestroyOnLoad(this);
        }

        IEnumerator Start() {
            yield return null;
            TryInitReflection();
            if (_refOk)
                Debug.Log("[DisableColliderNDB] initialized");
            else
                Debug.LogWarning("[DisableColliderNDB] initialization failed");

            // Unity scene load event — fires when the Studio scene is fully loaded.
            // This is AFTER KKPE has processed the scene (LoadElement / LoadXml),
            // so our flag change won't be overwritten by KKPE's scene load.
            try {
                SceneManager.sceneLoaded += OnSceneLoaded;
            } catch (Exception ex) {
                Debug.LogWarning("[DisableColliderNDB] SceneManager subscribe failed: " + ex.Message);
            }

            ProcessIfNeeded();
        }

        void OnDestroy() {
            try {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            } catch {
            }
        }

        // =========================================================
        // Scene-load detection (fires AFTER Studio scene fully loaded)
        // =========================================================

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            // Scene fully loaded — KKPE has finished its processing.
            // Run several delayed passes to catch any late re-loading
            // (e.g. KKPE LoadXml re-reading the flag from scene XML).
            StartCoroutine(ProcessAfterDelay(1.0f, "sceneLoaded+1s"));
            StartCoroutine(ProcessAfterDelay(3.0f, "sceneLoaded+3s"));
            StartCoroutine(ProcessAfterDelay(6.0f, "sceneLoaded+6s"));
        }

        private IEnumerator ProcessAfterDelay(float seconds, string tag) {
            yield return new WaitForSeconds(seconds);
            ProcessIfNeeded(tag);
        }

        // =========================================================
        // Reflection
        // =========================================================

        private void TryInitReflection() {
            const BindingFlags BFS = BindingFlags.Static | BindingFlags.Instance |
                                     BindingFlags.Public | BindingFlags.NonPublic;
            try {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly asm in asms) {
                    if (asm.GetName().Name == "KKPE") {
                        T_PoseController = asm.GetType("HSPE.PoseController");
                        T_CollidersEditor = asm.GetType("HSPE.AMModules.CollidersEditor");
                        T_GenericOCITarget = asm.GetType("HSPE.AMModules.GenericOCITarget");
                        break;
                    }
                }

                if (T_PoseController == null || T_CollidersEditor == null) {
                    Debug.LogWarning("[DisableColliderNDB] KKPE types not found. Is KKPE (Pose Editor) installed?");
                    return;
                }

                // PoseController._poseControllers (static HashSet)
                F_PoseControllers = T_PoseController.GetField("_poseControllers", BFS);

                // PoseController._collidersEditor
                F_Pose_CE = T_PoseController.GetField("_collidersEditor", BFS);

                // PoseController.target — try field first, then property, then _target field
                FieldInfo fTarget = T_PoseController.GetField("target", BFS);
                PropertyInfo pTarget = T_PoseController.GetProperty("target", BFS);
                FieldInfo fTarget2 = T_PoseController.GetField("_target", BFS);
                if (fTarget != null) M_Pose_Target = fTarget;
                else if (pTarget != null) M_Pose_Target = pTarget;
                else if (fTarget2 != null) M_Pose_Target = fTarget2;

                // GenericOCITarget.ociItem
                if (T_GenericOCITarget != null)
                    F_Target_ociItem = T_GenericOCITarget.GetField("ociItem", BFS);

                // CollidersEditor._addNewDynamicBonesAsDefault (PUBLIC bool field)
                F_CE_AddNewAsDefault = T_CollidersEditor.GetField("_addNewDynamicBonesAsDefault", BFS);

                // CollidersEditor._loneColliders (internal static dict)
                F_CE_LoneColliders = T_CollidersEditor.GetField("_loneColliders", BFS);

                // ---- Game types (Assembly-CSharp, compile-time access) ----
                // OCIItem.objectInfo via ObjectCtrlInfo
                Type tOCIItem = typeof(OCIItem);
                Type tObjectInfo = typeof(ObjectInfo);
                Type tOIItemInfo = tObjectInfo.Assembly.GetType("Studio.OIItemInfo");

                F_OCIItem_objectInfo = tOCIItem.GetField("objectInfo",
                    BindingFlags.Public | BindingFlags.Instance);

                if (tOIItemInfo != null) {
                    P_OIItem_group = tOIItemInfo.GetProperty("group");
                    P_OIItem_category = tOIItemInfo.GetProperty("category");
                }

                // ---- Validate ----
                List<string> missing = new List<string>();
                if (F_PoseControllers == null) missing.Add("_poseControllers");
                if (F_Pose_CE == null) missing.Add("_collidersEditor");
                if (M_Pose_Target == null) missing.Add("target");
                if (F_Target_ociItem == null) missing.Add("ociItem");
                if (F_CE_AddNewAsDefault == null) missing.Add("_addNewDynamicBonesAsDefault");
                if (F_CE_LoneColliders == null) missing.Add("_loneColliders");
                if (F_OCIItem_objectInfo == null) missing.Add("objectInfo");
                if (P_OIItem_group == null) missing.Add("OIItemInfo.group");
                if (P_OIItem_category == null) missing.Add("OIItemInfo.category");

                if (missing.Count > 0) {
                    Debug.LogWarning("[DisableColliderNDB] Reflection failed, missing: " + string.Join(", ", missing.ToArray()));
                    return;
                }
                _refOk = true;
            } catch (Exception ex) {
                Debug.LogError("[DisableColliderNDB] TryInitReflection exception: " + ex);
            }
        }

        // =========================================================
        // Scene detection
        // =========================================================

        void Update() {
            try {
                if (Studio.Studio.Instance == null) return;
                var dic = Studio.Studio.Instance.dicObjectCtrl;
                if (dic == null) return;

                int c = dic.Count;
                if (c != _lastObjectCount) {
                    _lastObjectCount = c;
                    // Use a longer delay (2s) for the count-change detection,
                    // since SceneManager.sceneLoaded is the primary trigger.
                    _processTimer = 2.0f;
                }
                if (_processTimer > 0f) {
                    _processTimer -= Time.deltaTime;
                    if (_processTimer <= 0f) {
                        // Only run via count-change if sceneLoaded hasn't triggered yet
                        // (sceneLoaded sets timer to 0 so this won't fire).
                        ProcessIfNeeded();
                    }
                }
            } catch {
            }
        }

        // =========================================================
        // Processing
        // =========================================================

        private void ProcessIfNeeded(string tag = "") {
            try {
                if (!_refOk) return;
                var dic = Studio.Studio.Instance?.dicObjectCtrl;
                if (dic == null || dic.Count == 0) return;

                var poses = F_PoseControllers.GetValue(null) as IEnumerable;
                var lone = F_CE_LoneColliders.GetValue(null) as IDictionary;

                int count = 0;

                // ---- Method A: iterate PoseControllers ----
                count += ProcessViaPoseControllers(poses);

                // ---- Method B: iterate _loneColliders ----
                int viaLone = ProcessViaLoneColliders(lone);
                count += viaLone;

                if (count > 0 || tag.Contains("sceneLoaded"))
                    Debug.Log("[DisableColliderNDB] " + tag + " set " + count + " collider flag(s) to false");
            } catch (Exception ex) {
                Debug.LogError("[DisableColliderNDB] ProcessIfNeeded exception: " + ex);
            }
        }

        /// <summary>Method A: scan every KKPE PoseController, find items with matching group/category, set flag.</summary>
        private int ProcessViaPoseControllers(IEnumerable poses) {
            int count = 0;
            if (poses == null) return 0;

            int idx = 0;
            foreach (var pc in poses) {
                if (pc == null) { idx++; continue; }

                object target = GetMemberValue(M_Pose_Target, pc);
                if (target == null) { idx++; continue; }

                object ociItem = (F_Target_ociItem != null) ? F_Target_ociItem.GetValue(target) : null;
                if (ociItem == null) { idx++; continue; }

                if (!IsTargetItem(ociItem)) { idx++; continue; }

                // Get CollidersEditor from the PoseController
                object ce = (F_Pose_CE != null) ? F_Pose_CE.GetValue(pc) : null;
                if (ce == null) { idx++; continue; }

                if (SetFlagFalse(ce))
                    count++;
                idx++;
            }
            return count;
        }

        /// <summary>Method B: scan KKPE's _loneColliders dict (standalone collider items).</summary>
        private int ProcessViaLoneColliders(IDictionary lone) {
            int count = 0;
            if (lone == null || lone.Count == 0) return 0;

            foreach (DictionaryEntry entry in lone) {
                object ce = entry.Value;
                if (ce == null) continue;

                if (SetFlagFalse(ce))
                    count++;
            }
            return count;
        }

        /// <summary>Set _addNewDynamicBonesAsDefault=false if currently true. Returns true if it changed.</summary>
        private bool SetFlagFalse(object ce) {
            try {
                object v = F_CE_AddNewAsDefault.GetValue(ce);
                bool current = (v is bool) && (bool)v;
                if (current) {
                    F_CE_AddNewAsDefault.SetValue(ce, false);
                    return true;
                }
                return false;
            } catch {
                return false;
            }
        }

        // =========================================================
        // Helpers
        // =========================================================

        private bool IsTargetItem(object ociItem) {
            try {
                object objInfo = F_OCIItem_objectInfo.GetValue(ociItem);
                if (objInfo == null) return false;
                if (P_OIItem_group == null || P_OIItem_category == null) return false;
                int group = (int)P_OIItem_group.GetValue(objInfo, null);
                int category = (int)P_OIItem_category.GetValue(objInfo, null);
                return group == TARGET_GROUP && category == TARGET_CATEGORY;
            } catch {
                return false;
            }
        }

        private static object GetMemberValue(MemberInfo mi, object target) {
            if (mi == null || target == null) return null;
            if (mi is FieldInfo) return ((FieldInfo)mi).GetValue(target);
            if (mi is PropertyInfo) return ((PropertyInfo)mi).GetValue(target, null);
            return null;
        }
    }
}
