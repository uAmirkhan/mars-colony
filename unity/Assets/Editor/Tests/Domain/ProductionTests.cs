using MarsColony.Domain;
using MarsColony.Domain.Config;
using NUnit.Framework;

namespace MarsColony.Domain.Tests
{
    /// <summary>
    /// Двойник `mars-colony/src/domain/__tests__/production.test.ts` для
    /// C#-порта грядок и очереди фабрики. Числа — те же, что в TS/каркасе.
    /// </summary>
    [TestFixture]
    public class ProductionTests
    {
        private static ProductionContext Ctx(double now, WarehouseState w, int credits = 100, int level = 10, Tuning tuning = null) =>
            new ProductionContext { now = now, warehouse = w, credits = credits, level = level, tuning = tuning };

        [Test]
        public void Plant_ChargesCredits_AndStartsGrowing()
        {
            var w = Warehouse.Create();
            var field = Production.CreateField(0);
            var ctx = Ctx(1000, w, credits: 10);

            var result = Production.Plant(field, Goods.ALGAE, ctx, new[] { field });

            Assert.IsTrue(result.ok);
            // plantingCost(price=2) = max(1, round(0.4*2)) = 1
            Assert.AreEqual(-1, result.credits_delta);
            Assert.AreEqual(FieldState.GROWING, field.state);
            Assert.AreEqual(1000 + Goods.Of(Goods.ALGAE).prod_time_sec, field.ends_at);
        }

        [Test]
        public void Plant_TimeScale_MultipliesDuration()
        {
            var w = Warehouse.Create();
            var field = Production.CreateField(0);
            var tuning = TuningDefaults.Default();
            tuning.time_scale = 10.0;
            var ctx = Ctx(0, w, credits: 10, tuning: tuning);

            Production.Plant(field, Goods.ALGAE, ctx, new[] { field });

            Assert.AreEqual(Goods.Of(Goods.ALGAE).prod_time_sec * 10.0, field.ends_at);
        }

        [Test]
        public void CollectField_DepositsHarvestQty_AndGrantsXp()
        {
            var w = Warehouse.Create();
            var field = Production.CreateField(0);
            field.state = FieldState.GROWING;
            field.good_id = Goods.ALGAE;
            field.ends_at = 100;
            var ctx = Ctx(100, w);

            var result = Production.CollectField(field, ctx);

            Assert.IsTrue(result.ok);
            int expected_qty = Goods.HarvestQty(Goods.ALGAE);
            Assert.AreEqual(expected_qty, Warehouse.QtyOf(w, Goods.ALGAE));
            Assert.AreEqual(Goods.Of(Goods.ALGAE).base_xp * expected_qty, result.xp_gained);
            Assert.AreEqual(FieldState.EMPTY, field.state);
        }

        [Test]
        public void CollectField_RejectedWhenWarehouseFull_SlotStaysReady()
        {
            var w = Warehouse.Create(3); // меньше, чем HARVEST_QTY(algae) = 4
            var field = Production.CreateField(0);
            field.state = FieldState.GROWING;
            field.good_id = Goods.ALGAE;
            field.ends_at = 0;
            var ctx = Ctx(0, w);

            var result = Production.CollectField(field, ctx);

            Assert.IsFalse(result.ok);
            Assert.AreEqual(ActionReason.warehouse_full, result.reason);
            Assert.AreEqual(FieldState.READY, field.state);
            Assert.AreEqual(0, Warehouse.QtyOf(w, Goods.ALGAE));
        }

        [Test]
        public void Enqueue_ConsumesInputsImmediately_WhenAvailable()
        {
            var w = Warehouse.Create();
            Warehouse.Deposit(w, Goods.SOY, 5);
            var slot = Production.CreateFactorySlot(0, BuildingType.food_module);
            var ctx = Ctx(0, w, level: 5);

            var result = Production.Enqueue(slot, Goods.PROTEIN_BAR, ctx);

            Assert.IsTrue(result.ok);
            Assert.AreEqual(FactorySlotState.PRODUCING, slot.state);
            // protein_bar требует soy x2
            Assert.AreEqual(3, Warehouse.AvailableOf(w, Goods.SOY));
            Assert.AreEqual(Goods.Of(Goods.PROTEIN_BAR).prod_time_sec, slot.ends_at);
        }

        [Test]
        public void Enqueue_QueuesWithoutReservingInputs_WhenMissing()
        {
            var w = Warehouse.Create();
            var slot = Production.CreateFactorySlot(0, BuildingType.food_module);
            var ctx = Ctx(0, w, level: 5);

            var result = Production.Enqueue(slot, Goods.PROTEIN_BAR, ctx);

            Assert.IsTrue(result.ok);
            Assert.AreEqual(ActionReason.no_inputs, result.reason);
            Assert.AreEqual(FactorySlotState.QUEUED, slot.state);
            Assert.AreEqual(0, Warehouse.QtyOf(w, Goods.SOY));
        }

        [Test]
        public void OnWarehouseStockIncreased_StartsQueuedSlot_WhenInputsArrive()
        {
            var w = Warehouse.Create();
            var slot = Production.CreateFactorySlot(0, BuildingType.food_module);
            var ctx = Ctx(0, w, level: 5);
            Production.Enqueue(slot, Goods.PROTEIN_BAR, ctx);
            Assert.AreEqual(FactorySlotState.QUEUED, slot.state);

            Warehouse.Deposit(w, Goods.SOY, 2);
            int started = Production.OnWarehouseStockIncreased(new[] { slot }, ctx);

            Assert.AreEqual(1, started);
            Assert.AreEqual(FactorySlotState.PRODUCING, slot.state);
            Assert.AreEqual(0, Warehouse.AvailableOf(w, Goods.SOY));
        }

        [Test]
        public void CollectFactory_DepositsOutput_AndXp()
        {
            var w = Warehouse.Create();
            var slot = Production.CreateFactorySlot(0, BuildingType.food_module);
            slot.state = FactorySlotState.PRODUCING;
            slot.good_id = Goods.PROTEIN_BAR;
            slot.ends_at = 50;
            var ctx = Ctx(50, w);

            var result = Production.CollectFactory(slot, ctx);

            Assert.IsTrue(result.ok);
            Assert.AreEqual(1, Warehouse.QtyOf(w, Goods.PROTEIN_BAR));
            Assert.AreEqual(Goods.Of(Goods.PROTEIN_BAR).base_xp, result.xp_gained);
            Assert.AreEqual(FactorySlotState.EMPTY, slot.state);
        }

        [Test]
        public void CollectFactory_RejectedWhenWarehouseFull()
        {
            var w = Warehouse.Create(0);
            var slot = Production.CreateFactorySlot(0, BuildingType.food_module);
            slot.state = FactorySlotState.PRODUCING;
            slot.good_id = Goods.PROTEIN_BAR;
            slot.ends_at = 0;
            var ctx = Ctx(0, w);

            var result = Production.CollectFactory(slot, ctx);

            Assert.IsFalse(result.ok);
            Assert.AreEqual(ActionReason.warehouse_full, result.reason);
            Assert.AreEqual(FactorySlotState.READY, slot.state);
        }

        [Test]
        public void Sell_CreditsFromAvailableStock()
        {
            var w = Warehouse.Create();
            Warehouse.Deposit(w, Goods.ALGAE, 10);
            var ctx = Ctx(0, w);

            var result = Production.Sell(Goods.ALGAE, 4, ctx);

            Assert.IsTrue(result.ok);
            Assert.AreEqual(2 * 4, result.credits_delta); // price=2, ratio=1.0
            Assert.AreEqual(6, Warehouse.QtyOf(w, Goods.ALGAE));
        }
    }
}
