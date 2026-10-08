using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 摄像机平移控制器：支持 WASD 键盘平移、滚轮缩放（调整高度）、
    /// 中键或空格+左键拖拽平移。
    /// 边界支持自动探测（跟随导航系统的世界范围，场地变化无需改代码），
    /// 缩放时限制视口始终不超过边界，从根源上避免越界后画面来回抖动。
    /// </summary>
    public sealed class CameraPanController : MonoBehaviour
    {
        //键盘平移速度（单位/秒）
        public float KeyboardSpeed = 680f;
        //当前摄像机高度系数（影响正交视口大小）
        public float CameraHeight = 1f;
        //最小高度系数（最放大）
        public float MinHeight = .70f;
        //最大高度系数（最缩小）
        public float MaxHeight = 1.60f;
        //每次滚轮滚动的高度步长
        public float HeightStep = .10f;
        //是否自动探测世界边界（探测到导航系统后跟随其 WorldBounds，场地变化自动同步）
        public bool AutoDetectBounds = true;
        //自动探测失败时的兜底边界
        public Rect worldBounds = new Rect(0f, 0f, 2400f, 1400f);

        //挂载的摄像机组件引用
        private Camera cameraComponent;
        //是否正在拖拽中
        private bool dragging;
        //本次拖拽的起始鼠标位置
        private Vector3 dragOrigin;
        //自动探测到的导航系统（缓存引用，避免每帧全场景查找）
        private CdtNavigationSystem detectedNavigation;
        //动态收敛后的最小高度系数：保证视口不超过世界边界
        private float effectiveMinHeight = .70f;

        //初始化：绑定摄像机与世界边界，并按默认高度应用一次缩放
        public void Initialize(Camera camera, Rect bounds)
        {
            cameraComponent = camera;
            worldBounds = bounds;
            ApplyHeight(CameraHeight);
        }

        //每帧处理边界探测、键盘平移、滚轮缩放与鼠标拖拽，最后做边界钳制
        private void Update()
        {
            //未显式绑定时尝试从自身物体上获取摄像机
            if (cameraComponent == null) cameraComponent = GetComponent<Camera>();
            //仍获取不到摄像机时跳过本帧，避免拖拽换算空引用
            if (cameraComponent == null) return;
            //自动探测世界边界（场地变化时自动跟随）
            DetectBounds();
            //按当前边界与窗口宽高比收敛缩放范围（窗口尺寸变化也能自适应）
            RecomputeZoomLimits();
            cameraComponent.orthographicSize = 420f / CameraHeight;

            //根据 WASD 输入合成平移方向
            Vector2 direction = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) direction.y += 1f;
            if (Input.GetKey(KeyCode.S)) direction.y -= 1f;
            if (Input.GetKey(KeyCode.A)) direction.x -= 1f;
            if (Input.GetKey(KeyCode.D)) direction.x += 1f;
            if (direction.sqrMagnitude > 0f)
                transform.position += (Vector3)(direction.normalized * KeyboardSpeed * Time.deltaTime);

            //滚轮调整摄像机高度（缩放视野）
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > .01f) ApplyHeight(CameraHeight + HeightStep);
            if (wheel < -.01f) ApplyHeight(CameraHeight - HeightStep);

            //按住中键，或空格+左键时进入拖拽平移
            bool dragKey = Input.GetMouseButton(2) || (Input.GetKey(KeyCode.Space) && Input.GetMouseButton(0));
            if (dragKey && !dragging) { dragging = true; dragOrigin = Input.mousePosition; }
            if (!dragKey) dragging = false;
            if (dragging)
            {
                //把屏幕像素位移换算为世界位移：屏幕高度像素对应 2×orthographicSize 的世界高度
                //反向拖动画面（抓住地图拖），与世界尺度一致，拖多少动多少
                Vector3 current = Input.mousePosition;
                Vector3 delta = current - dragOrigin;
                float worldPerPixel = 2f * cameraComponent.orthographicSize / Mathf.Max(1f, Screen.height);
                transform.position -= new Vector3(delta.x * worldPerPixel, delta.y * worldPerPixel, 0f);
                dragOrigin = current;
            }
            ClampPosition();
        }

        //自动探测世界边界：优先使用导航系统的 WorldBounds，场地变化时自动跟随
        private void DetectBounds()
        {
            if (!AutoDetectBounds) return;
            //缓存失效（场景重建/组件销毁）时重新查找
            if (detectedNavigation == null) detectedNavigation = FindObjectOfType<CdtNavigationSystem>();
            //导航系统已配置有效边界时同步采用
            if (detectedNavigation != null && detectedNavigation.WorldBounds.width > 0f && detectedNavigation.WorldBounds.height > 0f)
                worldBounds = detectedNavigation.WorldBounds;
        }

        //应用高度：钳制到允许区间并换算为摄像机正交尺寸
        private void ApplyHeight(float value)
        {
            RecomputeZoomLimits();
            CameraHeight = Mathf.Clamp(value, effectiveMinHeight, MaxHeight);
            if (cameraComponent != null) cameraComponent.orthographicSize = 420f / CameraHeight;
            ClampPosition();
        }

        //按当前边界与屏幕宽高比计算高度下限：视口最大时恰好贴合边界，杜绝越界抖动
        private void RecomputeZoomLimits()
        {
            if (worldBounds.width <= 0f || worldBounds.height <= 0f) return;
            float aspect = cameraComponent != null ? cameraComponent.aspect : 16f / 9f;
            //正交尺寸上限：同时受边界高度与边界宽度（按宽高比换算）约束
            float maxOrtho = Mathf.Min(worldBounds.height * .5f, worldBounds.width * .5f / Mathf.Max(.05f, aspect));
            //正交尺寸与高度系数成反比（ortho = 420 / height），视口贴合边界对应高度下限
            float minHeightForBounds = 420f / Mathf.Max(1f, maxOrtho);
            effectiveMinHeight = Mathf.Max(MinHeight, minHeightForBounds);
            //当前高度低于新下限时收敛到下限
            if (CameraHeight < effectiveMinHeight) CameraHeight = effectiveMinHeight;
        }

        //把摄像机位置钳制在世界边界内，保证画面不越界
        private void ClampPosition()
        {
            if (cameraComponent == null) return;
            //按正交尺寸与宽高比计算视口半宽半高
            float halfHeight = cameraComponent.orthographicSize;
            float halfWidth = halfHeight * cameraComponent.aspect;
            Vector3 position = transform.position;
            //某轴视口已不小于边界时该轴直接居中，避免 Clamp 区间反转导致来回抖动
            if (halfWidth * 2f >= worldBounds.width) position.x = worldBounds.center.x;
            else position.x = Mathf.Clamp(position.x, worldBounds.xMin + halfWidth, worldBounds.xMax - halfWidth);
            if (halfHeight * 2f >= worldBounds.height) position.y = worldBounds.center.y;
            else position.y = Mathf.Clamp(position.y, worldBounds.yMin + halfHeight, worldBounds.yMax - halfHeight);
            transform.position = position;
        }
    }
}
