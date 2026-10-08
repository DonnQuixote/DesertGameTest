using System;

namespace DesertStorm
{
    /// <summary>
    /// 神族单位数据：在通用单位数据基础上扩展护盾恢复、折跃部署与能量上限等种族特性。
    /// </summary>
    [Serializable]
    public sealed class ProtossUnitData : UnitData
    {
        // 神族特色：护盾恢复、折跃部署和能量上限。
        //是否支持折跃部署（默认开启）
        public bool CanWarpIn = false;
        //开始恢复护盾前的延迟（秒）
        public float ShieldRegenDelay = 1.5f;
        //每秒恢复的护盾值
        public float ProtossShieldRegenerationPerSecond = 8f;
        //能量上限（0 表示无能量系统）
        public float MaxEnergy;

        //护盾恢复延迟：没有护盾的单位设为无穷大（永不恢复）
        public override float ShieldRegenerationDelay => MaxShield > 0f ? ShieldRegenDelay : float.PositiveInfinity;
        //每秒护盾恢复量：没有护盾的单位为 0
        public override float ShieldRegenerationPerSecond => MaxShield > 0f ? ProtossShieldRegenerationPerSecond : 0f;

        //种族特性摘要：拼接护盾恢复、折跃与能量文本，供 UI 展示
        public override string FactionFeatureSummary
        {
            get
            {
                string result = MaxShield > 0f ? $"护盾恢复 {ProtossShieldRegenerationPerSecond:0.0}/秒" : string.Empty;
                if (CanWarpIn) result = string.IsNullOrEmpty(result) ? "可折跃" : result + "、可折跃";
                if (MaxEnergy > 0f) result += string.IsNullOrEmpty(result) ? $"能量 {MaxEnergy:0}" : $"、能量 {MaxEnergy:0}";
                return result;
            }
        }
    }
}
