using System.Collections.Generic;
using MarsColony.Domain;
using MarsColony.Domain.Config;
using NUnit.Framework;

namespace MarsColony.Domain.Tests
{
    /// <summary>
    /// Двойник `mars-colony/src/domain/__tests__/shuttle.test.ts`. Числа —
    /// каркас и [[tz-shuttle-mars]].
    /// </summary>
    [TestFixture]
    public class ShuttleTests
    {
        private static readonly string[] POOL = { Goods.ALGAE, Goods.SOY, Goods.MUSHROOMS };

        private static WarehouseState StockedWarehouse(int per_good = 40)
        {
            var w = Warehouse.Create(500);
            foreach (var id in POOL)
                Warehouse.Deposit(w, id, per_good);
            return w;
        }

        private static ShuttleGenContext GenCtx(bool is_first_trip = false, System.Func<double> rng = null) =>
            new ShuttleGenContext
            {
                level = 6,
                warehouse = StockedWarehouse(),
                available_goods = new List<string>(POOL),
                previous = null,
                is_first_trip = is_first_trip,
                arrival_no = 1,
                rng = rng ?? (() => 0.5),
                deficit_locks = null,
                now = 0,
            };

        private static DropContext Drop() =>
            new DropContext
            {
                pity = new ModuleCounts(),
                stock = new ModuleCounts(),
                need = new ModuleCounts(),
                warehouse_avg_24h = new ModuleCounts(),
                gated_open = false,
                arrival_no = 1,
                constructions = new List<ConstructionNeed>(),
                rng = () => 0.5,
            };

        [Test]
        public void FlightTimerMin_UsesLevelBracket()
        {
            Assert.AreEqual(60, Economy.FlightTimerMin(6));
            Assert.AreEqual(75, Economy.FlightTimerMin(12));
            Assert.AreEqual(90, Economy.FlightTimerMin(20));
        }

        [Test]
        public void GenerateTrip_Ftue_HasMinSlots_AndShortTimer()
        {
            var trip = Shuttle.GenerateTrip(GenCtx(is_first_trip: true));

            Assert.AreEqual(Economy.SLOT_COUNT_MIN, trip.slots.Count);
            Assert.AreEqual(Economy.FTUE_FIRST_TRIP_TIMER_MIN, trip.trip_min);
        }

        [Test]
        public void GenerateTrip_Normal_SlotCountWithinBounds()
        {
            for (uint seed = 0; seed < 30; seed++)
            {
                var rng = TestRng.Make(seed + 1);
                var trip = Shuttle.GenerateTrip(GenCtx(rng: rng));
                Assert.GreaterOrEqual(trip.slots.Count, Economy.SLOT_COUNT_MIN);
                Assert.LessOrEqual(trip.slots.Count, Economy.SLOT_COUNT_MAX);
            }
        }

        [Test]
        public void SlotXp_UsesMultiplierK8()
        {
            var trip = Shuttle.GenerateTrip(GenCtx());
            var slot = trip.slots[0];

            int expected = Goods.Of(slot.good_id).base_xp * Economy.XP_MULTIPLIER_K[Mechanic.shuttle] * slot.qty_required;
            Assert.AreEqual(expected, Shuttle.SlotXp(slot));
        }

        [Test]
        public void TripXp_SumsAllSlots()
        {
            var trip = Shuttle.GenerateTrip(GenCtx());
            int sum = 0;
            foreach (var s in trip.slots)
                sum += Shuttle.SlotXp(s);
            Assert.AreEqual(sum, Shuttle.TripXp(trip));
        }

        [Test]
        public void LoadingLastSlot_DepartsTrip_AndSetsArrivalByTripTimer()
        {
            var warehouse = StockedWarehouse();
            var trip = Shuttle.GenerateTrip(GenCtx(is_first_trip: true));
            var drop = Drop();

            LoadResult last = null;
            for (int i = 0; i < trip.slots.Count; i++)
                last = Shuttle.LoadSlot(trip, i, warehouse, 1000, drop);

            Assert.IsTrue(Shuttle.AllSlotsLoaded(trip));
            Assert.IsTrue(last.departed);
            Assert.AreEqual(ShuttleState.IN_TRANSIT, trip.state);
            Assert.AreEqual(1000 + trip.trip_min * 60, trip.arrives_at);
            Assert.IsNotNull(last.drop_state);
            Assert.AreEqual(trip.slots.Count, last.drop_state.modules.Count);
        }

        [Test]
        public void SkipFlight_ThenCollectContainer_ReturnsRewardOnce()
        {
            var warehouse = StockedWarehouse();
            var trip = Shuttle.GenerateTrip(GenCtx(is_first_trip: true));
            var drop = Drop();
            for (int i = 0; i < trip.slots.Count; i++)
                Shuttle.LoadSlot(trip, i, warehouse, 0, drop);

            Assert.IsTrue(Shuttle.SkipFlight(trip, 5000));
            Assert.AreEqual(ShuttleState.ARRIVED, trip.state);

            var first = Shuttle.CollectContainer(trip, 0);
            Assert.IsNotNull(first);
            var second = Shuttle.CollectContainer(trip, 0);
            Assert.IsNull(second); // уже собран — второй раз нечего брать
        }

        [Test]
        public void SlotCovered_TrueWhenWarehouseHasEnough()
        {
            var w = Warehouse.Create();
            Warehouse.Deposit(w, Goods.ALGAE, 5);
            var slot = new ShuttleSlot { idx = 0, good_id = Goods.ALGAE, qty_required = 5, qty_filled = 0 };

            Assert.IsTrue(Shuttle.SlotCovered(slot, w));

            var short_slot = new ShuttleSlot { idx = 0, good_id = Goods.ALGAE, qty_required = 6, qty_filled = 0 };
            Assert.IsFalse(Shuttle.SlotCovered(short_slot, w));
        }
    }
}
