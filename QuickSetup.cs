// #name Quick Setup State & Clothes
// #author Lu Jiejie
// #desc 一键设置工作室所有女角色的状态（服装、皮肤等）
// #proc_filter CharaStudio.exe
// v1.0.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class QuickSetup {
    // ═══════════════════════════════════════════════════════════════
    //  ★ 可配置区域 ★  —  修改下方数值即可调整效果
    // ═══════════════════════════════════════════════════════════════

    // ---- 快捷键 ----
    private static KeyCode _hotkey = KeyCode.F5;   // 触发快捷键
    private static bool _requireCtrl = false;        // 是否需要按住Ctrl
    private static bool _requireAlt = false;          // 是否需要按住Alt
    private static bool _requireShift = false;       // 是否需要按住Shift

    // ---- 1. 服装状态 (ClothesState) ----
    // 服装种类参考-第一列：
    //   top=0 上衣, bot=1 下衣, bra=2 胸罩, shorts=3 内裤, gloves=4 手套,
    //   panst=5 裤袜, socks=6 袜子, shoes_inner=7 室内鞋, shoes_outer=8 室外鞋
    // 状态值-第二列：0=穿, 1=半脱1(靠近穿的第一档), 2=半脱2(第二档), 3=全脱

    private static Dictionary<int, byte> _clothesStates = new Dictionary<int, byte> {
        { 0, 1 },   // top(0)         = 上衣 
        { 1, 1 },   // bot(1)         = 下衣
        { 2, 1 },   // bra(2)         = 胸罩
        { 3, 1 },   // shorts(3)      = 内裤
        { 4, 0 },   // gloves(4)      = 手套
        { 5, 0 },   // panst(5)       = 裤袜
        { 6, 0 },   // socks(6)       = 袜子
        { 7, 0 },   // shoes_inner(7) = 室内鞋
        { 8, 0 },   // shoes_outer(8) = 室外鞋
    };

    // ---- 2. 汁液 (Siru/Juice) ----
    // 部位参考-第一列：
    //   SiruKao=0 脸部, SiruFrontUp=1 胸部, SiruFrontDown=2 腹部,
    //   SiruBackUp=3 后背, SiruBackDown=4 屁股
    // 状态值-第二列：0=无, 1=少, 2=中, 3=多
    private static Dictionary<int, byte> _juiceLevels = new Dictionary<int, byte> {
        { 0, 0 },   // 脸部
        { 1, 1 },   // 胸部
        { 2, 1 },   // 腹部
        { 3, 1 },   // 后背
        { 4, 1 },   // 屁股
    };


    // ---- 3. 脸部效果 (原生工作室状态) ----
    // 眼泪 (Tears) 0=无, 1=少, 2=多, 3=最多
    private static byte _tearsLv = 3;
    // 脸部赤红 (Face Blush) 0.0=无, 1.0=最多
    private static float _hohoAkaRate = 0.0f;
    // 乳首凸起值 (Nipple Stand) 范围 0.0~1.0，0.35=35%
    private static float _nipStandValue = 0.5f;

    // ---- 4. 皮肤效果 (KK_SkinEffects / KKS_SkinEffects 插件) ----
    // 数值范围：0=无, 1=少, 2=多, 3=最多（部分属性可能只有0和1两级）
    private static int _sweatLevel = 3;           // 流汗 (Sweat)
    private static int _tearLevel = 3;            // 流泪 (Tear)
    private static int _droolLevel = 0;           // 流口水 (Drool)
    private static int _salivaLevel = 0;          // 口水 (Saliva)
    private static int _cumInNoseLevel = 0;       // 鼻涕 (Cum in Nose)
    private static int _blushLevel = 3;           // 身体和脸部红晕
    private static int _buttLevel = 3;            // 臀部红晕
    private static int _bukkakeLevel = 0;         // 外射 (Bukkake)
    private static int _analBukkakeLevel = 0;     // 菊部外射 (Anal Bukkake)
    private static int _bloodLevel = 0;           // 处女血 (Virgin Blood)
    private static int _pussyJuiceLevel = 3;      // 爱液 (Pussy Juice)

    // ═══════════════════════════════════════════════════════════════
    //  ★ 可配置区域结束 ★
    // ════════════════════════════════════════════════════════════════



    private const string GAME_OBJECT_NAME = "QuickSetup";

    private static GameObject _gameObject;
    private static Type _skinEffectsControllerType;
    private static Type _siruPartsType;
    private static bool _refOk;

    public static void Main() {
        DestroyOldInstances();
        _gameObject = new GameObject(GAME_OBJECT_NAME);
        _gameObject.AddComponent<SetupController>();
    }

    private static void DestroyOldInstances() {
        int n = 0;
        GameObject obj;
        while ((obj = GameObject.Find(GAME_OBJECT_NAME)) != null) {
            UnityEngine.Object.DestroyImmediate(obj);
            n++;
        }
        if (n > 0) Debug.Log("[QuickSetup] 已销毁 " + n + " 个旧实例（配置已更新）");
    }

    public static void Unload() {
        DestroyOldInstances();
        _gameObject = null;
    }

    private class SetupController : MonoBehaviour {
        void Awake() {
            DontDestroyOnLoad(this);
            InitReflection();
        }

        void Update() {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (_requireCtrl && !ctrl) return;
            if (_requireAlt && !alt) return;
            if (_requireShift && !shift) return;
            if (Input.GetKeyDown(_hotkey)) {
                Execute();
            }
        }

        private void InitReflection() {
            try {
                // 兼容 KK / KKS：尝试两个版本的 SkinEffects 程序集
                string[] asmNames = new string[] { "KK_SkinEffects", "KKS_SkinEffects" };
                for (int i = 0; i < asmNames.Length; i++) {
                    Assembly asm = null;
                    try {
                        asm = Assembly.Load(asmNames[i]);
                    } catch { }
                    if (asm == null) {
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
                            if (a.GetName().Name == asmNames[i]) { asm = a; break; }
                        }
                    }
                    if (asm != null) {
                        _skinEffectsControllerType = asm.GetType("KK_SkinEffects.SkinEffectsController");
                        if (_skinEffectsControllerType != null) {
                            Debug.Log("[QuickSetup] 找到 " + asmNames[i] + ".SkinEffectsController");
                            break;
                        }
                    }
                }
                // SiruParts 枚举类型
                _siruPartsType = typeof(ChaFileDefine).GetNestedType("SiruParts",
                    BindingFlags.Public | BindingFlags.NonPublic);
                _refOk = true;
            } catch (Exception ex) {
                Debug.LogError("[QuickSetup] 反射初始化失败: " + ex.Message);
            }
        }

        private void Execute() {
            if (!_refOk) {
                Debug.LogWarning("[QuickSetup] 反射未初始化，跳过执行");
                return;
            }

            var studio = Studio.Studio.Instance;
            if (studio == null || studio.dicInfo == null) {
                Debug.LogWarning("[QuickSetup] 未找到Studio实例");
                return;
            }

            // 获取要处理的女角色列表：选中了→只处理选中；否则全部
            var charList = new List<Studio.OCIChar>();
            string logPrefix = "全部";

            if (studio.treeNodeCtrl != null && studio.treeNodeCtrl.selectNode != null) {
                Studio.ObjectCtrlInfo ctrlInfo;
                if (studio.dicInfo.TryGetValue(studio.treeNodeCtrl.selectNode, out ctrlInfo)) {
                    var sel = ctrlInfo as Studio.OCIChar;
                    if (sel != null && sel.charInfo != null && sel is Studio.OCICharFemale) {
                        charList.Add(sel);
                        logPrefix = "选中";
                    }
                }
            }

            if (charList.Count == 0) {
                // 未选中女角色 → 全部
                charList = studio.dicInfo.Values
                    .OfType<Studio.OCIChar>()
                    .Where(x => x != null && x.charInfo != null && x is Studio.OCICharFemale)
                    .ToList();
                logPrefix = "全部";
            }

            if (charList.Count == 0) {
                Debug.LogWarning("[QuickSetup] 未找到任何女角色");
                return;
            }

            int count = 0;
            foreach (var ociChar in charList) {
                try {
                    ProcessCharacter(ociChar);
                    count++;
                } catch (Exception ex) {
                    Debug.LogError("[QuickSetup] 处理角色失败: " + ex.Message);
                }
            }

            Debug.Log("[QuickSetup] ✅ 已处理 " + logPrefix + " " + count + " 个女角色" +
                " | 泪=" + _tearsLv + " 乳=" + _nipStandValue +
                " 汗=" + _sweatLevel + " 赤=" + _blushLevel + " 尻赤=" + _buttLevel +
                " 涎=" + _droolLevel + " 唾=" + _salivaLevel +
                " 鼻=" + _cumInNoseLevel + " 外射=" + _bukkakeLevel +
                " 肛射=" + _analBukkakeLevel + " 血=" + _bloodLevel);
        }

        private void ProcessCharacter(Studio.OCIChar ociChar) {
            var chaCtrl = ociChar.charInfo;
            if (chaCtrl == null) return;
            bool isFemale = ociChar is Studio.OCICharFemale;

            // ---- 1. 服装状态 ----
            foreach (var kv in _clothesStates) {
                try {
                    ociChar.SetClothesState(kv.Key, kv.Value);
                } catch (Exception ex) {
                    Debug.LogWarning("[QuickSetup] SetClothesState(kind=" + kv.Key + ") 失败: " + ex.Message);
                }
            }

            // ---- 2. 汁液 (Siru) ----
            if (_siruPartsType != null) {
                foreach (var kv in _juiceLevels) {
                    try {
                        object siruPart = Enum.ToObject(_siruPartsType, kv.Key);
                        if (isFemale) {
                            // 女角色优先调用 OCICharFemale 的重载
                            var method = typeof(Studio.OCICharFemale).GetMethod("SetSiruFlags",
                                new[] { _siruPartsType, typeof(byte) });
                            if (method != null)
                                method.Invoke(ociChar, new[] { siruPart, kv.Value });
                        } else {
                            var method = typeof(Studio.OCIChar).GetMethod("SetSiruFlags",
                                new[] { _siruPartsType, typeof(byte) });
                            if (method != null)
                                method.Invoke(ociChar, new[] { siruPart, kv.Value });
                        }
                    } catch (Exception ex) {
                        Debug.LogWarning("[QuickSetup] SetSiruFlags(part=" + kv.Key + ") 失败: " + ex.Message);
                    }
                }
            }

            // ---- 乳首站立 (Nipple Stand) ----
            try {
                if (isFemale) {
                    var method = typeof(Studio.OCICharFemale).GetMethod("SetNipStand",
                        new[] { typeof(float) });
                    if (method != null)
                        method.Invoke(ociChar, new object[] { _nipStandValue });
                } else {
                    ociChar.SetNipStand(_nipStandValue);
                }
            } catch (Exception ex) {
                Debug.LogWarning("[QuickSetup] SetNipStand 失败: " + ex.Message);
            }

            // ---- 3. 脸部效果 ----
            // 眼泪
            try {
                ociChar.SetTearsLv(_tearsLv);
            } catch (Exception ex) {
                Debug.LogWarning("[QuickSetup] SetTearsLv 失败: " + ex.Message);
            }
            // 脸部赤红（强制设为无）
            try {
                ociChar.SetHohoAkaRate(_hohoAkaRate);
            } catch (Exception ex) {
                Debug.LogWarning("[QuickSetup] SetHohoAkaRate 失败: " + ex.Message);
            }

            // ---- 4. 皮肤效果 (KK_SkinEffects) ----
            if (_skinEffectsControllerType != null) {
                try {
                    var sec = chaCtrl.GetComponent(_skinEffectsControllerType);
                    if (sec != null) {
                        SetIntProp(sec, "SweatLevel", _sweatLevel);        
                        SetIntProp(sec, "TearLevel", _tearLevel);           
                        SetIntProp(sec, "DroolLevel", _droolLevel);         
                        SetIntProp(sec, "SalivaLevel", _salivaLevel);       
                        SetIntProp(sec, "CumInNoseLevel", _cumInNoseLevel); 
                        SetIntProp(sec, "BlushLevel", _blushLevel);        
                        SetIntProp(sec, "ButtLevel", _buttLevel);           
                        SetIntProp(sec, "BukkakeLevel", _bukkakeLevel);     
                        SetIntProp(sec, "AnalBukkakeLevel", _analBukkakeLevel); 
                        SetIntProp(sec, "BloodLevel", _bloodLevel);         
                        SetIntProp(sec, "PussyJuiceLevel", _pussyJuiceLevel); 

                        // 刷新所有皮肤纹理，使新数值立即生效
                        var updateMethod = _skinEffectsControllerType.GetMethod("UpdateAllTextures",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (updateMethod != null)
                            updateMethod.Invoke(sec, null);
                    } else {
                        Debug.LogWarning("[QuickSetup] 角色未找到 SkinEffectsController");
                    }
                } catch (Exception ex) {
                    Debug.LogWarning("[QuickSetup] 应用 SkinEffects 失败: " + ex.Message);
                }
            } else {
                Debug.Log("[QuickSetup] SkinEffects 未安装，跳过皮肤效果");
            }
        }

        private static void SetIntProp(object instance, string propName, int value) {
            var prop = _skinEffectsControllerType.GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) {
                var setter = prop.GetSetMethod();
                if (setter != null)
                    setter.Invoke(instance, new object[] { value });
            }
        }
    }
}
