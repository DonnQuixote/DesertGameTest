using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// buff 机制组件：按 AbilityCatalog 中 buff 类技能定义自动施放。
    /// 触发条件（全部满足）：正与真实敌方单位交战（敌对目标在射程内）+ 技能不在冷却 +
    /// 状态未生效 + 当前生命高于消耗代价（保留至少 1 点）。
    /// 施放效果：瞬间扣除 hpCost → 给自身挂 statusId 状态（时长/倍率来自技能表与状态表）。
    /// </summary>
    public sealed class AbilityComponent : MonoBehaviour
    {
        //宿主单位
        private UnitBase owner;
        //宿主状态系统
        private StatusSystem statusSystem;
        //技能定义
        private AbilityRow ability;
        //冷却剩余时间
        private float cooldownRemaining;
        //宿主静态数据（判定生命消耗）
        private UnitData data;

        //初始化：绑定单位与技能（UnitBase.Setup 后调用），技能 ID 无效则组件空转。
        //冷却清零：对象池复用时不继承上一世的技能冷却
        public void Initialize(UnitBase unit, StatusSystem status, string abilityId)
        {
            owner = unit;
            statusSystem = status;
            ability = AbilityCatalog.GetAbility(abilityId);
            data = unit != null ? unit.Data : null;
            cooldownRemaining = 0f;
        }

        //技能是否就绪（供 UI 展示）：定义有效且不在冷却
        public bool IsReady => ability != null && cooldownRemaining <= 0f;

        //每帧：交战检测 → 冷却推进 → 自动施放
        private void Update()
        {
            if (ability == null || owner == null || statusSystem == null || data == null) return;
            if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;

            //机制路由：目前实现 buff；其他机制（transform/summon/zone/projectile）按需扩展
            if (ability.mechanism != "buff") return;

            //状态仍在生效中不重复施放
            if (!string.IsNullOrEmpty(ability.statusId) && statusSystem.HasStatus(ability.statusId)) return;
            //必须正与真实敌方单位交战（目标在射程内）——行军/打假人/脱离接触都不施放
            if (!owner.IsEngagingEnemy) return;
            //生命太薄时不烧血：当前生命必须高于消耗代价（施放后至少保留 1 点）
            if (owner.CurrentHealth <= ability.hpCost) return;
            if (cooldownRemaining > 0f) return;

            Cast();
        }

        //执行施放：扣血 → 挂状态 → 进冷却
        public void Cast()
        {
            if (ability == null || owner == null || statusSystem == null) return;
            //施放前最终校验（外部手动调用 Cast 时也受保护）：
            //不在交战状态或生命不足以支付代价时拒绝施放
            if (!owner.IsEngagingEnemy) return;
            if (owner.CurrentHealth <= ability.hpCost) return;
            owner.ConsumeHealth(ability.hpCost);
            if (!string.IsNullOrEmpty(ability.statusId))
                statusSystem.ApplyStatus(ability.statusId, ability.duration, ability.abilityId);
            cooldownRemaining = Mathf.Max(0.1f, ability.cooldown);
        }
    }
}
