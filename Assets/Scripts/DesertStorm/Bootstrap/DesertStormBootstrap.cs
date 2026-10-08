using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 主场景引导脚本：程序化搭建世界（背景/障碍/训练假人/摄像机），
    /// 提供单位选择与生成、伤害统计、假人重置等测试功能，并用 OnGUI 绘制左右两块面板。
    /// </summary>
    public sealed class DesertStormBootstrap : MonoBehaviour
    {
        //三个可选阵营名称
        private static readonly string[] Factions = { "人族", "虫族", "神族" };
        //按单位 ID 记录的累计伤害
        private readonly Dictionary<string, float> damageByUnit = new Dictionary<string, float>();
        //按单位 ID 记录的命中次数
        private readonly Dictionary<string, int> hitByUnit = new Dictionary<string, int>();
        //按单位 ID 记录的生成数量
        private readonly Dictionary<string, int> spawnByUnit = new Dictionary<string, int>();
        //各阵营已生成的单位序号（用于排布出生位置）
        private readonly Dictionary<string, int> spawnSequence = new Dictionary<string, int> { { "人族", 0 }, { "虫族", 0 }, { "神族", 0 } };
        //场上所有已生成单位
        private readonly List<UnitBase> units = new List<UnitBase>();

        //单位数据目录（ID → 数据）
        private Dictionary<string, UnitData> catalog;
        //导航系统实例
        private CdtNavigationSystem navigation;
        //训练假人（攻击目标）
        private TrainingDummy dummy;
        //所有生成单位的父物体
        private GameObject unitsRoot;
        //程序生成的圆形/方形精灵
        private Sprite circleSprite;
        private Sprite squareSprite;
        //世界摄像机及其控制器
        private Camera worldCamera;
        private CameraPanController cameraController;
        //测试累计时长
        private float elapsed;
        //左侧面板当前选中的阵营与单位索引
        private int selectedFaction;
        private int selectedUnit;
        //每次生成的单位数量
        private int spawnCount = 5;
        //左右面板显隐开关
        private bool leftPanelVisible = true;
        private bool damagePanelVisible = true;
        //左右面板 UI 缩放系数
        private float leftScale = 1f;
        private float damageScale = 1f;
        //伤害明细面板滚动位置
        private Vector2 damageScroll;
        //场景切换中标记：为 true 时停止绘制 UI，防止访问已销毁对象
        private bool switching;

        //启动流程：加载单位目录、初始化统计字典、生成精灵、搭建世界与摄像机
        private void Start()
        {
            catalog = UnitCatalog.AllUnits();
            //为每个单位初始化伤害/命中/生成计数
            foreach (string id in catalog.Keys)
            {
                damageByUnit[id] = 0f;
                hitByUnit[id] = 0;
                spawnByUnit[id] = 0;
            }

            circleSprite = CreateCircleSprite(64);
            squareSprite = CreateSquareSprite();
            //主场景单位同样用软体人群模式（单位间不硬碰撞，靠位置去重叠），避免密集时凝滞
            UnitBase.SoftBodyCrowd = true;
            CreateWorld();
            CreateCamera();
            //接入单位信息检视面板：注入单位列表与拾取摄像机
            UnitInspectorPanel.Attach(units, worldCamera);
        }

        //每帧累计测试时长，Tab 键切换左侧面板显隐
        private void Update()
        {
            elapsed += Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.Tab)) leftPanelVisible = !leftPanelVisible;

            //单位信息检视：注册 UI 遮挡区（左右面板）→ 驱动热键与点击拾取
            UnitInspectorPanel.ClearUiBlocks();
            if (leftPanelVisible) UnitInspectorPanel.AddUiBlock(new Rect(18f, 18f, 388f, 586f));
            float damagePanelX = Screen.width - 18f - 370f * damageScale;
            if (damagePanelVisible) UnitInspectorPanel.AddUiBlock(new Rect(damagePanelX, 18f, 388f * damageScale, 606f));
            UnitInspectorPanel.AddUiBlock(UnitInspectorPanel.PanelRect);
            UnitInspectorPanel.Tick();
        }

        //搭建世界：背景、导航系统与障碍墙、训练假人、单位容器
        private void CreateWorld()
        {
            //世界边界与四块障碍矩形
            var boundary = new Rect(0f, 0f, 2400f, 1400f);
            var obstacles = new List<Rect>
            {
                new Rect(540f, 90f, 280f, 340f),
                new Rect(910f, 930f, 330f, 340f),
                new Rect(1340f, 170f, 360f, 330f),
                new Rect(1650f, 850f, 330f, 270f)
            };

            //深色背景板（放在最远层）
            GameObject background = CreateSpriteObject("DesertStorm_World", squareSprite, new Vector2(1200f, 700f), new Vector2(2400f, 1400f), Hex("#192b32"), 1f);
            background.transform.position = new Vector3(background.transform.position.x, background.transform.position.y, 2f);
            //创建导航系统并配置边界与障碍
            navigation = new GameObject("CDTNavigationSystem").AddComponent<CdtNavigationSystem>();
            navigation.Configure(boundary, obstacles);

            //每块障碍创建静态刚体与盒碰撞体，供物理阻挡
            foreach (Rect obstacle in obstacles)
            {
                GameObject wall = CreateSpriteObject("Obstacle", squareSprite, obstacle.center, obstacle.size, Hex("#443041"), .5f);
                var body = wall.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                var collider = wall.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
            }

            //右侧放置训练假人（圆形碰撞体），并创建单位容器
            GameObject dummyObject = CreateSpriteObject("DamageTestDummy", circleSprite, new Vector2(2200f, 700f), new Vector2(60f, 60f), Hex("#b87936"), -1f);
            dummy = dummyObject.AddComponent<TrainingDummy>();
            var dummyCollider = dummyObject.AddComponent<CircleCollider2D>();
            dummyCollider.radius = .5f;
            unitsRoot = new GameObject("GeneratedUnits");
        }

        //创建正交世界摄像机并挂接平移控制器
        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("WorldCamera");
            worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.orthographic = true;
            worldCamera.backgroundColor = Hex("#15202a");
            cameraController = cameraObject.AddComponent<CameraPanController>();
            cameraObject.transform.position = new Vector3(640f, 700f, -10f);
            cameraController.Initialize(worldCamera, new Rect(0f, 0f, 2400f, 1400f));
        }

        //生成当前选中的单位 spawnCount 个，并累加生成计数
        private void SpawnSelected()
        {
            List<string> ids = UnitCatalog.IdsForFaction(Factions[selectedFaction]);
            if (ids.Count == 0) return;
            //钳制选中索引，防止切阵营后越界
            selectedUnit = Mathf.Clamp(selectedUnit, 0, ids.Count - 1);
            string id = ids[selectedUnit];
            UnitData data = catalog[id];
            for (int i = 0; i < spawnCount; i++) SpawnUnit(data, NextSpawnPosition(data.Faction));
            spawnByUnit[id] += spawnCount;
        }

        //随机生成 5 种不同单位：洗牌目录后取前 5 种各生成一个
        private void SpawnRandomFive()
        {
            var ids = catalog.Keys.ToList();
            //Fisher-Yates 洗牌
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int swap = UnityEngine.Random.Range(0, i + 1);
                string temp = ids[i]; ids[i] = ids[swap]; ids[swap] = temp;
            }
            for (int i = 0; i < Mathf.Min(5, ids.Count); i++)
            {
                UnitData data = catalog[ids[i]];
                SpawnUnit(data, NextSpawnPosition(data.Faction));
                spawnByUnit[data.UnitId]++;
            }
        }

        //在指定位置实例化一个单位，挂接事件并登记到列表
        private void SpawnUnit(UnitData data, Vector2 position)
        {
            GameObject unitObject = new GameObject($"Unit_{data.UnitId}");
            unitObject.transform.position = position;
            unitObject.transform.SetParent(unitsRoot.transform);
            UnitBase unit = unitObject.AddComponent<UnitBase>();
            unit.Setup(data, dummy, navigation, circleSprite);
            //订阅伤害事件，用于统计面板
            unit.DamageDealt += OnUnitDamage;
            units.Add(unit);
        }

        //按阵营计算下一个出生点：每个阵营一个锚点，按序号排成纵列
        private Vector2 NextSpawnPosition(string faction)
        {
            int index = spawnSequence[faction]++;
            Vector2 anchor = faction == "人族" ? new Vector2(140f, 250f) : faction == "虫族" ? new Vector2(140f, 700f) : new Vector2(140f, 1150f);
            return anchor + new Vector2(-(index / 5) * 48f, (index % 5 - 2) * 48f);
        }

        //单位造成伤害时的回调：累加该单位的伤害与命中计数
        private void OnUnitDamage(UnitBase unit, UnitData data, float amount)
        {
            damageByUnit[data.UnitId] += amount;
            hitByUnit[data.UnitId]++;
        }

        //清空测试：销毁全部单位、清零所有统计与计时
        private void ClearTest()
        {
            foreach (UnitBase unit in units.Where(u => u != null).ToList()) Destroy(unit.gameObject);
            units.Clear();
            foreach (string id in catalog.Keys.ToList()) { damageByUnit[id] = 0f; hitByUnit[id] = 0; spawnByUnit[id] = 0; }
            spawnSequence["人族"] = 0; spawnSequence["虫族"] = 0; spawnSequence["神族"] = 0;
            elapsed = 0f;
        }

        //重置假人的累计伤害与命中统计
        private void ResetDummy() => dummy.ResetDummy();

        //切换到窄通道压力测试场景：销毁当前全部对象，重建 CorridorTestBootstrap
        private void SwitchToCorridorTest()
        {
            //标记为切换中：后续 OnGUI 立即返回，避免访问已销毁对象
            switching = true;
            //清理本场景生成的全部动态对象（单位、导航系统、假人、背景等）
            foreach (UnitBase unit in units.Where(u => u != null).ToList()) Destroy(unit.gameObject);
            units.Clear();
            if (dummy != null) Destroy(dummy.gameObject);
            if (navigation != null) Destroy(navigation.gameObject);
            if (worldCamera != null) Destroy(worldCamera.gameObject);
            var oldWorld = GameObject.Find("DesertStorm_World");
            if (oldWorld != null) Destroy(oldWorld);
            var oldWalls = GameObject.FindObjectsOfType<GameObject>().Where(go => go.name == "Obstacle").ToList();
            foreach (GameObject wall in oldWalls) Destroy(wall);
            //挂载通道测试引导脚本，由它重建场景（挂在同一物体上，Start 延迟到下一帧执行）
            gameObject.AddComponent<CorridorTestBootstrap>();
            //清理单位检视面板的静态状态（选中、标记、遮挡区），避免跨场景残留
            UnitInspectorPanel.Reset();
            Destroy(this);
        }

        //UI 总入口：目录未加载或切换中时提示/返回，否则绘制左右两块面板
        private void OnGUI()
        {
            //场景切换中停止绘制，避免访问已销毁的假人/单位
            if (switching) return;
            if (catalog == null)
            {
                GUI.Label(new Rect(24f, 24f, 320f, 32f), "正在加载单位数据...");
                return;
            }
            DrawLeftPanel();
            DrawDamagePanel();
            //单位信息面板最后绘制（盖在其他 UI 上层）
            UnitInspectorPanel.DrawPanel();
        }

        //绘制左侧测试面板：阵营/单位选择、生成数量滑条、操作按钮与假人状态
        private void DrawLeftPanel()
        {
            //用 GUI.matrix 实现整体 UI 缩放
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(18f, 18f, 0f), Quaternion.identity, Vector3.one * leftScale);
            if (leftPanelVisible)
            {
                GUI.Box(new Rect(0f, 0f, 370f, 520f), GUIContent.none);
                GUILayout.BeginArea(new Rect(16f, 14f, 338f, 490f));
                GUILayout.Label("伤害与寻路测试", HeaderStyle(22));
                GUILayout.Label("场景初始只有训练假人。单位使用 CDT 风格导航、A*、漏斗收束和冲击性避让。", WrapStyle());
                //UI 缩放调节行
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("UI −", GUILayout.Width(80))) leftScale = Mathf.Clamp(leftScale - .1f, .7f, 1.15f);
                GUILayout.Label($"UI {leftScale * 100f:0}%", CenterStyle(), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("UI +", GUILayout.Width(80))) leftScale = Mathf.Clamp(leftScale + .1f, .7f, 1.15f);
                GUILayout.EndHorizontal();
                //阵营选择网格
                GUILayout.Label("阵营");
                selectedFaction = GUILayout.SelectionGrid(selectedFaction, Factions, 3);
                //过滤掉目录中缺失的单位，并钳制选中索引
                List<string> ids = UnitCatalog.IdsForFaction(Factions[selectedFaction]);
                ids = ids.Where(id => catalog != null && catalog.ContainsKey(id) && catalog[id] != null).ToList();
                selectedUnit = Mathf.Clamp(selectedUnit, 0, Mathf.Max(0, ids.Count - 1));
                //单位选择网格与说明文字
                GUILayout.Label("单位");
                string[] names = ids.Select(id => catalog[id].DisplayName).ToArray();
                selectedUnit = GUILayout.SelectionGrid(selectedUnit, names, 3);
                if (ids.Count > 0) GUILayout.Label(catalog[ids[selectedUnit]].ShortDescription(), WrapStyle());
                //生成数量滑条与操作按钮
                GUILayout.Label($"本次生成数量：{spawnCount}");
                spawnCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(spawnCount, 1f, 50f));
                if (GUILayout.Button("生成单位（仅本次）")) SpawnSelected();
                if (GUILayout.Button("随机生成 5 种不同单位")) SpawnRandomFive();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("清空单位")) ClearTest();
                if (GUILayout.Button("重置假人")) ResetDummy();
                GUILayout.EndHorizontal();
                //切换到窄通道压力测试场景
                if (GUILayout.Button("切换到窄通道压力测试场景")) SwitchToCorridorTest();
                //状态行：假人承伤、命中、测试时长与场上单位数
                GUILayout.Label($"假人累计承伤：{dummy.TotalDamage:0.0} | 命中：{dummy.HitCount}");
                GUILayout.Label($"测试时长：{elapsed:0.0}s | 场上单位：{units.Count}");
                GUILayout.EndArea();
            }
            //面板底部显隐切换按钮
            if (GUI.Button(new Rect(0f, 548f, 370f, 38f), leftPanelVisible ? "隐藏测试面板 [Tab]" : "显示测试面板 [Tab]")) leftPanelVisible = !leftPanelVisible;
            GUI.matrix = old;
        }

        //绘制右侧伤害统计面板：假人总量、按单位的伤害/DPS/命中/数量明细
        private void DrawDamagePanel()
        {
            //面板贴屏幕右侧，按缩放系数计算左上角位置
            float x = Screen.width - 18f - 370f * damageScale;
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(x, 18f, 0f), Quaternion.identity, Vector3.one * damageScale);
            if (damagePanelVisible)
            {
                GUI.Box(new Rect(0f, 0f, 370f, 550f), GUIContent.none);
                GUILayout.BeginArea(new Rect(16f, 14f, 338f, 520f));
                GUILayout.Label("单位伤害统计", HeaderStyle(22));
                GUILayout.Label("按单位分类记录伤害、DPS、命中次数和生成数量。", WrapStyle());
                //统计面板缩放调节行
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("统计 −", GUILayout.Width(90))) damageScale = Mathf.Clamp(damageScale - .1f, .7f, 1.15f);
                GUILayout.Label($"统计 {damageScale * 100f:0}%", CenterStyle(), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("统计 +", GUILayout.Width(90))) damageScale = Mathf.Clamp(damageScale + .1f, .7f, 1.15f);
                GUILayout.EndHorizontal();
                GUILayout.Label($"假人累计承伤：{dummy.TotalDamage:0.0} | 总命中：{dummy.HitCount}");
                GUILayout.Label("分类明细");
                damageScroll = GUILayout.BeginScrollView(damageScroll, GUILayout.Height(405f));
                //只显示生成过的单位，按累计伤害从高到低排序
                var activeIds = spawnByUnit.Where(pair => pair.Value > 0).Select(pair => pair.Key).OrderByDescending(id => damageByUnit[id]).ToList();
                if (activeIds.Count == 0) GUILayout.Label("未生成单位。请在左侧选择单位后生成。");
                foreach (string id in activeIds)
                {
                    UnitData data = catalog[id];
                    //DPS = 累计伤害 / 测试时长（下限 0.1s 防除零）
                    float dps = damageByUnit[id] / Mathf.Max(.1f, elapsed);
                    //文字颜色与该单位阵营的主题色一致，阵营一目了然
                    GUILayout.Label($"{data.Faction} · {data.DisplayName}\n数量 {spawnByUnit[id]} | 伤害 {damageByUnit[id]:0.0} | DPS {dps:0.0} | 命中 {hitByUnit[id]}", FactionLabelStyle(data.AccentColor));
                    GUILayout.Space(4f);
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            //面板底部显隐切换按钮
            if (GUI.Button(new Rect(0f, 568f, 370f, 38f), damagePanelVisible ? "隐藏伤害面板" : "显示伤害面板")) damagePanelVisible = !damagePanelVisible;
            GUI.matrix = old;
        }

        //构造大号白色标题样式
        private static GUIStyle HeaderStyle(int size)
        {
            return new GUIStyle(GUI.skin.label) { fontSize = size, normal = { textColor = Color.white } };
        }

        //构造居中对齐的标签样式
        private static GUIStyle CenterStyle()
        {
            return new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }

        //构造自动换行的浅灰正文样式
        private static GUIStyle WrapStyle()
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(.66f, .72f, .80f) } };
        }

        //构造指定颜色的标签样式：伤害统计按单位阵营色着色
        private static GUIStyle FactionLabelStyle(Color factionColor)
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = factionColor } };
        }

        //通用精灵物体工厂：创建带 SpriteRenderer 的物体，按尺寸缩放并着色
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

        //生成 2x2 纯白方形贴图精灵（用作背景与障碍）
        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2f);
        }

        //程序生成 size x size 的圆形贴图精灵（逐像素判定圆内）
        private static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                //距圆心平方距离不超阈值则为白色，否则透明
                float dx = x - size * .5f + .5f;
                float dy = y - size * .5f + .5f;
                pixels[y * size + x] = dx * dx + dy * dy <= size * size * .24f ? Color.white : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        }

        //十六进制颜色字符串转 Color，解析失败返回白色
        private static Color Hex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.white;
        }
    }
}
