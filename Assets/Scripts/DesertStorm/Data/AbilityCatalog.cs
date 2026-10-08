using System;
using System.Collections.Generic;

namespace DesertStorm
{
    /// <summary>
    /// 技能定义行（AbilityTable.xlsx 技能表 → AbilityTable.json ability 字段）。
    /// </summary>
    [Serializable]
    public class AbilityRow
    {
        public string abilityId;
        public string displayName;
        //机制类型：buff=自增益 / transform=变形 / summon=召唤 / zone=区域 / projectile=弹道
        public string mechanism;
        //施放后挂到目标身上的状态 ID（对应状态表）
        public string statusId;
        //状态持续时间（秒）
        public float duration;
        //技能冷却时间（秒）
        public float cooldown;
        //施放瞬间消耗的生命值
        public float hpCost;
        public string description;

        //序列化为一行 JSON（导入器写 AbilityTable.json 用）
        public string ToJson()
        {
            return "    {\n" +
                   $"      \"abilityId\": \"{Escape(abilityId)}\",\n" +
                   $"      \"displayName\": \"{Escape(displayName)}\",\n" +
                   $"      \"mechanism\": \"{Escape(mechanism)}\",\n" +
                   $"      \"statusId\": \"{Escape(statusId)}\",\n" +
                   $"      \"duration\": {Fmt(duration)},\n" +
                   $"      \"cooldown\": {Fmt(cooldown)},\n" +
                   $"      \"hpCost\": {Fmt(hpCost)},\n" +
                   $"      \"description\": \"{Escape(description)}\"\n" +
                   "    }";
        }

        //JSON 字符串转义（引号与反斜杠；中文直接输出 UTF-8）
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        //浮点格式化：保留最多 4 位小数、去掉多余的 0
        private static string Fmt(float value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 状态效果定义行（AbilityTable.xlsx 状态表 → AbilityTable.json status 字段）。
    /// </summary>
    [Serializable]
    public class StatusRow
    {
        public string statusId;
        public string displayName;
        //移速倍率（1 = 无变化）
        public float moveSpeedMul;
        //攻速倍率（攻击间隔除数，1 = 无变化）
        public float attackSpeedMul;
        //护甲加成（可正可负）
        public float armorAdd;
        //伤害加成（平加）
        public float damageAdd;
        //是否禁止攻击
        public bool suppressAttack;
        public string description;

        //序列化为一行 JSON（导入器写 AbilityTable.json 用）
        public string ToJson()
        {
            return "    {\n" +
                   $"      \"statusId\": \"{Escape(statusId)}\",\n" +
                   $"      \"displayName\": \"{Escape(displayName)}\",\n" +
                   $"      \"moveSpeedMul\": {Fmt(moveSpeedMul)},\n" +
                   $"      \"attackSpeedMul\": {Fmt(attackSpeedMul)},\n" +
                   $"      \"armorAdd\": {Fmt(armorAdd)},\n" +
                   $"      \"damageAdd\": {Fmt(damageAdd)},\n" +
                   $"      \"suppressAttack\": {(suppressAttack ? "true" : "false")},\n" +
                   $"      \"description\": \"{Escape(description)}\"\n" +
                   "    }";
        }

        //JSON 字符串转义（引号与反斜杠；中文直接输出 UTF-8）
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        //浮点格式化：保留最多 4 位小数、去掉多余的 0
        private static string Fmt(float value)
        {
            return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 技能表 JSON 载荷。
    /// </summary>
    [Serializable]
    public class AbilityTablePayload
    {
        public string version;
        public AbilityRow[] abilities;
        public StatusRow[] statuses;
    }

    /// <summary>
    /// 技能/状态目录：从 Resources/AbilityTable.json 加载技能与状态定义，
    /// 提供按 ID 查询。JSON 缺失时回退内置硬编码（与表格数据一致），保证工程可运行。
    /// </summary>
    public static class AbilityCatalog
    {
        private static Dictionary<string, AbilityRow> abilities;
        private static Dictionary<string, StatusRow> statuses;

        public static Dictionary<string, AbilityRow> Abilities()
        {
            EnsureLoaded();
            return abilities;
        }

        public static Dictionary<string, StatusRow> Statuses()
        {
            EnsureLoaded();
            return statuses;
        }

        public static AbilityRow GetAbility(string abilityId)
        {
            EnsureLoaded();
            return abilityId != null && abilities.TryGetValue(abilityId, out var row) ? row : null;
        }

        public static StatusRow GetStatus(string statusId)
        {
            EnsureLoaded();
            return statusId != null && statuses.TryGetValue(statusId, out var row) ? row : null;
        }

        //数据来源说明（诊断用）
        public static string LastLoadSource { get; private set; } = string.Empty;

        private static void EnsureLoaded()
        {
            if (abilities != null) return;
            abilities = new Dictionary<string, AbilityRow>();
            statuses = new Dictionary<string, StatusRow>();

            var text = UnityEngine.Resources.Load<UnityEngine.TextAsset>("AbilityTable");
            if (text != null)
            {
                try
                {
                    var payload = UnityEngine.JsonUtility.FromJson<AbilityTablePayload>(text.text);
                    if (payload?.abilities != null)
                        foreach (var row in payload.abilities)
                            if (!string.IsNullOrEmpty(row.abilityId)) abilities[row.abilityId] = row;
                    if (payload?.statuses != null)
                        foreach (var row in payload.statuses)
                            if (!string.IsNullOrEmpty(row.statusId)) statuses[row.statusId] = row;
                    LastLoadSource = "AbilityTable.json";
                    return;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[AbilityCatalog] AbilityTable.json 解析失败，回退内置数据：{ex.Message}");
                }
            }

            //内置兜底：与 AbilityTable.xlsx 保持一致
            abilities["stimpack"] = new AbilityRow
            {
                abilityId = "stimpack",
                displayName = "兴奋剂",
                mechanism = "buff",
                statusId = "stimpack_up",
                duration = 15f,
                cooldown = 18f,
                hpCost = 10f,
                description = "人族生物单位：15秒内移速×1.5、攻速×1.33；施放瞬间消耗10点生命"
            };
            statuses["stimpack_up"] = new StatusRow
            {
                statusId = "stimpack_up",
                displayName = "兴奋剂强化",
                moveSpeedMul = 1.5f,
                attackSpeedMul = 1.33f,
                armorAdd = 0f,
                damageAdd = 0f,
                suppressAttack = false,
                description = "兴奋剂生效中的状态：移速与攻速提升"
            };
            LastLoadSource = "内置硬编码（未找到 AbilityTable.json，请运行 工具→导入技能数据表）";
        }
    }
}
