using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 通用战斗单位：负责沿导航路径推进、按射程攻击训练假人、
    /// 处理生命/护盾恢复，并通过事件向外部报告伤害与死亡。
    /// 支持陆军（物理碰撞+互相避让）与空军（无碰撞、可聚团、悬浮动画）两种模式，
    /// 陆军被障碍卡住时会自动检测并重新规划路径。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class UnitBase : MonoBehaviour
    {
        //造成伤害事件：参数为本单位、单位数据与本次伤害值
        public event Action<UnitBase, UnitData, float> DamageDealt;
        //单位死亡事件
        public event Action<UnitBase> UnitDied;

        //单位静态数据（属性、种族特性等）
        public UnitData Data { get; private set; }
        //是否空军：空军无碰撞体、不参与地面避让、可聚团、带飞行动画
        public bool IsAirUnit { get; private set; }
        //当前导航通行状态（clear/detour/push/separate），用于调试展示
        public string NavigationTrafficState { get; private set; } = "clear";
        //单位冲击性（决定避让主从关系）
        public float MovementImpact => Data != null ? Data.Impact : 0f;
        //阵营 ID
        public string FactionId => Data != null ? Data.Faction : string.Empty;
        //碰撞避让半径（空军返回 0，地面单位互相不避让空军）
        public float CollisionAvoidanceRadius => Data == null || IsAirUnit ? 0f : Data.CollisionRadius;
        //位置去重叠用的物理半径（空军无碰撞体，不参与）
        public float NavigationCollisionRadius => Data == null || IsAirUnit ? 0f : Data.CollisionRadius;
        //去重叠的质量权重（与刚体质量一致：重的单位被推得少）
        public float MovementMass => body != null ? body.mass : 1f;
        //累计造成的总伤害（对象池复用时由 ResetForReuse 清零）
        public float TotalDamage { get; private set; }
        //累计攻击次数（对象池复用时清零）
        public int AttackCount { get; private set; }
        //当前生命值
        public float CurrentHealth => currentHealth;
        //当前护盾值
        public float CurrentShield => currentShield;
        //当前实际移动速度（用于动画驱动）
        public float CurrentSpeed => currentSpeed;
        //当前刚体速度（供导航层做同向流动判定）
        public Vector2 CurrentVelocity => body != null ? body.velocity : Vector2.zero;
        //状态系统（技能 buff 等临时状态的宿主；无技能的单位也存在，方便统一访问）
        public StatusSystem Status { get; private set; }
        //主动技能组件（无技能的单位为 null）
        public AbilityComponent Ability { get; private set; }
        //是否已与敌人交战过（终身粘性标记：兴奋剂类技能的历史交战参考）
        public bool HasEngagedInCombat { get; private set; }
        //当前是否正与真实敌方单位交战（敌对目标在射程内，逐帧刷新）——
        //自动技能（兴奋剂等）的施放门槛：行军途中、打假人、被打后脱离都不算"遇到敌人"
        public bool IsEngagingEnemy { get; private set; }

        //软体人群模式开关（场景级）：开启后单位之间不发生物理硬碰撞（与墙仍硬碰撞），
        //改由导航层的"位置去重叠"负责防互叠。密集拥堵时速度不会被接触冲量清零，消除凝滞。
        public static bool SoftBodyCrowd = false;
        //软体层碰撞矩阵是否已配置（只需一次）
        private static bool softBodyLayerConfigured;

        //攻击目标（训练假人）
        private TrainingDummy target;
        //敌方单位注册表（单位对单位交火用；null = 无敌对模式，只打假人）
        private readonly List<UnitBase> enemyUnits = new List<UnitBase>();
        //当前锁定的敌方单位
        private UnitBase unitTarget;
        //纯目标点模式：不攻击假人，只移动到目标点（通道测试用）
        private bool useGoalPoint;
        private Vector2 goalPoint;
        //纯目标点模式的抵达半径
        private float goalArriveRadius = 90f;
        //导航系统引用
        private CdtNavigationSystem navigation;
        //2D 刚体与圆形碰撞体（空军会在初始化时销毁碰撞体）
        private Rigidbody2D body;
        private CircleCollider2D circleCollider;
        //当前路径与路点索引
        private List<Vector2> path = new List<Vector2>();
        private int pathIndex;
        //当前生命/护盾值
        private float currentHealth;
        private float currentShield;
        //攻击冷却计时
        private float attackTimer;
        //进入射程后的待机计时
        private float idleTimer;
        //恢复计时（超过恢复延迟后开始回血/回盾）
        private float regenerationTimer;
        //当前实际速度（每帧由位移估算）
        private float currentSpeed;

        //卡死检测：连续低速累计时长
        private float stuckTimer;
        //卡死窗口锚点：记录开始累计卡住时的位置，用净位移区分"真卡死"与"拥堵缓行"
        private Vector2 stuckAnchorPosition;
        //重寻路冷却（防止频繁重算路径）
        private float repathCooldown;
        //上次帧位置（用于估算实际速度）
        private Vector2 lastPosition;

        //飞行动画：悬浮相位与影子引用
        private float bobPhase;
        private Transform visualTransform;
        private SpriteRenderer shadowRenderer;

        //头顶血条（Setup 时挂载/复用）
        private HealthBar healthBar;

        //合围机动：是否已选中环上空位（粘性选位，防止相邻单位互相抢位抖动）
        private bool encircleHasSlot;
        //合围机动：空位重扫倒计时（到时重估，友军阵亡后及时补位收紧弧线）
        private float encircleRescanTimer;
        //合围机动：当前锁定的环上空位世界坐标
        private Vector2 encircleTarget;

        //对象池复用前的状态复位：清空引用与计时，防止上一次生命周期的数据残留。
        //Setup() 会重新填充全部字段，这里只做"清空"
        public void ResetForReuse()
        {
            //从旧导航系统注销（新 Setup 会注册到新系统）
            if (navigation != null) navigation.UnregisterAgent(this);
            navigation = null;
            target = null;
            unitTarget = null;
            enemyUnits.Clear();
            TeamId = string.Empty;
            path.Clear();
            pathIndex = 0;
            attackTimer = 0f;
            idleTimer = 0f;
            stuckTimer = 0f;
            repathCooldown = 0f;
            regenerationTimer = 0f;
            currentHealth = 0f;
            currentShield = 0f;
            currentSpeed = 0f;
            TotalDamage = 0f;
            AttackCount = 0;
            HasEngagedInCombat = false;
            IsEngagingEnemy = false;
            //合围机动状态一并清空（复用单位不应继承上一世的绕行记忆）
            encircleHasSlot = false;
            encircleRescanTimer = 0f;
            Data = null;
            Status = null;
            Ability = null;
            IsAirUnit = false;
            visualTransform = null;
            shadowRenderer = null;
            //清空事件订阅者（旧场景的回调不应跨生命周期存活）
            DamageDealt = null;
            UnitDied = null;
            //清掉空军视觉子物体（影子等），地面复用时不应残留
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "Shadow" || child.name == "HealthBar") UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        //初始化：配置刚体、碰撞体、外观，请求初始路径并注册到导航系统
        public void Setup(UnitData data, TrainingDummy trainingDummy, CdtNavigationSystem nav, Sprite visual, bool airUnit = false)
        {
            Data = data;
            target = trainingDummy;
            useGoalPoint = trainingDummy == null;
            navigation = nav;
            IsAirUnit = airUnit;
            body = GetComponent<Rigidbody2D>();
            circleCollider = GetComponent<CircleCollider2D>();

            //俯视角 2D 物理参数：无重力、高阻尼、锁定旋转、连续碰撞检测
            body.gravityScale = 0f;
            body.drag = 7f;
            body.angularDrag = 7f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            //质量随体积与冲击性增大，让高冲击单位在碰撞中占优
            body.mass = Mathf.Max(1f, data.UnitVolume * (1f + data.Impact));
            // The visual sprite is scaled to the gameplay radius, so keep the
            // local collider at 0.5 to produce an actual world-space radius
            // equal to data.CollisionRadius after transform scaling.
            circleCollider.radius = .5f;
            //无摩擦无弹性的物理材质，避免单位互相粘连或反弹
            var material = new PhysicsMaterial2D($"{data.UnitId}_NoFriction") { friction = 0f, bounciness = 0f };
            circleCollider.sharedMaterial = material;
            //软体人群模式：本层单位互相不碰撞（墙在 Default 层，硬碰撞保留），
            //接触冲量不再清零顶住单位的速度——这是密集时凝滞的物理根源
            if (SoftBodyCrowd)
            {
                int layer = LayerMask.NameToLayer("Ignore Raycast");
                gameObject.layer = layer;
                //层内互撞只关一次：Ignore Raycast 层的单位彼此不碰撞，与 Default 层（墙体）仍碰撞
                if (!softBodyLayerConfigured)
                {
                    Physics2D.IgnoreLayerCollision(layer, layer, true);
                    softBodyLayerConfigured = true;
                }
            }

            //外观：使用单位主题色着色，按碰撞半径缩放精灵
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = visual;
            renderer.color = data.AccentColor;
            transform.localScale = Vector3.one * (data.CollisionRadius * 2f);
            //略微抬高绘制层级，避免与背景重叠
            transform.position = new Vector3(transform.position.x, transform.position.y, -1f);

            if (IsAirUnit)
            {
                //空军：禁用碰撞体（RequireComponent 依赖不能销毁，禁用后同样不参与物理）
                //并退出刚体碰撞参与（可聚团、穿过地面单位）
                circleCollider.enabled = false;
                circleCollider = null;
                body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
                body.mass = 1f;
                //空军绘制在更高层级，视觉上"悬浮"在陆军之上
                renderer.sortingOrder = 5;
                renderer.color = new Color(data.AccentColor.r, data.AccentColor.g, data.AccentColor.b, .92f);
                SetupFlightVisual(renderer);
            }

            //初始化生命/护盾
            currentHealth = data.MaxHealth;
            currentShield = data.MaxShield;
            regenerationTimer = 0f;
            lastPosition = transform.position;

            //头顶血条：复用已有组件（对象池回收重挂）或新建
            healthBar = GetComponent<HealthBar>();
            if (healthBar == null) healthBar = gameObject.AddComponent<HealthBar>();
            healthBar.Attach(this, renderer);

            //状态系统与技能组件：复用池回收单位上已有的组件（AddComponent 会重复叠加，
            //导致 N 个技能组件各自独立施放兴奋剂、连环扣血），没有才新建
            Status = GetComponent<StatusSystem>();
            if (Status == null) Status = gameObject.AddComponent<StatusSystem>();
            Status.Initialize(this, data);
            if (data.Abilities != null && data.Abilities.Length > 0)
            {
                Ability = GetComponent<AbilityComponent>();
                if (Ability == null) Ability = gameObject.AddComponent<AbilityComponent>();
                //当前版本一个单位一个主动技能（取第一个），多技能扩展时改为列表
                Ability.Initialize(this, Status, data.Abilities[0]);
            }
            //纯目标点模式下 Setup 时目标点尚未设置（SetGoalPoint 在之后调用），
            //此时不寻路——留给 SetGoalPoint 触发首次寻路，避免误冲向默认 (0,0)
            if (!useGoalPoint)
            {
                path = navigation.RequestPath(transform.position, target.transform.position, NavigationRadius());
                pathIndex = path.Count > 1 ? 1 : 0;
            }
            else
            {
                path = new List<Vector2>();
                pathIndex = 0;
            }
            navigation.RegisterAgent(this);
        }

        //注册敌方单位列表（战斗场景用）：传入所有敌方单位，交火时从中选择最近目标。
        //teamId 是敌我判定的权威依据（同种族对战时双方 FactionId 相同，只能靠队伍区分）
        public void SetEnemyUnits(IEnumerable<UnitBase> enemies, string teamId = null)
        {
            if (!string.IsNullOrEmpty(teamId)) TeamId = teamId;
            enemyUnits.Clear();
            if (enemies != null) enemyUnits.AddRange(enemies);
        }

        //战斗队伍归属（blue/red），由场景在注册敌我关系时写入；空串表示未参战
        public string TeamId { get; private set; } = string.Empty;

        //纯目标点模式设置：通道测试等单位只需移动到某点、无需攻击假人时使用
        //设置后立即重新寻路（覆盖 Setup 期的空路径或旧目标路径）
        public void SetGoalPoint(Vector2 point, float arriveRadius = 90f)
        {
            bool changed = useGoalPoint != true || goalPoint != point;
            useGoalPoint = true;
            goalPoint = point;
            goalArriveRadius = arriveRadius;
            //目标变化时立即重寻路，让单位马上转向新目标
            if (changed && navigation != null && Data != null)
            {
                path = navigation.RequestPath(transform.position, goalPoint, NavigationRadius());
                pathIndex = path.Count > 1 ? 1 : 0;
            }
        }

        //搭建空军视觉：子物体缩放摆动（机体）+ 地面影子（椭圆暗斑）
        private void SetupFlightVisual(SpriteRenderer bodyRenderer)
        {
            bobPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            //机体本体缩小些，留出悬浮视觉空间
            visualTransform = bodyRenderer.transform;
            //影子：本体下的暗色椭圆（用方形压扁近似），随悬浮高度缩放
            GameObject shadow = new GameObject("Shadow");
            shadow.transform.SetParent(transform, false);
            shadow.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = bodyRenderer.sprite;
            shadowRenderer.color = new Color(0f, 0f, 0f, .30f);
            shadowRenderer.transform.localScale = new Vector3(1.1f, .5f, 1f);
            shadowRenderer.sortingOrder = -1;
        }

        //本单位的导航半径：空军以视觉尺寸寻路但不被墙碰撞体阻挡（净空要求减半）
        private float NavigationRadius()
        {
            return Data == null ? 10f : IsAirUnit ? Data.CollisionRadius * .5f : Data.CollisionRadius;
        }

        //按目标所在域取对应射程：空军目标用对空射程，其余用对地射程
        private float AttackRangeFor(Vector2 destination)
        {
            if (Data == null) return 0f;
            bool targetIsAir = IsAirDomainTarget(destination);
            return targetIsAir ? Data.AttackRangeAir : Data.AttackRange;
        }

        //判定某坐标处是否为空中目标：目标自身注册了空军单位时按其所在域判定，
        //否则用同阵营注册表回退（训练假人恒为地面目标，返回 false）
        private bool IsAirDomainTarget(Vector2 destination)
        {
            //训练假人是地面建筑，永远用对地射程
            if (target != null && !useGoalPoint) return false;
            //纯目标点模式：查询导航系统注册的空军（无则视为地面）
            return navigation != null && navigation.HasAirUnitNear(destination, Data.CollisionRadius * 3f);
        }

        //更新导航通行状态（由导航系统回调）
        public void SetNavigationTrafficState(string state)
        {
            NavigationTrafficState = state;
        }

        //接收外部冲击推力（高冲击单位推挤友军时调用）
        public void ReceiveImpactPush(Vector2 direction, float strength)
        {
            if (body == null || Data == null) return;
            body.AddForce(direction.normalized * strength, ForceMode2D.Force);
        }

        //固定帧主逻辑：恢复计时、寻路跟随或进入射程攻击
        private void FixedUpdate()
        {
            if (Data == null || navigation == null) return;
            //无敌对单位且既无假人也没有目标点时无事可做（战斗场景单位走敌对注册表）
            //冷却递减、恢复计时递增，超过延迟后按每秒速率回血/回盾（不超过上限）
            attackTimer = Mathf.Max(0f, attackTimer - Time.fixedDeltaTime);
            repathCooldown = Mathf.Max(0f, repathCooldown - Time.fixedDeltaTime);
            regenerationTimer += Time.fixedDeltaTime;
            if (regenerationTimer >= Data.HealthRegenerationDelay)
                currentHealth = Mathf.Min(Data.MaxHealth, currentHealth + Data.HealthRegenerationPerSecond * Time.fixedDeltaTime);
            if (regenerationTimer >= Data.ShieldRegenerationDelay)
                currentShield = Mathf.Min(Data.MaxShield, currentShield + Data.ShieldRegenerationPerSecond * Time.fixedDeltaTime);
            //当前移动目标点：敌对单位 > 纯目标点 > 假人
            bool hasUnitTarget = AcquireUnitTarget();
            Vector2 destination = hasUnitTarget ? (Vector2)unitTarget.transform.position
                : useGoalPoint ? goalPoint
                : target != null ? (Vector2)target.transform.position
                : goalPoint;
            //与目标的距离及有效射程（下限 62；射程属性 ×12 到世界尺度）。
            //单位对单位交火时只叠加目标自身碰撞半径（已是世界尺度，绝不能再 ×12），
            //近战贴身判定由此才成立——合围环带半径直接依赖这个值
            float distance = Vector2.Distance(transform.position, destination);
            float effectiveRange = hasUnitTarget
                ? Mathf.Max(62f, AttackRangeForUnit(unitTarget) * 12f) + unitTarget.Data.CollisionRadius
                : useGoalPoint ? goalArriveRadius
                : Mathf.Max(62f, AttackRangeFor(destination) * 12f);
            //逐帧刷新"正在交战"判定：敌对单位目标在射程内才成立（供自动技能门槛）。
            //行军途中、打训练假人、被远程点一下但目标已脱离，都不算"遇到敌人"
            IsEngagingEnemy = hasUnitTarget && distance <= effectiveRange;

            //合围机动分流：推进路上有友军挡道时，按冲击性决定"顶进推挤"还是"绕行找空位"。
            //空军无碰撞、可自由聚团，不需要合围机动；未挡路时交回下方正常寻路跟随
            if (hasUnitTarget && distance > effectiveRange && !IsAirUnit)
            {
                Vector2 encircleVelocity = ComputeEncircleVelocity(destination, effectiveRange, out bool encircleBlocked);
                if (encircleBlocked)
                {
                    //挡路：沿环带绕行寻找攻击空位（速度已含指向空位的转向与前进分量）
                    body.velocity = navigation.SteerVelocity(this, encircleVelocity, NavigationRadius());
                    UpdateAnimation();
                    DetectStuck();
                    return;
                }
            }

            if (distance > effectiveRange)
            {
                //未进射程：路径耗尽则重新寻路，否则跟随路点推进
                idleTimer = 0f;
                //纯目标点模式且无敌对目标时，路径为空说明 SetGoalPoint 尚未调用（同帧稍后才会设置），
                //本帧保持静止，避免用默认 (0,0) 当目标寻路
                if (!hasUnitTarget && useGoalPoint && path.Count == 0 && goalPoint == Vector2.zero)
                {
                    body.velocity = Vector2.zero;
                    UpdateAnimation();
                    return;
                }
                if (path.Count == 0 || pathIndex >= path.Count)
                {
                    path = navigation.RequestPath(transform.position, destination, NavigationRadius());
                    pathIndex = path.Count > 1 ? 1 : 0;
                }

                if (path.Count > 0 && pathIndex < path.Count)
                {
                    Vector2 waypoint = path[pathIndex];
                    //接近路点（阈值取 10 或碰撞半径）时切换下一个路点
                    if (Vector2.Distance(transform.position, waypoint) < Mathf.Max(10f, NavigationRadius()))
                        pathIndex++;
                    else
                    {
                        //期望速度 = 指向路点的方向 × 移速×27，经导航避让修正后写入刚体
                        Vector2 desired = ((Vector2)waypoint - (Vector2)transform.position).normalized * Data.EffectiveMoveSpeed * 27f;
                        body.velocity = navigation.SteerVelocity(this, desired, NavigationRadius());
                    }
                }
                else body.velocity = Vector2.zero;

                DetectStuck();
            }
            else
            {
                //已进射程/已抵达：停下待机，冷却结束、未被压制且能造成伤害时发起攻击
                body.velocity = Vector2.zero;
                stuckTimer = 0f;
                idleTimer += Time.fixedDeltaTime;
                bool canDealDamage = Data.AttackLightDamage > 0f || Data.AttackHeavyDamage > 0f;
                if (attackTimer <= 0f && canDealDamage && !Data.StatusSuppressAttack)
                {
                    if (hasUnitTarget) AttackUnitTarget();
                    else if (!useGoalPoint && target != null) AttackTarget();
                }
            }

            UpdateAnimation();
        }

        //卡死检测：区分"真卡死"与"拥堵缓行"——
        //用滑动窗口内的净位移判定：排队缓慢前进（有净位移）不算卡死，
        //长时间原地打转/顶墙（净位移≈0）才触发重寻路
        private void DetectStuck()
        {
            //空军不与墙碰撞，不会卡住
            if (IsAirUnit) { stuckTimer = 0f; return; }
            //实际速度由相邻帧位移估算
            Vector2 position = transform.position;
            float moved = Vector2.Distance(position, lastPosition);
            lastPosition = position;
            currentSpeed = moved / Mathf.Max(.001f, Time.fixedDeltaTime);

            //判定为"低速"的条件：瞬时速度极低，或贴近障碍物
            bool lowSpeed = currentSpeed < Data.EffectiveMoveSpeed * 27f * .18f;
            bool nearObstacle = navigation.IsStuckAgainstObstacle(position, Data.CollisionRadius);
            if (lowSpeed || nearObstacle)
            {
                if (stuckTimer <= 0f) stuckAnchorPosition = position;
                stuckTimer += Time.fixedDeltaTime;
            }
            else stuckTimer = Mathf.Max(0f, stuckTimer - Time.fixedDeltaTime * 2f);

            //持续低速 1.2 秒且重寻路冷却结束，且窗口内净位移确实几乎为零（< 半个身位）才重寻路。
            //拥堵排队的单位虽然瞬时速度低，但净位移在持续累积，不会走到这一步
            if (stuckTimer >= 1.2f && repathCooldown <= 0f)
            {
                float netDisplacement = Vector2.Distance(position, stuckAnchorPosition);
                if (netDisplacement < NavigationRadius() * .5f)
                {
                    Vector2 destination = useGoalPoint ? goalPoint : (Vector2)target.transform.position;
                    path = navigation.RequestPath(position + UnityEngine.Random.insideUnitCircle * NavigationRadius(),
                        destination, NavigationRadius());
                    pathIndex = path.Count > 1 ? 1 : 0;
                    repathCooldown = 1.2f;
                }
                //净位移足够（只是缓行）：重置计时，避免每 1.2 秒空转一次判定
                stuckTimer = 0f;
            }
        }

        //动画更新：陆军行走轻微摇摆，空军悬浮起伏+倾斜+影子缩放
        private void UpdateAnimation()
        {
            Vector2 velocity = body != null ? body.velocity : Vector2.zero;
            currentSpeed = velocity.magnitude;
            if (IsAirUnit)
            {
                //悬浮起伏：机体上下浮动 + 根据速度向前倾斜 + 影子随高度反向缩放
                bobPhase += Time.fixedDeltaTime * 5f;
                if (visualTransform != null)
                {
                    float bob = Mathf.Sin(bobPhase) * .12f;
                    float tilt = Mathf.Clamp(velocity.x * .0006f, -.18f, .18f);
                    visualTransform.localPosition = new Vector3(0f, bob, 0f);
                    visualTransform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    visualTransform.localScale = Vector3.one * (1f + Mathf.Sin(bobPhase * .7f) * .04f);
                }
                if (shadowRenderer != null)
                {
                    float lift = .9f + Mathf.Sin(bobPhase) * .1f;
                    shadowRenderer.transform.localScale = new Vector3(1.1f / lift, .5f / lift, 1f);
                }
            }
            else if (currentSpeed > 5f)
            {
                //陆军行走摇摆：小幅左右倾斜模拟步伐
                float sway = Mathf.Sin(Time.time * 10f + GetInstanceID() % 7) * .08f;
                visualTransform = visualTransform != null ? visualTransform : GetComponent<SpriteRenderer>().transform;
                visualTransform.localRotation = Quaternion.Euler(0f, 0f, sway);
            }
        }

        //敌我判定：优先按战斗队伍（TeamId）区分——同种族对战时双方 FactionId 相同，
        //种族只代表单位来源，不代表阵营；未分配队伍时退回种族判定（单人测试模式）
        public bool IsAlliedWith(UnitBase other)
        {
            if (other == null) return false;
            if (other == this) return true;
            //双方都有队伍归属时按队伍判定
            if (!string.IsNullOrEmpty(TeamId) && !string.IsNullOrEmpty(other.TeamId))
                return TeamId == other.TeamId;
            //任一方无队伍（如场景假人混合模式）：退回种族判定
            return FactionId == other.FactionId;
        }

        //从敌方注册表选择攻击目标：过滤死亡/射程外不可及者，取最近且本单位能攻击的（对空/对地）
        //每帧调用成本 O(n)，战斗场景单位数量级下可接受；返回是否有可用目标
        private bool AcquireUnitTarget()
        {
            unitTarget = null;
            if (enemyUnits.Count == 0 || Data == null) return false;
            float bestDistance = float.MaxValue;
            Vector2 origin = transform.position;
            for (int i = 0; i < enemyUnits.Count; i++)
            {
                UnitBase enemy = enemyUnits[i];
                //已阵亡（血量归零，池模式下对象仍存活）或同队伍跳过
                if (enemy == null || enemy.Data == null || enemy.CurrentHealth <= 0f) continue;
                if (IsAlliedWith(enemy)) continue;
                //对空能力判定：空军目标需要本单位 CanAttackAir
                if (enemy.IsAirUnit && !Data.CanAttackAir) continue;
                float distance = Vector2.Distance(origin, enemy.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    unitTarget = enemy;
                }
            }
            return unitTarget != null;
        }

        // ---------- 合围机动（弧形包夹）----------

        //合围核心：推进途中被友军堵住攻击线时的分流决策。
        //我方冲击更大 → 顶住友军向前推挤挤出通路，本体保持直行（blocked=false 交回正常寻路）；
        //同冲击友军占位 → 绕行：锁定目标周环上空位沿切线移动，友军越密搜得越远，30 单位渐成半圆弧。
        private Vector2 ComputeEncircleVelocity(Vector2 targetPosition, float effectiveRange, out bool blocked)
        {
            blocked = false;
            if (Data == null || navigation == null || unitTarget == null) return Vector2.zero;

            Vector2 selfPosition = transform.position;
            //目标→本单位方向，方位角以此为基准
            Vector2 radial = selfPosition - targetPosition;
            float currentRadius = radial.magnitude;
            if (radial.sqrMagnitude < 1f) return Vector2.zero;
            Vector2 radialDirection = radial / currentRadius;

            //① 挡路判定：攻击线（我→目标）上、攻击环内侧是否压着友军——外侧友军不构成阻挡
            UnitBase blocker = null;
            float blockerDistance = float.MaxValue;
            var agents = navigation.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                UnitBase ally = agents[i];
                if (!IsBlockingAlly(ally, selfPosition, targetPosition, currentRadius, radialDirection, effectiveRange)) continue;
                float d = Vector2.Distance((Vector2)ally.transform.position, selfPosition);
                if (d < blockerDistance) { blockerDistance = d; blocker = ally; }
            }
            if (blocker == null)
            {
                //攻击线畅通：清掉绕行状态，正常推进（速度供寻路为空时兜底直行）
                encircleHasSlot = false;
                return (targetPosition - selfPosition).normalized * Data.EffectiveMoveSpeed * 27f;
            }

            blocked = true;
            //② 冲击分流：我方冲击明显更大 → 顶住友军往前挤挤出通路；
            //本体 blocked=false 交回正常寻路继续推进（不能定在原地干推）
            if (MovementImpact > blocker.MovementImpact + .05f)
            {
                //接触距离内才施推力（够不着的不白推）
                if (blockerDistance < (Data.CollisionRadius + blocker.Data.CollisionRadius) * 3f)
                {
                    Vector2 pushDirection = ((Vector2)blocker.transform.position - selfPosition).normalized;
                    //推力朝前偏一点（混入朝目标的方向分量），让被推的友军沿通路向前滑而非乱飘
                    blocker.ReceiveImpactPush(pushDirection + radialDirection * .6f, (MovementImpact - blocker.MovementImpact + .5f) * 150f);
                }
                blocked = false;
                return Vector2.zero;
            }
            //③ 同冲击绕行：目标周环上锁定空位，沿环切线绕过去
            encircleRescanTimer -= Time.fixedDeltaTime;
            if (!encircleHasSlot || encircleRescanTimer <= 0f)
            {
                encircleRescanTimer = .55f;
                encircleHasSlot = TryAcquireEncircleSlot(targetPosition, radialDirection, effectiveRange);
            }
            if (!encircleHasSlot) return Vector2.zero;

            //绕行速度：指向环上空位（合成向量自然沿切线绕行），再交导航避让层约束
            return (encircleTarget - selfPosition).normalized * Data.EffectiveMoveSpeed * 27f;
        }

        //友军挡路判定：同队存活地面单位，压在攻击线走廊内、位于攻击环内侧、且不是当前锁定目标
        private bool IsBlockingAlly(UnitBase ally, Vector2 selfPosition, Vector2 targetPosition, float currentRadius, Vector2 radialDirection, float effectiveRange)
        {
            if (ally == null || ally == this || ally.Data == null || ally == unitTarget) return false;
            if (!IsAlliedWith(ally) || ally.CurrentHealth <= 0f) return false;
            if (ally.IsAirUnit) return false;

            Vector2 offset = (Vector2)ally.transform.position - selfPosition;
            float along = Vector2.Dot(offset, radialDirection);
            //在"我→目标"方向上：负值 = 友军在我身后，不可能挡路
            if (along <= 0f) return false;
            //友军必须已逼近攻击环（即将开火/正在占位）才算挡路——远处排队的友军不算，
            //此时队形保持排队推进由避让层处理；前面的友军到位后，后方单位才依次绕行合围
            float allyDistanceToTarget = currentRadius - along;
            if (allyDistanceToTarget > effectiveRange + ally.Data.CollisionRadius * 2f + 40f) return false;
            //走廊判定：横向偏离攻击线超过双方半径和即不挡道
            float lateral = Mathf.Abs(Vector2.Dot(offset, new Vector2(-radialDirection.y, radialDirection.x)));
            float corridorHalfWidth = Data.CollisionRadius + ally.Data.CollisionRadius;
            return lateral < corridorHalfWidth;
        }

        //在目标周环上寻找攻击空位：从当前方位角起按 ±6° 左右交替逐层外扩（最多 180°），
        //空位 = 该角度处环上点无同队地面单位贴近占位。友军越密集，可接受的空位角度越远——
        //大量单位从四面八方合拢时，弧线由近到远自然生长成半圆
        private bool TryAcquireEncircleSlot(Vector2 targetPosition, Vector2 radialDirection, float effectiveRange)
        {
            if (Data == null) return false;
            //环半径略小于有效射程：到达空位时必然已进射程开火（环设在射程外会导致
            //"到位后差半个身位进不了射程→继续绕行"的死循环抖动）
            float ringRadius = Mathf.Max(40f, effectiveRange - Data.CollisionRadius * 1.5f);
            //邻位间距：约一个身位再多一点，防贴合抖动
            float neighborSpacing = Data.CollisionRadius * 2.3f;
            float baseAngle = Mathf.Atan2(radialDirection.y, radialDirection.x);

            //粘性选位：已锁定的槽位仍空闲则不换（防止 0.55s 重扫时被临时贴近的友军挤走）
            if (encircleHasSlot && !IsSlotOccupied(encircleTarget, neighborSpacing * .9f))
                return true;

            for (float angle = 6f; angle <= 180f; angle += 6f)
            {
                for (int side = 0; side < 2; side++)
                {
                    float delta = angle * (side == 0 ? -1f : 1f) * Mathf.Deg2Rad;
                    float candidateAngle = baseAngle + delta;
                    Vector2 slot = targetPosition + new Vector2(Mathf.Cos(candidateAngle), Mathf.Sin(candidateAngle)) * ringRadius;
                    if (IsSlotOccupied(slot, neighborSpacing)) continue;
                    //选中：记录空位坐标（粘性选位，重扫倒计时内保持不动，防相邻单位抢位抖动）
                    encircleTarget = slot;
                    return true;
                }
            }
            //180° 内无空位：目标已被完全包住，原地待命
            return false;
        }

        //环上候选点是否已被同队地面单位占用：占据者分两类——
        //①已站在槽位附近的（正在开火的占环友军）；②同样锁定此槽位但还在赶路途中的
        //（槽位写入 encircleTarget 即登记所有权，后到单位扫描时会跳过，防止两人抢一位）
        private bool IsSlotOccupied(Vector2 slot, float spacing)
        {
            var agents = navigation != null ? navigation.Agents : null;
            if (agents == null) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                UnitBase occupant = agents[i];
                if (occupant == null || occupant == this || occupant.CurrentHealth <= 0f) continue;
                if (!IsAlliedWith(occupant) || occupant.IsAirUnit) continue;
                //位置贴近即占用（含正在环上开火的友军）
                if (Vector2.Distance((Vector2)occupant.transform.position, slot) < spacing) return true;
                //友军已锁定同一槽位（含本槽附近半间距内的槽）也在赶路：视为占用
                if (occupant.EncircleClaimedSlot(_ => Vector2.Distance(_, slot) < spacing * .8f)) return true;
            }
            return false;
        }

        //查询本单位是否锁定了满足谓词的合围槽位（供友军查重，防止多个后方单位抢同一空位）
        public bool EncircleClaimedSlot(Func<Vector2, bool> slotPredicate)
        {
            return encircleHasSlot && slotPredicate != null && slotPredicate(encircleTarget);
        }

        //对单位目标的有效射程：空军目标用对空射程，地面目标用对地射程
        private float AttackRangeForUnit(UnitBase enemy)
        {
            return enemy != null && enemy.IsAirUnit ? Data.AttackRangeAir : Data.AttackRange;
        }

        //对敌方单位执行一次攻击：按目标真实护甲类型结算轻/重伤，溅射范围伤害周围友邻敌人
        private void AttackUnitTarget()
        {
            attackTimer = Data.EffectiveAttackCooldown;
            AttackCount++;
            HasEngagedInCombat = true;
            //按目标护甲类型取伤害列，下限 1
            float amount = Mathf.Max(1f, Data.AttackDamageFor(unitTarget.Data.ArmorType));
            //溅射：主目标受全额，范围内其他敌人受溅射倍率伤害
            float splashRadiusWorld = Data.SplashRadius * 12f;
            if (splashRadiusWorld > 0f)
            {
                amount *= 1f + Data.SplashMultiplier;
                Vector2 center = unitTarget.transform.position;
                for (int i = 0; i < enemyUnits.Count; i++)
                {
                    UnitBase nearby = enemyUnits[i];
                    if (nearby == null || nearby == unitTarget || nearby.CurrentHealth <= 0f || IsAlliedWith(nearby)) continue;
                    if (Vector2.Distance(center, nearby.transform.position) <= splashRadiusWorld)
                        nearby.TakeDamage(amount * .6f, this);
                }
            }
            unitTarget.TakeDamage(amount, this);
            TotalDamage += amount;
            DamageDealt?.Invoke(this, Data, amount);
            //自爆单位攻击后死亡
            if (Data.SuicideAttack) Die();
        }

        //执行一次攻击：按目标护甲类型结算轻/重伤害、溅射加成，累加统计并广播事件，自爆单位攻击后自毁
        private void AttackTarget()
        {
            //重置攻击冷却并累加攻击次数
            attackTimer = Data.EffectiveAttackCooldown;
            AttackCount++;
            //标记已交战（兴奋剂类自动技能的触发条件）
            HasEngagedInCombat = true;
            //基础伤害按目标护甲类型取轻/重伤害列，下限为 1，带溅射的单位按溅射倍率放大
            float amount = Mathf.Max(1f, Data.AttackDamageFor("light"));
            if (Data.SplashRadius > 0f) amount *= 1f + Data.SplashMultiplier;
            target.TakeDamage(amount, this);
            TotalDamage += amount;
            DamageDealt?.Invoke(this, Data, amount);
            //自爆单位（如毒爆虫）攻击后死亡
            if (Data.SuicideAttack) Die();
        }

        //受到一次攻击（单位对单位交火用）：先扣护盾再扣生命，血量归零走死亡流程
        public void TakeDamage(float amount, UnitBase attacker)
        {
            if (amount <= 0f || currentHealth <= 0f) return;
            //护盾先吸收伤害
            if (currentShield > 0f)
            {
                float absorbed = Mathf.Min(currentShield, amount);
                currentShield -= absorbed;
                amount -= absorbed;
            }
            currentHealth -= amount;
            regenerationTimer = 0f;
            //受到攻击即视为进入交战（激发受击方的被动类技能判定）
            HasEngagedInCombat = true;
            if (currentHealth <= 0f) Die();
        }

        //技能消耗生命：直接扣当前生命（不触发死亡事件——生命不足时技能组件不会施放）
        public void ConsumeHealth(float amount)
        {
            if (amount <= 0f) return;
            currentHealth = Mathf.Max(1f, currentHealth - amount);
        }

        //死亡流程：广播死亡事件，由外部（场景/对象池）决定销毁或回收。
        //无监听者时兜底直接销毁，保证老场景不接池也能正常工作
        private void Die()
        {
            if (currentHealth <= 0f) currentHealth = 0f;
            UnitDied?.Invoke(this);
            if (UnitDied == null) Destroy(gameObject);
        }

        //软体人群：物理步进后做位置去重叠（静态标志保证每帧只跑一次，由任一存活单位驱动）
        private static int lastOverlapResolveFrame = -1;
        private void LateUpdate()
        {
            if (!SoftBodyCrowd || navigation == null) return;
            if (lastOverlapResolveFrame == Time.frameCount) return;
            lastOverlapResolveFrame = Time.frameCount;
            navigation.ResolveOverlaps();
        }

        //销毁时从导航系统注销，避免残留空引用
        private void OnDestroy()
        {
            if (navigation != null) navigation.UnregisterAgent(this);
        }
    }
}
