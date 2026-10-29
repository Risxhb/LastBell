using System.Collections.Generic;
using System.Linq;

namespace LastBell.Run.Shop
{
    // Greedy shopper for automated runs (-lastbellShopBot; run-shop-plan §5). It goes through the same host-checked
    // requests as the screen: Keystone if allowed, then Rare, then Common (class cards before general ones at each
    // tier), then Temper, with one reroll if nothing was affordable. Returns what it did, for the run log.
    public static class RunShopBot
    {
        public static List<string> Shop(ShopRun run, int slot)
        {
            var log = new List<string>(); var me = run.Slot(slot);
            if (me == null || !run.Open) return log;
            bool BuyBest()
            {
                var order = Enumerable.Range(0, me.StockCount).Where(i => me.Stock[i] != null)
                    .OrderByDescending(i => (int)me.Stock[i].Tier).ThenBy(i => me.Stock[i].General ? 1 : 0).ToList();
                foreach (int i in order)
                {
                    var def = me.Stock[i];
                    if (run.TryBuy(slot, i, out _)) { log.Add($"bought {def.Name} ({def.Tier}, {def.Price})"); return true; }
                }
                return false;
            }
            bool any = false;
            while (BuyBest()) any = true;
            if (!any && me.Chimes >= run.RerollPrice(me) + ItemCatalog.CommonPrice && run.TryReroll(slot, out _)) { log.Add("rerolled"); while (BuyBest()) { } }
            foreach (var o in me.Items.ToArray())
                if (run.TryTemper(slot, o.Def.Id, out _)) log.Add($"tempered {o.Def.Name}");
            log.Add($"left with {me.Chimes} Chimes; build: {string.Join(", ", me.Items.Select(o => o.Def.Name + (o.Tempered ? "+" : "")))}");
            return log;
        }
    }
}
