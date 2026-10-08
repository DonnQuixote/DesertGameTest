using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 单位状态系统：管理一个单位身上叠加的临时状态（兴奋剂、致盲雾等）。
    /// 每个状态实例带剩余时间，到期自动移除；增删时重算 UnitData 上的聚合修正器，
    /// 供 EffectiveMoveSpeed / EffectiveAttackCooldown / EffectiveArmor 与 UI 读取。
    /// </summary>
    public sealed class StatusSystem : MonoBehaviour
    {
        //单个运行时状态实例
        public sealed class ActiveStatus
        {
            //状态定义 ID（对应 AbilityCatalog 状态表）
            public string StatusId;
            //显示名（UI 用）
            public string DisplayName;
            //剩余时间（秒）
            public float Remaining;
            //总时长（秒，供 UI 进度展示）
            public float Duration;
            //来源技能 ID（可空）
            public string SourceAbility;
        }

        //宿主单位
        private UnitBase owner;
        //宿主静态数据（修正器写回目标）
        private UnitData data;
        //当前生效中的状态列表
        private readonly List<ActiveStatus> active = new List<ActiveStatus>();

        //当前状态快照（UI 只读展示用）
        public IReadOnlyList<ActiveStatus> Active => active;

        //状态变更事件（参数为状态ID与是否新增）：UI 面板监听刷新
        public event Action<string, bool> StatusChanged;

        //初始化：绑定宿主（UnitBase.Setup 时调用）。
        //清空旧状态列表：对象池复用时上一世的状态（如兴奋剂）不应带到新单位身上；
        //清空后立即重算，把共享 UnitData 上的聚合修正器复位
        public void Initialize(UnitBase unit, UnitData unitData)
        {
            owner = unit;
            data = unitData;
            if (active.Count > 0)
            {
                active.Clear();
                Recalculate();
            }
        }

        //给单位叠加一个状态：已存在同类状态则刷新时长（不叠加倍率，避免兴奋剂滚雪球）
        public void ApplyStatus(string statusId, float duration, string sourceAbility = null)
        {
            var def = AbilityCatalog.GetStatus(statusId);
            if (def == null)
            {
                Debug.LogWarning($"[StatusSystem] 未知状态『{statusId}』，忽略。");
                return;
            }
            var existing = active.Find(s => s.StatusId == statusId);
            if (existing != null)
            {
                existing.Remaining = duration;
                existing.Duration = duration;
            }
            else
            {
                active.Add(new ActiveStatus
                {
                    StatusId = statusId,
                    DisplayName = string.IsNullOrEmpty(def.displayName) ? statusId : def.displayName,
                    Remaining = duration,
                    Duration = duration,
                    SourceAbility = sourceAbility
                });
            }
            Recalculate();
            StatusChanged?.Invoke(statusId, true);
        }

        //主动移除一个状态
        public void RemoveStatus(string statusId)
        {
            int removed = active.RemoveAll(s => s.StatusId == statusId);
            if (removed > 0)
            {
                Recalculate();
                StatusChanged?.Invoke(statusId, false);
            }
        }

        //是否带有某状态
        public bool HasStatus(string statusId) => active.Exists(s => s.StatusId == statusId);

        //每帧推进：状态倒计时，到期移除并重算
        private void Update()
        {
            if (active.Count == 0) return;
            float dt = Time.deltaTime;
            bool changed = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                active[i].Remaining -= dt;
                if (active[i].Remaining <= 0f)
                {
                    string id = active[i].StatusId;
                    active.RemoveAt(i);
                    changed = true;
                    StatusChanged?.Invoke(id, false);
                }
            }
            if (changed) Recalculate();
        }

        //重算聚合修正器并写回 UnitData（遍历所有生效状态求乘积/和）
        private void Recalculate()
        {
            if (data == null) return;
            float moveMul = 1f, atkMul = 1f, armorAdd = 0f, damageAdd = 0f;
            bool suppress = false;
            foreach (var s in active)
            {
                var def = AbilityCatalog.GetStatus(s.StatusId);
                if (def == null) continue;
                moveMul *= def.moveSpeedMul > 0f ? def.moveSpeedMul : 1f;
                atkMul *= def.attackSpeedMul > 0f ? def.attackSpeedMul : 1f;
                armorAdd += def.armorAdd;
                damageAdd += def.damageAdd;
                suppress |= def.suppressAttack;
            }
            data.StatusMoveSpeedMultiplier = moveMul;
            data.StatusAttackSpeedMultiplier = atkMul;
            data.StatusArmorAdd = armorAdd;
            data.StatusDamageAdd = damageAdd;
            data.StatusSuppressAttack = suppress;
        }

        //销毁时清空静态数据上的修正器，防止数据对象被复用时残留
        private void OnDestroy()
        {
            if (data == null) return;
            data.StatusMoveSpeedMultiplier = 1f;
            data.StatusAttackSpeedMultiplier = 1f;
            data.StatusArmorAdd = 0f;
            data.StatusDamageAdd = 0f;
            data.StatusSuppressAttack = false;
        }
    }
}
