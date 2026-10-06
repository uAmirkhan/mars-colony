using MarsColony.Domain;
using MarsColony.Domain.Config;
using NUnit.Framework;

namespace MarsColony.Domain.Tests
{
    /// <summary>
    /// Тесты нового модуля добычи (нет в TS-домене, см. заголовок `Mining.cs`).
    /// Ключевое число из спеки ночи 3: три машины делят таймер лед-партии
    /// (prod_time_sec=600) на 200с = 3:20.
    /// </summary>
    [TestFixture]
    public class MiningTests
    {
        [Test]
        public void BatchDurationSec_DividesByMachineCount()
        {
            // water_ice.prod_time_sec = 600 (Goods.cs), три машины -> 200с (3:20).
            Assert.AreEqual(200.0, Mining.BatchDurationSec(Goods.WATER_ICE, 3));
        }

        [Test]
        public void BatchDurationSec_RespectsTimeScale()
        {
            var tuning = TuningDefaults.Default();
            tuning.time_scale = 10.0;
            Assert.AreEqual(2000.0, Mining.BatchDurationSec(Goods.WATER_ICE, 3, tuning));
        }

        [Test]
        public void Start_SetsWorkingState_AndEndsAt()
        {
            var site = Mining.CreateSite(Goods.WATER_ICE, machines: 3);
            var result = Mining.Start(site, 1000);

            Assert.IsTrue(result.ok);
            Assert.AreEqual(MiningState.WORKING, site.state);
            Assert.AreEqual(1000 + 200.0, site.ends_at);
        }

        [Test]
        public void Start_RejectedWhenNotEmpty()
        {
            var site = Mining.CreateSite(Goods.WATER_ICE);
            site.state = MiningState.WORKING;

            var result = Mining.Start(site, 0);
            Assert.IsFalse(result.ok);
            Assert.AreEqual(ActionReason.slot_busy, result.reason);
        }

        [Test]
        public void Refresh_TransitionsToReady_AfterEndsAt()
        {
            var site = Mining.CreateSite(Goods.REGOLITH);
            Mining.Start(site, 0);
            double ends = site.ends_at;

            Mining.Refresh(site, ends - 1);
            Assert.AreEqual(MiningState.WORKING, site.state);

            Mining.Refresh(site, ends);
            Assert.AreEqual(MiningState.READY, site.state);
        }

        [Test]
        public void Collect_DepositsQtyInRange_AndGrantsXp()
        {
            var site = Mining.CreateSite(Goods.WATER_ICE);
            site.state = MiningState.READY;
            var w = Warehouse.Create();

            var result = Mining.Collect(site, w, () => 0.5, 0);

            Assert.IsTrue(result.ok);
            int qty = Warehouse.QtyOf(w, Goods.WATER_ICE);
            Goods.MinMax range = Goods.GOOD_BASE_QTY[Goods.WATER_ICE];
            Assert.GreaterOrEqual(qty, range.min);
            Assert.LessOrEqual(qty, range.max);
            Assert.AreEqual(Goods.Of(Goods.WATER_ICE).base_xp * qty, result.xp_gained);
            Assert.AreEqual(MiningState.EMPTY, site.state);
        }

        [Test]
        public void Collect_RejectedWhenWarehouseFull_StaysReady()
        {
            var site = Mining.CreateSite(Goods.WATER_ICE);
            site.state = MiningState.READY;
            var w = Warehouse.Create(0);

            var result = Mining.Collect(site, w, () => 0.5, 0);

            Assert.IsFalse(result.ok);
            Assert.AreEqual(ActionReason.warehouse_full, result.reason);
            Assert.AreEqual(MiningState.READY, site.state);
        }

        [Test]
        public void Collect_RejectedBeforeReady()
        {
            var site = Mining.CreateSite(Goods.WATER_ICE);
            var w = Warehouse.Create();

            var result = Mining.Collect(site, w, () => 0.5, 0);

            Assert.IsFalse(result.ok);
            Assert.AreEqual(ActionReason.not_ready, result.reason);
        }
    }
}
