using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 解耦的 CDT 风格导航服务。
    /// 本迁移版使用障碍约束顶点构成可见性图，A* 搜索走廊，再做漏斗式/视线收束；
    /// 单位动态避让留在 steering 层，不重建静态导航网。
    /// </summary>
    public sealed class CdtNavigationSystem : MonoBehaviour
    {
        //导航世界的可行走区域边界
        public Rect WorldBounds { get; private set; }
        //场景中的静态障碍矩形列表
        public readonly List<Rect> Obstacles = new List<Rect>();
        //已注册到导航服务的单位（智能体）列表
        public readonly List<UnitBase> Agents = new List<UnitBase>();
        //软体人群：位置去重叠的迭代次数（1~2 轮即可，多轮会过校正）
        private const int OverlapRelaxIterations = 2;
        //软体人群：允许的最大重叠深度占双方半径和的比例（0.45 = 最多嵌合 55%）
        private const float MaxOverlapFraction = .45f;

        //配置导航世界边界与静态障碍，初始化时调用一次
        public void Configure(Rect boundary, IEnumerable<Rect> obstacles)
        {
            WorldBounds = boundary;
            Obstacles.Clear();
            Obstacles.AddRange(obstacles);
        }

        //注册单位为导航智能体，参与避让计算（去重）
        public void RegisterAgent(UnitBase agent)
        {
            if (agent != null && !Agents.Contains(agent)) Agents.Add(agent);
        }

        //软体人群：物理步进后的位置级去重叠（LateUpdate 时机由 UnitBase 驱动调用）。
        //把超过允许重叠深度的邻居对沿连线各推开一半超出量——位置修正不清零速度，
        //单位保持动量，密集时表现为"挤而缓行"而非冻结。O(n²) 每帧两轮，n=100 时可接受。
        public void ResolveOverlaps()
        {
            int count = Agents.Count;
            if (count < 2) return;
            for (int iteration = 0; iteration < OverlapRelaxIterations; iteration++)
            {
                for (int i = 0; i < count; i++)
                {
                    UnitBase a = Agents[i];
                    if (a == null) continue;
                    for (int j = i + 1; j < count; j++)
                    {
                        UnitBase b = Agents[j];
                        if (b == null) continue;
                        //空军不参与地面去重叠
                        if (a.IsAirUnit || b.IsAirUnit) continue;
                        Vector2 offset = (Vector2)b.transform.position - (Vector2)a.transform.position;
                        float distance = offset.magnitude;
                        //双方碰撞半径和的 55% 为"允许贴合线"，低于此才推开（允许轻微嵌合，视觉上仍像密集人群）
                        float minDistance = (a.NavigationCollisionRadius + b.NavigationCollisionRadius) * MaxOverlapFraction * 2f;
                        if (distance >= minDistance) continue;
                        if (distance <= .01f)
                        {
                            //完全重叠：按索引确定性方向分开，避免抖动
                            offset = new Vector2(((i + j) % 2) == 0 ? 1f : -1f, .5f).normalized;
                            distance = 1f;
                        }
                        Vector2 direction = offset / distance;
                        float push = (minDistance - distance) * .5f;
                        //质量加权：重的少动、轻的多动
                        float massA = a.MovementMass;
                        float massB = b.MovementMass;
                        float totalMass = massA + massB;
                        a.transform.position -= (Vector3)(direction * push * (massB / totalMass));
                        b.transform.position += (Vector3)(direction * push * (massA / totalMass));
                    }
                }
            }
        }

        //注销单位，单位销毁时调用
        public void UnregisterAgent(UnitBase agent)
        {
            Agents.Remove(agent);
        }

        //查询某坐标附近是否存在空军单位（对空射程判定用）：
        //目标点附近 radius 范围内有注册空军即视为空中目标
        public bool HasAirUnitNear(Vector2 position, float radius)
        {
            float sqrRadius = radius * radius;
            for (int i = 0; i < Agents.Count; i++)
            {
                UnitBase agent = Agents[i];
                if (agent == null || !agent.IsAirUnit) continue;
                if (((Vector2)agent.transform.position - position).sqrMagnitude <= sqrRadius) return true;
            }
            return false;
        }

        //寻路入口：根据障碍膨胀顶点与网格采样点构建可见性图，A* 搜索后再做视线收束，返回起点到终点的路径点列表
        public List<Vector2> RequestPath(Vector2 start, Vector2 goal, float radius)
        {
            //路径点先放入起点与终点
            var points = new List<Vector2> { start, goal };
            //障碍按单位半径外扩，保证通行净空
            float clearance = radius + 8f;
            //额外多退 1 单位：Rect.Contains 判定含边界，角点若恰在外扩矩形边缘会被误判为不可行走，
            //导致可见性图在窄通道处断链（A* 找不到路→单位直线冲墙堆积）
            float margin = clearance + 1f;
            foreach (Rect obstacle in Obstacles)
            {
                //每个障碍的四个外扩角点作为可见性图节点（含 1 单位安全余量）
                points.Add(new Vector2(obstacle.xMin - margin, obstacle.yMin - margin));
                points.Add(new Vector2(obstacle.xMin - margin, obstacle.yMax + margin));
                points.Add(new Vector2(obstacle.xMax + margin, obstacle.yMin - margin));
                points.Add(new Vector2(obstacle.xMax + margin, obstacle.yMax + margin));
            }

            //按固定步长在世界内采样网格点，补充开阔区域的可行走节点
            //y 从 60 起步：保证 y=700 这类居中通道线上有采样点（56 宽窄口内无 140 起步的采样点）
            for (int x = 160; x < WorldBounds.xMax; x += 160)
            for (int y = 60; y < WorldBounds.yMax; y += 160)
            {
                Vector2 sample = new Vector2(x, y);
                if (IsWalkable(sample, radius)) points.Add(sample);
            }

            //通道轴线加密采样：在 y=WorldBounds 中心线上按 80 步长补点，
            //保证窄通道/漏斗轴线处有密集节点，A* 不会因采样稀疏而断链
            float axisY = (WorldBounds.yMin + WorldBounds.yMax) * .5f;
            for (int x = 80; x < WorldBounds.xMax; x += 80)
            {
                Vector2 sample = new Vector2(x, axisY);
                if (IsWalkable(sample, radius)) points.Add(sample);
            }

            //在可见性图上执行 A* 搜索，再对结果做视线收束简化
            var path = SearchVisibilityGraph(points, radius);
            return ShortenPath(path, radius);
        }

        //动态避让（steering 层）：根据周围单位位置对期望速度施加分离/绕行/超车修正，返回最终速度
        //设计原则：转向力只改变方向、不突破单位自身移速（修复通道内超速）；
        //分离纵向压缩、横向保全（排队收紧但保留超车通道）；按速度差超车与阵营无关，
        //让不同移速的单位在拥挤中自然分层排序（快车绕过慢车）
        public Vector2 SteerVelocity(UnitBase agent, Vector2 desiredVelocity, float radius)
        {
            //速度上限基准：所有修正后仍不超过单位自身期望移速
            float maxSpeed = Mathf.Max(1f, desiredVelocity.magnitude);
            Vector2 steering = desiredVelocity;
            //当前行进方向（用于判断前方单位）
            Vector2 travelDirection = desiredVelocity.sqrMagnitude > .01f ? desiredVelocity.normalized : Vector2.zero;
            //行进方向的侧向法线（用于车道占用判定与超车方向）
            Vector2 sideAxis = travelDirection.sqrMagnitude > .01f ? new Vector2(-travelDirection.y, travelDirection.x) : Vector2.zero;
            //本单位的冲击力权重，用于决定避让主从关系
            float agentImpact = agent != null ? agent.MovementImpact : 0f;
            //本单位的阵营 ID，友方单位才做绕行协作
            string faction = agent != null ? agent.FactionId : string.Empty;
            agent?.SetNavigationTrafficState("clear");

            //分离力/绕行力/超车力分开累计，最后分别封顶，避免多邻居叠加后矢量爆表
            Vector2 separationForce = Vector2.zero;
            Vector2 detourForce = Vector2.zero;
            Vector2 overtakeForce = Vector2.zero;

            //倒序遍历所有智能体，顺便清理已销毁的引用
            for (int i = Agents.Count - 1; i >= 0; i--)
            {
                UnitBase other = Agents[i];
                if (other == null) { Agents.RemoveAt(i); continue; }
                if (other == agent) continue;
                //空军与空军之间不互相避让（表现为可聚团）；地面单位也不避让空军
                if (other.IsAirUnit) continue;

                //与对方的位置偏移向量及距离
                Vector2 offset = (Vector2)agent.transform.position - (Vector2)other.transform.position;
                float distance = offset.magnitude;
                if (distance <= .01f) continue;
                //双方碰撞半径之和再加安全余量，作为期望间距
                float separation = radius + other.CollisionAvoidanceRadius + 4f;
                //远离对方的单位方向
                Vector2 awayDirection = offset.normalized;
                //对方相对本单位的方位
                Vector2 otherDirection = -awayDirection;
                //对方是否在本单位前方（点积判定）
                bool isFront = travelDirection.sqrMagnitude > .01f && Vector2.Dot(travelDirection, otherDirection) > .45f;
                //是否同阵营（友军）
                bool isFriendly = !string.IsNullOrEmpty(faction) && faction == other.FactionId;

                if (isFriendly && isFront && distance < separation * 2.2f)
                {
                    if (agentImpact < other.MovementImpact)
                    {
                        //冲击性低的单位主动侧向绕开前方的高冲击友军
                        Vector2 side = new Vector2(-otherDirection.y, otherDirection.x);
                        //用实例 ID 奇偶决定绕行方向，避免全部挤向一侧
                        float sideSign = agent.GetInstanceID() % 2 == 0 ? -1f : 1f;
                        detourForce += side * sideSign * Mathf.Max(0f, separation * 2.2f - distance) * 2.5f;
                        //同时保持一定间距
                        separationForce += awayDirection * Mathf.Max(0f, separation - distance);
                        agent.SetNavigationTrafficState("detour");
                    }
                    else
                    {
                        //冲击性高的单位把前方低冲击友军轻推开，自己保持前进
                        //力度收敛：过大的 AddForce 只会造成单帧速度尖峰（表现为抽搐），推挤效果由碰撞体自然完成
                        float pushStrength = (agentImpact - other.MovementImpact + .5f) * 120f;
                        other.ReceiveImpactPush(otherDirection, pushStrength);
                        agent.SetNavigationTrafficState("push");
                    }
                }
                else
                {
                    //速度差超车：前方单位实际速度明显低于自己可达到的速度 → 车道被占用，横向绕行。
                    //只看实际速度差、与阵营无关——不同移速的单位拥挤时会自然分流（快车绕行、慢车留后）
                    if (isFront && distance < separation * 2.2f && sideAxis.sqrMagnitude > .01f)
                    {
                        float otherSpeed = other.CurrentVelocity.magnitude;
                        float speedAdvantage = maxSpeed - otherSpeed;
                        //明显更快（相对 >35% 且绝对差 >12）才触发，避免同速单位互相乱钻
                        if (speedAdvantage > maxSpeed * .35f && speedAdvantage > 12f)
                        {
                            //车道判定：横向偏移小于双方半径和的 1.1 倍才算挡道（斜前方但不挡道的不管）
                            float lateralOffset = Vector2.Dot(offset, sideAxis);
                            if (Mathf.Abs(lateralOffset) < (radius + other.CollisionAvoidanceRadius) * 1.1f)
                            {
                                //超车方向：延续自己相对对方所在的一侧（稳定不抖动）；
                                //恰好在正后方时用实例 ID 奇偶决定，避免全部挤向同一侧
                                float sideSign = Mathf.Abs(lateralOffset) > 2f ? Mathf.Sign(lateralOffset)
                                    : (agent.GetInstanceID() % 2 == 0 ? -1f : 1f);
                                float proximity = 1f - distance / (separation * 2.2f);
                                overtakeForce += sideAxis * sideSign * speedAdvantage * proximity;
                                agent.SetNavigationTrafficState("overtake");
                            }
                        }
                    }

                    if (distance < separation * 1.5f)
                    {
                        //分离强度按方位加权：正前方邻居权重最高、身后邻居几乎忽略（身后让行由对方负责）。
                        //不加权时密集人群中四周分离力对称抵消、方向场变成乱流，净位移趋近零——即"凝滞"
                        float ahead = travelDirection.sqrMagnitude > .01f ? Vector2.Dot(travelDirection, otherDirection) : 0f;
                        float weight = Mathf.Clamp01(.5f + .5f * ahead);
                        separationForce += awayDirection * (separation * 1.5f - distance) * weight;
                        agent.SetNavigationTrafficState("separate");
                    }
                }
            }

            //纵向/横向分解：流动压缩只作用于纵向（排队链收紧防追尾），
            //横向分离保持全强度——横向分量是快车滑向空隙、绕过慢车的通道，
            //若一并打折（旧版做法）人群会失去超车能力，变成"整体蠕动"
            if (travelDirection.sqrMagnitude > .01f)
            {
                float along = Vector2.Dot(separationForce, travelDirection);
                Vector2 alongPart = travelDirection * along;
                Vector2 lateralPart = separationForce - alongPart;
                separationForce = lateralPart + alongPart * .4f;
            }

            //分离/绕行/超车各自封顶（相对自身移速），保证叠加后仍以前进分量为主动力
            steering += Vector2.ClampMagnitude(separationForce, maxSpeed * .8f);
            steering += Vector2.ClampMagnitude(detourForce, maxSpeed * .55f);
            steering += Vector2.ClampMagnitude(overtakeForce, maxSpeed * .6f);

            //限速：最终速度不超过单位自身期望移速（修复通道内加速超速现象）
            Vector2 result = Vector2.ClampMagnitude(steering, maxSpeed);

            //前进保底：拥挤时分离力可能把推进分量抵消到接近零，
            //保证至少 35% 移速朝向目标，配合零摩擦碰撞体让拥堵人流缓慢滑行而非冻结
            if (travelDirection.sqrMagnitude > .01f)
            {
                float forward = Vector2.Dot(result, travelDirection);
                float minForward = maxSpeed * .35f;
                if (forward < minForward)
                {
                    result += travelDirection * (minForward - forward);
                    result = Vector2.ClampMagnitude(result, maxSpeed);
                }
            }
            return result;
        }

        //A* 搜索可见性图：起点索引 0、终点索引 1，f = g + 到终点的直线距离
        private List<Vector2> SearchVisibilityGraph(List<Vector2> points, float radius)
        {
            int count = points.Count;
            var g = new float[count];
            var f = new float[count];
            var cameFrom = new int[count];
            var open = new List<int> { 0 };
            var closed = new bool[count];
            //初始化所有节点的代价与回溯指针
            for (int i = 0; i < count; i++) { g[i] = float.PositiveInfinity; f[i] = float.PositiveInfinity; cameFrom[i] = -1; }
            g[0] = 0f;
            f[0] = Vector2.Distance(points[0], points[1]);

            while (open.Count > 0)
            {
                //线性扫描取 f 值最小的开放节点
                int current = open[0];
                for (int i = 1; i < open.Count; i++) if (f[open[i]] < f[current]) current = open[i];
                //到达终点即回溯路径并返回
                if (current == 1) return Reconstruct(points, cameFrom, current);
                open.Remove(current);
                closed[current] = true;

                //尝试从当前节点到其余所有节点的连线，只保留可行走且更优的
                for (int next = 0; next < count; next++)
                {
                    if (next == current || closed[next]) continue;
                    if (!SegmentWalkable(points[current], points[next], radius)) continue;
                    float tentative = g[current] + Vector2.Distance(points[current], points[next]);
                    if (tentative >= g[next]) continue;
                    cameFrom[next] = current;
                    g[next] = tentative;
                    f[next] = tentative + Vector2.Distance(points[next], points[1]);
                    if (!open.Contains(next)) open.Add(next);
                }
            }

            //搜索失败时退化为直线（起点直连终点）
            return new List<Vector2> { points[0], points[1] };
        }

        //从 cameFrom 回溯指针链重建路径（终点到起点），再反转为起点到终点
        private static List<Vector2> Reconstruct(List<Vector2> points, int[] cameFrom, int current)
        {
            var result = new List<Vector2>();
            while (current >= 0) { result.Add(points[current]); current = cameFrom[current]; }
            result.Reverse();
            return result;
        }

        //视线收束（字符串拉直）：只要锚点能直线走到更远的路径点，就跳过中间节点
        private List<Vector2> ShortenPath(List<Vector2> path, float radius)
        {
            if (path == null || path.Count <= 2) return path ?? new List<Vector2>();
            var result = new List<Vector2> { path[0] };
            int anchor = 0;
            while (anchor < path.Count - 1)
            {
                //从最远端向前找第一个可直接到达的点
                int candidate = path.Count - 1;
                while (candidate > anchor + 1 && !SegmentWalkable(path[anchor], path[candidate], radius)) candidate--;
                result.Add(path[candidate]);
                anchor = candidate;
            }
            return result;
        }

        //判定单个点是否可行走：必须在边界内且不落入外扩后的障碍
        //使用"严格包含"语义：落在外扩矩形边缘上的点视为可行走（留出贴墙通行的余量）
        private bool IsWalkable(Vector2 point, float radius)
        {
            if (point.x < WorldBounds.xMin || point.x > WorldBounds.xMax) return false;
            if (point.y < WorldBounds.yMin || point.y > WorldBounds.yMax) return false;
            foreach (Rect obstacle in Obstacles)
            {
                Rect expanded = Expand(obstacle, radius + 2f);
                //点在扩张矩形内部（不含边缘）才算撞障碍
                if (point.x > expanded.xMin && point.x < expanded.xMax &&
                    point.y > expanded.yMin && point.y < expanded.yMax) return false;
            }
            return true;
        }

        //供单位自查：当前位置按自身半径是否与障碍重叠（贴墙/卡进障碍时返回 true）
        public bool IsStuckAgainstObstacle(Vector2 position, float radius)
        {
            if (!WorldBounds.Contains(position)) return true;
            foreach (Rect obstacle in Obstacles)
                if (Expand(obstacle, radius * .8f).Contains(position)) return true;
            return false;
        }

        //供单位自查：从当前位置到目标点是否直线可达（不可达说明需要绕行重寻路）
        public bool HasDirectLine(Vector2 start, Vector2 goal, float radius)
        {
            return SegmentWalkable(start, goal, radius);
        }

        //判定线段是否可行走：对端点及沿线采样点逐一做可行走检测
        private bool SegmentWalkable(Vector2 start, Vector2 goal, float radius)
        {
            if (!IsWalkable(start, radius) || !IsWalkable(goal, radius)) return false;
            float distance = Vector2.Distance(start, goal);
            //采样密度与距离和半径相关，保证短段也有足够精度
            int samples = Mathf.Max(2, Mathf.CeilToInt(distance / Mathf.Max(10f, radius * 1.5f)));
            for (int i = 1; i < samples; i++)
            {
                Vector2 point = Vector2.Lerp(start, goal, i / (float)samples);
                if (!IsWalkable(point, radius)) return false;
            }
            return true;
        }

        //返回矩形向四周均匀外扩 amount 后的新矩形
        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.xMin - amount, rect.yMin - amount, rect.width + amount * 2f, rect.height + amount * 2f);
        }

        //编辑器/Scene 视图绘制障碍框（红色线框），便于调试
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(.82f, .38f, .42f, .7f);
            foreach (Rect obstacle in Obstacles) Gizmos.DrawWireCube(obstacle.center, obstacle.size);
        }
    }
}
