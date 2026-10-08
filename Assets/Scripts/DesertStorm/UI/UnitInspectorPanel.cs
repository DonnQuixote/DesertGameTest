using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 单位信息检视面板：点击场景中的单位小球后，在屏幕下侧中央显示该单位的静态属性与实时状态。
    /// 静态共享实现（主场景与漏斗测试场景通用），由各场景 Bootstrap 在 Update 中驱动拾取、
    /// 在 OnGUI 末尾驱动绘制。支持 F 键或面板按钮控制显隐；选中单位带白色圆环标记，
    /// 点击空白处取消选中。左右功能面板与信息面板本身所在的屏幕区域点击不会穿透拾取。
    /// </summary>
    public static class UnitInspectorPanel
    {
        //面板宽度（屏幕像素）
        private const float PanelWidth = 620f;
        //面板高度（屏幕像素）
        private const float PanelHeight = 170f;
        //面板距屏幕底部的边距
        private const float BottomMargin = 14f;
        //左上角状态图标区尺寸
        private const float StatusIconSize = 26f;
        private const float StatusIconPadding = 6f;

        //当前选中的单位（再次点击空白处取消选中）
        private static UnitBase selected;
        //面板显隐开关（F 键或面板按钮切换）
        private static bool visible = true;
        //场景注入的单位列表与拾取摄像机
        private static List<UnitBase> units;
        private static Camera pickCamera;
        //选中标记（白色圆环，跟随选中单位，按其碰撞直径缩放）
        private static Transform marker;
        //圆环精灵（懒生成，跨场景复用）
        private static Sprite ringSprite;
        //UI 遮挡矩形（屏幕 GUI 空间）：左右功能面板与信息面板本身，区域内点击不参与拾取
        private static readonly List<Rect> uiBlockRects = new List<Rect>();

        //信息面板的屏幕矩形（GUI 空间，原点左上），供绘制与拾取遮挡共用
        public static Rect PanelRect =>
            new Rect((Screen.width - PanelWidth) * .5f, Screen.height - PanelHeight - BottomMargin, PanelWidth, PanelHeight);

        //场景接入：注入单位列表与摄像机，重置选中与标记（场景切换/重开测试时调用）
        public static void Attach(List<UnitBase> unitList, Camera camera)
        {
            Reset();
            units = unitList;
            pickCamera = camera;
        }

        //场景卸载清理：清空选中、遮挡区与标记，恢复默认显隐
        public static void Reset()
        {
            selected = null;
            visible = true;
            units = null;
            pickCamera = null;
            uiBlockRects.Clear();
            if (marker != null) Object.Destroy(marker.gameObject);
            marker = null;
        }

        //注册一块 UI 遮挡矩形（GUI 空间），该区域内点击不会穿透拾取场景单位
        public static void AddUiBlock(Rect rect) => uiBlockRects.Add(rect);

        //清空遮挡矩形（每帧由 Bootstrap 重新注册）
        public static void ClearUiBlocks() => uiBlockRects.Clear();

        //每帧驱动：显隐热键 → 选中失效清理/标记跟随 → 点击拾取（由 Bootstrap 在 Update 中调用）
        public static void Tick()
        {
            //F 键切换面板显隐
            if (Input.GetKeyDown(KeyCode.F)) visible = !visible;

            if (selected == null)
            {
                //选中已清空：隐藏圆环标记
                if (marker != null && marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
            }
            else
            {
                //标记跟随选中单位：位置同步 + 按碰撞直径缩放
                EnsureMarker();
                if (marker != null)
                {
                    marker.gameObject.SetActive(true);
                    marker.position = new Vector3(selected.transform.position.x, selected.transform.position.y, -.5f);
                    float diameter = selected.Data != null ? selected.Data.CollisionRadius * 2.6f : 20f;
                    marker.localScale = Vector3.one * diameter;
                }
            }

            //左键点击拾取：空格+左键是拖拽、点在 UI 遮挡区内、无摄像机时都不处理
            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.Space) && pickCamera != null && !IsPointerOverUi())
            {
                Vector3 world = pickCamera.ScreenToWorldPoint(Input.mousePosition);
                selected = PickUnit(new Vector2(world.x, world.y));
            }
        }

        //绘制面板：OnGUI 末尾调用（排在左右功能面板之后，保证盖在其上层）
        public static void DrawPanel()
        {
            if (!visible)
            {
                //面板隐藏但有选中单位时，底部中央保留一个小的显示开关
                if (selected != null && GUI.Button(new Rect((Screen.width - 200f) * .5f, Screen.height - 48f, 200f, 34f), "显示单位信息 [F]"))
                    visible = true;
                return;
            }
            Rect panelRect = PanelRect;
            GUI.Box(panelRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(panelRect.x + 14f, panelRect.y + 10f, PanelWidth - 28f, PanelHeight - 20f));
            if (selected == null)
            {
                GUILayout.Label("单位信息", HeaderStyle(17));
                GUILayout.Label("点击场景中的单位小球查看属性；点击空白处取消选中，F 键或右侧按钮控制显隐。", WrapStyle());
            }
            else if (selected.Data != null)
            {
                UnitData data = selected.Data;
                string shieldText = data.MaxShield > 0f ? $" + 盾 {data.MaxShield:0}" : string.Empty;
                //标题行：单位名（阵营色）+ 显隐按钮
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{data.DisplayName} · {data.Faction} · T{data.Tier} · {(selected.IsAirUnit ? "空军" : "陆军")}",
                    FactionHeaderStyle(data.AccentColor), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("隐藏 [F]", GUILayout.Width(80f))) visible = false;
                GUILayout.EndHorizontal();
                //两列布局：左列静态属性（带状态修正 ± 标记）、右列实时状态
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                //修正标记：属性被临时状态改变时在原值后附加 ±数值（如 55-10）
                GUILayout.Label($"生命 {data.MaxHealth:0}{shieldText} | 攻击 {FormatAttack(data)} | 护甲 {FormatArmor(data)}", LineStyle());
                GUILayout.Label($"对地射程 {data.AttackRange:0} | 对空射程 {(data.CanAttackAir ? data.AttackRangeAir.ToString("0") : "—")} | 移速 {FormatMoveSpeed(data)} | 冲击 {data.Impact:0.00} | 体积 {data.UnitVolume:0.00}", LineStyle());
                GUILayout.Label($"碰撞半径 {data.CollisionRadius:0.0} | 攻击间隔 {FormatAttackCooldown(data)} | {data.ArmorType}/{data.UnitClass} | {(data.CanAttackAir ? "可对空" : "仅对地")}", LineStyle());
                GUILayout.EndVertical();
                GUILayout.BeginVertical(GUILayout.Width(230f));
                GUILayout.Label($"当前 HP {selected.CurrentHealth:0}/{data.MaxHealth:0}{shieldText}", LineStyle());
                GUILayout.Label($"当前速度 {selected.CurrentSpeed:0.0} | 通行状态 {selected.NavigationTrafficState}", LineStyle());
                GUILayout.Label($"累计伤害 {selected.TotalDamage:0.0} | 位置 ({selected.transform.position.x:0}, {selected.transform.position.y:0})", LineStyle());
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(data.FactionFeatureSummary))
                    GUILayout.Label($"种族特性：{data.FactionFeatureSummary}", WrapStyle());
                //状态行：当前生效中的临时状态（名称 + 剩余秒数）
                if (selected.Status != null && selected.Status.Active.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var s in selected.Status.Active)
                        parts.Add($"{s.DisplayName} {s.Remaining:0.0}s");
                    GUILayout.Label($"状态：{string.Join(" | ", parts)}", StatusLineStyle());
                }
            }
            GUILayout.EndArea();

            //左上角状态小图标区（预留位）：按状态数量绘制占位方块，后续替换为真实图标贴图
            if (selected != null && selected.Data != null && selected.Status != null && selected.Status.Active.Count > 0)
            {
                for (int i = 0; i < selected.Status.Active.Count && i < 8; i++)
                {
                    var s = selected.Status.Active[i];
                    float x = panelRect.x + StatusIconPadding + i * (StatusIconSize + StatusIconPadding);
                    float y = panelRect.y + StatusIconPadding;
                    Rect iconRect = new Rect(x, y, StatusIconSize, StatusIconSize);
                    //占位图标：绿色圆角块 + 状态名首字（真实美术图标就位后替换为 GUI.DrawTexture）
                    Color old = GUI.color;
                    GUI.color = new Color(.36f, .72f, .45f, .9f);
                    GUI.Box(iconRect, GUIContent.none);
                    GUI.color = old;
                    GUI.Label(new Rect(x, y + 3f, StatusIconSize, StatusIconSize - 6f), s.DisplayName.Length > 0 ? s.DisplayName.Substring(0, 1) : "?", StatusIconStyle());
                }
            }
        }

        //攻击展示：轻/重双伤害，带状态伤害加成时附加修正
        private static string FormatAttack(UnitData data)
        {
            string light = WithMark(data.AttackLightDamage + data.StatusDamageAdd, data.StatusDamageAdd);
            string heavy = WithMark(data.AttackHeavyDamage + data.StatusDamageAdd, data.StatusDamageAdd);
            return $"轻{light}/重{heavy}";
        }

        //护甲展示：基础值 + 状态加成修正
        private static string FormatArmor(UnitData data)
        {
            return WithMark(data.EffectiveArmor, data.StatusArmorAdd);
        }

        //移速展示：含状态倍率后的实际值（变化时标注当前倍率）
        private static string FormatMoveSpeed(UnitData data)
        {
            float effective = data.EffectiveMoveSpeed;
            return data.StatusMoveSpeedMultiplier != 1f ? $"{effective:0.00}(×{data.StatusMoveSpeedMultiplier:0.00})" : $"{effective:0.00}";
        }

        //攻击间隔展示：含攻速倍率（间隔缩短时标注倍率）
        private static string FormatAttackCooldown(UnitData data)
        {
            float effective = data.EffectiveAttackCooldown;
            return data.StatusAttackSpeedMultiplier != 1f ? $"{effective:0.00}s(×{data.StatusAttackSpeedMultiplier:0.00})" : $"{effective:0.00}s";
        }

        //数值 + 修正标记：修正非零时在数值后追加 ±增量（如 "9+3"）
        private static string WithMark(float value, float mark)
        {
            string m = Mark(mark);
            return $"{value:0.0}{m}";
        }

        //修正标记文本：正数 +N、负数 -N、零为空（纯文本版，无颜色标签）
        private static string Mark(float delta)
        {
            if (delta > .01f) return $"+{delta:0.0}";
            return delta < -.01f ? $"-{Mathf.Abs(delta):0.0}" : string.Empty;
        }

        //状态行样式（淡青色，与普通属性行区分）
        private static GUIStyle StatusLineStyle()
        {
            return new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(.55f, .85f, .75f) } };
        }

        //状态图标占位文字样式（白字居中）
        private static GUIStyle StatusIconStyle()
        {
            return new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        //点击拾取：取点击点落在拾取半径内的最近单位（拾取半径略大于碰撞半径，容差更好点）
        private static UnitBase PickUnit(Vector2 worldPoint)
        {
            if (units == null) return null;
            UnitBase best = null;
            float bestDistance = float.MaxValue;
            foreach (UnitBase unit in units)
            {
                if (unit == null || unit.Data == null) continue;
                float pickRadius = unit.Data.CollisionRadius * 1.2f + 8f;
                float distance = Vector2.Distance(worldPoint, unit.transform.position);
                if (distance <= pickRadius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = unit;
                }
            }
            return best;
        }

        //鼠标是否落在任一 UI 遮挡矩形内：GUI 坐标原点在左上，Input 鼠标原点在左下，需翻转 y
        private static bool IsPointerOverUi()
        {
            Vector3 mouse = Input.mousePosition;
            Vector2 guiPoint = new Vector2(mouse.x, Screen.height - mouse.y);
            for (int i = 0; i < uiBlockRects.Count; i++)
                if (uiBlockRects[i].Contains(guiPoint)) return true;
            return false;
        }

        //确保选中标记存在：懒创建（白色空心圆环，绘制在单位上层）
        private static void EnsureMarker()
        {
            if (marker != null) return;
            if (ringSprite == null) ringSprite = CreateRingSprite(96);
            GameObject go = new GameObject("UnitSelectionMarker");
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = ringSprite;
            renderer.sortingOrder = 10;
            marker = go.transform;
        }

        //生成 size x size 的空心圆环精灵：pixelsPerUnit = size 使精灵世界尺寸为 1×1，
        //localScale 直接等于期望直径（单位碰撞直径的 2.6 倍由调用方计算）
        private static Sprite CreateRingSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float outer = size * .5f - 2f;
            float inner = size * .5f - 9f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - size * .5f + .5f;
                float dy = y - size * .5f + .5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = dist <= outer && dist >= inner ? new Color(1f, 1f, 1f, .95f) : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        }

        //大号加粗标签样式（标题，可指定颜色）
        private static GUIStyle HeaderStyle(int size)
        {
            return new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        }

        //阵营色标题样式（单位名按阵营主题色显示）
        private static GUIStyle FactionHeaderStyle(Color factionColor)
        {
            return new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, normal = { textColor = factionColor } };
        }

        //自动换行的浅色正文样式
        private static GUIStyle WrapStyle()
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(.78f, .84f, .90f) } };
        }

        //单行信息样式
        private static GUIStyle LineStyle()
        {
            return new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(.86f, .90f, .95f) } };
        }
    }
}
