using System;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 训练假人：超高血量的木桩目标，用于测试单位输出。
    /// 记录累计伤害与命中次数，并通过事件把每次受击广播出去。
    /// </summary>
    public sealed class TrainingDummy : MonoBehaviour
    {
        //受击事件：参数为本次伤害值与攻击方单位
        public event Action<float, UnitBase> DamageReceived;
        //累计承受的总伤害
        public float TotalDamage { get; private set; }
        //累计被命中的次数
        public int HitCount { get; private set; }
        //假人血量上限（设为超大值，保证测试中不会被打死）
        public float MaxHealth { get; } = 100000000f;

        //受到一次攻击：累加伤害与命中计数，并广播受击事件
        public void TakeDamage(float amount, UnitBase attacker)
        {
            TotalDamage += amount;
            HitCount++;
            DamageReceived?.Invoke(amount, attacker);
        }

        //重置统计：清零累计伤害与命中次数
        public void ResetDummy()
        {
            TotalDamage = 0f;
            HitCount = 0;
        }
    }
}
