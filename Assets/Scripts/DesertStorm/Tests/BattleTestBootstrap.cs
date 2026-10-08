using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 双阵营交火测试场景：左翼（蓝方）与右翼（红方）各有一个生成面板，
    /// 两侧均可选择种族（人族/虫族/神族）与该种族下的单位，部队从各自营地出发，
    /// 自动向对方推进，进入射程后与真实敌方单位交火（按护甲类型结算轻/重伤）。
    /// 复用 UnitInspectorPanel：点击场景中的单位查看属性（含状态修正 ± 标记与状态图标位）。
    /// 单位阵亡后自动移除统计；提供清空与重开按钮。
    /// </summary>
    public sealed class BattleTestBootstrap : MonoBehaviour
    {
        //世界尺寸（与主场景一致）
        private const float WorldWidth = 2400f;
        private const float WorldHeight = 1400f;
        //左右营地锚点（部队出生位置中心）
        private static readonly Vector2 BlueAnchor = new Vector2(220f, 700f);
        private static readonly Vector2 RedAnchor = new Vector2(2180f, 700f);
        //可选种族（与单位表 Sheet/字段一致）
        private static readonly string[] Factions = { "人族", "虫族", "神族" };

        //导航系统
        private CdtNavigationSystem navigation;
        //场上所有单位（双方混合）
        private readonly List<UnitBase> units = new List<UnitBase>();
        //单位数据目录
        private Dictionary<string, UnitData> catalog;
        //程序生成的圆形/方形精灵
        private Sprite circleSprite;
        private Sprite squareSprite;
        //世界摄像机
        private Camera sceneCamera;
        //单位对象池：阵亡/清空的单位回收复用，避免反复内存申请
        private UnitObjectPool pool;

        //左右阵营状态：种族索引 / 单位索引（按种族分别记忆，切换种族时恢复）
        private int blueFactionIndex;
        private int redFactionIndex;
        private readonly int[] blueUnitIndices = new int[3];
        private readonly int[] redUnitIndices = new int[3];
        //每次生成数量
        private int spawnCount = 5;
        //测试时长
        private float elapsed;
        //面板显隐
        private bool bluePanelVisible = true;
        private bool redPanelVisible = true;
        //UI 缩放
        private float uiScale = 1f;

        //启动：加载目录、生成精灵、搭建世界、摄像机、接入检视面板
        private void Start()
        {
            catalog = UnitCatalog.AllUnits();
            UnitBase.SoftBodyCrowd = true;
            circleSprite = CreateCircleSprite(64);
            squareSprite = CreateSquareSprite();
            CreateWorld();
            CreateCamera();
            //对象池：挂在场景根（本引导物体）下
            pool = new UnitObjectPool(transform, circleSprite);
            UnitInspectorPanel.Attach(units, sceneCamera);
        }

        //每帧：计时、清理阵亡单位、驱动检视面板
        private void Update()
        {
            elapsed += Time.deltaTime;
            CleanupDead();
            UnitInspectorPanel.ClearUiBlocks();
            if (bluePanelVisible) UnitInspectorPanel.AddUiBlock(new Rect(18f, 18f, 360f, 460f));
            if (redPanelVisible) UnitInspectorPanel.AddUiBlock(new Rect(Screen.width - 18f - 360f * uiScale, 18f, 360f, 460f));
            UnitInspectorPanel.AddUiBlock(UnitInspectorPanel.PanelRect);
            UnitInspectorPanel.Tick();
        }

        //阵亡单位处理：从列表移除并归还对象池（GameObject 保留待复用）
        private void CleanupDead()
        {
            for (int i = units.Count - 1; i >= 0; i--)
            {
                //健康值归零 = 阵亡（对象池模式下 GameObject 仍存活，不能靠 Unity null 判断）
                if (units[i] != null && units[i].CurrentHealth > 0f) continue;
                if (units[i] != null) pool.Release(units[i]);
                units.RemoveAt(i);
            }
        }

        //搭建世界：背景、边界导航、中线两侧小障碍、双方营地标记
        private void CreateWorld()
        {
            var boundary = new Rect(0f, 0f, WorldWidth, WorldHeight);
            //少量中立障碍：交火路线上留两块掩体，增加绕行趣味
            var obstacles = new List<Rect>
            {
                new Rect(1150f, 380f, 100f, 260f),
                new Rect(1150f, 760f, 100f, 260f)
            };

            GameObject background = CreateSpriteObject("Battle_World", squareSprite, new Vector2(WorldWidth * .5f, WorldHeight * .5f), new Vector2(WorldWidth, WorldHeight), Hex("#1b2a33"), 1f);
            background.transform.position = new Vector3(background.transform.position.x, background.transform.position.y, 2f);

            navigation = new GameObject("CDTNavigationSystem").AddComponent<CdtNavigationSystem>();
            navigation.Configure(boundary, obstacles);
            foreach (Rect obstacle in obstacles)
            {
                GameObject wall = CreateSpriteObject("Obstacle", squareSprite, obstacle.center, obstacle.size, Hex("#3d4a52"), .5f);
                var body = wall.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                var collider = wall.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
            }

            //双方营地标记（蓝/红半透明圆）
            CreateSpriteObject("BlueCamp", circleSprite, BlueAnchor, new Vector2(360f, 360f), new Color(.39f, .65f, .91f, .25f), -1.5f);
            CreateSpriteObject("RedCamp", circleSprite, RedAnchor, new Vector2(360f, 360f), new Color(.89f, .38f, .35f, .25f), -1.5f);
        }

        //创建正交摄像机并挂接平移控制器
        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("BattleCamera");
            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.orthographic = true;
            sceneCamera.backgroundColor = Hex("#15202a");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(1200f, 700f, -10f);
            //正交视野覆盖约一半战场高度（单位更清晰），可平移查看全局
            sceneCamera.orthographicSize = 750f;
            sceneCamera.aspect = WorldWidth / WorldHeight;
            var controller = cameraObject.AddComponent<CameraPanController>();
            controller.Initialize(sceneCamera, new Rect(0f, 0f, WorldWidth, WorldHeight));
        }

        //当前某阵营可选单位 ID 列表（按种族过滤，按 ID 排序保证索引稳定）
        private List<string> UnitIdsFor(string faction)
        {
            return catalog.Values.Where(u => u.Faction == faction).Select(u => u.UnitId).OrderBy(id => id).ToList();
        }

        //生成一支蓝方部队
        private void SpawnBlue() => SpawnSquad(true);

        //生成一支红方部队
        private void SpawnRed() => SpawnSquad(false);

        //生成一支部队：按阵营当前选择的种族与单位，排成纵列从营地出发
        private void SpawnSquad(bool blue)
        {
            if (catalog == null || catalog.Count == 0) return;
            string faction = Factions[blue ? blueFactionIndex : redFactionIndex];
            var ids = UnitIdsFor(faction);
            if (ids.Count == 0) return;
            int sideIndex = blue ? blueFactionIndex : redFactionIndex;
            int[] indices = blue ? blueUnitIndices : redUnitIndices;
            int index = Mathf.Clamp(indices[sideIndex], 0, ids.Count - 1);
            UnitData data = catalog[ids[index]];
            Vector2 anchor = blue ? BlueAnchor : RedAnchor;
            for (int i = 0; i < spawnCount; i++)
            {
                //纵列排布：向后错开，横向轻微交错
                Vector2 offset = new Vector2((i % 2 == 0 ? -1 : 1) * 30f, -(i / 2) * 52f);
                SpawnUnit(data, anchor + offset, blue);
            }
            //双方部队都在场时刷新敌我注册
            RefreshEnemyRegistry();
        }

        //实例化一个单位：目标为敌方营地（进入射程后与真实敌方单位交火）。
        //走对象池：复用同 ID 空闲单位，无空闲才真正新建
        private void SpawnUnit(UnitData data, Vector2 position, bool blue)
        {
            UnitBase unit = pool.Acquire(data, position, navigation, data.Domain == "air");
            //推进目标：对方营地（交火由敌对注册表驱动，进射程即停火）
            unit.SetGoalPoint(blue ? RedAnchor : BlueAnchor, 60f);
            //队伍归属在生成时即确定（blue/red），与种族无关——同种族对战也分属两队
            unit.SetEnemyUnits(null, blue ? "blue" : "red");
            units.Add(unit);
        }

        //刷新全场敌我注册：每个单位拿到的敌方列表 = 对方队伍的全部存活单位。
        //归属以生成时写入的 TeamId（blue/red）为权威，位置只用于兜底
        private void RefreshEnemyRegistry()
        {
            var blueUnits = new List<UnitBase>();
            var redUnits = new List<UnitBase>();
            foreach (UnitBase unit in units)
            {
                //池模式下阵亡单位在 CleanupDead 前可能仍挂列表：健康值过滤
                if (unit == null || unit.CurrentHealth <= 0f) continue;
                if (IsBlueSide(unit)) blueUnits.Add(unit); else redUnits.Add(unit);
            }
            foreach (UnitBase unit in units)
            {
                if (unit == null || unit.CurrentHealth <= 0f) continue;
                bool isBlueSide = IsBlueSide(unit);
                var enemies = isBlueSide ? redUnits : blueUnits;
                unit.SetEnemyUnits(enemies.Where(e => e != null && e != unit), isBlueSide ? "blue" : "red");
            }
        }

        //UI 总入口：绘制左右两块生成面板与中央统计
        private void OnGUI()
        {
            if (catalog == null)
            {
                GUI.Label(new Rect(24f, 24f, 320f, 32f), "正在加载单位数据...");
                return;
            }
            DrawBluePanel();
            DrawRedPanel();
            DrawCenterStats();
            UnitInspectorPanel.DrawPanel();
        }

        //蓝方面板（左上角）：种族选择 + 单位选择 + 生成
        private void DrawBluePanel()
        {
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(18f, 18f, 0f), Quaternion.identity, Vector3.one * uiScale);
            if (bluePanelVisible)
            {
                GUI.Box(new Rect(0f, 0f, 340f, 400f), GUIContent.none);
                GUILayout.BeginArea(new Rect(14f, 12f, 312f, 376f));
                GUILayout.Label("蓝方", HeaderStyle(new Color(.55f, .78f, .98f)));
                GUILayout.Label("部队从左侧营地生成，自动向红方推进交火。", WrapStyle());
                GUILayout.Label("种族");
                int newFaction = GUILayout.SelectionGrid(blueFactionIndex, Factions, 3);
                if (newFaction != blueFactionIndex) { blueFactionIndex = newFaction; RefreshEnemyRegistry(); }
                var ids = UnitIdsFor(Factions[blueFactionIndex]);
                string[] names = ids.Select(id => catalog[id].DisplayName).ToArray();
                blueUnitIndices[blueFactionIndex] = Mathf.Clamp(blueUnitIndices[blueFactionIndex], 0, Mathf.Max(0, names.Length - 1));
                GUILayout.Label("出兵单位");
                blueUnitIndices[blueFactionIndex] = GUILayout.SelectionGrid(blueUnitIndices[blueFactionIndex], names, 2);
                if (names.Length > 0) GUILayout.Label(catalog[ids[blueUnitIndices[blueFactionIndex]]].ShortDescription(), WrapStyle());
                GUILayout.Label($"每次生成数量：{spawnCount}");
                spawnCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(spawnCount, 1f, 20f));
                if (GUILayout.Button("生成蓝方部队")) SpawnBlue();
                if (GUILayout.Button("清空蓝方")) ClearSide(true);
                GUILayout.EndArea();
            }
            if (GUI.Button(new Rect(0f, 408f, 340f, 34f), bluePanelVisible ? "隐藏蓝方面板" : "显示蓝方面板")) bluePanelVisible = !bluePanelVisible;
            GUI.matrix = old;
        }        //红方面板（右上角）：同蓝方结构
        private void DrawRedPanel()
        {
            float x = Screen.width - 18f - 340f * uiScale;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(x, 18f, 0f), Quaternion.identity, Vector3.one * uiScale);
            if (redPanelVisible)
            {
                GUI.Box(new Rect(0f, 0f, 340f, 400f), GUIContent.none);
                GUILayout.BeginArea(new Rect(14f, 12f, 312f, 376f));
                GUILayout.Label("红方", HeaderStyle(new Color(.98f, .62f, .58f)));
                GUILayout.Label("部队从右侧营地生成，自动向蓝方推进交火。", WrapStyle());
                GUILayout.Label("种族");
                int newFaction = GUILayout.SelectionGrid(redFactionIndex, Factions, 3);
                if (newFaction != redFactionIndex) { redFactionIndex = newFaction; RefreshEnemyRegistry(); }
                var ids = UnitIdsFor(Factions[redFactionIndex]);
                string[] names = ids.Select(id => catalog[id].DisplayName).ToArray();
                redUnitIndices[redFactionIndex] = Mathf.Clamp(redUnitIndices[redFactionIndex], 0, Mathf.Max(0, names.Length - 1));
                GUILayout.Label("出兵单位");
                redUnitIndices[redFactionIndex] = GUILayout.SelectionGrid(redUnitIndices[redFactionIndex], names, 2);
                if (names.Length > 0) GUILayout.Label(catalog[ids[redUnitIndices[redFactionIndex]]].ShortDescription(), WrapStyle());
                GUILayout.Label($"每次生成数量：{spawnCount}");
                if (GUILayout.Button("生成红方部队")) SpawnRed();
                if (GUILayout.Button("清空红方")) ClearSide(false);
                GUILayout.EndArea();
            }
            if (GUI.Button(new Rect(0f, 408f, 340f, 34f), redPanelVisible ? "隐藏红方面板" : "显示红方面板")) redPanelVisible = !redPanelVisible;
            GUI.matrix = old;
        }

        //中央统计条：时长、场上单位、双方存活 + 决战/血条显隐按钮
        private void DrawCenterStats()
        {
            int blueAlive = units.Count(u => u != null && u.CurrentHealth > 0f && IsBlueSide(u));
            int redAlive = units.Count(u => u != null && u.CurrentHealth > 0f && !IsBlueSide(u));
            GUI.Box(new Rect((Screen.width - 620f) * .5f, 14f, 620f, 40f), GUIContent.none);
            GUI.Label(new Rect((Screen.width - 620f) * .5f + 12f, 22f, 330f, 26f),
                $"{Factions[blueFactionIndex]} vs {Factions[redFactionIndex]} | 蓝 {blueAlive} | 红 {redAlive} | 池 {pool.TotalCount}(闲{pool.IdleCount})");
            if (GUI.Button(new Rect((Screen.width - 620f) * .5f + 350f, 20f, 110f, 28f), "决战×30"))
                StartFinalBattle(30);
            //全局血条显隐开关：所有单位血条同帧响应
            if (GUI.Button(new Rect((Screen.width - 620f) * .5f + 470f, 20f, 140f, 28f),
                HealthBar.GlobalVisible ? "隐藏血条" : "显示血条"))
                HealthBar.GlobalVisible = !HealthBar.GlobalVisible;
        }

        //决战模式：双方各生成 count 个当前选择单位（同一帧大批量生成，验证池自动扩容）
        private void StartFinalBattle(int count)
        {
            int saved = spawnCount;
            spawnCount = count;
            SpawnBlue();
            SpawnRed();
            spawnCount = saved;
        }

        //判定某单位属于蓝方：以生成时写入的 TeamId 为权威；
        //无队伍归属的单位（异常情况）退回种族或位置判定
        private bool IsBlueSide(UnitBase unit)
        {
            if (unit == null) return false;
            string teamId = unit.TeamId;
            if (teamId == "blue") return true;
            if (teamId == "red") return false;
            //兜底：先按种族，再按营地距离
            if (unit.FactionId == Factions[blueFactionIndex]) return true;
            if (unit.FactionId == Factions[redFactionIndex]) return false;
            return Vector2.Distance(unit.transform.position, BlueAnchor) < Vector2.Distance(unit.transform.position, RedAnchor);
        }

        //清空指定阵营的部队：走对象池回收（不销毁，待复用）
        private void ClearSide(bool blue)
        {
            var recycled = units.Where(u => u != null && (blue ? IsBlueSide(u) : !IsBlueSide(u))).ToList();
            foreach (UnitBase unit in recycled) pool.Release(unit);
            foreach (UnitBase unit in recycled) units.Remove(unit);
        }

        //大号加粗标题样式
        private static GUIStyle HeaderStyle(Color color)
        {
            return new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = color } };
        }

        //自动换行浅色正文
        private static GUIStyle WrapStyle()
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(.78f, .84f, .90f) } };
        }

        //通用精灵物体工厂
        private static GameObject CreateSpriteObject(string name, Sprite sprite, Vector2 position, Vector2 size, Color color, float z)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(position.x, position.y, z);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            return go;
        }

        //生成 2x2 纯白方形精灵
        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2f);
        }

        //程序生成 size x size 的圆形精灵
        private static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - size * .5f + .5f;
                float dy = y - size * .5f + .5f;
                pixels[y * size + x] = dx * dx + dy * dy <= size * size * .24f ? Color.white : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        }

        //十六进制颜色字符串转 Color
        private static Color Hex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.white;
        }
    }
}
