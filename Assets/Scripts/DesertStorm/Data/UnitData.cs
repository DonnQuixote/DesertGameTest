using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 单位数据基类：定义全部通用数值字段（生命、攻击、护甲、移速等），
    /// 种族子类（人族/虫族/神族）通过覆写虚属性接入各自的运行时规则。
    /// </summary>
    [Serializable]
    public abstract class UnitData
    {
        //单位ID
        public string UnitId;
        //单位名称
        public string DisplayName;
        //种族
        public string Faction;
        //科技解锁等级
        public int Tier;
        //最大生命值
        public float MaxHealth;
        //最大护盾值
        public float MaxShield;
        //对轻甲目标（light/psionic/none）单次伤害
        public float AttackLightDamage;
        //对重甲目标（armored/massive）单次伤害
        public float AttackHeavyDamage;
        //护甲
        public float Armor;
        //射程(对地)
        public float AttackRange;
        //对空射程
        public float AttackRangeAir;
        //移速
        public float MoveSpeed;
        //攻击间隔
        public float AttackCooldown;
        //技能间隔
        public float SkillCooldown;
        //冲击性
        public float Impact;
        //攻击区域(天空、地面、二者都可)
        public string TargetDomain;
        //所属区域(陆地/天空)
        public string Domain;
        //护甲类型(轻甲、重甲、无甲)
        public string ArmorType;
        //单位种类(生物、机械)
        public string UnitClass;
        //是否可以攻击天空
        public bool CanAttackAir;
        //单位体积
        public float UnitVolume;
        //溅射范围
        public float SplashRadius;
        //溅射半径
        public float SplashMultiplier;
        //是否辅助支援单位
        public bool IsSupport;
        //是否攻击后自毁
        public bool SuicideAttack;
        //人口补给占用（0 = 派生默认 1）
        public float UnitSupply;
        //表格指定的碰撞半径（0 = 由体积派生）
        public float UnitRadius;
        //威胁值（决定被敌方攻击的优先级）
        public float Threat;
        //阵营颜色
        public Color AccentColor;
        //碰撞体积
        public float CollisionRadius;
        //主动技能 ID 列表（对应 AbilityCatalog）
        public string[] Abilities;

        //状态修正器（由 StatusSystem 维护，随状态增删实时更新）
        //当前生效的移速倍率乘积（如兴奋剂 1.5）
        public float StatusMoveSpeedMultiplier = 1f;
        //当前生效的攻速倍率乘积（攻速倍率 = 攻击间隔除数，如兴奋剂 1.33）
        public float StatusAttackSpeedMultiplier = 1f;
        //当前生效的护甲加成（可正可负）
        public float StatusArmorAdd;
        //当前生效的伤害加成（平加到最终伤害上）
        public float StatusDamageAdd;
        //当前是否被禁止攻击（如致盲雾）
        public bool StatusSuppressAttack;

        // 种族子类通过这些虚方法接入自己的运行时规则。
        //实际移速（种族加成后，再乘状态倍率）
        public virtual float EffectiveMoveSpeed => MoveSpeed * StatusMoveSpeedMultiplier;
        //实际攻击间隔（种族加成后，再除以攻速倍率——倍率越大间隔越短）
        public virtual float EffectiveAttackCooldown => AttackCooldown / Mathf.Max(.01f, StatusAttackSpeedMultiplier);
        //实际护甲（基础值 + 状态加成）
        public float EffectiveArmor => Armor + StatusArmorAdd;
        //生命恢复延迟（默认不恢复）
        public virtual float HealthRegenerationDelay => float.PositiveInfinity;
        //每秒生命恢复量（默认为 0）
        public virtual float HealthRegenerationPerSecond => 0f;
        //护盾恢复延迟（默认不恢复）
        public virtual float ShieldRegenerationDelay => float.PositiveInfinity;
        //每秒护盾恢复量（默认为 0）
        public virtual float ShieldRegenerationPerSecond => 0f;
        //种族特性摘要文本（供 UI 展示）
        public virtual string FactionFeatureSummary => string.Empty;

        //按目标护甲类型取本次攻击伤害：重甲系（armored/massive）用对重甲伤害，其余用对轻甲伤害
        public float AttackDamageFor(string targetArmorType)
        {
            bool heavy = targetArmorType == "armored" || targetArmorType == "massive";
            float baseDamage = heavy ? AttackHeavyDamage : AttackLightDamage;
            //目标护甲类型缺失时按轻甲处理
            return (baseDamage > 0f ? baseDamage : AttackLightDamage) + StatusDamageAdd;
        }

        //按目标护甲类型取本次攻击伤害（目标类型未知时的重载，按轻甲结算）
        public float AttackDamageFor()
        {
            return AttackLightDamage + StatusDamageAdd;
        }

        //拼装一行单位简述：名称、生命、护盾、攻击（轻/重）、对地/对空射程、移速、冲击、体积、对空与种族特性
        public string ShortDescription()
        {
            string shield = MaxShield > 0f ? $" + 护盾 {MaxShield:0}" : string.Empty;
            string air = CanAttackAir ? $" | 对空 {AttackRangeAir:0.0}" : string.Empty;
            string feature = string.IsNullOrEmpty(FactionFeatureSummary) ? string.Empty : $" | {FactionFeatureSummary}";
            return $"{DisplayName} | HP {MaxHealth:0}{shield} | 轻伤 {AttackLightDamage:0.0} 重伤 {AttackHeavyDamage:0.0} | 射程 {AttackRange:0.0}{air} | 速度 {EffectiveMoveSpeed:0.00} | 冲击 {Impact:0.0} | 体积 {UnitVolume:0.0}{feature}";
        }
    }
}
