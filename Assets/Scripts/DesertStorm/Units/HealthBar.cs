using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 单位头顶血条：生命（红）条 + 护盾（蓝）条（仅护盾上限 &gt; 0 的单位显示）。
    /// 结构：本组件挂在单位本体上，视觉放在子物体 BarRoot 下；护盾底槽与前景
    /// 同属 ShieldGroup 容器——隐藏护盾时整组一起隐藏（此前底槽与前景分离，
    /// 只藏了底槽导致无盾单位仍显示蓝条）。
    /// 每帧对 BarRoot 做反向缩放（1/单位世界缩放）——血条世界尺寸恒定，
    /// 且绝不修改单位本体的 Transform（此前版本覆盖了单位缩放导致所有单位一样大）。
    /// 血条宽度随单位碰撞半径调整，长度每帧按当前血/盾比例实时刷新。
    /// 全局显隐：HealthBar.GlobalVisible 由场景 UI 控制，所有血条同帧响应。
    /// </summary>
    public sealed class HealthBar : MonoBehaviour
    {
        //血条高度（世界像素）
        private const float BarHeight = 5f;
        //头顶偏移（世界像素，相对单位中心）
        private const float HeadOffset = 16f;

        //全局显隐开关：场景 UI 切换，所有血条实例同帧响应
        public static bool GlobalVisible = true;

        //宿主单位
        private UnitBase owner;
        //血条视觉根（每帧反向缩放的对象）
        private Transform barRoot;
        //生命条前景（红色）
        private Transform healthFill;
        //护盾整组（底槽+前景，无盾单位整组隐藏）
        private Transform shieldGroup;
        //护盾条前景（蓝色）
        private Transform shieldFill;
        //血条宽度（按宿主碰撞半径计算，世界像素）
        private float barWidth = 30f;
        //程序生成的纯白精灵（静态缓存，所有血条实例共用）
        private static Sprite whiteSprite;

        //创建/重建血条视觉：池复用时 Setup 会再次调用，先清掉旧视觉
        public void Attach(UnitBase unit, SpriteRenderer hostRenderer)
        {
            owner = unit;
            whiteSprite = CreateWhiteSprite();
            //宽度随单位碰撞半径调整：大单位血条更宽，钳制可读范围
            barWidth = unit.Data != null ? Mathf.Clamp(unit.Data.CollisionRadius * 2.2f, 26f, 64f) : 30f;

            //清理旧血条（Destroy 延迟到帧末，但引用立即指向新建结构，渲染以新为准）
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "BarRoot") Destroy(child.gameObject);
            }

            barRoot = new GameObject("BarRoot").transform;
            barRoot.SetParent(transform, false);
            barRoot.localPosition = Vector3.zero;

            //排序：压在宿主精灵之上，避免被其他单位盖住
            int order = hostRenderer != null ? hostRenderer.sortingOrder + 1 : 6;
            Color backColor = new Color(.12f, .14f, .16f, .9f);
            //生命条（底槽 + 前景）：头顶偏移在 barRoot 内部——barRoot 已反向缩放，世界偏移恒定
            CreateBar(barRoot, new Vector3(0f, HeadOffset, 0f), backColor, order - 1);
            healthFill = CreateBar(barRoot, new Vector3(0f, HeadOffset, 0f), new Color(.90f, .25f, .22f, 1f), order);

            //护盾组（容器）：底槽与前景都挂在组下，隐藏时整组消失（无盾单位不显示蓝条）
            Vector3 shieldPos = new Vector3(0f, HeadOffset + BarHeight + 1f, 0f);
            shieldGroup = new GameObject("ShieldGroup").transform;
            shieldGroup.SetParent(barRoot, false);
            shieldGroup.localPosition = Vector3.zero;
            CreateBar(shieldGroup, shieldPos, backColor, order - 1);
            shieldFill = CreateBar(shieldGroup, shieldPos, new Color(.36f, .62f, .95f, 1f), order);
            shieldGroup.gameObject.SetActive(false);
            //初始比例
            RefreshFill();
        }

        //在指定父节点下创建一条：位置、颜色、排序；宽度按 barWidth
        private Transform CreateBar(Transform parent, Vector3 position, Color color, int order)
        {
            GameObject bar = new GameObject("Bar");
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = position;
            var renderer = bar.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            bar.transform.localScale = new Vector3(barWidth, BarHeight, 1f);
            return bar.transform;
        }

        //每帧：全局显隐 + 跟随宿主 + 反向补偿缩放（世界尺寸恒定）+ 实时刷新血/盾长度
        private void LateUpdate()
        {
            if (owner == null || owner.Data == null || barRoot == null) return;
            //全局隐藏：整根血条关闭，恢复显示时自动回来
            if (!GlobalVisible)
            {
                if (barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(false);
                return;
            }
            if (!barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);
            //反向缩放：单位世界缩放为 S 时 BarRoot 取 1/S，血条世界尺寸保持恒定
            float unitScale = Mathf.Max(.0001f, transform.lossyScale.x);
            barRoot.localScale = Vector3.one * (1f / unitScale);
            RefreshFill();
        }

        //按宿主当前血/盾刷新前景长度与左侧锚定位置
        private void RefreshFill()
        {
            float hpFraction = Mathf.Clamp01(owner.CurrentHealth / owner.Data.MaxHealth);
            healthFill.localScale = new Vector3(Mathf.Max(.0001f, barWidth * hpFraction), BarHeight, 1f);
            //血量收缩时锚定左端（中心收缩会两边同时缩）
            healthFill.localPosition = new Vector3(-(1f - hpFraction) * barWidth * .5f, HeadOffset, 0f);
            //护盾条：仅护盾上限 > 0 的单位显示整组
            bool hasShield = owner.Data.MaxShield > 0f;
            if (shieldGroup.gameObject.activeSelf != hasShield) shieldGroup.gameObject.SetActive(hasShield);
            if (hasShield)
            {
                float shieldFraction = Mathf.Clamp01(owner.CurrentShield / owner.Data.MaxShield);
                shieldFill.localScale = new Vector3(Mathf.Max(.0001f, barWidth * shieldFraction), BarHeight, 1f);
                shieldFill.localPosition = new Vector3(-(1f - shieldFraction) * barWidth * .5f, HeadOffset + BarHeight + 1f, 0f);
            }
        }

        //销毁时清引用
        private void OnDestroy()
        {
            owner = null;
            barRoot = null;
            healthFill = null;
            shieldFill = null;
            shieldGroup = null;
        }

        //纯白精灵（静态缓存：所有血条实例共用一张纹理）
        private static Sprite CreateWhiteSprite()
        {
            if (whiteSprite != null) return whiteSprite;
            var texture = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), texture.width);
            return whiteSprite;
        }
    }
}
