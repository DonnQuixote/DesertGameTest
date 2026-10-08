using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 寻路演示场景引导脚本：程序化搭建世界（背景、障碍、目标点、摄像机），
    /// 生成三条演示路径单位，并用 OnGUI 绘制说明面板与状态列表。
    /// </summary>
    public sealed class NavigationDemoBootstrap : MonoBehaviour
    {
        //四块静态障碍矩形（与世界边界一起构成寻路环境）
        private readonly List<Rect> obstacles = new List<Rect>
        {
            new Rect(540f, 90f, 280f, 340f),
            new Rect(910f, 930f, 330f, 340f),
            new Rect(1340f, 170f, 360f, 330f),
            new Rect(1650f, 850f, 330f, 270f)
        };

        //导航系统实例
        private CdtNavigationSystem navigation;
        //所有演示单位的父物体
        private GameObject agentsRoot;
        //程序生成的圆形/方形精灵
        private Sprite circleSprite;
        private Sprite squareSprite;
        //场景中的演示单位列表
        private readonly List<NavigationDemoAgent> agents = new List<NavigationDemoAgent>();
        //摄像机控制器
        private CameraPanController cameraController;
        //说明面板滚动位置
        private Vector2 panelScroll;

        //启动流程：生成精灵 → 搭建世界 → 创建摄像机 → 重置演示单位
        private void Start()
        {
            circleSprite = CreateCircleSprite(64);
            squareSprite = CreateSquareSprite();
            CreateWorld();
            CreateCamera();
            ResetDemo();
        }

        //搭建世界：背景板、导航系统、静态障碍刚体与碰撞体、目标点
        private void CreateWorld()
        {
            //2400x1400 的深色背景
            CreateSpriteObject("NavigationDemo_World", squareSprite, new Vector2(1200f, 700f), new Vector2(2400f, 1400f), Hex("#192b32"), 2f);
            //创建导航系统并配置边界与障碍
            navigation = new GameObject("CDTNavigationSystem").AddComponent<CdtNavigationSystem>();
            navigation.Configure(new Rect(0f, 0f, 2400f, 1400f), obstacles);
            //每块障碍创建静态刚体与盒碰撞体，供物理阻挡
            foreach (Rect obstacle in obstacles)
            {
                GameObject wall = CreateSpriteObject("NavigationObstacle", squareSprite, obstacle.center, obstacle.size, Hex("#443041"), .5f);
                var body = wall.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                var collider = wall.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
            }

            //创建单位容器与三个目标点（右侧三条路线终点）
            agentsRoot = new GameObject("NavigationDemoAgents");
            CreateGoal(new Vector2(2250f, 1000f));
            CreateGoal(new Vector2(2250f, 700f));
            CreateGoal(new Vector2(2250f, 400f));
        }

        //创建正交演示摄像机并挂接平移控制器
        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("NavigationDemoCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.backgroundColor = Hex("#15202a");
            cameraObject.transform.position = new Vector3(640f, 700f, -10f);
            cameraController = cameraObject.AddComponent<CameraPanController>();
            cameraController.Initialize(camera, new Rect(0f, 0f, 2400f, 1400f));
        }

        //重置演示：销毁旧单位后重新生成三条路线的演示单位
        private void ResetDemo()
        {
            foreach (NavigationDemoAgent agent in agents) if (agent != null) Destroy(agent.gameObject);
            agents.Clear();
            //三条路线：左上→右上、左中→右中、左下→右下，颜色各不相同
            CreateAgent("上路", new Vector2(150f, 250f), new Vector2(2250f, 1000f), Hex("#51a7ff"));
            CreateAgent("中路", new Vector2(150f, 700f), new Vector2(2250f, 700f), Hex("#f0c45b"));
            CreateAgent("下路", new Vector2(150f, 1150f), new Vector2(2250f, 400f), Hex("#e66d8b"));
        }

        //创建一个演示单位：建物体、设置位置并初始化导航组件
        private void CreateAgent(string label, Vector2 start, Vector2 goal, Color color)
        {
            GameObject agentObject = new GameObject($"PathAgent_{label}");
            agentObject.transform.SetParent(agentsRoot.transform);
            agentObject.transform.position = start;
            var agent = agentObject.AddComponent<NavigationDemoAgent>();
            agent.Setup(label, navigation, goal, 150f, 14f, circleSprite, color);
            agents.Add(agent);
        }

        //在指定位置创建目标点标记（小圆，画在最上层之下）
        private void CreateGoal(Vector2 position)
        {
            CreateSpriteObject("PathGoal", circleSprite, position, new Vector2(44f, 44f), Hex("#b87936"), -1.5f);
        }

        //绘制说明面板：标题、操作提示、重新生成按钮与各单位状态
        private void OnGUI()
        {
            GUI.Box(new Rect(18f, 18f, 410f, 310f), GUIContent.none);
            GUILayout.BeginArea(new Rect(36f, 32f, 375f, 280f));
            GUILayout.Label("CDT / A* / 漏斗寻路演示", HeaderStyle(22));
            GUILayout.Label("障碍约束顶点构成可见性图，A* 选择最短走廊，再进行视线收束。彩色线段是单位当前路径。", WrapStyle());
            //点击按钮重新生成三条演示路径
            if (GUILayout.Button("重新生成三条演示路径")) ResetDemo();
            panelScroll = GUILayout.BeginScrollView(panelScroll, GUILayout.Height(150f));
            //逐行显示每个单位的当前寻路状态
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null) GUILayout.Label($"{agents[i].AgentName}：{agents[i].Status()}");
            GUILayout.EndScrollView();
            GUILayout.Label("WASD 移动摄像头 · 滚轮调整高度 · 中键拖拽");
            GUILayout.EndArea();
        }

        //通用精灵物体工厂：创建带 SpriteRenderer 的物体，按尺寸缩放并着色
        private static GameObject CreateSpriteObject(string name, Sprite sprite, Vector2 position, Vector2 size, Color color, float z)
        {
            var obj = new GameObject(name);
            obj.transform.position = new Vector3(position.x, position.y, z);
            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            obj.transform.localScale = new Vector3(size.x, size.y, 1f);
            return obj;
        }

        //生成 2x2 纯白方形贴图精灵（用作背景与障碍）
        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(2, 2);
            for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) texture.SetPixel(x, y, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2f);
        }

        //程序生成 size x size 的圆形贴图精灵（逐像素判定圆内）
        private static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                //圆内像素为白色，圆外透明
                float distance = Vector2.Distance(new Vector2(x, y), center);
                texture.SetPixel(x, y, distance <= radius ? Color.white : Color.clear);
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        }

        //十六进制颜色字符串转 Color，解析失败返回品红（便于发现问题）
        private static Color Hex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.magenta;
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
    }
}
