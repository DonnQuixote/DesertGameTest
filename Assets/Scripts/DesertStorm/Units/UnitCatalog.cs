using System;
using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 单位目录：对外 API（AllUnits / IdsForFaction）不变。
    /// 数据来源两级：优先加载 Resources/UnitTable.json（由 Assets/DataConfig/UnitTable.xlsx
    /// 经编辑器菜单 工具→导入单位数据表 生成，表格改完回 Unity 自动重新导入）；
    /// JSON 缺失或解析失败时回退到 LegacyAllUnits 硬编码数据，保证工程永远可运行。
    /// </summary>
    public static class UnitCatalog
    {
        //缓存：只在首次访问时加载一次
        private static Dictionary<string, UnitData> cached;
        //本次使用的数据来源（供诊断/日志）
        public static string LastLoadSource { get; private set; } = string.Empty;

        //构建全部单位数据字典（键为单位 ID）
        public static Dictionary<string, UnitData> AllUnits()
        {
            if (cached != null) return cached;

            var table = LoadFromJson();
            if (table != null)
            {
                cached = table;
                LastLoadSource = "UnitTable.json";
                return cached;
            }

            cached = LegacyAllUnits();
            LastLoadSource = "内置硬编码（未找到 UnitTable.json，请运行 工具→导入单位数据表）";
            return cached;
        }

        //返回指定阵营的全部单位 ID 列表
        public static List<string> IdsForFaction(string faction)
        {
            var result = new List<string>();
            foreach (var unit in AllUnits().Values)
                if (unit.Faction == faction) result.Add(unit.UnitId);
            return result;
        }

        //---- JSON 加载：Resources/UnitTable.json → UnitData 子类 ----
        private static Dictionary<string, UnitData> LoadFromJson()
        {
            var text = Resources.Load<TextAsset>("UnitTable");
            if (text == null) return null;
            UnitTablePayload payload;
            try
            {
                payload = JsonUtility.FromJson<UnitTablePayload>(text.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnitCatalog] UnitTable.json 解析失败，回退内置数据：{ex.Message}");
                return null;
            }
            if (payload == null || payload.units == null || payload.units.Length == 0)
            {
                Debug.LogWarning("[UnitCatalog] UnitTable.json 为空，回退内置数据。");
                return null;
            }

            var result = new Dictionary<string, UnitData>();
            foreach (var row in payload.units)
            {
                UnitData data = BuildFromRow(row);
                if (data != null && !string.IsNullOrEmpty(data.UnitId)) result[data.UnitId] = data;
            }
            if (result.Count == 0)
            {
                Debug.LogWarning("[UnitCatalog] UnitTable.json 未产生任何有效单位，回退内置数据。");
                return null;
            }
            return result;
        }

        //按表行构造对应种族的 UnitData 子类，并套用通用派生逻辑
        private static UnitData BuildFromRow(UnitRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.id)) return null;
            //三族主题色（派生字段不进表，颜色作为种族级常量保留在代码里）
            Color color = row.faction == "人族" ? ColorFromHex("#63a7e8")
                : row.faction == "虫族" ? ColorFromHex("#bd70df")
                : ColorFromHex("#e6bd5b");

            UnitData data;
            switch (row.faction)
            {
                case "人族":
                    var terran = new TerranUnitData();
                    if (row.terran != null)
                    {
                        terran.CanBeRepaired = row.terran.canBeRepaired;
                        terran.HasStimpack = row.terran.hasStimpack;
                        terran.CanEnterSiegeMode = row.terran.canSiege;
                        terran.StimpackMoveSpeedMultiplier = row.terran.stimSpeedMul;
                        terran.StimpackAttackCooldownMultiplier = row.terran.stimCdMul;
                    }
                    else
                    {
                        //扩展表缺行：用类别推导默认值，保持与旧逻辑一致
                        terran.CanBeRepaired = row.unitClass == "mechanical";
                    }
                    data = terran;
                    break;
                case "虫族":
                    var zerg = new ZergUnitData();
                    if (row.zerg != null)
                    {
                        zerg.CanBurrow = row.zerg.canBurrow;
                        zerg.RegeneratesHealth = row.zerg.regenPerSecond > 0f;
                        zerg.ZergHealthRegenerationPerSecond = Mathf.Max(0f, row.zerg.regenPerSecond);
                        zerg.CreepMoveSpeedMultiplier = row.zerg.creepMul;
                    }
                    else
                    {
                        //扩展表缺行：虫族默认开启自然恢复 2/秒（与旧工厂默认一致）
                        zerg.RegeneratesHealth = true;
                        zerg.ZergHealthRegenerationPerSecond = 2f;
                    }
                    data = zerg;
                    break;
                case "神族":
                    var protoss = new ProtossUnitData();
                    if (row.protoss != null)
                    {
                        protoss.CanWarpIn = row.protoss.canWarpIn;
                        protoss.ShieldRegenDelay = row.protoss.shieldRegenDelay;
                        protoss.ProtossShieldRegenerationPerSecond = row.protoss.shieldRegenPerSecond;
                        protoss.MaxEnergy = row.protoss.maxEnergy;
                    }
                    data = protoss;
                    break;
                default:
                    Debug.LogWarning($"[UnitCatalog] 未知种族『{row.faction}』，跳过单位 {row.id}。");
                    return null;
            }
            ApplyCommon(data, row, color);
            return data;
        }

        //通用属性填充：优先使用表格新列（unitRadius/impact/threat），缺省回退派生公式
        private static void ApplyCommon(UnitData data, UnitRow row, Color color)
        {
            //体积：表格指定单位半径时以表格为准（×16 世界换算，放大视觉），未指定时按生命值幂函数推导
            float volume = row.unitRadius > 0f
                ? Mathf.Clamp(row.unitRadius * 16f, 7f, 24f)
                : VolumeFromHealth(row.maxHealth);
            //冲击性：表格指定 >0 用表格值，否则按护甲类型分级（massive 3 / armored 1.5 / light 0.5 / 其他 0）
            float impact = row.impact > 0f
                ? row.impact
                : row.armorType == "massive" ? 3f : row.armorType == "armored" ? 1.5f : row.armorType == "light" ? .5f : 0f;
            data.UnitId = row.id;
            data.DisplayName = row.name;
            data.Faction = row.faction;
            data.Tier = row.tier;
            data.MaxHealth = row.maxHealth;
            data.MaxShield = row.maxShield;
            data.AttackLightDamage = row.attackLightDamage;
            data.AttackHeavyDamage = row.attackHeavyDamage;
            data.Armor = row.armor;
            data.AttackRange = row.attackRange;
            data.AttackRangeAir = row.attackRangeAir;
            data.MoveSpeed = row.moveSpeed;
            data.AttackCooldown = row.attackCooldown;
            data.SkillCooldown = 0f;
            data.Impact = impact;
            data.TargetDomain = row.targetDomain;
            data.Domain = row.domain;
            data.ArmorType = row.armorType;
            data.UnitClass = row.unitClass;
            //能否对空由目标区域决定（air 或 both）
            data.CanAttackAir = row.targetDomain == "air" || row.targetDomain == "both";
            data.UnitVolume = volume;
            data.SplashRadius = row.splashRadius;
            data.SplashMultiplier = row.splashMultiplier;
            data.IsSupport = row.isSupport;
            data.SuicideAttack = row.isSuicide;
            data.UnitSupply = row.unitSupply;
            data.UnitRadius = row.unitRadius;
            data.Threat = row.threat;
            data.Abilities = row.abilities;
            data.AccentColor = color;
            //碰撞半径：表格指定单位半径时直接换算（unitRadius×16），否则按生命值推导
            data.CollisionRadius = row.unitRadius > 0f
                ? Mathf.Clamp(row.unitRadius * 16f, 7f, 24f)
                : CollisionRadiusFromHealth(row.maxHealth);
        }

        //按生命值推导碰撞半径（两条路径共用，保证表格半径/生命推导视觉标准统一）：
        //幂函数放大大小差异（200 血为基准），钳制 [9, 22]
        private static float CollisionRadiusFromHealth(float maxHealth)
        {
            return Mathf.Clamp(9f + Mathf.Pow(Mathf.Max(1f, maxHealth) / 200f, .7f) * 7f, 9f, 22f);
        }

        //按生命值推导体积（质量/避让用，与碰撞半径同标准）
        private static float VolumeFromHealth(float maxHealth)
        {
            return Mathf.Clamp(Mathf.Pow(Mathf.Max(1f, maxHealth) / 200f, .7f) * 2f + .6f, .6f, 3.2f);
        }

        //---- 内置兜底数据（与 UnitTable.xlsx 当前数据对齐：双伤害列 + 通用扩展列 + abilities）----
        private static Dictionary<string, UnitData> LegacyAllUnits()
        {
            Color terran = ColorFromHex("#63a7e8");
            Color zerg = ColorFromHex("#bd70df");
            Color protoss = ColorFromHex("#e6bd5b");
            var units = new List<UnitData>
            {
                // Terran（轻伤/重伤、护甲、对地/对空射程、移速、间隔、补助/半径/威胁/冲击）
                MakeTerran("marine", "基础陆战队", "人族", 1, 55, 9, 9, 0, 5, 5, 3.15f, .86f, "ground", "light", "biological", terran, supply: 1, targetDomain: "both", abilities: new[] { "stimpack" }),
                MakeTerran("marauder", "基础重装部队", "人族", 1, 125, 10, 14, 1, 6, 0, 2.25f, 1.50f, "ground", "armored", "biological", terran, supply: 2, targetDomain: "ground", abilities: new[] { "stimpack" }),
                MakeTerran("medivac", "医疗船", "人族", 1, 150, 0, 0, 0, 4, 4, 3.50f, 1f, "air", "light", "mechanical", terran, supply: 2, targetDomain: "both", support: true),
                MakeTerran("cyclone", "飓风导弹车", "人族", 2, 200, 24, 24, 1, 5, 0, 4.13f, 1f, "ground", "armored", "mechanical", terran, supply: 3, threat: 1),
                MakeTerran("siege_tank", "工程坦克", "人族", 2, 160, 15, 30, 1, 7, 0, 2.25f, 1.50f, "ground", "armored", "mechanical", terran, supply: 3, splashRadius: 55, splashMultiplier: .35f, siegeMode: true),
                MakeTerran("thor", "雷神", "人族", 3, 400, 78, 78, 4, 7, 15.5f, 1.88f, 1.28f, "ground", "armored", "mechanical", terran, supply: 6, splashRadius: 35, splashMultiplier: .25f),
                MakeTerran("liberator", "解放者", "人族", 3, 180, 20, 30, 0, 5, 5, 4.13f, 1f, "air", "armored", "mechanical", terran, supply: 3, siegeMode: true),
                MakeTerran("banshee", "女妖", "人族", 3, 140, 12, 18, 0, 6, 0, 3.50f, .89f, "air", "light", "mechanical", terran, supply: 3),
                MakeTerran("medivac_transport", "医疗运输机", "人族", 3, 150, 0, 0, 0, 4, 4, 3.50f, 1f, "air", "light", "mechanical", terran, supply: 2, support: true),

                // Zerg
                MakeZerg("zergling", "跳虫", "虫族", 1, 35, 5, 5, 0, 1, 0, 4.13f, .50f, "ground", "light", "biological", zerg, supply: .5f, regen: 1.5f),
                MakeZerg("roach", "蟑螂", "虫族", 1, 145, 16, 16, 1, 4, 0, 1.97f, 1f, "ground", "armored", "biological", zerg, supply: 2, burrow: true, regen: 5f),
                MakeZerg("ravager", "火蟑螂", "虫族", 1, 120, 20, 20, 1, 6, 6, 2.62f, 1.50f, "ground", "armored", "biological", zerg, supply: 3, regen: 4f),
                MakeZerg("hydralisk", "刺蛇", "虫族", 1, 90, 12, 12, 0, 5, 5, 3.15f, .83f, "ground", "light", "biological", zerg, supply: 2, regen: 3f),
                MakeZerg("ultralisk", "小牛", "虫族", 1, 500, 35, 35, 2, 1, 0, 4.13f, .80f, "ground", "massive", "biological", zerg, supply: 6, splashRadius: 35, splashMultiplier: .30f, regen: 8f),
                MakeZerg("baneling", "毒爆虫", "虫族", 1, 30, 35, 35, 0, 1, 0, 3.50f, .20f, "ground", "light", "biological", zerg, supply: .5f, splashRadius: 50, splashMultiplier: .50f, suicide: true, burrow: true, regen: 1f),
                MakeZerg("corruptor", "腐化", "虫族", 1, 200, 14, 14, 2, 6, 6, 4.73f, 1.50f, "air", "armored", "biological", zerg, supply: 2, regen: 5f),
                MakeZerg("mutalisk", "飞龙", "虫族", 2, 120, 12, 12, 0, 3, 3, 5.60f, 1f, "air", "light", "biological", zerg, supply: 2, regen: 3f),
                MakeZerg("ultralisk_advanced", "莽兽", "虫族", 2, 650, 42, 42, 3, 1, 0, 4.13f, .85f, "ground", "massive", "biological", zerg, supply: 8, splashRadius: 40, splashMultiplier: .30f, regen: 10f),
                MakeZerg("brood_lord", "大龙", "虫族", 2, 225, 20, 20, 1, 9, 0, 1.97f, 1.50f, "air", "massive", "biological", zerg, supply: 4, regen: 5f),
                MakeZerg("infestor", "感染", "虫族", 2, 90, 10, 10, 0, 6, 0, 2.80f, 2f, "ground", "armored", "biological", zerg, supply: 2, support: true, burrow: true, regen: 4f),
                MakeZerg("vipers", "飞蛇", "虫族", 3, 150, 12, 12, 0, 8, 8, 4.13f, 2f, "air", "armored", "biological", zerg, supply: 3, support: true, regen: 4f),

                // Protoss
                MakeProtoss("zealot", "叉子", "神族", 1, 100, 8, 8, 1, 1, 0, 3.15f, 1.20f, "ground", "light", "biological", protoss, shield: 50, supply: 2),
                MakeProtoss("adept", "使徒", "神族", 1, 70, 10, 10, 1, 4, 0, 3.50f, 1.60f, "ground", "light", "biological", protoss, shield: 70, supply: 2),
                MakeProtoss("stalker", "追猎", "神族", 1, 80, 13, 13, 1, 6, 6, 4.13f, 1.34f, "ground", "armored", "mechanical", protoss, shield: 80, supply: 3),
                MakeProtoss("void_ray", "虚空", "神族", 2, 150, 20, 20, 0, 6, 6, 2.62f, 1f, "air", "armored", "mechanical", protoss, shield: 100, supply: 4),
                MakeProtoss("archon", "白球", "神族", 2, 10, 25, 25, 0, 3, 3, 3.94f, 1.50f, "ground", "massive", "psionic", protoss, shield: 350, supply: 4, splashRadius: 45, splashMultiplier: .25f, energy: 200),
                MakeProtoss("immortal", "不朽", "神族", 2, 200, 20, 20, 1, 6, 0, 3.15f, 1.50f, "ground", "armored", "mechanical", protoss, shield: 100, supply: 4),
                MakeProtoss("sentry", "哨兵", "神族", 2, 40, 6, 6, 0, 5, 5, 3.15f, 1f, "ground", "light", "mechanical", protoss, shield: 40, supply: 2, support: true, energy: 200),
                MakeProtoss("colossus", "巨像", "神族", 3, 200, 20, 20, 1, 7, 0, 3.15f, 1.50f, "ground", "massive", "mechanical", protoss, shield: 150, supply: 6, splashRadius: 60, splashMultiplier: .50f),
                MakeProtoss("high_templar", "闪电", "神族", 3, 40, 16, 16, 0, 6, 6, 2.62f, 2.20f, "ground", "light", "psionic", protoss, shield: 40, supply: 2, splashRadius: 60, splashMultiplier: .50f, support: true, energy: 200),
                MakeProtoss("carrier", "航母", "神族", 3, 300, 24, 24, 2, 8, 8, 2.62f, 2f, "air", "armored", "mechanical", protoss, shield: 150, supply: 8),
                MakeProtoss("tempest", "风暴", "神族", 3, 300, 30, 30, 2, 10, 10, 3.15f, 2f, "air", "massive", "mechanical", protoss, shield: 200, supply: 8)
            };
            //按单位 ID 建立字典索引
            var result = new Dictionary<string, UnitData>();
            foreach (var unit in units) result[unit.UnitId] = unit;
            return result;
        }

        //人族工厂：轻伤/重伤双列；按机械类别决定可维修性；abilities 携带主动技能
        private static TerranUnitData MakeTerran(string id, string name, string faction, int tier, float hp,
            float lightDamage, float heavyDamage, float armor, float range, float airRange, float speed, float cooldown,
            string domain, string armorType, string unitClass, Color color,
            float shield = 0f, string targetDomain = "ground", float splashRadius = 0f, float splashMultiplier = 0f,
            bool support = false, bool suicide = false, bool stimpack = false, bool siegeMode = false,
            float supply = 0f, float radius = 0f, float threat = 0f, float impact = 0f, string[] abilities = null)
        {
            var data = new TerranUnitData
            {
                CanBeRepaired = unitClass == "mechanical",
                HasStimpack = stimpack || (abilities != null && System.Array.IndexOf(abilities, "stimpack") >= 0),
                CanEnterSiegeMode = siegeMode
            };
            ApplyLegacyCommon(data, id, name, faction, tier, hp, lightDamage, heavyDamage, armor, range, airRange, speed, cooldown, domain, armorType, unitClass, color, shield, targetDomain, splashRadius, splashMultiplier, support, suicide, supply, radius, threat, impact, abilities);
            return data;
        }

        //虫族工厂：填充潜地与生命恢复特性后应用通用属性
        private static ZergUnitData MakeZerg(string id, string name, string faction, int tier, float hp,
            float lightDamage, float heavyDamage, float armor, float range, float airRange, float speed, float cooldown,
            string domain, string armorType, string unitClass, Color color,
            float shield = 0f, string targetDomain = "ground", float splashRadius = 0f, float splashMultiplier = 0f,
            bool support = false, bool suicide = false, bool burrow = false, float regen = 2f,
            float supply = 0f, float radius = 0f, float threat = 0f, float impact = 0f, string[] abilities = null)
        {
            var data = new ZergUnitData
            {
                CanBurrow = burrow,
                RegeneratesHealth = regen > 0f,
                ZergHealthRegenerationPerSecond = Mathf.Max(0f, regen)
            };
            ApplyLegacyCommon(data, id, name, faction, tier, hp, lightDamage, heavyDamage, armor, range, airRange, speed, cooldown, domain, armorType, unitClass, color, shield, targetDomain, splashRadius, splashMultiplier, support, suicide, supply, radius, threat, impact, abilities);
            return data;
        }

        //神族工厂：填充能量上限后应用通用属性
        private static ProtossUnitData MakeProtoss(string id, string name, string faction, int tier, float hp,
            float lightDamage, float heavyDamage, float armor, float range, float airRange, float speed, float cooldown,
            string domain, string armorType, string unitClass, Color color,
            float shield = 0f, string targetDomain = "ground", float splashRadius = 0f, float splashMultiplier = 0f,
            bool support = false, bool suicide = false, float energy = 0f,
            float supply = 0f, float radius = 0f, float threat = 0f, float impact = 0f, string[] abilities = null)
        {
            var data = new ProtossUnitData
            {
                MaxEnergy = energy
            };
            ApplyLegacyCommon(data, id, name, faction, tier, hp, lightDamage, heavyDamage, armor, range, airRange, speed, cooldown, domain, armorType, unitClass, color, shield, targetDomain, splashRadius, splashMultiplier, support, suicide, supply, radius, threat, impact, abilities);
            return data;
        }

        //旧版通用属性填充（兜底数据专用，逻辑与 ApplyCommon 保持一致）
        private static void ApplyLegacyCommon(UnitData data, string id, string name, string faction, int tier, float hp,
            float lightDamage, float heavyDamage, float armor, float range, float airRange, float speed, float cooldown,
            string domain, string armorType, string unitClass, Color color, float shield, string targetDomain,
            float splashRadius, float splashMultiplier, bool support, bool suicide,
            float supply, float radius, float threat, float impact, string[] abilities)
        {
            //体积：表格指定单位半径时以表格为准（×16 世界换算），否则按生命值幂函数推导
            float volume = radius > 0f ? Mathf.Clamp(radius * 16f, 7f, 24f) : VolumeFromHealth(hp);
            float impactValue = impact > 0f ? impact : armorType == "massive" ? 3f : armorType == "armored" ? 1.5f : armorType == "light" ? .5f : 0f;
            data.UnitId = id;
            data.DisplayName = name;
            data.Faction = faction;
            data.Tier = tier;
            data.MaxHealth = hp;
            data.MaxShield = shield;
            data.AttackLightDamage = lightDamage;
            data.AttackHeavyDamage = heavyDamage;
            data.Armor = armor;
            data.AttackRange = range;
            data.AttackRangeAir = airRange;
            data.MoveSpeed = speed;
            data.AttackCooldown = cooldown;
            data.SkillCooldown = 0f;
            data.Impact = impactValue;
            data.TargetDomain = targetDomain;
            data.Domain = domain;
            data.ArmorType = armorType;
            data.UnitClass = unitClass;
            data.CanAttackAir = targetDomain == "air" || targetDomain == "both";
            data.UnitVolume = volume;
            data.SplashRadius = splashRadius;
            data.SplashMultiplier = splashMultiplier;
            data.IsSupport = support;
            data.SuicideAttack = suicide;
            data.UnitSupply = supply;
            data.UnitRadius = radius;
            data.Threat = threat;
            data.Abilities = abilities;
            data.AccentColor = color;
            //碰撞半径：表格指定单位半径时直接换算（unitRadius×16），否则按生命值推导
            data.CollisionRadius = radius > 0f ? Mathf.Clamp(radius * 16f, 7f, 24f) : CollisionRadiusFromHealth(hp);
        }

        //十六进制颜色字符串转 Color，解析失败返回白色
        private static Color ColorFromHex(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.white;
        }
    }
}
