using MarsColony.Domain;
using MarsColony.Domain.Config;
using NUnit.Framework;

namespace MarsColony.Domain.Tests
{
    /// <summary>
    /// Двойник `mars-colony/src/domain/__tests__/rushcost.test.ts`. Спека задает
    /// конкретные числа лестницы округления — тест обязан проверять числа, а
    /// не «больше — меньше» (см. заголовок оригинального файла).
    /// </summary>
    [TestFixture]
    public class RushCostTests
    {
        [TestCase(12, 10)]
        [TestCase(13, 15)]
        [TestCase(47, 45)]
        [TestCase(49, 50)]
        [TestCase(51, 50)]
        [TestCase(175, 180)]
        [TestCase(195, 200)]
        [TestCase(210, 200)]
        [TestCase(225, 250)]
        [TestCase(975, 1000)]
        [TestCase(1040, 1000)]
        [TestCase(1050, 1100)]
        public void RoundToShowcase_MatchesLadder(double value, double expected)
        {
            Assert.AreEqual(expected, RushCost.RoundToShowcase(value));
        }

        [TestCase(0)]
        [TestCase(0.4)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(2.5)]
        public void RoundToShowcase_NeverZero(double value)
        {
            Assert.Greater(RushCost.RoundToShowcase(value), 0);
        }

        [Test]
        public void RushCostOf_CheaperWithStockedInput()
        {
            var stocked = Warehouse.Create();
            Warehouse.Deposit(stocked, Goods.COTTON, 50);
            var empty = Warehouse.Create();

            Assert.Less(RushCost.RushCostOf(Goods.FABRIC, 1, stocked), RushCost.RushCostOf(Goods.FABRIC, 1, empty));
        }

        [Test]
        public void RushCostOf_ZeroWhenFinishedGoodInStock()
        {
            var stocked = Warehouse.Create();
            Warehouse.Deposit(stocked, Goods.FABRIC, 10);
            Assert.AreEqual(0, RushCost.RushCostOf(Goods.FABRIC, 1, stocked));
        }

        [Test]
        public void RushCostOf_PartialStock_ReducesButNotToZero()
        {
            var partial = Warehouse.Create();
            Warehouse.Deposit(partial, Goods.FABRIC, 1);
            double price = RushCost.RushCostOf(Goods.FABRIC, 3, partial);

            Assert.Greater(price, 0);
            Assert.Less(price, RushCost.RushCostOf(Goods.FABRIC, 3));
        }

        [Test]
        public void RushCostOf_ReservedStockDoesNotCountAsOwned()
        {
            var w = Warehouse.Create();
            Warehouse.Deposit(w, Goods.FABRIC, 5);
            double free = RushCost.RushCostOf(Goods.FABRIC, 5, w);

            w.cells[Goods.FABRIC].reserved = 5;
            Assert.Greater(RushCost.RushCostOf(Goods.FABRIC, 5, w), free);
        }

        [Test]
        public void RushCostOf_CheapLink_CostsAtLeastFloor()
        {
            // Водоросли: 120с = 2мин x 5 изотопов/мин = 10 = ровно пол.
            Assert.AreEqual(Economy.SPEEDUP_FLOOR_ISOTOPES["crop"], RushCost.RushCostOf(Goods.ALGAE, 1));
        }

        [Test]
        public void RushCostOf_ChainOfCheapLinks_CostsMoreThanOneFloor()
        {
            Assert.Greater(RushCost.RushCostOf(Goods.JUMPSUIT, 1), Economy.SPEEDUP_FLOOR_ISOTOPES["factory"] * 2);
        }

        [Test]
        public void RushCostOf_ExpensiveLink_UsesRateNotFloor()
        {
            Good coffee = Goods.Of(Goods.COFFEE_BEANS);
            double by_rate = (coffee.prod_time_sec / 60.0) * Economy.SPEEDUP_RATE_ISOTOPES_PER_MIN[GoodKind.crop];
            Assert.Greater(by_rate, Economy.SPEEDUP_FLOOR_ISOTOPES["crop"]);
            Assert.AreEqual(by_rate, RushCost.RushCostOf(Goods.COFFEE_BEANS, 1), 0.00001);
        }

        [Test]
        public void PurchaseMargin_WithinCorridor()
        {
            Assert.GreaterOrEqual(RushCost.PURCHASE_MARGIN, 1.0);
            Assert.LessOrEqual(RushCost.PURCHASE_MARGIN, RushCost.SPEEDUP_MARGIN_CEILING);
        }

        [Test]
        public void BuyoutPrice_NeverExceedsCorridorByMoreThanShowcaseSlack()
        {
            foreach (var id in Goods.ALL_GOOD_IDS)
            {
                double raw = RushCost.RushCostOf(id, 1);
                int shown = RushCost.BuyoutPrice(id, 1);
                Assert.LessOrEqual(shown, raw * RushCost.SPEEDUP_MARGIN_CEILING + 5, $"good={id}");
            }
        }
    }
}
