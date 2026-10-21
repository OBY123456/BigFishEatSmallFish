using UnityEngine;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

[DisallowMultipleComponent]
public class FishManagerRuntimeConfig : MonoBehaviour
{
    private FishManager manager;
    private bool initialized;
    private bool panelVisible;
    private FieldInfo[] configFields;
    private readonly Dictionary<string, string> editText = new Dictionary<string, string>();
    private readonly Dictionary<string, string> originalText = new Dictionary<string, string>();
    private readonly Dictionary<string, bool> boolDraft = new Dictionary<string, bool>();
    private readonly HashSet<string> dirtyFields = new HashSet<string>();
    private Vector2 scrollPosition;
    private string searchText = "";
    private int selectedCategory;
    private string statusText = "F5 打开/隐藏配置面板";
    private float descriptionWidth = 700f;
    private Texture2D panelTexture;
    private Font guiFont;
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle descriptionStyle;
    private GUIStyle keyStyle;
    private GUIStyle statusStyle;
    private GUIStyle buttonStyle;
    private GUIStyle inputStyle;
    private GUIStyle toggleStyle;
    private GUIStyle rowStyle;
    private readonly string[] categories = { "全部", "事件", "NPC/特殊鱼", "道具", "漩涡", "玩家/移动", "边界/相机", "资源" };

    public void Initialize(FishManager target)
    {
        if (target == null) return;
        if (initialized && manager == target) return;
        manager = target;
        initialized = true;
        LoadOrCreateConfig();
    }

    private void Awake()
    {
        FishManager found = GetComponent<FishManager>();
        if (found != null) Initialize(found);
    }

    private void Update()
    {
        if (manager == null) manager = GetComponent<FishManager>();
        if (manager == null || !manager.isActiveAndEnabled) return;
        if (!initialized) Initialize(manager);
        if (Input.GetKeyDown(KeyCode.F5)) panelVisible = !panelVisible;
    }

    private void OnDestroy()
    {
        if (panelTexture != null) Destroy(panelTexture);
        if (guiFont != null) Destroy(guiFont);
    }

    private void InitializeConfigFields()
    {
        if (configFields != null || manager == null) return;
        FieldInfo[] source = typeof(FishManager).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        List<FieldInfo> visible = new List<FieldInfo>();
        for (int i = 0; i < source.Length; i++)
        {
            if (Attribute.IsDefined(source[i], typeof(HideInInspector))) continue;
            visible.Add(source[i]);
        }
        visible.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
        configFields = visible.ToArray();
    }

    private static bool IsEditableType(Type type)
    {
        return type == typeof(float) || type == typeof(int) || type == typeof(bool) ||
            type == typeof(KeyCode) || type == typeof(Vector3);
    }

    private static string GetFieldDescription(FieldInfo field)
    {
        HeaderAttribute header = Attribute.GetCustomAttribute(field, typeof(HeaderAttribute)) as HeaderAttribute;
        TooltipAttribute tooltip = Attribute.GetCustomAttribute(field, typeof(TooltipAttribute)) as TooltipAttribute;
        string description = header != null ? header.header : field.Name;
        if (tooltip != null && !string.IsNullOrEmpty(tooltip.tooltip)) description += "（" + tooltip.tooltip + "）";
        return description;
    }

    // 输入框统一显示两位小数；底层float和保存的JSON仍保留实际精度。
    private static string FormatFloat(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string FormatValue(object value, Type type)
    {
        if (type == typeof(float)) return FormatFloat((float)value);
        if (type == typeof(int)) return ((int)value).ToString(CultureInfo.InvariantCulture);
        return value != null ? value.ToString() : "";
    }

    private void RebuildEditTexts()
    {
        InitializeConfigFields();
        editText.Clear();
        originalText.Clear();
        boolDraft.Clear();
        dirtyFields.Clear();
        if (manager == null || configFields == null) return;
        for (int i = 0; i < configFields.Length; i++)
        {
            FieldInfo field = configFields[i];
            if (!IsEditableType(field.FieldType)) continue;
            object value = field.GetValue(manager);
            if (field.FieldType == typeof(bool))
            {
                boolDraft[field.Name] = (bool)value;
            }
            else if (field.FieldType == typeof(Vector3))
            {
                Vector3 vector = (Vector3)value;
                editText[field.Name + ".x"] = FormatFloat(vector.x);
                editText[field.Name + ".y"] = FormatFloat(vector.y);
                editText[field.Name + ".z"] = FormatFloat(vector.z);
            }
            else editText[field.Name] = FormatValue(value, field.FieldType);
        }
        foreach (KeyValuePair<string, string> pair in editText) originalText[pair.Key] = pair.Value;
    }

    private static bool TryParseFloat(string source, out float value)
    {
        bool parsed = float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            float.TryParse(source, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static object ClampFieldValue(FieldInfo field, object value)
    {
        if (field.FieldType != typeof(float) && field.FieldType != typeof(int)) return value;
        float number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
        RangeAttribute range = Attribute.GetCustomAttribute(field, typeof(RangeAttribute)) as RangeAttribute;
        MinAttribute minimum = Attribute.GetCustomAttribute(field, typeof(MinAttribute)) as MinAttribute;
        if (range != null) number = Mathf.Clamp(number, range.min, range.max);
        if (minimum != null) number = Mathf.Max(number, minimum.min);
        return field.FieldType == typeof(int) ? (object)Mathf.RoundToInt(number) : number;
    }

    private static bool TryReadJsonValue(FieldInfo field, JToken token, out object value)
    {
        value = null;
        if (token == null) return false;
        Type type = field.FieldType;
        if (type == typeof(bool))
        {
            bool parsed;
            if (!bool.TryParse(token.ToString(), out parsed)) return false;
            value = parsed;
            return true;
        }
        if (type == typeof(int))
        {
            int parsed;
            if (!int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return false;
            value = ClampFieldValue(field, parsed);
            return true;
        }
        if (type == typeof(float))
        {
            float parsed;
            if (!TryParseFloat(token.ToString(), out parsed)) return false;
            value = ClampFieldValue(field, parsed);
            return true;
        }
        if (type == typeof(KeyCode))
        {
            try { value = (KeyCode)Enum.Parse(typeof(KeyCode), token.ToString(), true); return true; }
            catch { return false; }
        }
        if (type == typeof(Vector3))
        {
            JObject vector = token as JObject;
            if (vector == null) return false;
            float x, y, z;
            if (!TryParseFloat(vector["x"] != null ? vector["x"].ToString() : "", out x) ||
                !TryParseFloat(vector["y"] != null ? vector["y"].ToString() : "", out y) ||
                !TryParseFloat(vector["z"] != null ? vector["z"].ToString() : "", out z)) return false;
            value = new Vector3(x, y, z);
            return true;
        }
        return false;
    }

    private JToken WriteJsonValue(FieldInfo field)
    {
        object value = field.GetValue(manager);
        if (field.FieldType == typeof(Vector3))
        {
            Vector3 vector = (Vector3)value;
            return new JObject { ["x"] = vector.x, ["y"] = vector.y, ["z"] = vector.z };
        }
        if (field.FieldType == typeof(KeyCode)) return new JValue(value.ToString());
        return JToken.FromObject(value);
    }

    private string GetConfigPath()
    {
        return Path.Combine(Application.streamingAssetsPath, "FishManagerConfig.txt");
    }

    private string GetLegacyJsonPath()
    {
        return Path.Combine(Application.streamingAssetsPath, "FishManagerConfig.json");
    }

    private void LoadOrCreateConfig()
    {
        InitializeConfigFields();
        string currentPath = GetConfigPath();
        if (File.Exists(currentPath))
        {
            LoadConfigFromPath(currentPath);
        }
        else if (File.Exists(GetLegacyJsonPath()))
        {
            // 旧文件只改扩展名；内容仍按原有JSON结构读取，再写入新的.txt文件。
            if (LoadConfigFromPath(GetLegacyJsonPath()))
            {
                if (SaveConfig()) statusText = "已从旧JSON迁移到TXT（内容仍为JSON）；旧文件保留不删除。";
            }
            else SaveConfig();
        }
        else SaveConfig();
        RebuildEditTexts();
    }

    private bool SaveConfig()
    {
        try
        {
            InitializeConfigFields();
            JArray parameters = new JArray();
            for (int i = 0; i < configFields.Length; i++)
            {
                FieldInfo field = configFields[i];
                if (!IsEditableType(field.FieldType)) continue;
                JObject parameter = new JObject();
                parameter["name"] = field.Name;
                parameter["description"] = GetFieldDescription(field);
                parameter["type"] = field.FieldType.Name;
                parameter["value"] = WriteJsonValue(field);
                parameters.Add(parameter);
            }
            JObject document = new JObject();
            document["version"] = 1;
            document["note"] = "文件扩展名为.txt，内容仍是JSON；修改value即可。Prefab、数组、Transform、Camera引用只在Inspector绑定。";
            document["parameters"] = parameters;
            string path = GetConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, document.ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
            statusText = "配置已保存（JSON内容）：" + path;
            Debug.Log("[FishManagerRuntimeConfig] " + statusText);
            return true;
        }
        catch (Exception ex)
        {
            statusText = "保存TXT配置失败：" + ex.Message;
            Debug.LogError("[FishManagerRuntimeConfig] " + statusText);
            return false;
        }
    }

    private bool LoadConfigFromPath(string path)
    {
        try
        {
            JObject document = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            JArray entries = document["parameters"] as JArray;
            if (entries == null) throw new Exception("JSON缺少parameters数组");
            InitializeConfigFields();
            Dictionary<string, FieldInfo> lookup = new Dictionary<string, FieldInfo>();
            for (int i = 0; i < configFields.Length; i++)
                if (IsEditableType(configFields[i].FieldType)) lookup[configFields[i].Name] = configFields[i];

            int loaded = 0;
            int rejected = 0;
            foreach (JToken entry in entries)
            {
                if (!(entry is JObject)) { rejected++; continue; }
                string name = entry["name"] != null ? entry["name"].ToString() : "";
                FieldInfo field;
                if (!lookup.TryGetValue(name, out field)) continue;
                object value;
                if (!TryReadJsonValue(field, entry["value"], out value))
                {
                    rejected++;
                    Debug.LogWarning("[FishManagerRuntimeConfig] 配置值无效，已忽略：" + name);
                    continue;
                }
                field.SetValue(manager, value);
                loaded++;
            }
            RebuildEditTexts();
            manager.NotifyRuntimeConfigApplied();
            statusText = "加载配置完成：" + loaded + "项" + (rejected > 0 ? "；忽略无效项" + rejected : "");
            Debug.Log("[FishManagerRuntimeConfig] " + statusText + "，文件=" + path);
            return true;
        }
        catch (Exception ex)
        {
            statusText = "读取配置失败，继续使用当前Inspector参数：" + ex.Message;
            Debug.LogError("[FishManagerRuntimeConfig] " + statusText);
            return false;
        }
    }

    private bool ApplyEdits()
    {
        InitializeConfigFields();
        Dictionary<FieldInfo, object> changes = new Dictionary<FieldInfo, object>();
        List<string> invalid = new List<string>();
        for (int i = 0; i < configFields.Length; i++)
        {
            FieldInfo field = configFields[i];
            if (!IsEditableType(field.FieldType)) continue;
            if (field.FieldType == typeof(bool))
            {
                if (dirtyFields.Contains(field.Name) && boolDraft.ContainsKey(field.Name))
                    changes[field] = boolDraft[field.Name];
                continue;
            }
            if (field.FieldType == typeof(Vector3))
            {
                Vector3 current = (Vector3)field.GetValue(manager);
                bool changed = false;
                float component;
                string xKey = field.Name + ".x";
                string yKey = field.Name + ".y";
                string zKey = field.Name + ".z";
                if (dirtyFields.Contains(xKey))
                {
                    if (TryParseFloat(GetEditText(xKey), out component)) { current.x = component; changed = true; }
                    else invalid.Add(xKey);
                }
                if (dirtyFields.Contains(yKey))
                {
                    if (TryParseFloat(GetEditText(yKey), out component)) { current.y = component; changed = true; }
                    else invalid.Add(yKey);
                }
                if (dirtyFields.Contains(zKey))
                {
                    if (TryParseFloat(GetEditText(zKey), out component)) { current.z = component; changed = true; }
                    else invalid.Add(zKey);
                }
                if (changed) changes[field] = current;
                continue;
            }
            if (!dirtyFields.Contains(field.Name)) continue;
            string input = GetEditText(field.Name);
            object value = null;
            if (field.FieldType == typeof(float))
            {
                float parsed;
                if (TryParseFloat(input, out parsed)) value = ClampFieldValue(field, parsed);
            }
            else if (field.FieldType == typeof(int))
            {
                int parsed;
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) value = ClampFieldValue(field, parsed);
            }
            else if (field.FieldType == typeof(KeyCode))
            {
                try { value = (KeyCode)Enum.Parse(typeof(KeyCode), input, true); }
                catch { }
            }
            if (value == null) invalid.Add(field.Name);
            else changes[field] = value;
        }

        if (invalid.Count > 0)
        {
            statusText = "参数格式有误，请检查：" + string.Join("、", invalid.ToArray());
            return false;
        }
        foreach (KeyValuePair<FieldInfo, object> change in changes) change.Key.SetValue(manager, change.Value);
        manager.NotifyRuntimeConfigApplied();
        RebuildEditTexts();
        statusText = changes.Count > 0 ? "已应用 " + changes.Count + " 项修改。" : "没有待应用的输入；当前参数未改变。";
        return true;
    }

    private string GetEditText(string key)
    {
        string value;
        return editText.TryGetValue(key, out value) ? value : "";
    }

    private void DrawTextInput(string key, params GUILayoutOption[] options)
    {
        string before = GetEditText(key);
        string after = GUILayout.TextField(before, inputStyle, options);
        if (after != before)
        {
            editText[key] = after;
            string original;
            if (originalText.TryGetValue(key, out original) && after == original) dirtyFields.Remove(key);
            else dirtyFields.Add(key);
        }
    }

    private void InitializeStyles()
    {
        if (panelStyle != null) return;
        panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        panelTexture.SetPixel(0, 0, new Color(0.055f, 0.085f, 0.15f, 0.95f));
        panelTexture.Apply();
        try { guiFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16); }
        catch { guiFont = null; }

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = panelTexture;
        panelStyle.padding = new RectOffset(12, 12, 12, 12);
        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 18;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;
        descriptionStyle = new GUIStyle(GUI.skin.label);
        descriptionStyle.fontSize = 14;
        descriptionStyle.wordWrap = true;
        descriptionStyle.alignment = TextAnchor.UpperLeft;
        descriptionStyle.padding = new RectOffset(0, 0, 2, 3);
        descriptionStyle.normal.textColor = new Color(0.92f, 0.95f, 1f);
        keyStyle = new GUIStyle(GUI.skin.label);
        keyStyle.fontSize = 12;
        keyStyle.wordWrap = true;
        keyStyle.alignment = TextAnchor.UpperLeft;
        keyStyle.normal.textColor = new Color(0.58f, 0.80f, 0.95f);
        statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = 12;
        statusStyle.wordWrap = true;
        statusStyle.normal.textColor = new Color(0.98f, 0.90f, 0.55f);
        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;
        buttonStyle.wordWrap = true;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        buttonStyle.padding = new RectOffset(6, 6, 4, 4);
        inputStyle = new GUIStyle(GUI.skin.textField);
        toggleStyle = new GUIStyle(GUI.skin.toggle);
        rowStyle = new GUIStyle(GUI.skin.box);
        rowStyle.padding = new RectOffset(8, 8, 6, 6);
        rowStyle.stretchWidth = true;
        if (guiFont != null)
        {
            titleStyle.font = guiFont;
            descriptionStyle.font = guiFont;
            keyStyle.font = guiFont;
            statusStyle.font = guiFont;
            panelStyle.font = guiFont;
            buttonStyle.font = guiFont;
            inputStyle.font = guiFont;
            toggleStyle.font = guiFont;
            rowStyle.font = guiFont;
        }
    }

    private float GetLabelHeight(GUIStyle style, string text, float width)
    {
        float safeWidth = Mathf.Max(80f, width);
        return Mathf.Max(style.lineHeight + 2f, style.CalcHeight(new GUIContent(text), safeWidth) + 5f);
    }

    private void DrawConfigEditor(FieldInfo field, float availableWidth)
    {
        Type type = field.FieldType;
        float safeWidth = Mathf.Max(90f, availableWidth);
        if (type == typeof(bool))
        {
            bool before = boolDraft.ContainsKey(field.Name) ? boolDraft[field.Name] : (bool)field.GetValue(manager);
            bool after = GUILayout.Toggle(before, before ? "开启" : "关闭", toggleStyle,
                GUILayout.Width(Mathf.Min(130f, safeWidth)), GUILayout.MinHeight(27f));
            if (after != before)
            {
                boolDraft[field.Name] = after;
                dirtyFields.Add(field.Name);
            }
        }
        else if (type == typeof(Vector3))
        {
            if (availableWidth < 260f)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("X", keyStyle, GUILayout.Width(18f));
                DrawTextInput(field.Name + ".x", GUILayout.ExpandWidth(true), GUILayout.MinHeight(27f));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Y", keyStyle, GUILayout.Width(18f));
                DrawTextInput(field.Name + ".y", GUILayout.ExpandWidth(true), GUILayout.MinHeight(27f));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Z", keyStyle, GUILayout.Width(18f));
                DrawTextInput(field.Name + ".z", GUILayout.ExpandWidth(true), GUILayout.MinHeight(27f));
                GUILayout.EndHorizontal();
            }
            else
            {
                float axisWidth = Mathf.Clamp((availableWidth - 65f) / 3f, 48f, 110f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("X", keyStyle, GUILayout.Width(12f));
                DrawTextInput(field.Name + ".x", GUILayout.Width(axisWidth), GUILayout.MinHeight(27f));
                GUILayout.Label("Y", keyStyle, GUILayout.Width(12f));
                DrawTextInput(field.Name + ".y", GUILayout.Width(axisWidth), GUILayout.MinHeight(27f));
                GUILayout.Label("Z", keyStyle, GUILayout.Width(12f));
                DrawTextInput(field.Name + ".z", GUILayout.Width(axisWidth), GUILayout.MinHeight(27f));
                GUILayout.EndHorizontal();
            }
        }
        else if (IsEditableType(type))
        {
            float fieldWidth = Mathf.Min(220f, safeWidth);
            DrawTextInput(field.Name, GUILayout.Width(fieldWidth), GUILayout.MinHeight(27f));
        }
    }

    private void DrawConfigField(FieldInfo field)
    {
        string description = GetFieldDescription(field);
        Type type = field.FieldType;
        float width = Mathf.Max(120f, descriptionWidth);
        float height = GetLabelHeight(descriptionStyle, description, width);
        GUILayout.BeginVertical(rowStyle, GUILayout.ExpandWidth(true));
        GUILayout.Label(description, descriptionStyle, GUILayout.Width(width), GUILayout.MinHeight(height));
        GUILayout.Space(3f);

        if (!IsEditableType(type))
        {
            object value = field.GetValue(manager);
            string info = value == null ? "未配置" : value.ToString();
            Array array = value as Array;
            if (array != null)
            {
                List<string> assigned = new List<string>();
                for (int i = 0; i < array.Length; i++)
                {
                    UnityEngine.Object asset = array.GetValue(i) as UnityEngine.Object;
                    assigned.Add(i + ":" + (asset != null ? asset.name : "空"));
                }
                info = string.Join("  ", assigned.ToArray());
            }
            GUILayout.Label(field.Name, keyStyle, GUILayout.Width(width), GUILayout.MinHeight(GetLabelHeight(keyStyle, field.Name, width)));
            string referenceText = "Inspector引用（只读）:" + info;
            GUILayout.Label(referenceText, keyStyle, GUILayout.Width(width), GUILayout.MinHeight(GetLabelHeight(keyStyle, referenceText, width)));
        }
        else if (width < 620f)
        {
            GUILayout.Label(field.Name, keyStyle, GUILayout.Width(width), GUILayout.MinHeight(GetLabelHeight(keyStyle, field.Name, width)));
            DrawConfigEditor(field, width);
        }
        else
        {
            GUILayout.BeginHorizontal();
            float keyWidth = Mathf.Min(320f, width * 0.48f);
            GUILayout.Label(field.Name, keyStyle, GUILayout.Width(keyWidth), GUILayout.MinHeight(GetLabelHeight(keyStyle, field.Name, keyWidth)));
            GUILayout.FlexibleSpace();
            DrawConfigEditor(field, width - keyWidth - 12f);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    private bool MatchesCategory(FieldInfo field)
    {
        if (selectedCategory == 0) return true;
        string lower = field.Name.ToLowerInvariant();
        if (selectedCategory == 7) return !IsEditableType(field.FieldType);
        if (selectedCategory == 1) return lower.Contains("event") || lower.Contains("madness") ||
            lower.Contains("aurora") || lower.Contains("darkness") || lower.Contains("refreshmultiplier") || lower == "firstspecialeventtype";
        if (selectedCategory == 2) return lower.Contains("npc") || lower.Contains("specialfish") || lower.Contains("shark") || lower.Contains("lightning") || lower.Contains("giant");
        if (selectedCategory == 3) return lower.Contains("item") || lower.Contains("shield");
        if (selectedCategory == 4) return lower.Contains("vortex");
        if (selectedCategory == 5) return lower.Contains("player") || lower.Contains("boost") || lower.Contains("levelspeed") || lower.Contains("globalbasespeed") || lower.Contains("keyboard");
        if (selectedCategory == 6) return lower.Contains("boundary") || lower.Contains("bounds") || lower.Contains("camera") || lower.Contains("fishplane") || lower.Contains("root") || lower.Contains("padding") || lower.Contains("debugpanelscale");
        return true;
    }

    private void DrawCategoryRow(int startIndex, int count)
    {
        GUILayout.BeginHorizontal();
        for (int i = startIndex; i < startIndex + count && i < categories.Length; i++)
        {
            int categoryIndex = i;
            if (GUILayout.Button(categories[i], buttonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(32f)))
                selectedCategory = categoryIndex;
        }
        GUILayout.EndHorizontal();
    }

    private void OnGUI()
    {
        if (!panelVisible || manager == null || !manager.isActiveAndEnabled) return;
        InitializeConfigFields();
        InitializeStyles();

        float autoScale = Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 540f), 1f, 2.4f);
        float scale = Mathf.Clamp(autoScale * Mathf.Clamp(manager.debugPanelScale, 0.5f, 3f), 0.75f, 2.4f);
        Matrix4x4 oldMatrix = GUI.matrix;
        int oldDepth = GUI.depth;
        GUI.depth = -1000;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Mathf.Max(200f, Screen.width / scale - 20f);
        float height = Mathf.Max(170f, Screen.height / scale - 20f);
        Rect panelRect = new Rect(10f, 10f, Mathf.Min(1080f, width), height);
        descriptionWidth = Mathf.Max(120f, panelRect.width - 76f);
        GUILayout.BeginArea(panelRect, panelStyle);

        GUILayout.Label("大鱼吃小鱼 | 运行时调参（F5关闭）", titleStyle);
        GUILayout.Label(manager.GetRuntimeStatusText(), descriptionStyle);

        // 操作按钮分行并自动分配宽度，避免固定宽度导致中文按钮文字被裁切。
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("应用修改", buttonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f))) ApplyEdits();
        if (GUILayout.Button("应用并保存TXT", buttonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f)))
            if (ApplyEdits()) SaveConfig();
        if (GUILayout.Button("从TXT重新加载", buttonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f)))
            LoadConfigFromPath(GetConfigPath());
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("撤销未应用输入", buttonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f))) RebuildEditTexts();
        if (GUILayout.Button("立即测试下一事件", buttonStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(36f))) manager.RequestRuntimeNextEvent();
        GUILayout.EndHorizontal();

        GUILayout.Label(statusText, statusStyle);
        GUILayout.Label("TXT配置位置（文件内容仍为JSON）: " + GetConfigPath(), keyStyle);
        GUILayout.Label("数值/开关/按键/Vector3可保存；Prefab、数组、Transform、Camera请在Inspector设置。", keyStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("查找：", descriptionStyle, GUILayout.Width(46f));
        searchText = GUILayout.TextField(searchText, inputStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(27f));
        GUILayout.EndHorizontal();

        // 分类按钮拆成两行，每行最多4个，确保长标签有足够空间。
        DrawCategoryRow(0, 4);
        DrawCategoryRow(4, 4);

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        for (int i = 0; i < configFields.Length; i++)
        {
            FieldInfo field = configFields[i];
            string description = GetFieldDescription(field);
            if (!MatchesCategory(field)) continue;
            if (!string.IsNullOrEmpty(searchText) &&
                field.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                description.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0) continue;
            DrawConfigField(field);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.matrix = oldMatrix;
        GUI.depth = oldDepth;
    }
}
