using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 导路演示单位：沿 CDT 导航系统求得的路径逐点移动，
    /// 自带圆形外观与路径线渲染，用于展示三条演示路径的寻路效果。
    /// </summary>
    public sealed class NavigationDemoAgent : MonoBehaviour
    {
        //单位显示名称（如"上路""中路""下路"）
        public string AgentName { get; private set; }
        //当前寻得的路径点列表
        public List<Vector2> Path { get; private set; } = new List<Vector2>();
        //当前正在前往的路径点索引
        public int CurrentWaypoint { get; private set; }

        //移动速度（单位/秒）
        private float speed;
        //碰撞半径（决定寻路净空与抵达判定）
        private float radius;
        //目标终点坐标
        private Vector2 goal;
        //路径可视化线渲染组件
        private LineRenderer routeLine;

        //初始化：创建外观与路径线，向导航系统请求路径并开始跟随
        public void Setup(string name, CdtNavigationSystem navigation, Vector2 target, float movementSpeed, float collisionRadius, Sprite visual, Color color)
        {
            AgentName = name;
            speed = movementSpeed;
            radius = collisionRadius;
            goal = target;

            //创建圆形精灵外观，并按碰撞半径缩放
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = visual;
            renderer.color = color;
            transform.localScale = Vector3.one * (radius * 2f);
            //略微抬高绘制层级，避免与背景重叠
            transform.position = new Vector3(transform.position.x, transform.position.y, -1f);

            //创建路径线渲染器：半透明渐变，画在单位图层之下
            routeLine = gameObject.AddComponent<LineRenderer>();
            routeLine.useWorldSpace = true;
            routeLine.startWidth = 5f;
            routeLine.endWidth = 5f;
            routeLine.material = new Material(Shader.Find("Sprites/Default"));
            routeLine.startColor = new Color(color.r, color.g, color.b, .55f);
            routeLine.endColor = new Color(color.r, color.g, color.b, .15f);
            routeLine.sortingOrder = -2;

            //向导航系统请求路径并设置首个路点
            Path = navigation.RequestPath(transform.position, goal, radius);
            CurrentWaypoint = Path.Count > 1 ? 1 : 0;
            RefreshRouteLine();
        }

        //每帧沿路径推进：先判定是否到达当前路点，再朝其移动
        private void Update()
        {
            if (Path == null || CurrentWaypoint >= Path.Count) return;
            Vector2 waypoint = Path[CurrentWaypoint];
            //距路点足够近（至少 8 单位）时切换到下一个路点
            if (Vector2.Distance(transform.position, waypoint) <= Mathf.Max(8f, radius))
            {
                CurrentWaypoint++;
                return;
            }

            //匀速朝当前路点移动
            transform.position = Vector2.MoveTowards(transform.position, waypoint, speed * Time.deltaTime);
        }

        //刷新路径线渲染：把路径点写入 LineRenderer
        private void RefreshRouteLine()
        {
            if (routeLine == null) return;
            routeLine.positionCount = Path != null ? Path.Count : 0;
            for (int i = 0; i < Path.Count; i++) routeLine.SetPosition(i, new Vector3(Path[i].x, Path[i].y, 0f));
        }

        //返回当前状态文本，供 UI 面板显示（无可行路径/已抵达/路径进度）
        public string Status()
        {
            if (Path == null || Path.Count == 0) return "无可行路径";
            if (CurrentWaypoint >= Path.Count) return "已抵达目标";
            return $"路径节点 {CurrentWaypoint}/{Path.Count - 1}";
        }
    }
}
