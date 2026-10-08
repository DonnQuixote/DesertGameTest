using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 漏斗通道压力测试场景：左侧是大片开阔生成区（约 900 宽），单位在此自由聚集；
    /// 中段两块楔形障碍墙构成漏斗，收束到按雷神直径两倍宽的窄口，
    /// 通过窄通道后前往右侧集合点。用于观察漏斗收束处的寻路、挤压与避让表现。
    /// 单位有边界夹持保护，不会掉出可视区域。
    /// </summary>
    public sealed class CorridorTestBootstrap : MonoBehaviour
    {
        //雷神生命值（用于按其体积推导通道宽度）
        private const float ThorHp = 400f;
        //窄口宽度 = 雷神碰撞直径 × 2（雷神 CollisionRadius=14，直径 28，窄口 56）
        //注意：Mathf.Clamp 非编译期常量，故用 static readonly 而非 const
        private static readonly float ThorRadius = 6f + Mathf.Clamp(ThorHp / 200f, .6f, 3.2f) * 4f;
        private static readonly float NarrowWidth = ThorRadius * 4f;
        //世界尺寸
        private const float WorldWidth = 2400f;
        private const float WorldHeight = 1400f;
        //漏斗布局参数：左侧生成区到 x=900；漏斗从 x=900 收束到 x=1250 进入窄段；窄段到 x=1650
        private const float FunnelStartX = 900f;
        private const float FunnelEndX = 1250f;
        private const float NarrowEndX = 1650f;
        //窄段中心线
        private const float CorridorCenterY = 700f;

        //导航系统
        private CdtNavigationSystem navigation;
        //所有参与测试的单位
        private readonly List<UnitBase> units = new List<UnitBase>();
        //程序生成的圆形/方形精灵
        private Sprite circleSprite;
        private Sprite squareSprite;
        //测试统计
        private float elapsed;
        private readonly HashSet<UnitBase> arrivedSet = new HashSet<UnitBase>();
        //集合点（通道右端出口外）
        private Vector2 goalPoint = new Vector2(2150f, 700f);
        //GUI 面板滚动与显隐
        private Vector2 panelScroll;
        private bool panelVisible = true;
        //进度条贴图
        private Texture2D whiteTexture;

        //启动：生成精灵 → 搭建漏斗世界 → 摄像机 → 生成 100 单位
        private void Start()
        {
            //软体人群：单位间不硬碰撞（位置去重叠防互叠），消除漏斗口凝滞
            UnitBase.SoftBodyCrowd = true;
            circleSprite = CreateCircleSprite(64);
            squareSprite = CreateSquareSprite();
            whiteTexture = CreateSquareTexture();
            CreateWorld();
            CreateCamera();
            SpawnTestUnits();
            //接入单位信息检视面板：注入单位列表与拾取摄像机（直接传字段引用，不依赖 Camera.main 标签）
            UnitInspectorPanel.Attach(units, sceneCamera);
        }

        //每帧计时、统计已抵达单位，并把单位夹持在可视区域内
        private void Update()
        {
            elapsed += Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.Tab)) panelVisible = !panelVisible;
            foreach (UnitBase unit in units)
            {
                if (unit == null) continue;
                //夹持位置：任何单位都不会被推出视图（世界）范围
                Vector2 p = unit.transform.position;
                Vector2 clamped = new Vector2(
                    Mathf.Clamp(p.x, 8f, WorldWidth - 8f),
                    Mathf.Clamp(p.y, 8f, WorldHeight - 8f));
                if ((Vector2)unit.transform.position != clamped) unit.transform.position = clamped;
                //距集合点 60 以内视为"已通过漏斗抵达另一侧"
                if (!arrivedSet.Contains(unit) && Vector2.Distance(p, goalPoint) < 60f) arrivedSet.Add(unit);
            }

            //单位信息检视：注册 UI 遮挡区 → 驱动热键与点击拾取
            UnitInspectorPanel.ClearUiBlocks();
            if (panelVisible) UnitInspectorPanel.AddUiBlock(new Rect(18f, 18f, 440f, 280f));
            UnitInspectorPanel.AddUiBlock(UnitInspectorPanel.PanelRect);
            UnitInspectorPanel.Tick();
        }

        //搭建漏斗世界：背景、四块障碍（上/下外墙 + 两块漏斗楔形墙），导航系统
        private void CreateWorld()
        {
            var boundary = new Rect(0f, 0f, WorldWidth, WorldHeight);
            float halfNarrow = NarrowWidth * .5f;
            //漏斗开口端：上下墙各距中心线 460（开口 920，覆盖左侧生成区右缘）
            float funnelMouthHalf = 460f;

            var obstacles = new List<Rect>
            {
                //上外墙：x∈[900,1650] 从漏斗开口斜收到窄口；x>1650 保持窄口高度直到右边界
                new Rect(FunnelStartX, CorridorCenterY + halfNarrow, FunnelEndX - FunnelStartX, funnelMouthHalf - halfNarrow),
                new Rect(FunnelEndX, CorridorCenterY + halfNarrow, WorldWidth - FunnelEndX, WorldHeight - (CorridorCenterY + halfNarrow)),
                //下外墙：镜像
                new Rect(FunnelStartX, CorridorCenterY - funnelMouthHalf, FunnelEndX - FunnelStartX, funnelMouthHalf - halfNarrow),
                new Rect(FunnelEndX, 0f, WorldWidth - FunnelEndX, CorridorCenterY - halfNarrow)
            };

            //深色背景板
            GameObject background = CreateSpriteObject("Funnel_World", squareSprite, new Vector2(WorldWidth * .5f, WorldHeight * .5f), new Vector2(WorldWidth, WorldHeight), Hex("#192b32"), 1f);
            background.transform.position = new Vector3(background.transform.position.x, background.transform.position.y, 2f);
            //导航系统：边界与障碍
            navigation = new GameObject("CDTNavigationSystem").AddComponent<CdtNavigationSystem>();
            navigation.Configure(boundary, obstacles);
            //墙：静态刚体 + 盒碰撞体，物理阻挡陆军
            foreach (Rect obstacle in obstacles)
            {
                GameObject wall = CreateSpriteObject("FunnelWall", squareSprite, obstacle.center, obstacle.size, Hex("#443041"), .5f);
                var body = wall.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                var collider = wall.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
            }

            //漏斗斜边提示线（亮色半透明长条，沿收束方向摆放，示意漏斗轮廓）
            CreateSpriteObject("FunnelHintTop", squareSprite, new Vector2((FunnelStartX + FunnelEndX) * .5f, CorridorCenterY + (funnelMouthHalf + halfNarrow) * .5f), new Vector2(FunnelEndX - FunnelStartX, 8f), new Color(1f, 1f, 1f, .18f), 0f);
            CreateSpriteObject("FunnelHintBottom", squareSprite, new Vector2((FunnelStartX + FunnelEndX) * .5f, CorridorCenterY - (funnelMouthHalf + halfNarrow) * .5f), new Vector2(FunnelEndX - FunnelStartX, 8f), new Color(1f, 1f, 1f, .18f), 0f);
            //集合点标记（金色圆）
            CreateSpriteObject("RallyPoint", circleSprite, goalPoint, new Vector2(110f, 110f), new Color(1f, .84f, .35f, .5f), -1.5f);
        }

        //世界摄像机（供单位拾取面板使用）
        private Camera sceneCamera;

        //创建正交摄像机并挂接平移控制器
        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("FunnelCamera");
            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.orthographic = true;
            sceneCamera.backgroundColor = Hex("#15202a");
            //打上 MainCamera 标签：Camera.main 及依赖它的逻辑才能找到这台相机
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(640f, 700f, -10f);
            var controller = cameraObject.AddComponent<CameraPanController>();
            controller.Initialize(sceneCamera, new Rect(0f, 0f, WorldWidth, WorldHeight));
        }

        //生成 100 个体积/碰撞半径不一的陆军单位，散布在左侧开阔生成区（非密集网格，自由聚集）
        private void SpawnTestUnits()
        {
            var catalog = UnitCatalog.AllUnits();
            //陆军池：碰撞半径从跳虫(≈7.8)到雷神/莽兽(≈14)不等，充分制造挤压
            string[] groundIds = { "marine", "zergling", "zealot", "marauder", "roach", "stalker", "siege_tank", "ultralisk", "thor", "archon" };
            int total = 100;
            //生成区：x∈[70, 870]、y∈[120, 1280] 的开阔区域，网格加随机抖动避免完全重叠
            int columns = 10;
            int rows = 10;
            float cellW = 86f;
            float cellH = 122f;
            Vector2 origin = new Vector2(70f, 120f);
            int index = 0;
            for (int row = 0; row < rows && index < total; row++)
            for (int col = 0; col < columns && index < total; col++)
            {
                //种类轮换，保证体积/碰撞半径差异
                string id = groundIds[index % groundIds.Length];
                if (!catalog.ContainsKey(id)) { index++; continue; }
                UnitData data = catalog[id];
                //网格位置加随机抖动，出生更自然
                Vector2 jitter = new Vector2(UnityEngine.Random.Range(-18f, 18f), UnityEngine.Random.Range(-26f, 26f));
                Vector2 position = origin + new Vector2(col * cellW, row * cellH) + jitter;
                SpawnUnit(data, position);
                index++;
            }
        }

        //实例化一个陆军测试单位：带物理碰撞与互相避让
        private void SpawnUnit(UnitData data, Vector2 position)
        {
            GameObject unitObject = new GameObject($"FunnelUnit_{data.UnitId}");
            unitObject.transform.position = new Vector3(position.x, position.y, -1f);
            UnitBase unit = unitObject.AddComponent<UnitBase>();
            unit.Setup(data, null, navigation, circleSprite, false);
            //设置移动目标点：漏斗出口右侧的集合点
            unit.SetGoalPoint(goalPoint, 60f);
            units.Add(unit);
        }

        //绘制统计面板：进度、漏斗内滞留数与图例
        private void OnGUI()
        {
            if (!panelVisible)
            {
                if (GUI.Button(new Rect(18f, 18f, 220f, 34f), "显示漏斗测试面板 [Tab]")) panelVisible = true;
                //统计面板隐藏时单位信息面板仍要绘制（否则 Tab 后点选面板跟着消失）
                UnitInspectorPanel.DrawPanel();
                return;
            }
            GUI.Box(new Rect(18f, 18f, 440f, 280f), GUIContent.none);
            GUILayout.BeginArea(new Rect(34f, 30f, 408f, 256f));
            GUILayout.Label("漏斗通道压力测试", HeaderStyle(22));
            GUILayout.Label($"左侧开阔生成区（900 宽）→ 漏斗收束 → 窄口 {NarrowWidth:0}（雷神直径 {ThorRadius * 2f:0} × 2）→ 右侧集合点。观察收束处的寻路与挤压。", WrapStyle());
            GUILayout.Label($"测试时长：{elapsed:0.0}s | 场上单位：{units.Count(u => u != null)}");
            GUILayout.Label($"已抵达集合点：{arrivedSet.Count} / {units.Count} | 未抵达：{Mathf.Max(0, units.Count(u => u != null) - arrivedSet.Count)}");
            //通过率进度条
            Rect progressRect = GUILayoutUtility.GetRect(390f, 16f);
            float ratio = units.Count > 0 ? Mathf.Clamp01((float)arrivedSet.Count / units.Count) : 0f;
            GUI.color = new Color(.36f, .72f, .45f);
            GUI.DrawTexture(new Rect(progressRect.x, progressRect.y, progressRect.width * ratio, progressRect.height), whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, .12f);
            GUI.DrawTexture(new Rect(progressRect.x + progressRect.width * ratio, progressRect.y, progressRect.width * (1f - ratio), progressRect.height), whiteTexture);
            GUI.color = Color.white;
            GUILayout.Space(6f);
            GUILayout.Label("单位 = 圆形（陆军） · 集合点 = 金色圆 · 单位不会掉出视图");
            GUILayout.Label("WASD 移动摄像头 · 滚轮缩放 · 中键/空格+左键拖拽");
            if (GUILayout.Button("重新开始测试")) RestartTest();
            GUILayout.EndArea();

            //单位信息面板最后绘制（盖在其他 UI 上层）
            UnitInspectorPanel.DrawPanel();
        }

        //重新开始：销毁全部单位、清零统计、重新生成
        private void RestartTest()
        {
            foreach (UnitBase unit in units) if (unit != null) Destroy(unit.gameObject);
            units.Clear();
            arrivedSet.Clear();
            elapsed = 0f;
            SpawnTestUnits();
            //重开测试后单位列表已替换，重新注入检视面板
            UnitInspectorPanel.Attach(units, sceneCamera);
        }

        //构造大号加粗白色标题样式
        private static GUIStyle HeaderStyle(int size)
        {
            return new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        }

        //构造自动换行的浅色正文样式
        private static GUIStyle WrapStyle()
        {
            return new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(.82f, .88f, .92f) } };
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

        //生成 2x2 纯白贴图（进度条用）
        private static Texture2D CreateSquareTexture()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            return texture;
        }

        //生成 2x2 纯白方形精灵（背景与障碍）
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
