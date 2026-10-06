using System.Collections.Generic;
using MarsColony.Domain;
using MarsColony.Domain.Config;
using NUnit.Framework;

namespace MarsColony.Domain.Tests
{
    /// <summary>
    /// Двойник `mars-colony/src/domain/__tests__/drone.test.ts`. Числа —
    /// каркас раздел 7 и [[tz-drone-mars]].
    /// </summary>
    [TestFixture]
    public class DroneTests
    {
        private static readonly string[] POOL = { Goods.ALGAE, Goods.SOY, Goods.MUSHROOMS };

        private static WarehouseState StockedWarehouse(IEnumerable<string> goods, int per_good = 30)
        {
            var w = Warehouse.Create(500);
            foreach (var id in goods)
                Warehouse.Deposit(w, id, per_good);
            return w;
        }

        private static GeneratorContext Ctx(System.Func<double> rng = null) =>
            new GeneratorContext
            {
                level = 6,
                warehouse = StockedWarehouse(POOL),
                available_goods = new List<string>(POOL),
                board = new List<OrderSlot>(),
                rng = rng ?? (() => 0.5),
                deficit_locks = null,
                now = 0,
            };

        [Test]
        public void OrderReward_PremiumStaysWithinCorridor()
        {
            for (uint seed = 0; seed < 40; seed++)
            {
                var rng = TestRng.Make(seed + 1);
                var positions = new List<OrderPosition>
                {
                    new OrderPosition { good_id = Goods.ALGAE, qty = 5, easy = true },
                    new OrderPosition { good_id = Goods.SOY, qty = 3, easy = true },
                };
                var reward = Drone.OrderReward(positions, rng(), has_deficit_position: rng() > 0.5);

                Assert.GreaterOrEqual(reward.premium, 1 + Economy.DRONE_PREMIUM_RANGE_MIN);
                Assert.LessOrEqual(reward.premium, 1 + Economy.DRONE_PREMIUM_RANGE_MAX);
            }
        }

        [Test]
        public void OrderReward_XpEqualsSumOfBaseXpTimesK()
        {
            var positions = new List<OrderPosition>
            {
                new OrderPosition { good_id = Goods.ALGAE, qty = 5, easy = true },
                new OrderPosition { good_id = Goods.SOY, qty = 3, easy = true },
            };
            var reward = Drone.OrderReward(positions);

            int expected_xp =
                Goods.Of(Goods.ALGAE).base_xp * Economy.XP_MULTIPLIER_K[Mechanic.drone] * 5
                + Goods.Of(Goods.SOY).base_xp * Economy.XP_MULTIPLIER_K[Mechanic.drone] * 3;
            Assert.AreEqual(expected_xp, reward.xp);
        }

        [Test]
        public void DiscardOrder_SetsCooldown22Minutes()
        {
            var slot = new OrderSlot { idx = 0, state = OrderSlotState.active, positions = new List<OrderPosition>() };
            Drone.DiscardOrder(slot, 1000);

            Assert.AreEqual(OrderSlotState.empty_cooldown, slot.state);
            Assert.AreEqual(1000 + 22 * 60, slot.refresh_at);
            Assert.AreEqual(1320, Economy.DRONE_REFRESH_FREE_SEC);
        }

        [TestCase(961.0, 10)] // > 15 мин
        [TestCase(600.0, 7)] // > 8 мин
        [TestCase(300.0, 4)] // > 3 мин
        [TestCase(60.0, 2)] // > 0
        [TestCase(0.0, 0)] // истек — бесплатно
        public void DroneRefreshPrice_FollowsLadder(double remaining_sec, int expected)
        {
            Assert.AreEqual(expected, Economy.DroneRefreshPrice(remaining_sec));
        }

        [Test]
        public void ApplyPinch_ClampsBetweenMinAndMax()
        {
            // gap = target - stock; результат = stock + clamp(gap, 1, 3).
            Assert.AreEqual(0 + Economy.PINCH_MIN, Drone.ApplyPinch(0, 0)); // gap=0 -> клемп до PINCH_MIN
            Assert.AreEqual(2 + Economy.PINCH_MAX, Drone.ApplyPinch(2, 100)); // gap огромный -> клемп до PINCH_MAX
            Assert.AreEqual(2 + 2, Drone.ApplyPinch(2, 4)); // gap=2, внутри диапазона
        }

        [Test]
        public void GenerateOrder_ProducesNonEmptyOrder_WithCoverageHeld()
        {
            for (uint seed = 0; seed < 30; seed++)
            {
                var rng = TestRng.Make(seed + 100);
                var ctx = Ctx(rng);
                var order = Drone.GenerateOrder(0, ctx);

                Assert.Greater(order.positions.Count, 0);
                Assert.AreEqual(OrderSlotState.active, order.state);
            }
        }

        [Test]
        public void LoadPosition_PartialLoad_ThenCompletes_MakesSlotReady()
        {
            var w = Warehouse.Create();
            Warehouse.Deposit(w, Goods.ALGAE, 3);
            var slot = new OrderSlot
            {
                idx = 0,
                state = OrderSlotState.active,
                positions = new List<OrderPosition>
                {
                    new OrderPosition { good_id = Goods.ALGAE, qty = 5, qty_filled = 0, filled_by = FilledBy.none, qty_purchased = 0, easy = true },
                },
            };

            Assert.IsTrue(Drone.LoadPosition(slot, 0, w));
            Assert.AreEqual(OrderSlotState.in_progress, slot.state);
            Assert.AreEqual(3, slot.positions[0].qty_filled);

            Warehouse.Deposit(w, Goods.ALGAE, 2);
            Assert.IsTrue(Drone.LoadPosition(slot, 0, w));
            Assert.AreEqual(OrderSlotState.ready, slot.state);
            Assert.AreEqual(5, slot.positions[0].qty_filled);
        }

        [Test]
        public void SendOrder_ShipsStockedPortion_AndReturnsReward()
        {
            var w = Warehouse.Create();
            Warehouse.Deposit(w, Goods.ALGAE, 5);
            var slot = new OrderSlot
            {
                idx = 0,
                state = OrderSlotState.active,
                positions = new List<OrderPosition>
                {
                    new OrderPosition { good_id = Goods.ALGAE, qty = 5, qty_filled = 0, filled_by = FilledBy.none, qty_purchased = 0, easy = true },
                },
                credits_reward = 42,
                xp_reward = 7,
            };
            Drone.LoadPosition(slot, 0, w);
            Assert.AreEqual(OrderSlotState.ready, slot.state);

            var result = Drone.SendOrder(slot, w);

            Assert.IsTrue(result.ok);
            Assert.AreEqual(42, result.credits);
            Assert.AreEqual(7, result.xp);
            Assert.AreEqual(0, Warehouse.QtyOf(w, Goods.ALGAE));
        }
    }
}
