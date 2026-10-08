using System.Collections.Generic;
using UnityEngine;

namespace DesertStorm
{
    /// <summary>
    /// 单位对象池：跨单位类型复用 GameObject，避免高频生成/阵亡带来的反复内存申请。
    /// 空闲队列不区分单位种类——取出任意空闲对象后由 Setup() 重新配置数据/外观/体积，
    /// 等价于"换掉对象的属性"。池无硬上限：常规战斗维持约 50 个常备对象水位，
    /// 决战等高峰场景按需自动扩容（用多少建多少），回落后对象留在池中持续复用。
    /// </summary>
    public sealed class UnitObjectPool
    {
        //常备水位（诊断展示用；不作为硬上限，高峰期允许超过）
        public const int SoftCapacity = 50;

        //池根节点（回收的单位挂这里，保持 Hierarchy 整洁）
        private readonly Transform poolRoot;
        //跨类型空闲队列：任何空闲对象都可复用，取出后换属性（后进先出，缓存友好）
        private readonly Stack<UnitBase> idle = new Stack<UnitBase>();
        //池中全部单位（含在用与空闲），用于统计与场景清理
        private readonly List<UnitBase> all = new List<UnitBase>();
        //共享外观精灵，新建单位时传入
        private readonly Sprite circleSprite;

        //创建池：parent 为池根节点的父物体；sprite 为单位外观精灵
        public UnitObjectPool(Transform parent, Sprite sprite, string rootName = "UnitPool")
        {
            var rootObject = new GameObject(rootName);
            rootObject.transform.SetParent(parent, false);
            poolRoot = rootObject.transform;
            circleSprite = sprite;
        }

        //从池中取一个单位：优先复用任意空闲对象（换属性），池空才新建
        public UnitBase Acquire(UnitData data, Vector2 position, CdtNavigationSystem navigation, bool airUnit, TrainingDummy dummy = null)
        {
            UnitBase unit = null;
            //弹出第一个存活的空闲对象（不挑类型——属性由 Setup 全量重设）
            while (idle.Count > 0 && unit == null)
            {
                UnitBase candidate = idle.Pop();
                //池内引用可能已被场景切换销毁（Unity null），跳过
                if (candidate != null) unit = candidate;
            }
            if (unit == null)
            {
                //新建：挂到池根节点下（后续复用不改变父级）
                GameObject unitObject = new GameObject("PooledUnit");
                unitObject.transform.SetParent(poolRoot, false);
                unit = unitObject.AddComponent<UnitBase>();
                all.Add(unit);
            }
            unit.gameObject.SetActive(true);
            unit.transform.position = new Vector3(position.x, position.y, -1f);
            //清空上一轮状态后，按新单位数据全量重配（数据/颜色/体积/血条/技能）
            unit.ResetForReuse();
            unit.Setup(data, dummy, navigation, circleSprite, airUnit);
            return unit;
        }

        //归还单位到池：隐藏并复位，等待下次复用（任何类型都可进同一队列）
        public void Release(UnitBase unit)
        {
            if (unit == null) return;
            if (!all.Contains(unit)) all.Add(unit);
            unit.ResetForReuse();
            unit.gameObject.SetActive(false);
            if (unit.transform.parent != poolRoot) unit.transform.SetParent(poolRoot, false);
            idle.Push(unit);
        }

        //批量归还（战斗场景清空一侧/清理全场时用）
        public void ReleaseAll(IEnumerable<UnitBase> units)
        {
            if (units == null) return;
            foreach (UnitBase unit in units) Release(unit);
        }

        //场景销毁时彻底清空池（真 Destroy，不保留复用）
        public void ClearAll()
        {
            foreach (UnitBase unit in all)
                if (unit != null) Object.Destroy(unit.gameObject);
            all.Clear();
            idle.Clear();
        }

        //当前在用单位数量（诊断用）
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (UnitBase unit in all) if (unit != null && unit.gameObject.activeSelf) count++;
                return count;
            }
        }

        //空闲可复用数量（诊断用）
        public int IdleCount => idle.Count;

        //池内总对象数（诊断用：在用 + 空闲）
        public int TotalCount => all.Count;
    }
}
